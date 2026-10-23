using Microsoft.AspNetCore.Mvc;
using OctoSupply.Api.Models;
using OctoSupply.Api.Repositories;

namespace OctoSupply.Api.Controllers;

/// <summary>Manages anonymous carts using opaque bearer identifiers.</summary>
[ApiController]
[Route("api/carts")]
[ProducesResponseType<CartErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<CartErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<CartErrorResponse>(StatusCodes.Status409Conflict)]
public sealed class CartsController(CartsRepository repository) : ControllerBase
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
}
