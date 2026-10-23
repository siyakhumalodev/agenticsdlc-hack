using Microsoft.AspNetCore.Mvc;
using OctoSupply.Api.Models;
using OctoSupply.Api.Services;

namespace OctoSupply.Api.Controllers;

/// <summary>Receives authenticated Stripe checkout events, including after the browser closes.</summary>
[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(CheckoutService checkout) : ControllerBase
{
    /// <summary>Verifies the raw Stripe signature and idempotently reconciles payment state.</summary>
    [HttpPost("webhook")]
    [RequestSizeLimit(262144)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<CartErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<CartErrorResponse>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        await checkout.HandleWebhookAsync(payload, Request.Headers["Stripe-Signature"].ToString(), cancellationToken);
        return Ok();
    }
}
