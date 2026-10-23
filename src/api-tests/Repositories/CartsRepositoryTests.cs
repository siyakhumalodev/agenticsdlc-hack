using Microsoft.Data.Sqlite;
using OctoSupply.Api.Models;
using OctoSupply.Api.Repositories;
using OctoSupply.Api.Utils;
using Xunit;

namespace OctoSupply.Api.Tests.Repositories;

public sealed class CartsRepositoryTests : IAsyncLifetime
{
    private readonly CartTestDatabase _database = new();
    private CartsRepository _sut => new(_database.Factory);

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GivenCart_WhenAddingUpdatingAndRemoving_PersistsAccurateSnapshots()
    {
        var cart = await _sut.CreateAsync();
        Assert.Empty(cart.Items);
        Assert.Equal(0m, cart.Total);
        await _sut.AddAsync(cart.CartId, new(1, 2));
        cart = await _sut.AddAsync(cart.CartId, new(1, 1));
        Assert.Single(cart.Items);
        Assert.Equal(3, cart.Items[0].Quantity);
        Assert.Equal(50.97m, cart.Total);

        cart = await _sut.AddAsync(cart.CartId, new(2, 3));
        Assert.Equal(0.51m, cart.Items[1].UnitPrice);
        Assert.Equal(52.50m, cart.Total);
        cart = await _sut.UpdateAsync(cart.CartId, 1, 1);
        Assert.Equal(18.52m, cart.Total);
        await _sut.RemoveAsync(cart.CartId, 1);
        await _sut.RemoveAsync(cart.CartId, 2);
        cart = await _sut.FindAsync(cart.CartId);
        Assert.Empty(cart.Items);
        Assert.Equal(0m, cart.Total);
    }

    [Fact]
    public async Task GivenIndependentCarts_WhenRepositoryRecreated_PreservesIsolationAndPersistence()
    {
        var first = await _sut.CreateAsync();
        var second = await _sut.CreateAsync();
        await _sut.AddAsync(first.CartId, new(3, 2));
        var recreated = new CartsRepository(_database.Factory);
        Assert.Equal(20.20m, (await recreated.FindAsync(first.CartId)).Total);
        Assert.Empty((await recreated.FindAsync(second.CartId)).Items);
    }

    [Fact]
    public async Task GivenConcurrentAdds_WhenRequestsFinish_NoIncrementsAreLost()
    {
        var cart = await _sut.CreateAsync();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => _sut.AddAsync(cart.CartId, new(1, 1)))));
        Assert.Equal(8, (await _sut.FindAsync(cart.CartId)).Items[0].Quantity);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public async Task GivenInvalidItem_WhenAdded_RejectsWithoutChangingCart(int productId, int quantity)
    {
        var cart = await _sut.CreateAsync();
        await Assert.ThrowsAsync<ValidationException>(() => _sut.AddAsync(cart.CartId, new(productId, quantity)));
        Assert.Empty((await _sut.FindAsync(cart.CartId)).Items);
    }

    [Fact]
    public async Task GivenMaximumQuantity_WhenAddedAgain_RejectsOverflowWithoutMutation()
    {
        var cart = await _sut.CreateAsync();
        await _sut.AddAsync(cart.CartId, new(1, int.MaxValue));
        await Assert.ThrowsAsync<ConflictException>(() => _sut.AddAsync(cart.CartId, new(1, 1)));
        Assert.Equal(int.MaxValue, (await _sut.FindAsync(cart.CartId)).Items[0].Quantity);
    }

    [Fact]
    public async Task GivenMissingEntities_WhenMutated_ReturnsNotFound()
    {
        var cart = await _sut.CreateAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.FindAsync(new string('a', 64)));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.AddAsync(cart.CartId, new(99999, 1)));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.UpdateAsync(cart.CartId, 1, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RemoveAsync(cart.CartId, 1));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.FindAsync("bad"));
    }

    [Theory]
    [InlineData("price = -1")]
    [InlineData("price = 1e100")]
    [InlineData("discount = -0.1")]
    [InlineData("discount = 1.1")]
    public async Task GivenInvalidCatalogPricing_WhenAdded_RollsBack(string assignment)
    {
        var cart = await _sut.CreateAsync();
        await _database.ExecuteAsync($"UPDATE products SET {assignment} WHERE product_id = 1;");
        await Assert.ThrowsAsync<ConflictException>(() => _sut.AddAsync(cart.CartId, new(1, 1)));
        Assert.Empty((await _sut.FindAsync(cart.CartId)).Items);
    }

    [Fact]
    public async Task GivenCatalogChange_WhenRead_UsesCurrentPriceAndCascadesDeletedProducts()
    {
        var cart = await _sut.CreateAsync();
        await _sut.AddAsync(cart.CartId, new(3, 2));
        await _database.ExecuteAsync("UPDATE products SET price = 5 WHERE product_id = 3;");
        Assert.Equal(10m, (await _sut.FindAsync(cart.CartId)).Total);
        await _database.ExecuteAsync("DELETE FROM products WHERE product_id = 3;");
        Assert.Empty((await _sut.FindAsync(cart.CartId)).Items);
    }

    [Fact]
    public async Task GivenDirectInvalidWrites_WhenExecutingSql_ConstraintsRejectThem()
    {
        var cart = await _sut.CreateAsync();
        await _sut.AddAsync(cart.CartId, new(1, 1));
        await Assert.ThrowsAsync<SqliteException>(() =>
            _database.ExecuteAsync("UPDATE cart_items SET quantity = 0;"));
        await Assert.ThrowsAsync<SqliteException>(() =>
            _database.ExecuteAsync("UPDATE cart_items SET quantity = 1.5;"));
        await Assert.ThrowsAsync<SqliteException>(() =>
            _database.ExecuteAsync("UPDATE cart_items SET product_id = 99999;"));
    }
}
