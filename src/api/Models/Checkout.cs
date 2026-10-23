namespace OctoSupply.Api.Models;

/// <summary>A request to pay the current cart for an existing branch.</summary>
public sealed record StartCheckoutRequest(int BranchId);

/// <summary>A durable priced attempt; its identifier is also the provider idempotency key.</summary>
public sealed record CheckoutAttempt(
    string CheckoutId, int BranchId, Cart Snapshot, long ExpiresAt,
    string? SessionId, string State, CheckoutReceipt? Receipt);

/// <summary>A confirmed purchase using the original paid prices, independent of later catalog edits.</summary>
public sealed record CheckoutReceipt(
    int OrderId, int BranchId, string OrderDate, string Status,
    IReadOnlyList<CartItem> Items, decimal Total, string Currency);

/// <summary>The current checkout, with a hosted URL only while payment remains open.</summary>
public sealed record CheckoutStatus(string State, string? PaymentUrl, CheckoutReceipt? Order, Cart? Snapshot = null);

/// <summary>The provider fields required for trusted payment reconciliation.</summary>
public sealed record PaymentSession(
    string Id, string CheckoutId, string Status, string PaymentStatus,
    long AmountTotal, string Currency, string? Url);
