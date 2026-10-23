---
title: Cart payments
description: Configure and test Stripe-hosted checkout for the C# API and shared frontend.
---

## Checkout behavior

From the cart, choose **Proceed to checkout**, select an order branch, then choose
**Start secure payment** and **Continue to secure payment**. Enter card details on
Stripe's hosted page. OctoCAT never receives or stores raw card details.

The API snapshots discounted, cent-rounded USD prices and reserves the cart while
payment is active. A verified paid session creates one `confirmed` order, its detail
rows and a durable receipt, then clears the cart in the same SQLite transaction.
Repeat confirmation and duplicate webhooks return the same purchase.

Declines are displayed by Stripe and leave items intact. You can retry on the same
hosted session. **Check payment again** reconciles uncertain responses.
**Cancel payment and edit cart** first expires payment at Stripe and only then
unlocks the unchanged cart. If payment wins that race, the confirmed order is shown.
Returning with a success-looking URL never proves payment.

## Environment configuration

The active backend in this branch is [src/api](../api). Its `.env.example` lists
settings, but the API does not load `.env` files automatically. Set environment
variables in the process that starts the API or in your deployment's secret store.

| Variable | Required value |
|---|---|
| `PAYMENT_PROVIDER` | `stripe`; no simulated-success fallback |
| `STRIPE_SECRET_KEY` | Your Stripe sandbox secret API key for local testing |
| `STRIPE_WEBHOOK_SECRET` | Signing secret for this webhook endpoint or Stripe CLI listener |
| `PAYMENT_FRONTEND_ORIGIN` | Exact frontend origin, e.g. `http://localhost:5137`; HTTPS outside localhost |

No publishable key is needed for this hosted-redirect flow. Never prefix server
secrets with `VITE_`, put them in browser storage, commit them, or log provider
payloads/authorization headers. Missing or unsupported configuration returns
`503 PAYMENT_NOT_CONFIGURED` without attempting payment.

Use `API_CORS_ORIGINS` to specify your frontend's allowed origin. Payment redirects
come only from the configured origin, not a caller-supplied return URL.

For this C# checkout, run from `src/api`:

```powershell
dotnet run
```

Run the frontend from `src/frontend` with `npm run dev`. If your frontend uses
another port, update `PAYMENT_FRONTEND_ORIGIN` to that exact origin.
The existing Makefile still names API directories absent from this branch; the
direct commands above use the active implementation.

## Webhook delivery and sandbox smoke test

1. Set the environment variables using a Stripe sandbox account.
2. Forward Stripe events locally with the Stripe CLI:

   ```powershell
   stripe listen --events checkout.session.completed,checkout.session.expired --forward-to http://localhost:3000/api/payments/webhook
   ```

3. Set `STRIPE_WEBHOOK_SECRET` to the listener's signing secret, then restart the API.
4. Add products worth at least $0.50, choose a branch and start checkout.
5. On Stripe's test page, use test card `4242 4242 4242 4242`, a future expiration
   and any valid test CVC. Verify a confirmed order, exact paid details and an empty cart.
6. Repeat using Stripe's declined test card `4000 0000 0000 0002`. Verify Stripe's
   decline error and that the cart is not cleared. Cancel and verify it becomes editable.
7. Test successful payment with the browser closed: the signed webhook must still
   create the order. Reopening checkout shows its persisted receipt.

Register the same webhook events on a publicly reachable HTTPS endpoint when
deploying. Preserve the SQLite database and keep webhook delivery working even if
the customer never returns to the shop. Only card payments are enabled; delayed
payment methods, tax, shipping and refunds are not implemented.

## API operations

All cart URLs require the opaque guest cart bearer identifier. Do not share or log
it. See the generated OpenAPI at `/api-docs.json` for request/response schemas.

| Method and path | Behavior |
|---|---|
| `POST /api/carts/{cartId}/checkout` | Body `{ "branchId": 1 }`; reserve or resume payment |
| `GET /api/carts/{cartId}/checkout` | Reconcile payment; return state, hosted URL, frozen snapshot or receipt |
| `POST /api/carts/{cartId}/checkout/cancel` | Expire before unlocking, or return the already-paid order |
| `POST /api/payments/webhook` | Verify raw `Stripe-Signature` (five-minute tolerance) and reconcile |

States are `none`, `active`, `expired` and `completed`. A completed response's
`order` contains `orderId`, `branchId`, `orderDate`, `status`, item details, `total`
and `currency`. Provider failures return a safe `502 PAYMENT_PROVIDER_ERROR`.
Validation returns `400`; active-cart edits and payment mismatches return `409`.
Checkout accepts 1–100 distinct products and totals from $0.50 to $999,999.99 USD.

Sessions have a fixed 24-hour expiration. Unknown session-creation outcomes reuse
the persisted attempt's idempotency key, never create a new attempt automatically.
After 23 hours an unbound attempt fails closed, before Stripe can discard the key.
An operator must reconcile it in Stripe before unlocking or creating another payment.
Never delete a pending checkout to bypass this protection.

Checkout references prevent branch deletion and active/paid product deletion.
Expired attempts release product references; paid references and receipts remain
durable. Keep these restrictions in mind when cleaning up test data.

## Automated validation

From the repository root:

```powershell
dotnet test .\src\api-tests\OctoSupply.Api.Tests.csproj --filter "FullyQualifiedName~Checkout|FullyQualifiedName~StripePayment|FullyQualifiedName~Carts"
Set-Location .\src\frontend
npm run build
npx eslint src\api\checkout.ts src\components\entity\cart\Checkout.tsx
```

With a frontend already running, set `PLAYWRIGHT_WEB_SERVER=false` and
`PLAYWRIGHT_BASE_URL` to its origin, then run `npx playwright test checkout.spec.ts`.
The browser suite mocks payment API responses. Backend tests run real SQLite and
HTTP integration flows with an injected fake provider; provider tests verify
Stripe request encoding and signatures. These do not replace the configured
Stripe sandbox smoke test above. No live charges are part of automated tests.

See [Stripe Checkout](https://docs.stripe.com/payments/checkout) and
[webhook configuration](https://docs.stripe.com/webhooks).
