namespace OctoSupply.Api.Models;

/// <summary>A persisted guest cart with current catalog prices and cent-rounded totals.</summary>
public sealed record Cart(string CartId, IReadOnlyList<CartItem> Items, decimal Total);

/// <summary>A cart line priced after discount, rounded to cents before multiplying by quantity.</summary>
public sealed record CartItem(
    int ProductId, string Name, string? ImgName, decimal UnitPrice, int Quantity, decimal LineTotal);

/// <summary>Adds a positive integer quantity to a product's existing cart line.</summary>
public sealed record AddCartItemRequest(int ProductId, int Quantity);

/// <summary>Replaces a cart line's quantity with a positive integer.</summary>
public sealed record UpdateCartItemRequest(int Quantity);

/// <summary>The error body shared by cart validation and domain failures.</summary>
public sealed record CartErrorResponse(CartError Error);

/// <summary>A stable machine-readable code and safe user-facing explanation.</summary>
public sealed record CartError(string Code, string Message);
