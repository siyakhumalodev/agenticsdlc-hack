using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OctoSupply.Api.Models;
using OctoSupply.Api.Services;
using OctoSupply.Api.Utils;
using Xunit;

namespace OctoSupply.Api.Tests.Controllers;

public sealed class CheckoutControllerTests : IAsyncLifetime
{
    private readonly CartTestDatabase _database = new();
    private readonly FakePaymentProvider _provider = new();
    private readonly Dictionary<string, string?> _previous = new();
    private PaymentApplication _application = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["PAYMENT_PROVIDER"] = "stripe", ["STRIPE_SECRET_KEY"] = "test-placeholder",
            ["STRIPE_WEBHOOK_SECRET"] = "test-placeholder", ["PAYMENT_FRONTEND_ORIGIN"] = "http://localhost:5137",
        })
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
        await _database.InitializeAsync();
        await _database.ExecuteAsync("""
            INSERT INTO headquarters (headquarters_id, name) VALUES (1, 'Test HQ');
            INSERT INTO branches (branch_id, headquarters_id, name) VALUES (1, 1, 'Test branch');
            """);
        _application = new PaymentApplication(_provider);
        _client = _application.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _application.DisposeAsync();
        await _database.DisposeAsync();
        foreach (var (name, value) in _previous) Environment.SetEnvironmentVariable(name, value);
    }

    private async Task<Cart> CreateCartAsync()
    {
        var created = await _client.PostAsync("/api/carts", null);
        var cart = (await created.Content.ReadFromJsonAsync<Cart>())!;
        var added = await _client.PostAsJsonAsync($"/api/carts/{cart.CartId}/items", new { productId = 1, quantity = 2 });
        return (await added.Content.ReadFromJsonAsync<Cart>())!;
    }

    private Task<HttpResponseMessage> StartAsync(Cart cart, int branchId = 1) =>
        _client.PostAsJsonAsync($"/api/carts/{cart.CartId}/checkout", new { branchId, total = 0.01 });

    [Fact]
    public async Task GivenPaidSession_WhenConfirmedTwice_CreatesOneOrderWithFrozenDetailsAndClearsCart()
    {
        var cart = await CreateCartAsync();
        Assert.Equal(HttpStatusCode.OK, (await StartAsync(cart)).StatusCode);
        await _database.ExecuteAsync("UPDATE products SET price = -1 WHERE product_id = 1;");
        var resumed = (await _client.GetFromJsonAsync<CheckoutStatus>($"/api/carts/{cart.CartId}/checkout"))!;
        Assert.Equal(cart.Total, resumed.Snapshot!.Total);
        Assert.Equal(HttpStatusCode.OK, (await StartAsync(cart)).StatusCode);
        _provider.Paid = true;
        var first = (await _client.GetFromJsonAsync<CheckoutStatus>($"/api/carts/{cart.CartId}/checkout"))!;
        var second = (await _client.GetFromJsonAsync<CheckoutStatus>($"/api/carts/{cart.CartId}/checkout"))!;
        Assert.Equal("completed", first.State);
        Assert.Equal(33.98m, first.Order!.Total);
        Assert.Equal("confirmed", first.Order.Status);
        Assert.Equal(16.99m, Assert.Single(first.Order.Items).UnitPrice);
        Assert.Equal(first.Order.OrderId, second.Order!.OrderId);
        var orders = (await _client.GetFromJsonAsync<Order[]>("/api/orders"))!;
        Assert.Single(orders);
        var details = (await _client.GetFromJsonAsync<OrderDetail[]>("/api/order-details"))!;
        Assert.Equal(2, Assert.Single(details).Quantity);
        Assert.Equal(16.99, details[0].UnitPrice);
        Assert.Empty((await _client.GetFromJsonAsync<Cart>($"/api/carts/{cart.CartId}"))!.Items);
        Assert.Equal(1, _provider.Created);
    }

    [Fact]
    public async Task GivenUnpaidOrDeclinedPayment_WhenResumed_CartIsIntactAndWritesAreLockedUntilCancel()
    {
        var cart = await CreateCartAsync();
        await StartAsync(cart);
        var state = (await _client.GetFromJsonAsync<CheckoutStatus>($"/api/carts/{cart.CartId}/checkout"))!;
        Assert.Equal("active", state.State);
        Assert.NotNull(state.PaymentUrl);
        Assert.Equal(cart.Total, (await _client.GetFromJsonAsync<Cart>($"/api/carts/{cart.CartId}"))!.Total);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PutAsJsonAsync($"/api/carts/{cart.CartId}/items/1", new { quantity = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"/api/carts/{cart.CartId}/items/1")).StatusCode);
        var canceled = await _client.PostAsync($"/api/carts/{cart.CartId}/checkout/cancel", null);
        Assert.Equal("expired", (await canceled.Content.ReadFromJsonAsync<CheckoutStatus>())!.State);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PutAsJsonAsync($"/api/carts/{cart.CartId}/items/1", new { quantity = 3 })).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<Order[]>("/api/orders"))!);
    }

    [Fact]
    public async Task GivenProviderFailure_WhenRetried_ReusesPersistedAttemptAndPreservesCart()
    {
        var cart = await CreateCartAsync();
        _provider.Fail = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await StartAsync(cart)).StatusCode);
        Assert.Equal(cart.Total, (await _client.GetFromJsonAsync<Cart>($"/api/carts/{cart.CartId}"))!.Total);
        var original = _provider.Attempt!.CheckoutId;
        _provider.Fail = false;
        Assert.Equal(HttpStatusCode.OK, (await StartAsync(cart)).StatusCode);
        Assert.Equal(original, _provider.Attempt!.CheckoutId);
        Assert.Equal(HttpStatusCode.OK, (await StartAsync(cart)).StatusCode);
        Assert.Equal(2, _provider.Created); // Two calls, same provider key; third uses the persisted session.
    }

    [Fact]
    public async Task GivenMismatchedAmount_WhenProviderClaimsPaid_NoOrderOrCartClearOccurs()
    {
        var cart = await CreateCartAsync();
        await StartAsync(cart);
        _provider.Paid = true;
        _provider.Mismatch = true;
        Assert.Equal(HttpStatusCode.Conflict, (await _client.GetAsync($"/api/carts/{cart.CartId}/checkout")).StatusCode);
        Assert.Equal(cart.Total, (await _client.GetFromJsonAsync<Cart>($"/api/carts/{cart.CartId}"))!.Total);
        Assert.Empty((await _client.GetFromJsonAsync<Order[]>("/api/orders"))!);
    }

    [Fact]
    public async Task GivenPaymentWinsCancellationRace_WhenCancelRequested_ReturnsConfirmedOrder()
    {
        var cart = await CreateCartAsync();
        await StartAsync(cart);
        _provider.PayOnExpire = true;
        var response = await _client.PostAsync($"/api/carts/{cart.CartId}/checkout/cancel", null);
        Assert.Equal("completed", (await response.Content.ReadFromJsonAsync<CheckoutStatus>())!.State);
    }

    [Fact]
    public async Task GivenMissingConfigurationOrInvalidInput_WhenStarting_ReturnsSafeErrorAndNoPayment()
    {
        var cart = await CreateCartAsync();
        Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", null);
        var response = await StartAsync(cart);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("PAYMENT_NOT_CONFIGURED", (await response.Content.ReadFromJsonAsync<CartErrorResponse>())!.Error.Code);
        Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", "test-placeholder");
        Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(cart, 999)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/carts/bad/checkout", new { branchId = 1 })).StatusCode);
        var empty = (await (await _client.PostAsync("/api/carts", null)).Content.ReadFromJsonAsync<Cart>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(empty)).StatusCode);
        Assert.Equal(0, _provider.Created);
    }

    [Fact]
    public async Task GivenWebhook_WhenDeliveredTwice_FulfillsWithoutBrowserAndDoesNotDuplicateOrder()
    {
        var cart = await CreateCartAsync();
        await StartAsync(cart);
        _provider.Paid = true;
        for (var index = 0; index < 2; index++)
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/payments/webhook", new StringContent("{}"))).StatusCode);
        Assert.Single((await _client.GetFromJsonAsync<Order[]>("/api/orders"))!);
        Assert.Empty((await _client.GetFromJsonAsync<Cart>($"/api/carts/{cart.CartId}"))!.Items);
    }

    [Fact]
    public async Task GivenActiveCheckout_WhenReferencedProductDeleted_ForeignKeyPreventsLosingPaidItems()
    {
        var cart = await CreateCartAsync();
        await StartAsync(cart);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            _database.ExecuteAsync("DELETE FROM products WHERE product_id = 1;"));
    }

    [Fact]
    public async Task GivenPaidSession_WhenConcurrentConfirmationsArrive_OnlyOneOrderIsCreated()
    {
        var cart = await CreateCartAsync();
        await StartAsync(cart);
        _provider.Paid = true;
        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => _client.GetFromJsonAsync<CheckoutStatus>($"/api/carts/{cart.CartId}/checkout")));
        Assert.Single(results.Select(result => result!.Order!.OrderId).Distinct());
        Assert.Single((await _client.GetFromJsonAsync<Order[]>("/api/orders"))!);
    }

    [Fact]
    public async Task GivenDetailWriteFailure_WhenPaymentIsReconciled_RollsBackAndCanRecoverOnRetry()
    {
        var cart = await CreateCartAsync();
        await StartAsync(cart);
        await _database.ExecuteAsync("""
            CREATE TRIGGER fail_checkout_detail BEFORE INSERT ON order_details
            BEGIN SELECT RAISE(ABORT, 'Test detail failure'); END;
            """);
        _provider.Paid = true;
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await _client.GetAsync($"/api/carts/{cart.CartId}/checkout")).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<Order[]>("/api/orders"))!);
        Assert.Equal(cart.Total, (await _client.GetFromJsonAsync<Cart>($"/api/carts/{cart.CartId}"))!.Total);
        await _database.ExecuteAsync("DROP TRIGGER fail_checkout_detail;");
        Assert.Equal("completed",
            (await _client.GetFromJsonAsync<CheckoutStatus>($"/api/carts/{cart.CartId}/checkout"))!.State);
        Assert.Single((await _client.GetFromJsonAsync<Order[]>("/api/orders"))!);
    }

    [Fact]
    public async Task GivenCheckoutEndpoints_WhenOpenApiRequested_DocumentsPaymentAndOrderContracts()
    {
        var document = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api-docs.json");
        var paths = document.GetProperty("paths");
        var checkout = paths.GetProperty("/api/carts/{cartId}/checkout");
        Assert.True(checkout.TryGetProperty("get", out _));
        Assert.True(checkout.GetProperty("post").GetProperty("responses").TryGetProperty("503", out _));
        Assert.True(paths.GetProperty("/api/carts/{cartId}/checkout/cancel").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/payments/webhook").TryGetProperty("post", out _));
    }

    private sealed class PaymentApplication(FakePaymentProvider provider) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "api")));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPaymentProvider>();
                services.AddSingleton<IPaymentProvider>(provider);
            });
        }
    }

    private sealed class FakePaymentProvider : IPaymentProvider
    {
        public CheckoutAttempt? Attempt { get; private set; }
        public int Created { get; private set; }
        public bool Paid { get; set; }
        public bool Fail { get; set; }
        public bool Mismatch { get; set; }
        public bool PayOnExpire { get; set; }
        private bool _expired;
        public Task<PaymentSession> CreateAsync(CheckoutAttempt attempt, CancellationToken ct)
        {
            Attempt = attempt;
            Created++;
            if (Fail) throw new ApiException("Payment could not be verified. Your cart is saved.",
                "PAYMENT_PROVIDER_ERROR", 502);
            return GetAsync($"cs_{attempt.CheckoutId}", ct);
        }
        public Task<PaymentSession> GetAsync(string id, CancellationToken ct) => Task.FromResult(new PaymentSession(
            id, Attempt!.CheckoutId, Paid ? "complete" : _expired ? "expired" : "open",
            Paid ? "paid" : "unpaid", (long)(Attempt.Snapshot.Total * 100) + (Mismatch ? 1 : 0), "usd",
            "https://checkout.stripe.com/test"));
        public Task<PaymentSession> ExpireAsync(string id, CancellationToken ct)
        {
            Paid = PayOnExpire;
            _expired = !Paid;
            return GetAsync(id, ct);
        }
        public (string CheckoutId, string SessionId)? VerifyWebhook(string payload, string signature) =>
            (Attempt!.CheckoutId, $"cs_{Attempt.CheckoutId}");
    }
}
