using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OctoSupply.Api.Models;
using OctoSupply.Api.Utils;

namespace OctoSupply.Api.Services;

/// <summary>Payment operations required by checkout; never accepts raw card details.</summary>
public interface IPaymentProvider
{
    /// <summary>Creates or recovers a hosted session with the attempt's stable idempotency key.</summary>
    Task<PaymentSession> CreateAsync(CheckoutAttempt attempt, CancellationToken ct);
    /// <summary>Retrieves authoritative payment state from the provider.</summary>
    Task<PaymentSession> GetAsync(string sessionId, CancellationToken ct);
    /// <summary>Expires an open session before releasing its cart.</summary>
    Task<PaymentSession> ExpireAsync(string sessionId, CancellationToken ct);
    /// <summary>Verifies the raw webhook and returns a checkout/session reference when relevant.</summary>
    (string CheckoutId, string SessionId)? VerifyWebhook(string payload, string signature);
}

/// <summary>Environment-only payment settings, validated before any payment operation.</summary>
public sealed record PaymentSettings(string SecretKey, string WebhookSecret, string FrontendOrigin)
{
    /// <summary>Fails closed when Stripe or its trusted redirect origin has not been configured.</summary>
    public static PaymentSettings FromEnvironment()
    {
        var provider = Environment.GetEnvironmentVariable("PAYMENT_PROVIDER");
        var key = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
        var webhook = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
        var origin = Environment.GetEnvironmentVariable("PAYMENT_FRONTEND_ORIGIN");
        if (provider != "stripe" || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(webhook) ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new ApiException("Payments are not configured. Contact the shop before checking out.",
                "PAYMENT_NOT_CONFIGURED", StatusCodes.Status503ServiceUnavailable);
        return new PaymentSettings(key, webhook, uri.GetLeftPart(UriPartial.Authority));
    }
}

/// <summary>Stripe-hosted card checkout using its form-encoded REST API and signed webhooks.</summary>
public sealed class StripePaymentProvider(
    HttpClient client, ILogger<StripePaymentProvider> logger) : IPaymentProvider
{
    /// <inheritdoc/>
    public Task<PaymentSession> CreateAsync(CheckoutAttempt attempt, CancellationToken ct)
    {
        var settings = PaymentSettings.FromEnvironment();
        // Stripe may discard idempotency keys after 24h. Never risk re-creating an aged attempt.
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > attempt.ExpiresAt - 3600)
            throw new ConflictException("Checkout needs payment reconciliation. Contact support; do not submit another payment.");
        var fields = new Dictionary<string, string>
        {
            ["mode"] = "payment",
            ["payment_method_types[0]"] = "card",
            ["success_url"] = $"{settings.FrontendOrigin}/checkout?payment=return",
            ["cancel_url"] = $"{settings.FrontendOrigin}/checkout?payment=cancel",
            ["metadata[checkout_id]"] = attempt.CheckoutId,
            ["expires_at"] = attempt.ExpiresAt.ToString(CultureInfo.InvariantCulture),
        };
        for (var index = 0; index < attempt.Snapshot.Items.Count; index++)
        {
            var item = attempt.Snapshot.Items[index];
            var prefix = $"line_items[{index}]";
            fields[$"{prefix}[price_data][currency]"] = "usd";
            fields[$"{prefix}[price_data][product_data][name]"] = item.Name;
            fields[$"{prefix}[price_data][unit_amount]"] = ((long)(item.UnitPrice * 100)).ToString(CultureInfo.InvariantCulture);
            fields[$"{prefix}[quantity]"] = item.Quantity.ToString(CultureInfo.InvariantCulture);
        }
        return SendAsync(HttpMethod.Post, "checkout/sessions", fields, attempt.CheckoutId, ct);
    }

    /// <inheritdoc/>
    public Task<PaymentSession> GetAsync(string sessionId, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"checkout/sessions/{Uri.EscapeDataString(sessionId)}", null, null, ct);

    /// <inheritdoc/>
    public async Task<PaymentSession> ExpireAsync(string sessionId, CancellationToken ct)
    {
        var session = await GetAsync(sessionId, ct);
        if (session.Status != "open") return session;
        try
        {
            return await SendAsync(HttpMethod.Post, $"checkout/sessions/{Uri.EscapeDataString(sessionId)}/expire",
                new Dictionary<string, string>(), null, ct);
        }
        catch (ApiException ex) when (ex.Code == "PAYMENT_PROVIDER_ERROR")
        {
            // Payment may win the expiration race; retrieve rather than releasing the cart.
            return await GetAsync(sessionId, ct);
        }
    }

    /// <inheritdoc/>
    public (string CheckoutId, string SessionId)? VerifyWebhook(string payload, string signature)
    {
        var settings = PaymentSettings.FromEnvironment();
        var parts = signature.Split(',').Select(part => part.Trim().Split('=', 2)).ToArray();
        var timestamp = parts.FirstOrDefault(part => part.Length == 2 && part[0] == "t")?[1];
        if (!long.TryParse(timestamp, out var seconds) ||
            seconds < DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 300 ||
            seconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300)
            throw new ValidationException("Invalid payment webhook signature.");
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.WebhookSecret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        var valid = parts.Where(part => part.Length == 2 && part[0] == "v1")
            .Select(part => part[1])
            .Where(value => value.Length == 64 && value.All(char.IsAsciiHexDigit))
            .Any(value => CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(value)));
        if (!valid) throw new ValidationException("Invalid payment webhook signature.");
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type is not ("checkout.session.completed" or "checkout.session.expired")) return null;
            var session = root.GetProperty("data").GetProperty("object");
            var checkoutId = session.GetProperty("metadata").GetProperty("checkout_id").GetString();
            var sessionId = session.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(checkoutId) || string.IsNullOrWhiteSpace(sessionId))
                throw new ValidationException("Missing payment webhook reference.");
            return (checkoutId, sessionId);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ValidationException("Invalid payment webhook body.");
        }
    }

    private async Task<PaymentSession> SendAsync(
        HttpMethod method, string path, Dictionary<string, string>? fields, string? key, CancellationToken ct)
    {
        var settings = PaymentSettings.FromEnvironment();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SecretKey);
        request.Headers.Add("Stripe-Version", "2025-02-24.acacia");
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (fields is not null) request.Content = new FormUrlEncodedContent(fields);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Payment provider rejected an operation with HTTP {StatusCode}", (int)response.StatusCode);
                throw ProviderError();
            }
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = document.RootElement;
            return new PaymentSession(
                RequiredText(root, "id"),
                RequiredText(root.GetProperty("metadata"), "checkout_id"),
                RequiredText(root, "status"),
                RequiredText(root, "payment_status"),
                root.GetProperty("amount_total").GetInt64(),
                RequiredText(root, "currency"),
                root.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Payment provider operation failed ({FailureType})", ex.GetType().Name);
            throw ProviderError();
        }
    }

    private static string RequiredText(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { Length: > 0 } value
            ? value : throw new JsonException("Missing payment field");

    private static ApiException ProviderError() => new(
        "Payment could not be verified. Your cart is saved. Resume checkout to check payment before trying again.",
        "PAYMENT_PROVIDER_ERROR", StatusCodes.Status502BadGateway);
}
