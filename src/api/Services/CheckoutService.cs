using OctoSupply.Api.Models;
using OctoSupply.Api.Repositories;
using OctoSupply.Api.Utils;

namespace OctoSupply.Api.Services;

/// <summary>Coordinates external payments without holding database transactions across network calls.</summary>
public sealed class CheckoutService(
    CheckoutsRepository repository, CartsRepository carts, IPaymentProvider provider)
{
    /// <summary>Starts or resumes the cart's single active payment attempt.</summary>
    public async Task<CheckoutStatus> StartAsync(string token, int branchId, CancellationToken ct)
    {
        PaymentSettings.FromEnvironment();
        var existing = await repository.FindForCartAsync(token, ct);
        if (existing?.State == "active")
        {
            if (existing.BranchId != branchId)
                throw new ConflictException("Cancel the active checkout before changing branch.");
            return await ReconcileAsync(existing, false, ct);
        }
        var attempt = await repository.ReserveAsync(token, branchId, ct);
        return await ReconcileAsync(attempt, false, ct);
    }

    /// <summary>Returns verified state; completed receipts are durable and do not require provider access.</summary>
    public async Task<CheckoutStatus> GetAsync(string token, bool cancel, CancellationToken ct)
    {
        var attempt = await repository.FindForCartAsync(token, ct);
        if (attempt is null)
        {
            await carts.FindAsync(token, ct);
            return new CheckoutStatus("none", null, null);
        }
        if (attempt.State != "active") return new CheckoutStatus(attempt.State, null, attempt.Receipt);
        return await ReconcileAsync(attempt, cancel, ct);
    }

    /// <summary>Reconciles a signed event by retrieving current provider state, not trusting event contents.</summary>
    public async Task HandleWebhookAsync(string payload, string signature, CancellationToken ct)
    {
        var reference = provider.VerifyWebhook(payload, signature);
        if (reference is null) return;
        var attempt = await repository.FindAsync(reference.Value.CheckoutId, ct);
        var session = await provider.GetAsync(reference.Value.SessionId, ct);
        await repository.ApplyAsync(attempt.CheckoutId, session, ct);
    }

    private async Task<CheckoutStatus> ReconcileAsync(CheckoutAttempt attempt, bool cancel, CancellationToken ct)
    {
        var session = attempt.SessionId is null
            ? await provider.CreateAsync(attempt, ct)
            : await provider.GetAsync(attempt.SessionId, ct);
        if (cancel && session.Status == "open")
            session = await provider.ExpireAsync(session.Id, ct);
        var applied = await repository.ApplyAsync(attempt.CheckoutId, session, ct);
        if (cancel && applied.State == "active")
            throw new ConflictException("Payment is still in progress. Your cart is saved; check payment again before editing.");
        if (applied.State == "active" &&
            (!Uri.TryCreate(session.Url, UriKind.Absolute, out var url) ||
             url.Scheme != "https" || url.Host != "checkout.stripe.com"))
            throw new ConflictException("Payment is not complete. Your cart is saved; check payment again.");
        return new CheckoutStatus(applied.State, applied.State == "active" ? session.Url : null,
            applied.Receipt, applied.State == "active" ? applied.Snapshot : null);
    }
}
