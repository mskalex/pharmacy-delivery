---
name: add-migration
description: Create, review and apply an EF Core migration in PharmacyDelivery.Repository, then refresh the database documentation. Use when an entity, EF configuration, index or constraint changes.
---

# Add an EF Core migration

1. Change the EF configuration in `src/PharmacyDelivery.Repository/Configurations/`:
   - snake_case names come from the naming convention; give constraints and indexes explicit names (`pk_…`, `fk_…`, `ux_…`, `ix_…`, `ck_…`)
     — `PostgresErrorTranslator` maps constraint names to error codes (add a mapping for a new unique/check constraint);
   - application-generated keys: `ValueGeneratedNever()`;
   - every new index must answer a concrete query — add it to the index table in `docs/database.md`.
2. Generate: `dotnet tool restore` then
   `dotnet ef migrations add <PascalCaseName> --project src/PharmacyDelivery.Repository --output-dir Migrations`
   (design-time factory, no database needed).
3. Review the generated `Up`/`Down`:
   - expand/contract: add first, remove in a later release; no destructive change in the same release as the code that stops using it;
   - big tables in production: `CREATE INDEX CONCURRENTLY` via `migrationBuilder.Sql(..., suppressTransaction: true)`;
   - raw SQL EF cannot express (partitions of `courier_locations`) stays hand-written; keep `Down` symmetric.
4. Apply: start the stand (`run-local`); the API migrates on startup (`Database:ApplyMigrationsOnStartup`, under an advisory lock).
5. Refresh docs: `python scripts/generate-db-schema.py` (tables/columns/constraints + ER) — add a purpose line for a new table in its
   `TABLES` map; update DESIGN.md §9 if the model changed.
6. Run the integration tests (`run-tests` skill, full) — `PlatformTests.KeyQueries_UseTheirIndexes` checks index usage.
