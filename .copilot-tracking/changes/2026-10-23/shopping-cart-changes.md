<!-- markdownlint-disable-file -->
# RPI Changes: Complete the shopping cart

## Metadata

* Task ID: shopping-cart-2026-10-23
* Related plan: [.copilot-tracking/plans/2026-10-23/shopping-cart-plan.md](../../plans/2026-10-23/shopping-cart-plan.md)
* Implementation date: 2026-10-23

## Execution Status

* Status: Complete for the full approved plan
* Declared invocation scope: full plan
* Completed scope markers: P01/P01-T01, P02/P02-T01, P03/P03-T01/P03-T02, P04/P04-T01
* All remaining active-plan markers: none

## Execution Summary

Delivered the .NET/SQLite guest cart and typed React cart lifecycle. The API owns persisted quantities, current discounted prices, and cent-rounded totals; the browser stores only the cart identifier. Real HTTP host-restart and production-browser tests verify persistence, exact totals, isolation, failures, and responsive navigation. All cart checks pass; two pre-existing supplier cleanup tests remain failing outside scope.

## Completed Work

### Durable cart behavior

* Related task: P01-T01
* Files: [src/api/Repositories/CartsRepository.cs](../../../src/api/Repositories/CartsRepository.cs), [src/api/Models/Cart.cs](../../../src/api/Models/Cart.cs), [src/database/migrations/003_add_carts.sql](../../../src/database/migrations/003_add_carts.sql), [src/api-tests/Repositories/CartsRepositoryTests.cs](../../../src/api-tests/Repositories/CartsRepositoryTests.cs), [src/api-tests/CartTestDatabase.cs](../../../src/api-tests/CartTestDatabase.cs)
* Behavior: isolated token-addressed carts persist in SQLite; transactional increments, absolute updates, removal, current discounted decimal prices, and constraints prevent invalid writes.
* Validation: 14 focused repository cases passed, including concurrency, rollback, rounding, persistence, isolation, overflow, and deletion.

### Typed HTTP cart contract

* Related task: P02-T01
* Files: [src/api/Controllers/CartsController.cs](../../../src/api/Controllers/CartsController.cs), [src/api/Program.cs](../../../src/api/Program.cs), [src/api-tests/Controllers/CartsControllerTests.cs](../../../src/api-tests/Controllers/CartsControllerTests.cs), [src/api/cart.http](../../../src/api/cart.http)
* Behavior: all five endpoints return typed responses and existing-style errors, new-controller binding failures are normalized, OpenAPI describes the operations, and framework request-path logging is suppressed below Warning to protect tokens.
* Validation: 12 HTTP tests passed, including application recreation with the same database, actual binding failures, error envelopes, status codes, pricing, and OpenAPI. All 26 cart tests passed in the full run.

### Server-backed catalog mutations

* Related task: P03-T01
* Files: [src/frontend/src/api/cart.ts](../../../src/frontend/src/api/cart.ts), [src/frontend/src/api/useCart.ts](../../../src/frontend/src/api/useCart.ts), [src/frontend/src/components/entity/product/Products.tsx](../../../src/frontend/src/components/entity/product/Products.tsx)
* Behavior: React Query shares snapshots by identity; lazy creation and writes serialize; only the identifier is stored locally; failed writes refetch authoritative data without automatic mutation retries.
* Validation: frontend type/production build passed; frontend lint passed after applying required braces.

### Accessible cart view and navigation

* Related task: P03-T02
* Files: [src/frontend/src/components/entity/cart/Cart.tsx](../../../src/frontend/src/components/entity/cart/Cart.tsx), [src/frontend/src/App.tsx](../../../src/frontend/src/App.tsx), [src/frontend/src/components/Navigation.tsx](../../../src/frontend/src/components/Navigation.tsx), [src/frontend/src/components/Footer.tsx](../../../src/frontend/src/components/Footer.tsx)
* Behavior: cart page displays API prices/totals, positive-integer quantity forms, removal, zero-total empty state, loading/errors and explicit unavailable-cart reset. Header and footer cart links work; header controls remain within 320px for guest and demo-login states. Route entry scrolls to/focuses the cart heading; removal restores heading focus.
* Validation: build, lint, real browser keyboard interactions, focus assertions, light/dark screenshots and 320/375/768/1280px layout checks passed.

### Verified end-to-end behavior and usage contract

* Related task: P04-T01
* Files: [src/frontend/tests/e2e/cart.spec.ts](../../../src/frontend/tests/e2e/cart.spec.ts), [src/api/README.md](../../../src/api/README.md), [src/docs/sqlite-integration.md](../../../src/docs/sqlite-integration.md)
* Behavior: documented guest ownership, endpoint/status/error shapes, cent-rounding order, persistence, bearer-token handling, and actual project/test commands.
* Validation: 26/26 cart repository/HTTP cases and 9/9 browser cases passed. Browser cases use the real API/database for lifecycle and totals, scoped transport failure injection for error feedback, and cart-key-specific storage failure injection. Exact total fixture: `19.99 * (1 - 0.15)` rounds to `16.99`; three units plus three `0.51` units total `52.50`.
* Cleanup: temporary test servers stopped, validation-only browser config removed, isolated database and known failed-test temporary databases removed; developer database untouched. Final frontend bundle rebuilt with normal API configuration.

