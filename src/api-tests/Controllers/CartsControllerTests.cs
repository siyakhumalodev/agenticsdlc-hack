using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OctoSupply.Api.Models;
using Xunit;

namespace OctoSupply.Api.Tests.Controllers;

public sealed class CartsControllerTests : IAsyncLifetime
{
    private readonly CartTestDatabase _database = new();
    private CartApplication _application = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _application = new CartApplication();
        _client = _application.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _application.DisposeAsync();
        await _database.DisposeAsync();
    }

    private async Task<Cart> CreateAsync()
    {
        var response = await _client.PostAsync("/api/carts", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cart = (await response.Content.ReadFromJsonAsync<Cart>())!;
        Assert.Equal($"/api/carts/{cart.CartId}", response.Headers.Location!.AbsolutePath);
        return cart;
    }

    [Fact]
    public async Task GivenCart_WhenHttpLifecycleCompletes_ReturnsExpectedShapesAndPersistsAfterHostRestart()
    {
        var cart = await CreateAsync();
        Assert.Empty(cart.Items);
        Assert.Equal(0m, cart.Total);
        var response = await _client.PostAsJsonAsync($"/api/carts/{cart.CartId}/items",
            new { productId = 1, quantity = 2, unitPrice = 0.01, price = 0.01 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var added = (await response.Content.ReadFromJsonAsync<Cart>())!;
        Assert.Equal(33.98m, added.Total);
        Assert.Equal(16.99m, added.Items[0].UnitPrice);

        response = await _client.PutAsJsonAsync($"/api/carts/{cart.CartId}/items/1", new { quantity = 3 });
        Assert.Equal(50.97m, (await response.Content.ReadFromJsonAsync<Cart>())!.Total);

        _client.Dispose();
        await _application.DisposeAsync();
        _application = new CartApplication();
        _client = _application.CreateClient();
        Assert.Equal(50.97m, (await _client.GetFromJsonAsync<Cart>($"/api/carts/{cart.CartId}"))!.Total);

        var second = await CreateAsync();
        Assert.Empty((await _client.GetFromJsonAsync<Cart>($"/api/carts/{second.CartId}"))!.Items);
        response = await _client.DeleteAsync($"/api/carts/{cart.CartId}/items/1");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        Assert.Equal(0m, (await _client.GetFromJsonAsync<Cart>($"/api/carts/{cart.CartId}"))!.Total);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"productId\":1}")]
    [InlineData("{\"productId\":1,\"quantity\":0}")]
    [InlineData("{\"productId\":1,\"quantity\":-1}")]
    [InlineData("{\"productId\":1,\"quantity\":1.5}")]
    [InlineData("{\"productId\":1,\"quantity\":2147483648}")]
    [InlineData("null")]
    [InlineData("{invalid")]
    public async Task GivenInvalidBody_WhenAdding_ReturnsValidationEnvelope(string json)
    {
        var cart = await CreateAsync();
        var response = await _client.PostAsync($"/api/carts/{cart.CartId}/items",
            new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await response.Content.ReadFromJsonAsync<CartErrorResponse>())!.Error.Code);
    }

    [Fact]
    public async Task GivenInvalidOrMissingResources_WhenRequested_ReturnsConsistentErrors()
    {
        var cart = await CreateAsync();
        foreach (var path in new[] { "/api/carts/bad", $"/api/carts/{cart.CartId}/items/bad" })
        {
            var response = path.EndsWith("items/bad", StringComparison.Ordinal)
                ? await _client.PutAsJsonAsync(path, new { quantity = 1 })
                : await _client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("VALIDATION_ERROR", (await response.Content.ReadFromJsonAsync<CartErrorResponse>())!.Error.Code);
        }

        var missing = await _client.GetAsync($"/api/carts/{new string('a', 64)}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("NOT_FOUND", (await missing.Content.ReadFromJsonAsync<CartErrorResponse>())!.Error.Code);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync($"/api/carts/{cart.CartId}/items", new { productId = 99999, quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PutAsJsonAsync($"/api/carts/{cart.CartId}/items/1", new { quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.DeleteAsync($"/api/carts/{cart.CartId}/items/1")).StatusCode);
    }

    [Fact]
    public async Task GivenMaximumQuantity_WhenAddingAgain_ReturnsConflict()
    {
        var cart = await CreateAsync();
        await _client.PostAsJsonAsync($"/api/carts/{cart.CartId}/items", new { productId = 1, quantity = int.MaxValue });
        var response = await _client.PostAsJsonAsync($"/api/carts/{cart.CartId}/items", new { productId = 1, quantity = 1 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("CONFLICT", (await response.Content.ReadFromJsonAsync<CartErrorResponse>())!.Error.Code);
    }

    [Fact]
    public async Task GivenOpenApi_WhenRequested_DocumentsCartOperations()
    {
        var document = await _client.GetFromJsonAsync<JsonElement>("/api-docs.json");
        var paths = document.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/carts").GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(paths.GetProperty("/api/carts/{cartId}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/carts/{cartId}/items").TryGetProperty("post", out _));
        var item = paths.GetProperty("/api/carts/{cartId}/items/{productId}");
        Assert.True(item.TryGetProperty("put", out _));
        Assert.True(item.GetProperty("delete").GetProperty("responses").TryGetProperty("204", out _));
    }

    private sealed class CartApplication : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "api")));
    }
}
