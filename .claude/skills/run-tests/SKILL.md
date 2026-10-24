---
name: run-tests
description: Build and run the PharmacyDelivery tests (fast unit/architecture/docs set or the full suite with Testcontainers), analyse failures and report results. Use after any code change or when asked to run tests.
---

# Run tests

1. Build both solutions (warnings are errors): `dotnet build src/PharmacyDelivery.slnx` and `dotnet build src/PharmacyDelivery.Tests.slnx`.
   On Windows prefix with `DOTNET_CLI_UI_LANGUAGE=en`.
2. Fast set (always): `dotnet test src/PharmacyDelivery.Tests.slnx --no-build --filter "TestCategory!=Integration"`.
3. Full set when the change touches the database, messaging, Redis, auth or HTTP behaviour:
   - `docker version` must work; if the compose stand runs and Docker memory is small, `docker compose stop` first and start it again after;
   - `dotnet test src/PharmacyDelivery.Tests.slnx --no-build` (~2–3 min; containers start once).
4. Narrow down: `--filter "FullyQualifiedName~<Fixture>"`; show details with `--logger "console;verbosity=normal"`.
5. Typical failures:
   - `DocumentationTests` — a new setting is missing in docs/configuration.md, or a `TC-` id is not in the test plan (regenerate it);
   - `InterfaceDocumentationTests` — XML `<summary>` missing on an interface member;
   - `FolderLayoutTests` — a file directly in `Abstract/`/`Concrete/` or a namespace containing the area folder;
   - `ArchitectureTests` — layer violation, DI outside `DependencyInjection.cs`, state-changing action without `[Audited]`/`[AuditExempt]`;
   - integration timeouts — Docker memory, or an asynchronous step that never happened (check consumer logs in the test output).
6. Report pass/fail counts per set; quote failing test names with the assertion message. Never claim the full suite passed if only the fast
   set ran.
