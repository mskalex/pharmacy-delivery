# PharmacyDelivery — quick-commerce ordering for pharmacies

[![CI](https://github.com/mskalex/pharmacy-delivery/actions/workflows/ci.yml/badge.svg)](https://github.com/mskalex/pharmacy-delivery/actions/workflows/ci.yml)

A design document and a working implementation of a quick-commerce ordering system for pharmacies. The customer fills a cart,
checks out, learns **which pharmacy** will deliver the order and **watches the courier live on a map**. .NET 10, DDD,
PostgreSQL/PostGIS, Redis, RabbitMQ (MassTransit), SignalR, OpenTelemetry, Docker.

Run it: `docker compose up -d --build`, open <http://localhost:5080> and sign in as `emily.johnson@example.com` / `Demo-Password-2026!`.

## Background

The project started as a take-home assignment for the company soft4you:

> *Prepare a design document for the implementation of the following system: quick-commerce order handling for pharmacies. The
> customer puts together an order for pharmaceutical products and goes through a standard shopping cart. After placing the order, the
> customer learns which pharmacy the delivery will come from and sees the courier's route in real time. You may make assumptions that
> simplify the design document.*

The repository contains the **design document** ([DESIGN.md](DESIGN.md)) and a **working implementation** of the whole flow:

1. the customer browses the catalog and fills the cart;
2. checkout selects a pharmacy (PostGIS: radius, opening hours, the whole cart in stock, delivery-time ranking) and reserves the goods
   without any risk of overselling;
3. payment (a simulated PSP with a signed webhook) → the pharmacy accepts and packs the order (or rejects it, and the order moves on to
   the next pharmacy);
4. the system dispatches the nearest available courier;
5. the customer sees the pharmacy, the ETA and **the courier's position on a live map** (SignalR).

The key decisions and their rationale are in DESIGN.md: assumptions (§2), architecture (§4), domain model (§5), concurrency and locking
(§10), asynchronous messaging (§11), security (§15). The demo data is set in New York City (prices in USD).

## Stack

.NET 10 · ASP.NET Core · EF Core 10 + Npgsql · PostgreSQL 17 + PostGIS · Redis 7 (HybridCache, GEO, locks, SignalR backplane) ·
RabbitMQ 4 + MassTransit 8 (outbox/inbox) · MediatR 12 (domain events) · SignalR · FluentValidation · NLog · OpenTelemetry →
Aspire Dashboard · Swagger · NUnit + Moq + Testcontainers + NetArchTest · Docker (multistage, chiseled). Full list with licenses:
[docs/third-party.md](docs/third-party.md).

## Quick start (Docker) — the `Test` stand

Requirements: Docker Desktop / Docker Engine with Compose v2, about 3 GB of free memory for Docker.

```bash
git clone https://github.com/mskalex/pharmacy-delivery.git
cd pharmacy-delivery
```

```bash
docker compose up -d --build        # first build ~3–5 min: the API image is built together with the unit tests
docker compose ps                   # all services "healthy" after ~30–60 s
```

| What | Address |
|---|---|
| Demo page (cart, order, map with the courier) | <http://localhost:5080> |
| Swagger (API v1) | <http://localhost:5080/swagger> |
| Health | <http://localhost:5080/health/ready> |
| Aspire Dashboard (traces, metrics, logs) | <http://localhost:18888> |
| RabbitMQ Management | <http://localhost:15672> (`pharmacy_delivery` / `pharmacy-delivery-test-only`) |
| PostgreSQL | `localhost:5432`, database `pharmacy_delivery`, `pharmacy_delivery` / `pharmacy-delivery-test-only` |
| Redis | `localhost:6379` |

The passwords above are **values for the local test stand only** (defaults in `docker-compose.yml`); each one can be overridden with
an environment variable or an `--env-file` (templates in [env/](env/)).

### Demo accounts

Password of every account: `Demo-Password-2026!`

| Role | Login |
|---|---|
| Customer | `emily.johnson@example.com`, `david.cohen@example.com` |
| Pharmacist (one pharmacy each) | `pharmacist.nyc-mid@pharmacy-delivery.local`, `pharmacist.nyc-soh@pharmacy-delivery.local`, … (`nyc-che`, `bkn-wbg`, `nyc-fid`, `nyc-ues`, `nyc-evi`, `nyc-har`) |
| Courier (used by the simulator) | `courier1@pharmacy-delivery.local` … `courier6@pharmacy-delivery.local` |
| Administrator | `admin@pharmacy-delivery.local` |

### Demo scenario in the browser

1. Open <http://localhost:5080> and sign in as Emily (the form is prefilled).
2. Add products to the cart, pick an address and click "Place order and pay" — you see **the fulfilling pharmacy** and **the ETA**;
   the payment goes through the simulated PSP (signed webhook).
3. "Demo mode" (on by default) signs in as the pharmacist of that pharmacy, then accepts and packs the order. Without it, do the
   same in Swagger as the pharmacist (`POST /api/v1/pharmacy/orders/{id}/accept`, then `/ready`).
4. The simulator takes the delivery as a courier, drives to the pharmacy and on to the customer — **the courier marker moves on the
   map**, the ETA updates and the status ends with "delivered".

The same from a terminal (no browser): `python scripts/demo-order.py` — the script runs the whole flow through the API and prints
the statuses and courier positions (`--drive-courier` — the script drives the courier itself when the simulator is off).

### What the courier simulator is

`PharmacyDelivery.CourierSimulator` is a separate service (container `courier-simulator`) that **plays the couriers' mobile app**. It
signs in as the 6 seeded couriers and uses only the public API, like the real app would. Idle couriers report a GPS position every
20 s. When a courier gets a delivery, it drives in a straight line at ~90 km/h to the pharmacy, confirms the pickup, drives to the
customer and confirms the delivery. It exists only so that real-time tracking can be seen without real couriers; Staging and
Production do not run it.

### Managing the stand

```bash
docker compose logs -f api                                         # API logs (JSON, NLog)
SEED_MODE=Reset docker compose up -d api                           # delete all data and load the seed again at startup
SEED_PROFILE=large SEED_MODE=Reset docker compose up -d api        # large data set (index measurements; takes a few minutes)
docker compose down                                                # stop (data stays in the volumes)
docker compose down -v                                             # stop and delete the data
```

Reset without a restart: `POST /api/v1/admin/maintenance/seed/reset` with the body `{"profile": "demo"}` as the administrator
(Development and Test only).

David (`david.cohen@example.com`) has an order "on the way" in the seed data — right after signing in, the courier is on the map.

## Running locally (`dotnet run`, Development environment)

Requirements: .NET SDK 10.0.201+ (`global.json`), Docker for the infrastructure.

```bash
docker compose up -d postgres redis rabbitmq aspire-dashboard      # infrastructure only (the API from compose would take port 5080)
dotnet run --project src/PharmacyDelivery.Api                      # http://localhost:5080, Swagger opens by itself
dotnet run --project src/PharmacyDelivery.CourierSimulator         # in a second terminal — couriers on the map
```

Development uses the same infrastructure and the same passwords as compose. Migrations and the `demo` seed run at startup.
Another environment locally: `ASPNETCORE_ENVIRONMENT=Test dotnet run --project src/PharmacyDelivery.Api` (`--environment` is not
honored).

## Tests

One test project `src/PharmacyDelivery.Tests` (solution `src/PharmacyDelivery.Tests.slnx`):

```bash
dotnet test src/PharmacyDelivery.Tests.slnx --filter "TestCategory!=Integration"   # unit + architecture + documentation, no Docker, ~10 s
dotnet test src/PharmacyDelivery.Tests.slnx                                        # everything incl. integration (Testcontainers), ~2–3 min
```

- Integration tests start their own PostgreSQL/PostGIS, Redis and RabbitMQ containers. They need a running Docker and ~2 GB of
  memory; with little Docker memory, stop the stand first (`docker compose stop`).
- Test data: seed templates in [src/PharmacyDelivery.Tests/Seed/](src/PharmacyDelivery.Tests/Seed/) (`minimal`, `concurrency`,
  `reassign`, `dispatch`), selected with the `[UseSeed(TestSeedProfiles.Minimal)]` attribute.
- Base class [TestBase](src/PharmacyDelivery.Tests/TestBase.cs): every test gets its own DI container, settings
  (`Settings["Section:Key"]`) bound to the options classes, automatic mocks of dependencies (`MockOf<T>()`), a fixed clock and
  captured logs; the service under test — `Service<T>()`.
- Test plan with `7.M.K.S` numbering and a requirements matrix: [docs/test-plan.md](docs/test-plan.md); every automated test has a
  `[Property("TestCase", "TC-…")]` attribute, and a test checks that the plan matches the code.

**Test report from the image build.** Building the API image runs the unit tests and writes `junit.xml`, `tests.trx` and
`coverage.cobertura.xml`. When a test fails, no image is produced, but the report is still written:

```bash
docker build -f src/PharmacyDelivery.Api/Dockerfile --target test-results --output type=local,dest=test-results .
```

**CI.** Two equivalent pipelines:

- **GitHub Actions** ([.github/workflows/ci.yml](.github/workflows/ci.yml)) — on every push and PR: build and unit tests, integration
  tests (Testcontainers on the runner's Docker), images with the tests inside, code style (ReSharper). The test and coverage summary is
  on the run page (job summary), JUnit/TRX/Cobertura reports in the artifacts; a `v*` tag publishes the images to GitHub Container
  Registry. GitHub Actions is free for public repositories.
- **GitLab CI** ([.gitlab-ci.yml](.gitlab-ci.yml)) — the report goes to the MR test tab (`artifacts:reports:junit`) and coverage to the
  diff (Cobertura).

## Repository layout

```text
DESIGN.md                    design document: assumptions, architecture, Mermaid diagrams, decisions
README.md                    this file
CHANGELOG.md                 change history (Keep a Changelog, versions from git tags)
CLAUDE.md, AGENTS.md         instructions for AI agents (project map, commands, rules)
.claude/skills/              project skills: run-local, demo-order, run-tests, add-use-case, add-migration, add-integration-event, review-change, format-code
docker-compose.yml           Test stand: postgres, redis, rabbitmq, aspire-dashboard, jwt-keygen, api, courier-simulator
.github/workflows/ci.yml     GitHub Actions CI: build, unit/integration tests, images with tests, code style, publishing to GHCR
.gitlab-ci.yml               GitLab CI: tests in the image build + reports, integration tests (dind), image publishing
docs/
  configuration.md           every setting: type, default, environment variable, per-environment values
  database.md                database schema (tables, columns, constraints), indexes, EXPLAIN measurements, monitoring
  test-plan.md               test plan (generated: scripts/generate-test-plan.py)
  third-party.md             libraries, images and licenses
  uml.md                     UML diagrams (use cases, components, classes, sequences, states, deployment, ER)
  ai/                        docs for AI agents: architecture, domain, conventions, concurrency, messaging, testing, runbook
deploy/postgres/init/        PostgreSQL extensions (postgis, pg_trgm, pg_stat_statements)
scripts/                     demo-order.py (E2E scenario), generate-test-plan.py, generate-db-schema.py, junit-summary.py
src/
  PharmacyDelivery.slnx                product solution
  PharmacyDelivery.Tests.slnx          test solution
  PharmacyDelivery.Common/             shared code without dependencies: error codes, exceptions, JSON, HMAC, clock, geo
  PharmacyDelivery.Domain/             DDD: aggregates (Order, Pharmacy, StockItem, Courier, Delivery, User…), events, state machine, policies
  PharmacyDelivery.Repository/         everything about the database: DbContext, EF configurations, migrations, repositories, NoTracking queries, audit
  PharmacyDelivery.Services/           service layer: use cases and ports (checkout, payments, pharmacy, dispatch, tracking, auth)
  PharmacyDelivery.Messaging/          integration event contracts (V1) and bus settings
  PharmacyDelivery.Infrastructure/     adapters: Redis, MassTransit (consumers), PSP, ETA, JWT, background jobs, seed (Seed/Data/*.json)
  PharmacyDelivery.Api/                host: controllers, SignalR hub, DTOs, validation, errors, audit, DI (DependencyInjection.cs), demo page (wwwroot),
                                       PharmacyDelivery.Api.http — ready-made requests of the demo scenario (VS/Rider/VS Code)
  PharmacyDelivery.CourierSimulator/   simulator of the couriers' app
  PharmacyDelivery.Tests/              the only test project: Common/ Domain/ Services/ Infrastructure/ Messaging/ Architecture/ Integration/ Seed/ Support/
```

Code conventions: interfaces in `Abstract/`, implementations in `Concrete/`, both split into area subfolders (`Concrete/Seed`,
`Concrete/Jobs`, `Abstract/Tracking`…); models from interface signatures in `Models/`; one type per file; all DI in one file
`src/PharmacyDelivery.Api/DependencyInjection.cs`; ReSharper code style (`.editorconfig` + `*.DotSettings`,
`dotnet jb cleanupcode src/PharmacyDelivery.slnx --profile="PharmacyDelivery: Full Cleanup"`). Every API action, the SignalR hub and
the v1 DTOs carry XML documentation, which Swagger shows.

## Configuration and environments

Environments: `Development` (local), **`Test`** (the compose stand — this is where we work), `Staging`, `Production`. The environment
only selects the file `appsettings.{Environment}.json`; all behavior (seed, Swagger, PSP, error details…) is switched on by settings,
and dangerous combinations (e.g. the fake PSP in production) stop the startup. Every setting is described in
[docs/configuration.md](docs/configuration.md).

## Assumptions and limitations

One city (New York, demo data in Manhattan and Brooklyn), OTC products only (no prescriptions), one pharmacy fulfills an order,
prepayment, an own courier fleet. The PSP, the integration with pharmacy stock systems and routing are stubs behind interfaces. Full
list: DESIGN.md §2.

## License

MIT — [LICENSE](LICENSE).
