using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OctoSupply.Api.Models;
using OctoSupply.Api.Services;
using OctoSupply.Api.Utils;
using Xunit;

namespace OctoSupply.Api.Tests.Services;

public sealed class StripePaymentProviderTests : IDisposable
{
    private readonly Dictionary<string, string?> _previous = new();
    private readonly RecordingHandler _handler = new();
    private readonly HttpClient _client;
    private readonly StripePaymentProvider _sut;

    public StripePaymentProviderTests()
    {
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["PAYMENT_PROVIDER"] = "stripe", ["STRIPE_SECRET_KEY"] = "test-placeholder",
            ["STRIPE_WEBHOOK_SECRET"] = "webhook-test-placeholder", ["PAYMENT_FRONTEND_ORIGIN"] = "http://localhost:5137",
        })
        {
            _previous[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
        _client = new HttpClient(_handler) { BaseAddress = new Uri("https://api.stripe.com/v1/") };
        _sut = new StripePaymentProvider(_client, NullLogger<StripePaymentProvider>.Instance);
    }

    public void Dispose()
    {
        _client.Dispose();
        foreach (var (key, value) in _previous) Environment.SetEnvironmentVariable(key, value);
    }

    private static CheckoutAttempt Attempt() => new("checkout-test", 1,
        new Cart(new string('a', 64), [new CartItem(1, "Test item", null, 16.99m, 2, 33.98m)], 33.98m),
        DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds(), null, "active", null);

    private static string Signature(string body, long timestamp)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes("webhook-test-placeholder"),
            Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    [Fact]
    public async Task GivenSnapshot_WhenCreating_UsesExactCentsTrustedUrlsAndStableIdempotency()
    {
        var attempt = Attempt();
        var result = await _sut.CreateAsync(attempt, default);
        Assert.Equal("cs_test", result.Id);
        Assert.Equal("checkout-test", _handler.IdempotencyKey);
        Assert.Equal("Bearer test-placeholder", _handler.Authorization);
        Assert.Contains("line_items%5B0%5D%5Bprice_data%5D%5Bunit_amount%5D=1699", _handler.Body);
        Assert.Contains("line_items%5B0%5D%5Bquantity%5D=2", _handler.Body);
        Assert.Contains("payment_method_types%5B0%5D=card", _handler.Body);
        Assert.Contains("success_url=http%3A%2F%2Flocalhost%3A5137%2Fcheckout%3Fpayment%3Dreturn", _handler.Body);
        Assert.DoesNotContain(attempt.Snapshot.CartId, _handler.Body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GivenInvalidOrOldSignature_WhenWebhookReceived_RejectsIt(bool old)
    {
        const string body = """{"type":"checkout.session.completed","data":{"object":{"id":"cs_test","metadata":{"checkout_id":"checkout-test"}}}}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (old ? 301 : 0);
        var header = old ? Signature(body, timestamp) : $"t={timestamp},v1={new string('0', 64)}";
        Assert.Throws<ValidationException>(() => _sut.VerifyWebhook(body, header));
    }

    [Fact]
    public void GivenValidSignedWebhook_WhenParsed_ReturnsBoundAttemptAndSession()
    {
        const string body = """{"type":"checkout.session.completed","data":{"object":{"id":"cs_test","metadata":{"checkout_id":"checkout-test"}}}}""";
        var reference = _sut.VerifyWebhook(body, Signature(body, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        Assert.Equal(("checkout-test", "cs_test"), reference!.Value);
        Assert.Throws<ValidationException>(() => _sut.VerifyWebhook(body + " ", Signature(body, DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
    }

    [Fact]
    public async Task GivenAgedUnboundAttempt_WhenResuming_DoesNotCreateAnotherCharge()
    {
        await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateAsync(
            Attempt() with { ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3500 }, default));
        Assert.Null(_handler.Body);
    }

    [Theory]
    [InlineData(402)]
    [InlineData(500)]
    public async Task GivenDeclineOrProviderError_WhenCreating_ReturnsSafeActionableError(int status)
    {
        _handler.Status = (HttpStatusCode)status;
        var error = await Assert.ThrowsAsync<ApiException>(() => _sut.CreateAsync(Attempt(), default));
        Assert.Equal(502, error.StatusCode);
        Assert.Contains("cart is saved", error.Message);
        Assert.DoesNotContain("test-placeholder", error.Message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? Authorization { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Authorization = request.Headers.Authorization?.ToString();
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null;
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "cs_test", metadata = new { checkout_id = "checkout-test" }, status = "open",
                    payment_status = "unpaid", amount_total = 3398, currency = "usd",
                    url = "https://checkout.stripe.com/test",
                }), Encoding.UTF8, "application/json"),
            };
        }
    }
}
