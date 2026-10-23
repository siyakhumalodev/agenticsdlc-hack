<!-- markdownlint-disable-file -->
# RPI Changes: Cart payment integration

## Metadata

Task ID: payment-integration. Date: 2026-10-23.
Plan: [.copilot-tracking/plans/2026-10-23/payment-integration-plan.md](../../plans/2026-10-23/payment-integration-plan.md).

## Execution Status

Complete; full-plan scope. P01 and P01-T01/T02/T03 complete. Critique Complete/Pass.

## Execution Summary

Implement durable Stripe checkout, then UI, tests and documentation.

## Completed Work

### P01-T01: Durable payment and atomic confirmed orders

* [src/api/Repositories/CheckoutsRepository.cs](../../../src/api/Repositories/CheckoutsRepository.cs)
  persists frozen attempts, locks cart mutation, and commits order, details, receipt and cart clear together.
* [src/api/Services/StripePaymentProvider.cs](../../../src/api/Services/StripePaymentProvider.cs)
  implements hosted card checkout, stable creation keys, safe provider failures and signed webhook verification.
* [src/api/Services/CheckoutService.cs](../../../src/api/Services/CheckoutService.cs)
  reconciles provider status on return, cancellation and webhook delivery.
* [src/database/migrations/004_add_checkouts.sql](../../../src/database/migrations/004_add_checkouts.sql)
  adds active-attempt uniqueness and protective references.
* Validation: 41 focused XUnit tests passed, including existing cart regressions and new HTTP/provider tests.

### P01-T02: Accessible review, payment recovery and receipt

* [src/frontend/src/components/entity/cart/Checkout.tsx](../../../src/frontend/src/components/entity/cart/Checkout.tsx)
  provides a branch form, secure hosted-payment link, cancellation/recheck actions and durable receipt.
* [src/frontend/src/api/checkout.ts](../../../src/frontend/src/api/checkout.ts)
  uses React Query without automatic write retries or clearing cart identity on failure.
* Cart, router and footer now navigate to checkout. The visual direction retains the existing
  green/neutral palette and uses a supply-manifest summary with monospace quantities and totals.
* Validation: frontend build and scoped lint passed. Six Playwright tests passed using installed
  headless Edge, including keyboard navigation and no overflow at 390/768/1280 px.
* Current browser summaries use the frozen checkout snapshot, not potentially changed catalog prices.

### P01-T03: Acceptance evidence and operational setup

* [src/api-tests/Controllers/CheckoutControllerTests.cs](../../../src/api-tests/Controllers/CheckoutControllerTests.cs)
  covers real HTTP/SQLite conversion, concurrency, rollback/retry, frozen pricing, configuration,
  cart preservation, locks, cancellation races, duplicate webhooks and OpenAPI.
* [src/api-tests/Services/StripePaymentProviderTests.cs](../../../src/api-tests/Services/StripePaymentProviderTests.cs)
  verifies cents, idempotency headers, trusted return URLs, signed webhook integrity/tolerance and safe declines.
* [src/frontend/tests/e2e/checkout.spec.ts](../../../src/frontend/tests/e2e/checkout.spec.ts)
  covers checkout, errors, receipt persistence, canceled/unpaid returns and responsive keyboard interaction.
* [src/docs/payments.md](../../../src/docs/payments.md) and
  [src/api/.env.example](../../../src/api/.env.example) document configuration, recovery and sandbox verification.

## Implementation-Time Plan Updates

Added P01-T02 Guidance pointing at the new checkout endpoint and receipt contracts.
Local implementation choice: fixed 24-hour provider session expiration, with fail-closed
creation recovery after 23 hours, preserving Stripe's minimum expiry and key retention window.
No architecture or acceptance changes.

## Business Alignment

None; no BRD, PRD or ADR cited.

## Validation Record

* `dotnet build` in default Debug: failed because an existing user API process locks the executable.
  Left that process untouched.
* `dotnet test src/api-tests/OctoSupply.Api.Tests.csproj -c Payment --no-restore` with focused checkout,
  provider and cart filter: Passed, 41 tests. Separate configuration avoids the running app's files.
* Pre-existing NuGet advisories reported for `Microsoft.OpenApi` and `SQLitePCLRaw.lib.e_sqlite3`.
* Final focused backend check: Passed, 44 tests (including concurrency and rollback recovery).
* Final `npm run build`: Passed. Scoped ESLint across all changed frontend modules and browser spec: Passed.
* Final Playwright check: Passed, 6 tests, installed headless Edge. Default Chromium was missing;
  its install stalled after download and was stopped. A temporary config selected installed Edge.
* Browser API responses and provider operations are mocked; no Stripe credentials used or charges made.
* Telemetry: default HTTP tracing remains; payment client request loggers removed to avoid recording
  provider session paths. New structured warning logs contain only HTTP status or exception type,
  with no tokens, keys, card data or payloads (telemetry-foundations PII denylist: drop).

## Pre-Review Reconciliation

Plan markers, completed work, scope and validation reconciled. Ready for Review.

## Blockers

None for local implementation. Real Stripe sandbox validation requires operator configuration.

## Remaining Work

None.

## Follow-Up Items

No unrelated work selected.
