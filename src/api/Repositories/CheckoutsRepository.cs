using System.Text.Json;
using Microsoft.Data.Sqlite;
using OctoSupply.Api.Data;
using OctoSupply.Api.Models;
using OctoSupply.Api.Utils;

namespace OctoSupply.Api.Repositories;

/// <summary>Reserves immutable payment snapshots and atomically fulfills each attempt once.</summary>
public sealed class CheckoutsRepository(SqliteConnectionFactory factory, CartsRepository carts)
{
    /// <summary>Returns the most recent attempt belonging to the bearer cart, or null.</summary>
    public async Task<CheckoutAttempt?> FindForCartAsync(string token, CancellationToken ct)
    {
        CartsRepository.ValidateToken(token);
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ch.* FROM checkouts ch JOIN carts c ON c.cart_id = ch.cart_id
            WHERE c.token = $token ORDER BY ch.rowid DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$token", token);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    /// <summary>Looks up an attempt referenced by a verified provider event.</summary>
    public async Task<CheckoutAttempt> FindAsync(string id, CancellationToken ct)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM checkouts WHERE checkout_id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : throw new NotFoundException("Checkout not found");
    }

    /// <summary>Freezes current prices and branch under the cart's immediate transaction.</summary>
    public async Task<CheckoutAttempt> ReserveAsync(string token, int branchId, CancellationToken ct)
    {
        CheckoutAttempt? attempt = null;
        await carts.WithSnapshotAsync(token, async (connection, transaction, cartId, snapshot) =>
        {
            await using var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = "SELECT * FROM checkouts WHERE cart_id = $cartId AND state = 'active';";
            existing.Parameters.AddWithValue("$cartId", cartId);
            await using (var reader = await existing.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    attempt = Map(reader);
                    if (attempt.BranchId != branchId)
                        throw new ConflictException("Cancel the active checkout before changing branch.");
                    return;
                }
            }

            if (snapshot.Items.Count is < 1 or > 100 || snapshot.Total < 0.50m || snapshot.Total > 999999.99m)
                throw new ValidationException("Checkout requires 1 to 100 products and a total between $0.50 and $999,999.99.");
            await using var branch = connection.CreateCommand();
            branch.Transaction = transaction;
            branch.CommandText = "SELECT COUNT(*) FROM branches WHERE branch_id = $branchId;";
            branch.Parameters.AddWithValue("$branchId", branchId);
            if (branchId < 1 || Convert.ToInt32(await branch.ExecuteScalarAsync(ct)) == 0)
                throw new ValidationException("Choose an existing branch.");

            attempt = new CheckoutAttempt(Guid.NewGuid().ToString("N"), branchId, snapshot,
                DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds(), null, "active", null);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO checkouts (checkout_id, cart_id, branch_id, snapshot, expires_at)
                VALUES ($id, $cartId, $branchId, $snapshot, $expiry);
                INSERT INTO checkout_products (checkout_id, product_id)
                SELECT $id, product_id FROM cart_items WHERE cart_id = $cartId;
                """;
            insert.Parameters.AddWithValue("$id", attempt.CheckoutId);
            insert.Parameters.AddWithValue("$cartId", cartId);
            insert.Parameters.AddWithValue("$branchId", branchId);
            insert.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(snapshot));
            insert.Parameters.AddWithValue("$expiry", attempt.ExpiresAt);
            await insert.ExecuteNonQueryAsync(ct);
        }, ct);
        return attempt ?? throw new DatabaseException("Checkout reservation was not persisted");
    }

    /// <summary>Records the provider result, creating exactly one order when paid.</summary>
    public async Task<CheckoutAttempt> ApplyAsync(string id, PaymentSession session, CancellationToken ct)
    {
        await using var connection = await factory.OpenConnectionAsync();
        using var transaction = connection.BeginTransaction(deferred: false);
        await using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT * FROM checkouts WHERE checkout_id = $id;";
        lookup.Parameters.AddWithValue("$id", id);
        CheckoutAttempt attempt;
        await using (var reader = await lookup.ExecuteReaderAsync(ct))
            attempt = await reader.ReadAsync(ct) ? Map(reader) : throw new NotFoundException("Checkout not found");

        if (session.CheckoutId != id || session.Currency != "usd" ||
            session.AmountTotal != checked((long)(attempt.Snapshot.Total * 100)) ||
            (attempt.SessionId is not null && attempt.SessionId != session.Id))
            throw new ConflictException("Payment verification failed. Your cart has not been cleared. Contact support.");
        if (attempt.State == "completed") return attempt;
        if (attempt.State == "expired")
        {
            if (session.Status == "expired") return attempt;
            throw new ConflictException("This checkout has expired.");
        }

        var state = session.Status == "expired" ? "expired" : "active";
        CheckoutReceipt? receipt = null;
        if (session.Status == "complete" && session.PaymentStatus == "paid")
        {
            var date = DateTimeOffset.UtcNow.ToString("O");
            await using var order = connection.CreateCommand();
            order.Transaction = transaction;
            order.CommandText = """
                INSERT INTO orders (branch_id, order_date, name, description, status)
                VALUES ($branch, $date, 'Online purchase', 'Paid through Stripe Checkout', 'confirmed');
                SELECT last_insert_rowid();
                """;
            order.Parameters.AddWithValue("$branch", attempt.BranchId);
            order.Parameters.AddWithValue("$date", date);
            var orderId = Convert.ToInt32(await order.ExecuteScalarAsync(ct));
            foreach (var item in attempt.Snapshot.Items)
            {
                await using var detail = connection.CreateCommand();
                detail.Transaction = transaction;
                detail.CommandText = """
                    INSERT INTO order_details (order_id, product_id, quantity, unit_price)
                    VALUES ($order, $product, $quantity, $price);
                    """;
                detail.Parameters.AddWithValue("$order", orderId);
                detail.Parameters.AddWithValue("$product", item.ProductId);
                detail.Parameters.AddWithValue("$quantity", item.Quantity);
                detail.Parameters.AddWithValue("$price", item.UnitPrice);
                await detail.ExecuteNonQueryAsync(ct);
            }
            await using var clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM cart_items WHERE cart_id = (SELECT cart_id FROM checkouts WHERE checkout_id = $id);";
            clear.Parameters.AddWithValue("$id", id);
            await clear.ExecuteNonQueryAsync(ct);
            receipt = new CheckoutReceipt(orderId, attempt.BranchId, date, "confirmed",
                attempt.Snapshot.Items, attempt.Snapshot.Total, "usd");
            state = "completed";
        }
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE checkouts SET session_id = $session, state = $state, receipt = $receipt WHERE checkout_id = $id;
            """;
        update.Parameters.AddWithValue("$id", id);
        update.Parameters.AddWithValue("$session", session.Id);
        update.Parameters.AddWithValue("$state", state);
        update.Parameters.AddWithValue("$receipt", receipt is null ? DBNull.Value : JsonSerializer.Serialize(receipt));
        await update.ExecuteNonQueryAsync(ct);
        if (state == "expired")
        {
            await using var release = connection.CreateCommand();
            release.Transaction = transaction;
            release.CommandText = "DELETE FROM checkout_products WHERE checkout_id = $id;";
            release.Parameters.AddWithValue("$id", id);
            await release.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return attempt with { SessionId = session.Id, State = state, Receipt = receipt };
    }

    private static CheckoutAttempt Map(SqliteDataReader reader) => new(
        reader.GetString(reader.GetOrdinal("checkout_id")),
        reader.GetInt32(reader.GetOrdinal("branch_id")),
        JsonSerializer.Deserialize<Cart>(reader.GetString(reader.GetOrdinal("snapshot"))) ??
            throw new DatabaseException("Invalid checkout snapshot"),
        reader.GetInt64(reader.GetOrdinal("expires_at")),
        reader["session_id"] as string,
        reader.GetString(reader.GetOrdinal("state")),
        reader["receipt"] is string receipt ? JsonSerializer.Deserialize<CheckoutReceipt>(receipt) : null);
}
