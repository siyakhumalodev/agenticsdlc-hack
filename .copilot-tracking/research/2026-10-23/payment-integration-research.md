<!-- markdownlint-disable-file -->
# Task Research: payment-integration

| Field | Value |
|---|---|
| Date | 2026-10-23 (user clock) |
| Researcher | RPI Agent |
| Output mode | convergence |

## Executive Summary

Research complete. Use Stripe-hosted card checkout with a durable server-priced snapshot,
idempotent order creation, and signed webhook plus browser-return reconciliation.

## What You May Not Know

The checkout must choose a branch because the existing order schema requires one.
Only the C# API exists in this checkout; the Makefile's four-language description is stale.

## Findings

### The guest cart is already persisted and server-priced

Evidence-backed finding C1: src/api/Repositories/CartsRepository.cs ExecuteAsync serializes
mutations and rounds discounted unit prices before multiplying. Reuse this transaction boundary.
Confidence: high, verified source. Checkout must freeze its snapshot while payment is active.

### Orders require a branch and separate detail rows

Evidence-backed finding C2: src/database/migrations/001_init.sql orders and order_details
require a valid branch and product references. Create the header, details, receipt and cart clear
in one transaction. Confidence: high.

### Hosted checkout avoids raw card handling

Evidence-backed finding W1: https://docs.stripe.com/api/checkout/sessions,
retrieved 2026-10-23 through Context7 official Stripe documentation. Sessions expose
payment_status, amount_total, currency and status; creation and expiration are supported.
Confidence: high. A redirect alone cannot prove payment.

## Recommendation and Alternatives

Select Stripe-hosted Checkout, card payments, USD matching existing dollar cart presentation.
Use environment-only provider, secret, webhook signing key and frontend origin.
Reject an in-app raw-card form (unnecessary sensitive-data handling) and a default simulated
success provider (would not establish a real payment integration).
Persist the checkout before contacting Stripe, retry creation with its stable idempotency key,
and reconcile ambiguous responses instead of permitting a second charge.

## Scope and Questions

Implement the customer's payment story for the existing cart. Audience: maintainers and customers.
Include dedicated checkout, API order details, environment configuration and failure preservation.
Exclude live charges, deployment, unrelated cleanup and remote mutations.

| ID | Question | Status |
|---|---|---|
| Q1 | Where are cart state, checkout and order persistence implemented? | answered C1, C2 |
| Q2 | Which API implementations support the current cart? | answered C3 |
| Q3 | Which provider and recovery design fit existing conventions? | answered W1 |

## Decisions and Feedback

Automatic progression and agent-owned decisions authorized by end-to-end selection.
Provider selection and active C# scope are agent-owned, based on C1-C3 and W1.
No material research decision remains for a user walkthrough.

## Risks and Open Questions

No live credentials or live provider validation are available. Validate against a fake provider
and documented REST contract; disclose that a Stripe sandbox smoke test remains operator setup.
Protect active cart lines and branches/products from deletion while payment is outstanding.
Persist snapshots so later catalog edits cannot alter the paid order.

## Planning Readiness and Next Step

Ready for Plan. Disposition: executed. All three waves complete, agent-owned participation.
Automatic parent continues; no research blocker. Gates: source and provider contracts verified.

## Research Record

### Method and Boundaries

Balanced posture by default; no helpers dispatched. Initial areas: frontend cart, API orders,
database transactions, environment examples, tests and official payment-provider documentation.
Root: .copilot-tracking/research; date source: user clock; primary path:
.copilot-tracking/research/2026-10-23/payment-integration-research.md.
Prior knowledge is unverified until checked against repository files.

### Extension Registry

Selected: repository API/frontend rules, tracking location/conventions, Markdown/writing rules,
licensing posture and untrusted-content boundary. Payment and UI skills will be selected if relevant.
Other domain skills are deferred until concrete technologies are established.

### Research Cycle Log

Cycle 1:
1. Wider: inspected actual API/frontend layout, cart hook, controller, orders and schema.
   Reflection: actual C# folders override stale multi-variant guidance.
2. Deeper: traced immediate transactions, error envelopes, persisted bearer IDs and HTTP tests.
   Reflection: share pricing and require branch selection; use existing test database isolation.
3. Contrarian: considered redirect trust, lost responses, abandoned browser, catalog edits,
   duplicate requests and mutation races. Stripe retrieval and expiration evidence reject
   clearing on redirect or unlocking before the provider confirms expiry.
Synthesis: hosted checkout and persistent attempts cover the story; another cycle is unnecessary.

### Evidence Log

| ID | Source and locator | Claim | Provenance |
|---|---|---|---|
| C1 | src/api/Repositories/CartsRepository.cs ExecuteAsync | Server-owned cart pricing and transaction boundary | Direct source read |
| C2 | src/database/migrations/001_init.sql orders/order_details | Branch and transactional detail requirements | Direct source read |
| C3 | src/api/Program.cs; src/frontend/src/api/useCart.ts; src/frontend/src/App.tsx | Active C# implementation and React Query cart/routes | Direct source and file discovery |
| C4 | src/api-tests/CartTestDatabase.cs; Controllers/CartsControllerTests.cs | Real SQLite and HTTP integration patterns | Direct source read |
| W1 | https://docs.stripe.com/api/checkout/sessions | Create, retrieve and expire hosted payment sessions | Context7 official docs, 2026-10-23 |

Self-check: scope covered, evidence grounded, alternatives challenged, no helpers, no secrets.
