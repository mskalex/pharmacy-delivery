---
name: add-use-case
description: Add a feature to PharmacyDelivery end to end, layer by layer (domain → repository → service → infrastructure → API → DI → tests → docs) following the project rules. Use when implementing a new endpoint, business scenario or port.
---

# Add a use case

Read CLAUDE.md rules first; concurrency-sensitive areas: docs/ai/concurrency.md.

1. **Domain** (`src/PharmacyDelivery.Domain/<Context>/`): behaviour as aggregate methods; invariants throw `DomainException(ErrorCodes.X, …)`;
   state changes raise domain events. New error code → `src/PharmacyDelivery.Common/Errors/ErrorCodes.cs` (`area.reason`).
   Order statuses: change `OrderStateMachine` and DESIGN.md §7 together.
2. **Repository** (`src/PharmacyDelivery.Repository/`): writes — repository interface in `Abstract/<Area>/`, implementation in `Concrete/<Area>/`;
   reads — `*Queries` with `AsNoTracking()` + projection into `Models/`. EF configuration in `Configurations/` with explicit constraint
   names; schema change → `add-migration` skill.
3. **Service** (`src/PharmacyDelivery.Services/`): interface `Abstract/<Area>/I<Name>Service.cs` with XML `<summary>` on the interface and every
   member; implementation `Concrete/<Area>/<Name>Service.cs` (primary constructor, `IClock`, `IOptions<T>`, `ILogger<T>`); result models in
   `Models/` (one record per file). Locks → `IUnitOfWork.ExecuteInTransactionAsync`; no external calls inside it.
4. **Infrastructure** (only for a new port): adapter in `src/PharmacyDelivery.Infrastructure/Concrete/<Area>/`.
5. **API** (`src/PharmacyDelivery.Api/`): controller in its own file deriving `ApiControllerBase`; request/response records, validator and mapping in
   `Contracts/V1/`; policy from `Policies`; `[Audited("area.action")]` or `[AuditExempt("reason")]` on state-changing actions;
   `[ProducesResponseType]` for every status.
6. **DI**: register in `src/PharmacyDelivery.Api/DependencyInjection.cs` in the matching `AddPharmacyDelivery…` method (scoped for services/repositories).
7. **Settings** (if any): Options class property with `const SectionName`, value in `appsettings.json` (+ per-environment files),
   row in `docs/configuration.md`.
8. **Tests**: unit test fixture (own file) on `TestBase` using `Service<T>()` / `MockOf<T>()`; integration test on `IntegrationTestBase`
   with `[UseSeed(...)]` for HTTP behaviour, authorization (401/403/404) and DB effects. Add the cases to `scripts/generate-test-plan.py`,
   regenerate, tag tests with `[Property("TestCase", "TC-…")]`.
9. **Docs**: DESIGN.md (API table §13, flows), docs/uml.md if a flow changed, README if user-visible.
10. Verify: build both solutions, `run-tests` skill (full suite if DB/bus touched), `format-code`, `review-change`.
