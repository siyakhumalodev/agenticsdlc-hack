# OctoSupply API (C#)

ASP.NET Core (.NET 10) port of the OctoSupply API.

## Run

```bash
dotnet restore
dotnet run
```

Default API URL: `http://localhost:3000`

## Environment

- `PORT` (default: `3000`)
- `DB_FILE` (default: `./data/app.db`)
- `API_CORS_ORIGINS` (comma-separated override; optional)

## Database

On startup, the API applies SQL migrations from:

- `../database/migrations/*.sql`

and seeds from:

- `../database/seed/*.sql`

## OpenAPI

- OpenAPI JSON: `http://localhost:3000/api-docs.json`

## Tests

Run C# tests from the `src/` directory:

```bash
make test-cs
```

## Guest shopping carts

Cart contents persist in SQLite; the browser stores only an opaque cart identifier.
Guest carts belong to the browser profile, not the demo login, and do not synchronize
across devices. Clearing browser storage loses the pointer to the saved cart.
Treat the identifier as a bearer credential. Do not share it, put it in browser
navigation URLs, or record it in application, proxy, or access logs. Framework
request/action logs below Warning are suppressed; apply equivalent path redaction
to deployment access logs.

| Method | Path | Body | Success |
|--------|------|------|---------|
| POST | `/api/carts` | None | `201` with empty cart and `Location` |
| GET | `/api/carts/{cartId}` | None | `200` with cart |
| POST | `/api/carts/{cartId}/items` | `{ "productId": 1, "quantity": 2 }` | `200` with updated cart |
| PUT | `/api/carts/{cartId}/items/{productId}` | `{ "quantity": 3 }` | `200` with updated cart |
| DELETE | `/api/carts/{cartId}/items/{productId}` | None | `204` |

Repeated additions increment one product line; updates replace its quantity.
Quantities must be positive 32-bit integers. Remove a line explicitly rather
than submitting zero. Invalid input returns `400`, missing carts/products/lines
return `404`, and addition overflow or invalid catalog pricing returns `409`.
Errors follow `{ "error": { "code": "...", "message": "..." } }`.

Responses include `cartId`, `items` (`productId`, `name`, `imgName`, `unitPrice`,
`quantity`, `lineTotal`), and `total`. The server uses current catalog prices and
fractional discounts, rounds each discounted unit price to cents (midpoints away
from zero), then multiplies by quantity and sums line totals. Client prices are
never trusted. An empty cart has `items: []` and `total: 0`; no taxes, shipping,
checkout, or reservations are included. Product deletion removes its cart lines.

See [cart.http](./cart.http) for executable requests and `/api-docs.json` for
generated OpenAPI. Do not automatically retry additive requests after an uncertain
network failure; reload the cart first.

The checked-in .NET project can be tested directly from the repository root:

```powershell
dotnet test .\src\api-tests\OctoSupply.Api.Tests.csproj
dotnet build .\src\api\OctoSupply.Api.csproj -c Release
```

Cart tests use isolated temporary SQLite files and run without parallel environment
variable changes. Browser tests require an API running against a disposable database
(they create and remove product fixtures), plus the frontend configured to use it.
Install the configured browser with `npx playwright install chromium` if it is missing.
With both servers responsive, run from `src/frontend`:

```powershell
$env:PLAYWRIGHT_WEB_SERVER = 'false'
$env:PLAYWRIGHT_BASE_URL = 'http://localhost:5137'
$env:CART_TEST_API_URL = 'http://localhost:3000'
npm run test:e2e -- tests/e2e/cart.spec.ts tests/e2e/product-navigation.spec.ts
```
