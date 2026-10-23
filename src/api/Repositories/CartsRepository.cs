using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using OctoSupply.Api.Data;
using OctoSupply.Api.Models;
using OctoSupply.Api.Utils;

namespace OctoSupply.Api.Repositories;

/// <summary>Persists isolated guest carts and prices consistent transactional snapshots.</summary>
public sealed class CartsRepository(SqliteConnectionFactory connectionFactory)
{
    /// <summary>Creates an empty cart with an unguessable bearer identifier.</summary>
    public async Task<Cart> CreateAsync(CancellationToken cancellationToken = default)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var connection = await connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO carts (token) VALUES ($token);";
        command.Parameters.AddWithValue("$token", token);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new Cart(token, [], 0m);
    }

    /// <summary>Reads a cart using current catalog prices; an unknown token returns a domain 404.</summary>
    public Task<Cart> FindAsync(string token, CancellationToken cancellationToken = default) =>
        ExecuteAsync(token, null, cancellationToken);

    /// <summary>Atomically increments a line, rejecting invalid quantities and integer overflow.</summary>
    public Task<Cart> AddAsync(string token, AddCartItemRequest request, CancellationToken cancellationToken = default)
    {
        ValidateItem(request.ProductId, request.Quantity);
        return ExecuteAsync(token, async (connection, transaction, cartId) =>
        {
            await using var product = connection.CreateCommand();
            product.Transaction = transaction;
            product.CommandText = "SELECT COUNT(*) FROM products WHERE product_id = $productId;";
            product.Parameters.AddWithValue("$productId", request.ProductId);
            if (Convert.ToInt32(await product.ExecuteScalarAsync(cancellationToken)) == 0)
            {
                throw new NotFoundException("Product", request.ProductId);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cart_items (cart_id, product_id, quantity)
                VALUES ($cartId, $productId, $quantity)
                ON CONFLICT(cart_id, product_id) DO UPDATE
                SET quantity = cart_items.quantity + excluded.quantity
                WHERE cart_items.quantity <= 2147483647 - excluded.quantity;
                """;
            command.Parameters.AddWithValue("$cartId", cartId);
            command.Parameters.AddWithValue("$productId", request.ProductId);
            command.Parameters.AddWithValue("$quantity", request.Quantity);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new ConflictException("Cart quantity would exceed the supported integer range");
            }
        }, cancellationToken);
    }

    /// <summary>Replaces a line's quantity; an absent line returns a domain 404.</summary>
    public Task<Cart> UpdateAsync(string token, int productId, int quantity, CancellationToken cancellationToken = default)
    {
        ValidateItem(productId, quantity);
        return ExecuteAsync(token, async (connection, transaction, cartId) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE cart_items SET quantity = $quantity
                WHERE cart_id = $cartId AND product_id = $productId;
                """;
            command.Parameters.AddWithValue("$cartId", cartId);
            command.Parameters.AddWithValue("$productId", productId);
            command.Parameters.AddWithValue("$quantity", quantity);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new NotFoundException("Cart item", productId);
            }
        }, cancellationToken);
    }

    /// <summary>Removes a line without pricing the remaining items, so invalid catalog data cannot block removal.</summary>
    public async Task RemoveAsync(string token, int productId, CancellationToken cancellationToken = default)
    {
        ValidateItem(productId, 1);
        await ExecuteAsync(token, async (connection, transaction, cartId) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM cart_items WHERE cart_id = $cartId AND product_id = $productId;";
            command.Parameters.AddWithValue("$cartId", cartId);
            command.Parameters.AddWithValue("$productId", productId);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new NotFoundException("Cart item", productId);
            }
        }, cancellationToken, includeItems: false);
    }

    private static void ValidateItem(int productId, int quantity)
    {
        if (productId < 1 || quantity < 1)
        {
            throw new ValidationException("productId and quantity must be positive integers");
        }
    }

    private async Task<Cart> ExecuteAsync(
        string token,
        Func<SqliteConnection, SqliteTransaction, long, Task>? mutation,
        CancellationToken cancellationToken,
        bool includeItems = true)
    {
        if (token.Length != 64 || token.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ValidationException("Invalid cart identifier");
        }

        await using var connection = await connectionFactory.OpenConnectionAsync();
        // Immediate write transactions serialize read/modify/write operations across requests.
        using var transaction = connection.BeginTransaction(deferred: mutation is null);
        await using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT cart_id FROM carts WHERE token = $token;";
        lookup.Parameters.AddWithValue("$token", token);
        var result = await lookup.ExecuteScalarAsync(cancellationToken);
        if (result is null)
        {
            throw new NotFoundException("Cart not found");
        }

        var cartId = Convert.ToInt64(result);
        if (mutation is not null)
        {
            await mutation(connection, transaction, cartId);
        }

        var items = new List<CartItem>();
        decimal total = 0;
        if (includeItems)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT p.product_id, p.name, p.img_name, p.price, p.discount, ci.quantity
                FROM cart_items ci JOIN products p ON p.product_id = ci.product_id
                WHERE ci.cart_id = $cartId ORDER BY p.product_id;
                """;
            command.Parameters.AddWithValue("$cartId", cartId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var price = reader.GetDouble(3);
                var discount = reader.IsDBNull(4) ? 0 : reader.GetDouble(4);
                if (!double.IsFinite(price) || price < 0 || price > (double)decimal.MaxValue ||
                    !double.IsFinite(discount) || discount < 0 || discount > 1)
                {
                    throw new ConflictException("A cart product has invalid catalog pricing");
                }

                try
                {
                    var unitPrice = decimal.Round((decimal)price * (1 - (decimal)discount), 2, MidpointRounding.AwayFromZero);
                    var quantity = reader.GetInt32(5);
                    var lineTotal = unitPrice * quantity;
                    total += lineTotal;
                    items.Add(new CartItem(reader.GetInt32(0), reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2), unitPrice, quantity, lineTotal));
                }
                catch (OverflowException)
                {
                    throw new ConflictException("Cart total exceeds the supported monetary range");
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new Cart(token, items, total);
    }
}
