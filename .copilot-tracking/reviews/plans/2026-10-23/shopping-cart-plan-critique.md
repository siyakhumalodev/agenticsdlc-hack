<!-- markdownlint-disable-file -->
# RPI Plan Critique: Complete the shopping cart

## Metadata

* Task ID: shopping-cart-2026-10-23
* Critique date: 2026-10-23
* Plan: .copilot-tracking/plans/2026-10-23/shopping-cart-plan.md
* Critique execution: Complete
* Critique depth: standard
* Depth provenance: default
* Critique type: initial
* Earlier critique: not applicable

## Inputs and Criterion Boundary

* User requirements: add, update, remove, accurate total, API persistence, type safety, scoped changes, and plan-before-implementation.
* Confirmed decision: use the existing .NET API and anonymous per-browser cart; only the cart identifier is stored locally.
* Evidence considered: src/api/Program.cs; src/api/Models/Product.cs; src/api/Repositories/ProductsRepository.cs; src/api/Controllers/ProductsController.cs; src/api/Utils/ApiExceptions.cs; src/api/Utils/ErrorHandlingMiddleware.cs; src/api/Data/SqliteConnectionFactory.cs; src/api/Data/DatabaseMigrator.cs; src/database/migrations/001_init.sql; src/api-tests/Repositories/SuppliersRepositoryTests.cs; src/api-tests/OctoSupply.Api.Tests.csproj; src/frontend/src/components/entity/product/Products.tsx; src/frontend/src/components/Navigation.tsx; src/frontend/src/App.tsx; src/frontend/src/context/AuthContext.tsx; src/frontend/src/api/config.ts; src/frontend/package.json; src/frontend/playwright.config.ts; src/frontend/tests/e2e/product-navigation.spec.ts; src/Makefile.
* Assessment boundary: P01 through P04, all five tasks, FR-001 through FR-005, NFR-001 through NFR-004, decisions, contracts, dependencies, test ownership, and risks.
* This checks planning credibility, not whether unimplemented code passes tests.

## Lens Assessments

| Lens | Scope | Reasoning effort and source | Status | Candidates |
|------|-------|-----------------------------|--------|------------|
| Coverage | All requirements and task blocks | Host default, direct assessment | direct | No actionable omissions |
| Evidence | Repository patterns, ownership, money and tooling | Host default, direct assessment | direct | No unsupported architectural expansion |
| Executability | Phase order, project commands, fixtures and dependency changes | Host default, direct assessment | direct | Existing stale startup and SQLite connection issues explicitly accommodated |
| Risk | Mutation races, bearer identity, failures and scope | Host default, direct assessment | direct | Accepted guest-ownership limitations recorded below |

## Coverage Assessment

| IDs | Coverage | Evidence or concern |
|-----|----------|---------------------|
| FR-001 | Covered | P01 atomic increments, P02 POST contract, P03 catalog mutation and pending control, P04 repeated-add/concurrency tests |
| FR-002, FR-003 | Covered | Positive-integer replacement and separate removal; missing-line errors and final-line empty-state tests |
| FR-004 | Covered | Server-only decimal arithmetic, fractional discounts, explicit unit cent rounding, current catalog prices and exact-value tests |
| FR-005 | Covered | SQLite migration, opaque cart lookup, refresh/restart/isolation tests; localStorage stores no items |
| NFR-001, NFR-002 | Covered | Parameterized repository, constraints/transactions, DTOs, HTTP binding errors, domain envelope and generated OpenAPI |
| NFR-003 | Covered | Mobile-visible cart navigation, keyboard controls, theme, loading/pending/empty/error/success states |
| NFR-004 | Covered | Three focused test files and optional fixture, no test removals, real backend tests plus existing product regression, actual project validation commands |
| P01 through P04 | Covered | Database precedes endpoints, endpoints precede frontend, lifecycle verification follows integration |

## Verdict

* Verdict: Pass
* Rationale: No evidence-supported implementation blocker or missing success criterion remains. The plan addresses this checkout's actual backend, missing authentication, SQLite connection lifecycle, monetary rounding, failed mutations, and stale Makefile/Playwright startup without broad cleanup.
* Severity counts: Critical 0; High 0; Medium 0; Low 0

## Findings

None.

## Strengths and Residual Risk

* Temporary-file persistence tests and host recreation distinguish actual durable state from mocked or React-only success.
* Guest identifiers are bearer capabilities, not authenticated ownership. Anyone possessing one can access that cart. Cross-device identity, account merging, expiry/cleanup, and recovery after browser storage deletion are outside this feature.
* Simultaneous creation from separate tabs and cross-tab cache synchronization are not guaranteed. Existing-cart increments remain atomic in SQLite; absolute quantity updates have last-successful-write semantics.
* API routes contain the opaque identifier. Application and deployment request logs must not expose full cart tokens; implementation must account for default request-path logging and document deployment log hygiene.
* Current product prices are floating-point data. Decimal cart calculations make the cart contract deterministic but do not constitute a catalog-wide money-storage migration.

## Questions or Blocking Evidence Gaps

None. Proposed item and pricing semantics remain visible for user approval of the overall plan.

## Limitations

* Direct single-context assessment under host delegation guidance, not an independently delegated review.
* No application changes, builds, tests, live server checks, or browser checks were performed during planning.
* Documentation and tooling outside the supplied evidence were not audited.

## Recommended Next Action

* Highest-impact finding: none
* Action owner: planning parent
* Smallest next action: mark planning ready, retain residual risks, present the plan, and wait for implementation approval
* User response required: approval before implementation, not a further planning clarification
