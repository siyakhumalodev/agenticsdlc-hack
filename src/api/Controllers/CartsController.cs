using Microsoft.AspNetCore.Mvc;
using OctoSupply.Api.Models;
using OctoSupply.Api.Repositories;
using OctoSupply.Api.Services;

namespace OctoSupply.Api.Controllers;

/// <summary>Manages anonymous carts using opaque bearer identifiers.</summary>
[ApiController]
[Route("api/carts")]
[ProducesResponseType<CartErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<CartErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<CartErrorResponse>(StatusCodes.Status409Conflict)]
public sealed class CartsController(CartsRepository repository, CheckoutService checkout) : ControllerBase
{
    /// <summary>Creates an empty cart; retain its identifier to access it again.</summary>
    [HttpPost]
    [ProducesResponseType<Cart>(StatusCodes.Status201Created)]
    public async Task<ActionResult<Cart>> Create(CancellationToken cancellationToken)
    {
        var cart = await repository.CreateAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { cartId = cart.CartId }, cart);
    }

    /// <summary>Reads the cart with current discounted catalog prices.</summary>
    [HttpGet("{cartId}")]
    [ProducesResponseType<Cart>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Cart>> Get(string cartId, CancellationToken cancellationToken) =>
        Ok(await repository.FindAsync(cartId, cancellationToken));

    /// <summary>Adds a positive quantity, incrementing an existing line atomically.</summary>
    [HttpPost("{cartId}/items")]
    [ProducesResponseType<Cart>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Cart>> Add(
        string cartId, AddCartItemRequest request, CancellationToken cancellationToken) =>
        Ok(await repository.AddAsync(cartId, request, cancellationToken));

    /// <summary>Replaces a line's quantity with a positive integer.</summary>
    [HttpPut("{cartId}/items/{productId}")]
    [ProducesResponseType<Cart>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Cart>> Update(
        string cartId, int productId, UpdateCartItemRequest request, CancellationToken cancellationToken) =>
        Ok(await repository.UpdateAsync(cartId, productId, request.Quantity, cancellationToken));

    /// <summary>Removes a cart line.</summary>
    [HttpDelete("{cartId}/items/{productId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(string cartId, int productId, CancellationToken cancellationToken)
    {
        await repository.RemoveAsync(cartId, productId, cancellationToken);
        return NoContent();
    }

    /// <summary>Starts or resumes Stripe-hosted card payment using current server prices.</summary>
    [HttpPost("{cartId}/checkout")]
    [ProducesResponseType<CheckoutStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType<CartErrorResponse>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<CartErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CheckoutStatus>> StartCheckout(
        string cartId, StartCheckoutRequest request, CancellationToken cancellationToken) =>
        Ok(await checkout.StartAsync(cartId, request.BranchId, cancellationToken));

    /// <summary>Reconciles payment and returns the confirmed order with priced details when paid.</summary>
    [HttpGet("{cartId}/checkout")]
    [ProducesResponseType<CheckoutStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType<CartErrorResponse>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<CheckoutStatus>> GetCheckout(string cartId, CancellationToken cancellationToken) =>
        Ok(await checkout.GetAsync(cartId, false, cancellationToken));

    /// <summary>Expires provider payment before unlocking the unchanged cart; paid sessions return their order.</summary>
    [HttpPost("{cartId}/checkout/cancel")]
    [ProducesResponseType<CheckoutStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType<CartErrorResponse>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<CheckoutStatus>> CancelCheckout(string cartId, CancellationToken cancellationToken) =>
        Ok(await checkout.GetAsync(cartId, true, cancellationToken));
}
