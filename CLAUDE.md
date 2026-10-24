# CLAUDE.md

Guidance for AI agents (Claude Code and others) working in this repository. Human-facing docs: [README.md](README.md),
design document [DESIGN.md](DESIGN.md). Detailed agent docs: [docs/ai/](docs/ai/).

## What this is

Quick-commerce ordering for pharmacies: cart → checkout picks a pharmacy (PostGIS) and reserves stock → payment (fake PSP, signed webhook)
→ pharmacy accepts/packs → nearest courier is dispatched → the customer watches the courier live (SignalR).
.NET 10 modular monolith, DDD, PostgreSQL/PostGIS, Redis, RabbitMQ via MassTransit 8, MediatR 12, NLog, OpenTelemetry.

## Commands

```bash
dotnet build src/PharmacyDelivery.slnx                                              # product (warnings are errors)
dotnet build src/PharmacyDelivery.Tests.slnx                                        # tests
dotnet test src/PharmacyDelivery.Tests.slnx --filter "TestCategory!=Integration"    # fast: unit + architecture + docs (~10 s, no Docker)
dotnet test src/PharmacyDelivery.Tests.slnx                                         # all, incl. Testcontainers integration (~2–3 min, Docker)
docker compose up -d --build                                              # Test stand: http://localhost:5080
dotnet tool restore && dotnet jb cleanupcode src/PharmacyDelivery.slnx --profile="PharmacyDelivery: Full Cleanup" --exclude="**/Migrations/**"
python scripts/generate-test-plan.py                                      # after adding/removing a test case
python scripts/generate-db-schema.py                                      # after a migration (needs compose postgres + api)
```

On Windows set `DOTNET_CLI_UI_LANGUAGE=en` for readable build output. Low Docker memory: `docker compose stop` before integration tests.

## Map

| Project | Owns | May reference |
|---|---|---|
| `PharmacyDelivery.Common` | error codes, exceptions, JSON options, HMAC/hashing, clock, geo, paging | nothing (BCL only) |
| `PharmacyDelivery.Domain` | aggregates, value objects, domain events, `OrderStateMachine`, domain policies | Common |
| `PharmacyDelivery.Repository` | everything DB: DbContext, EF configurations, migrations, repositories, NoTracking queries, audit, error translation | Common, Domain |
| `PharmacyDelivery.Services` | use cases (services) and ports, MediatR handlers, telemetry | Common, Domain, Repository (abstractions), Messaging |
| `PharmacyDelivery.Messaging` | integration event contracts `Contracts/V1`, bus conventions | MassTransit only |
| `PharmacyDelivery.Infrastructure` | adapters: Redis, MassTransit consumers, PSP, ETA, JWT, background jobs, seeders | all above |
| `PharmacyDelivery.Api` | host: controllers, SignalR hub, DTOs + validators, error handling, audit middleware, **all DI** | all |
| `PharmacyDelivery.CourierSimulator` | fake courier app (public API only) | Common |
| `PharmacyDelivery.Tests` | the only test project | all |

Details: [docs/ai/architecture.md](docs/ai/architecture.md).

## Rules (enforced by tests or the build)

1. **Layering** — see the table; checked by `ArchitectureTests`. Controllers never touch `DbContext`/repositories; Services never use EF Core.
2. **DI in one file**: `src/PharmacyDelivery.Api/DependencyInjection.cs` (simulator: its own `DependencyInjection.cs`). No `IServiceCollection` anywhere else.
3. **Folders**: interfaces in `Abstract/<Area>/`, implementations in `Concrete/<Area>/`, models from interface signatures in `Models/`.
   Area subfolders do NOT change the namespace (`<Project>.Abstract` / `<Project>.Concrete`). One type per file — also one test fixture per file.
4. **XML `<summary>` on every interface and every interface member** (`InterfaceDocumentationTests`).
5. **Every state-changing API action** has `[Audited("…")]` or `[AuditExempt("reason")]`; anonymous endpoints are explicit `[AllowAnonymous]`.
6. **Every setting** in `appsettings*.json` / Options classes is documented in `docs/configuration.md` (`DocumentationTests`).
7. **Every automated test** has `[Property("TestCase", "TC-7.M.K")]` and its case exists in `docs/test-plan.md` (generated — edit
   `scripts/generate-test-plan.py`, never renumber; retired cases stay as `retired`).
8. **Style**: ReSharper conventions from `.editorconfig`. A one-line `if`/`foreach` body has no braces and goes on the next line; a multi-line
   body (or condition) gets braces. Warnings are errors.
9. **Reads use NoTracking projections** (`*Queries`); writes go through repositories + `IUnitOfWork`. Transactions with row locks run in
   `IUnitOfWork.ExecuteInTransactionAsync` (retry strategy + `lock_timeout`).
10. **Business rules live in aggregates**; state changes raise domain events → MediatR handlers → integration events in the MassTransit
    outbox (same transaction). Never publish to the bus directly from a controller.
11. **No secrets in the repo** except values marked test-only/dev-only. Logs must not contain tokens or PII (NLog `${mask-pii}`).
12. **Docs change with the code** in the same change: DESIGN.md, docs/*.md, README, this file.

## Where to look first

- Concurrency (stock locks, outbox, jobs): [docs/ai/concurrency.md](docs/ai/concurrency.md) — read before touching checkout, reservations, dispatch.
- Domain and order lifecycle: [docs/ai/domain.md](docs/ai/domain.md).
- Events and consumers: [docs/ai/messaging.md](docs/ai/messaging.md).
- Conventions (errors, validation, logging, JSON): [docs/ai/conventions.md](docs/ai/conventions.md).
- Tests (TestBase, seeds, Testcontainers): [docs/ai/testing.md](docs/ai/testing.md).
- Running, migrations, troubleshooting: [docs/ai/runbook.md](docs/ai/runbook.md).

Project skills in `.claude/skills/`: `run-local`, `demo-order`, `run-tests`, `add-use-case`, `add-migration`, `add-integration-event`,
`review-change`, `format-code`.