## Implementation-Time Plan Updates

### Complete cart entry points and small-screen header

* Affected tasks: P03-T02, P04-T01
* Evidence: successful mobile screenshot showed Login clipping after adding Cart; existing footer still linked My Cart to `#`.
* Clarification: wire both existing cart entry points and compact only small-screen header spacing/branding. Add 320px document-overflow and Login-bounds assertions, not merely main-content overflow.
* Footer navigation also needed cart-entry scroll/focus restoration. The header is explicitly anchored to the viewport top.
* No scope expansion: both changes directly complete cart navigation and responsive behavior. Phase structure and user direction are unchanged.

### Browser verification environment

* Affected task: P04-T01
* Evidence: pinned Chromium executable was absent; its installer stalled after download and was stopped. System Chrome is installed and supported by Playwright.
* Validation-only adjustment: use a temporary config selecting system Chrome; remove it after tests. No checked-in runner or browser dependency changes.
* Vite development watcher exited with `EBUSY` on the generated Playwright report. Use the production build/preview against the isolated API instead, with `VITE_API_URL` set during the build. This verifies the actual production bundle without changing application behavior.

* Invocation authorizes the full plan; no further approval needed.
* Follow existing direct repository/controller and error-envelope patterns rather than introducing unrelated service layers or changing global error formats.

## Business Alignment

* None; no cited BRD, PRD, or ADR

## Validation Record

| Check | Scope | Status | Evidence or reason |
|-------|-------|--------|--------------------|
| Runtime availability | .NET / Node | Passed | .NET 10.0.402, Node 26.12.1 |
| Focused cart repository tests | P01-T01 | Passed | 14/14; pre-existing NuGet audit warnings for Microsoft.OpenApi 2.0.0 and SQLitePCLRaw.lib.e_sqlite3 2.1.11 |
| Full backend tests | P02/P04 | Failed baseline only | 26 cart cases passed; 2 existing supplier cases fail deleting pooled SQLite files on Windows. Reproduced both failures with supplier-only selector. |
| Focused cart repository and HTTP tests | P01/P02/P04 | Passed | `dotnet test .\src\api-tests\OctoSupply.Api.Tests.csproj --no-restore --filter 'FullyQualifiedName~CartsRepositoryTests\|FullyQualifiedName~CartsControllerTests'`: 26/26 |
| Release API build | P02/P04 | Passed with existing audit warnings | `dotnet build .\src\api\OctoSupply.Api.csproj -c Release --no-restore`: 0 errors, 2 pre-existing NuGet warnings |
| Frontend build and lint | P03/P04 | Passed | Final `npm run build` and `npm run lint` both succeeded |
| Browser lifecycle and product discovery | Real API and temporary SQLite | Passed | 9/9 in installed Chrome against production preview; temporary channel override only |
| Default pinned-browser run | Browser tooling | Unavailable | Missing pinned executable; install stalled and was stopped. Equivalent supported installed Chrome used successfully. |
| Visual and keyboard checks | Cart route | Passed | Light/dark screenshots; 320/375/768/1280px; tab/Enter quantity update, route/removal heading focus, header/footer entry points |
| Whitespace check | Tracked source | Passed | `git diff --check` |
| Validation cleanup | Owned processes/files | Passed | No listeners on 5300/5317 after stopping test servers; temporary config/database files removed |

## Pre-Review Reconciliation

* Plan markers and task context: current; all five tasks and all four phases complete
* Completed-work entries and handoff prose: reconciled with actual implemented behavior
* Validation, blockers, remaining work, and follow-up items: current; baseline failures explicitly distinguished from cart checks
* Review readiness: ready, with documented unrelated baseline test failures and dependency warnings

## Blockers

* None

## Remaining Work

* None in the approved plan

## Follow-Up Items

* Broader stale Makefile and multi-language documentation repair is outside cart scope; repository maintainers own follow-up.
* Existing supplier-test SQLite cleanup and existing NuGet audit warnings are outside cart scope; repository maintainers own follow-up.
* Existing theme initialization fails if all storage access is denied. Cart-key-specific read/write failure tests isolate the new behavior without changing theme code; global theme storage resilience is a maintainer follow-up.

## Return-to-Caller State

* Implementation execution: Complete for the full plan; all phase/task markers complete
* Validation: 26 cart backend tests and 9 browser tests passed; Release API build, frontend build/lint, and whitespace checks passed
* Blockers: none
* Plan updates: scoped cart footer/mobile/focus clarification and validation-only system-Chrome/production-preview adjustment
* Business alignment: none; no cited BRD/PRD/ADR requirements
* Planning/critique: approved scope preserved; initial direct critique remains Pass; no follow-up critique run
* Follow-ups: unrelated supplier cleanup, dependency audit warnings, stale Makefile/docs, and global theme storage resilience
* Review readiness: ready; standalone user may invoke `/rpi-review`
