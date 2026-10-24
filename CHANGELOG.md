# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions: [SemVer](https://semver.org/) from git tags (MinVer).

## [Unreleased]

### Added

- Startup report in the log: tree of host facts, every configuration value with its source and the environment variables, secrets
  masked (`StartupReport:Enabled`, `StartupReport:IncludeEnvironmentVariables`).
- GitHub Actions workflow: build, unit and integration tests, images with tests inside, code style, GHCR on tags; JUnit job summary.

### Changed

- The project is renamed to PharmacyDelivery (solutions, projects, namespaces, images, database, Redis/queue/metric prefixes, JWT
  issuer/audience). All documentation, UI texts and demo data are in English.
- Demo market moved to New York City: pharmacies, addresses and US ZIP codes, `America/New_York` opening hours, prices in USD, US OTC
  catalog; the named real PSP is Stripe. Demo and test accounts renamed (e.g. `emily.johnson@example.com`, `pharmacist.<code>@…`,
  `courier<n>@…`); the demo password is `Demo-Password-2026!`.
- Every API action, the tracking hub and the v1 DTOs carry XML documentation (shown in Swagger).
- NLog: `Microsoft.*` and `System.*` logs are written to the console only, never to the log file.
- NLog: log files in one folder per day and one file per level (`logs/<yyyy-MM-dd>/<level>.log`), each rotated separately.

### Fixed

- `.gitlab-ci.yml` was not valid YAML (an unquoted `: ` inside a script line).

## [0.1.0] - 2026-10-21

### Added

- Design document (DESIGN.md): assumptions, requirements, architecture, DDD model, order flow and pharmacy selection, order state
  machine, real-time tracking, database and indexes, concurrency, messaging, cache, API, versioning, security, audit, observability,
  environments, tests, deployment, scaling; glossary; Mermaid and UML diagrams (docs/uml.md).
- Ordering flow: catalog with trigram search and HybridCache, cart in Redis, checkout with PostGIS pharmacy selection and row-locked stock
  reservation, idempotent checkout, fake PSP with HMAC-signed webhook and refunds, pharmacy panel (accept / reject / ready) with reassignment
  on reject or timeout, courier dispatch (Redis GEO + distributed lock), live tracking over SignalR with polling fallback, demo page with a map.
- Messaging: MassTransit 8 over RabbitMQ with EF Core outbox/inbox, integration contracts V1, MediatR 12 for domain events.
- Security: own JWT issuer (ES256, JWKS) with optional OIDC, tokens stored as hashes, refresh rotation with reuse detection, immediate
  access-token revocation, NIST password policy, lockout, rate limiting, security headers, PII masking in logs.
- Audit of API actions (`[Audited]`): old → new values per entity property.
- Seed templates chosen by string (`[SeedProfile]`): `demo`, `empty`, `large`; reset at startup or via the admin endpoint; test templates.
- Observability: NLog, OpenTelemetry traces/metrics/logs → Aspire Dashboard; business spans and metrics.
- Courier simulator service.
- Tests: one NUnit project — unit, architecture, documentation consistency, Testcontainers integration (PostgreSQL/PostGIS, Redis,
  RabbitMQ); `TestBase` with per-test DI, settings, auto-mocks; numbered test plan (docs/test-plan.md).
- Docker multistage images (tests run in the build, JUnit/Cobertura reports exported), docker-compose test stand, GitLab CI.
- Documentation: README, configuration reference, database schema and indexes with EXPLAIN measurements, third-party licences, AI docs
  (CLAUDE.md, docs/ai/) and project skills (.claude/skills/).
