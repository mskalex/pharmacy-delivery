# Configuration

Every application setting is described below. The test `DocumentationTests.EverySetting_IsDocumented_AndEveryOptionsPropertyHasASetting` (`TC-7.18.1`) compares this document with `appsettings*.json` and the Options classes — an undocumented key fails the build.

## Principles

- **Configuration layers** (a later one overrides an earlier one): `appsettings.json` → `appsettings.{Environment}.json` → environment variables → (Development) user secrets.
- **Environment variable** = the key with `__` instead of `:` — e.g. `Database:LockTimeoutMs` → `Database__LockTimeoutMs`.
- **Validation at startup**: every section has an Options class with `DataAnnotations` attributes + `ValidateOnStart()`; on top of that `EnvironmentGuards` blocks dangerous combinations (e.g. the fake PSP in Production). An invalid configuration stops the application right away, with a readable message.
- **The environment only selects the settings file.** There is no `if (env.IsProduction())` outside the composition root — behavior is switched on by settings.
- **Secrets** (marked 🔒) never go into the repository: only environment variables / CI secrets / a volume with the key. The values in `appsettings.Development.json` and the defaults in `docker-compose.yml` are **test-only** and described as such.

The **Environments** column shows overrides: **D** = Development, **T** = Test, **S** = Staging, **P** = Production (no entry = the default value).

## ConnectionStrings

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `ConnectionStrings:Postgres` 🔒 | string | – (required) | D: localhost; T: compose | Connection to PostgreSQL 17 + PostGIS. The application appends `statement_timeout` (see `Database:StatementTimeoutMs`). |
| `ConnectionStrings:Redis` | string | – (required) | D: `localhost:6379`; T: `redis:6379` | Redis 7.2: cart, L2 cache, courier positions (GEO), distributed locks, token denylist, SignalR backplane. |

## Database

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Database:ApplyMigrationsOnStartup` | bool | `true` | | EF Core migrations at startup (under `pg_advisory_lock` — one instance runs them). In production with many nodes this can be turned off and the migrations run in a CD step. |
| `Database:MaxRetryCount` | int 0–20 | `5` | | Number of retries of the Npgsql execution strategy for transient errors (deadlock 40P01, serialization failure 40001, lost connection). |
| `Database:MaxRetryDelaySeconds` | int 1–60 | `5` | | Maximum delay between retries (exponential backoff). |
| `Database:CommandTimeoutSeconds` | int 1–600 | `30` | | Client-side command timeout (Npgsql). |
| `Database:LockTimeoutMs` | int 100–60000 | `2000` | | `SET LOCAL lock_timeout` in transactions with row locks (stock reservation). When exceeded: 409 `stock.busy`. |
| `Database:StatementTimeoutMs` | int 1000–600000 | `30000` | | Session `statement_timeout` (connection parameter). When exceeded: 504 `database.timeout`. The `large` seeder lifts it only for the duration of COPY. |
| `Database:SlowQueryThresholdMs` | int 1–60000 | `500` | | Queries slower than the threshold are logged as `Warning` (interceptor). |
| `Database:EnableSensitiveDataLogging` | bool | `false` | D: `true` | Logging of SQL parameter values. **Development only** (personal data!). |

## Redis

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Redis:KeyPrefix` | string | `pharmacy-delivery:` | | Prefix of all application keys. A data reset deletes only keys with this prefix (never `FLUSHALL`). |

## RabbitMq

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `RabbitMq:Host` | string | `localhost` | T: `rabbitmq` | Broker host (MassTransit 8.x). |
| `RabbitMq:Port` | int | `5672` | | AMQP port. |
| `RabbitMq:VirtualHost` | string | `/` | | Virtual host. |
| `RabbitMq:Username` | string | `pharmacy_delivery` | | User. |
| `RabbitMq:Password` 🔒 | string | – | D: test value | Broker password. |
| `RabbitMq:ManagementUrl` | string | `http://localhost:15672` | T: `http://rabbitmq:15672` | Management API — used only to purge queues on reset (`Seed:PurgeQueuesOnReset`). |

## Auth

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Auth:Mode` | `Local` \| `Oidc` | `Local` | | `Local`: the API issues the tokens (ES256). `Oidc`: an external IdP issues them (Keycloak / Entra ID), the API is only a resource server; the login endpoints are not used then. |
| `Auth:Issuer` | string | `pharmacy-delivery-api` | | `iss` claim of local tokens. |
| `Auth:Audience` | string | `pharmacy-delivery` | | `aud` claim. |
| `Auth:AccessTokenMinutes` | int 1–120 | `15` | | Lifetime of the access token (JWT). Short, because tokens can be revoked immediately anyway (`jti` registry). |
| `Auth:CustomerRefreshTokenDays` | int 1–365 | `14` | | Lifetime of the refresh token of a customer, pharmacist, admin. |
| `Auth:CourierRefreshTokenDays` | int 1–365 | `30` | | Lifetime of a courier's refresh token (mobile app). |
| `Auth:MaxFailedAttempts` | int 1–100 | `5` | | Failed logins before the account is locked. |
| `Auth:LockoutMinutes` | int 1–1440 | `15` | | Account lockout duration. |
| `Auth:TokenRetentionDays` | int 1–365 | `7` | | How many days after expiry sessions and tokens are deleted (job `RetentionCleanup`). |
| `Auth:Password:MinLength` | int 8–128 | `12` | | Minimum password length (Unicode characters after NFKC normalization). |
| `Auth:Password:MaxLength` | int 16–1024 | `128` | | Maximum password length. |
| `Auth:Password:RequireUppercase` | bool | `false` | | Require an uppercase letter (NIST SP 800-63B recommends no composition rules — off by default). |
| `Auth:Password:RequireLowercase` | bool | `false` | | Require a lowercase letter. |
| `Auth:Password:RequireDigit` | bool | `false` | | Require a digit. |
| `Auth:Password:RequireNonAlphanumeric` | bool | `false` | | Require a special character. |
| `Auth:Password:HistoryCount` | int 0–24 | `5` | | How many previous passwords may not be reused. |
| `Auth:Password:MaxAgeDays` | int 0–3650 | `0` | | Forced password change after N days; `0` = off (NIST recommendation). |
| `Auth:Jwt:SigningKeyPem` 🔒 | string (PEM PKCS#8) | – | | ES256 private key (P-256). One of `SigningKeyPem` / `SigningKeyPath` is required in `Local` mode outside Development. |
| `Auth:Jwt:SigningKeyPath` | string | – | T: `/keys/jwt-signing-key.pem` | Path to the PEM file; in compose the `jwt-keygen` container generates the file into the `jwt-keys` volume. |
| `Auth:Jwt:KeyId` | string | `pharmacy-delivery-es256-1` | | `kid` of the key (key rotation, JWKS at `/.well-known/jwks.json`). |
| `Auth:Jwt:AllowEphemeralSigningKey` | bool | `false` | D: `true` | No key → an ephemeral in-memory key (tokens die with the process). Outside Development the startup fails. |
| `Auth:Oidc:Authority` | string | – | | IdP address (required with `Auth:Mode=Oidc`). |
| `Auth:Oidc:Audience` | string | – | | Audience of the IdP tokens; empty = `Auth:Audience`. |
| `Auth:Oidc:RoleClaim` | string | `role` | | Claim with the application role in the IdP tokens. |
| `Auth:Oidc:RequireHttpsMetadata` | bool | `true` | | Require HTTPS for the IdP metadata. |

## Checkout

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Checkout:DeliveryRadiusKm` | double 0.5–50 | `5` | | Maximum distance between the pharmacy and the delivery address (PostGIS `ST_DWithin`). |
| `Checkout:MaxPharmacyAttempts` | int 1–10 | `3` | | How many ranked pharmacies are tried before checkout gives up (e.g. stock bought out concurrently). |
| `Checkout:ReservationTtlMinutes` | int 1–120 | `15` | | Validity of the reservation of an unpaid order; then the `ReservationExpiry` job releases the stock and cancels the order. |
| `Checkout:MaxCartItems` | int 1–100 | `30` | | Maximum number of different products in the cart. |
| `Checkout:MaxQuantityPerItem` | int 1–100 | `10` | | Maximum quantity of one line (DTO and service validation). |
| `Checkout:CartTtlHours` | int 1–720 | `24` | | Cart TTL in Redis (renewed on every change). |
| `Checkout:TimeZone` | string (IANA) | `America/New_York` | | Time zone in which pharmacy opening hours are evaluated. |

## Pharmacy

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Pharmacy:AcceptanceTimeoutMinutes` | int 1–60 | `3` | | Time for the pharmacy to accept the order; after it, the order moves to the next pharmacy. |
| `Pharmacy:MinutesPerQueuedOrder` | double 0–30 | `2` | | Ranking: how many minutes each queued order of the pharmacy "costs". |
| `Pharmacy:BasePreparationMinutes` | double 0–60 | `5` | | Ranking and ETA: base time to put the order together. |

## Dispatch

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Dispatch:SearchRadiusKm` | double 0.5–50 | `7` | | Radius in which couriers are searched around the pharmacy (Redis `GEOSEARCH`). |
| `Dispatch:MaxCandidates` | int 1–100 | `10` | | Maximum number of couriers checked (nearest first). |
| `Dispatch:MaxPositionAgeSeconds` | int 10–3600 | `120` | | A courier with an older position is treated as offline. |
| `Dispatch:CourierLockSeconds` | int 1–120 | `15` | | TTL of the Redis lock on a courier during assignment. |

## Tracking

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Tracking:MaxBatchSize` | int 1–500 | `50` | | Maximum number of GPS points in one request. |
| `Tracking:MaxSpeedKmh` | double 10–300 | `120` | | Points implying a higher speed are dropped as GPS errors. |
| `Tracking:MaxPointAgeMinutes` | int 1–1440 | `10` | | Older points are dropped (the app's buffer after a long network outage). |
| `Tracking:PushIntervalMs` | int 100–60000 | `1000` | | Minimum interval between SignalR pushes for one order (throttling). |
| `Tracking:EtaRecalcSeconds` | int 1–600 | `30` | | How often the ETA is recalculated while tracking. |
| `Tracking:ChannelCapacity` | int 100–1000000 | `10000` | | Capacity of the in-memory queue (`Channel`) between point ingestion and the history writer; an overflow drops the oldest points. |
| `Tracking:HistoryBatchSize` | int 1–10000 | `500` | | Size of a history write batch (binary COPY). |
| `Tracking:HistoryFlushIntervalMs` | int 100–60000 | `1000` | | Maximum time to wait for a batch to fill up. |
| `Tracking:HistoryRetentionDays` | int 1–365 | `30` | | Retention of the GPS history (privacy); older partitions are dropped. |
| `Tracking:PartitionsAheadDays` | int 1–60 | `7` | | How many days ahead `courier_locations` partitions are created. |

## Eta

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Eta:AverageSpeedKmh` | double 5–100 | `25` | | Average city speed (stand-in for a routing API). |
| `Eta:RoadFactor` | double 1–3 | `1.3` | | Multiplier from straight-line distance to road distance. |
| `Eta:MaxConcurrentRequests` | int 1–256 | `8` | | Limit of concurrent routing calls (`SemaphoreSlim`), as with a real rate-limited API. |

## Payments

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Payments:Provider` | `Fake` \| `Stripe` | `Stripe` | D, T: `Fake`; S, P: `Stripe` | PSP adapter. Only `Fake` is implemented in this repository; `Stripe` deliberately stops the startup with a message (the adapter is out of scope — DESIGN.md §2). `Fake` in Production is blocked by `EnvironmentGuards`. |
| `Payments:WebhookSecret` 🔒 | string ≥ 16 | – | D: test value | HMAC-SHA256 secret of the PSP webhook signature. |
| `Payments:SignatureToleranceSeconds` | int 10–3600 | `300` | | Allowed clock difference of the webhook timestamp (replay protection). |
| `Payments:FakeWebhookBaseUrl` | string | `http://localhost:8080` | D: `http://localhost:5080` | Address the fake PSP sends the webhook to (the API itself). |

## Seed

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Seed:Mode` | `None` \| `IfEmpty` \| `Reset` | `None` | D, T, S: `IfEmpty`; P: `None` | `IfEmpty` — fill an empty database; `Reset` — **delete all data** (TRUNCATE + Redis keys with the prefix) and fill it again. With many instances one of them runs the reset (advisory lock + a marker in Redis for 5 min). |
| `Seed:Profile` | string | `demo` | S: `empty` | Data template chosen by name; classes marked `[SeedProfile("…")]`: `empty`, `demo`, `large` (+ test ones). An unknown name stops the startup with a list of the available ones. |
| `Seed:AllowReset` | bool | `false` | D, T: `true` | Without it `Reset` (at startup and through the admin endpoint) is blocked. Cannot be on together with a real PSP. |
| `Seed:PurgeQueuesOnReset` | bool | `false` | | On reset, also purge the RabbitMQ queues with the `pharmacy-delivery` prefix (management API). |
| `Seed:UsersPassword` 🔒 | string | – | D: `Demo-Password-2026!` | Password of all demo accounts; it must pass the password policy (a weak password stops the seed). |
| `Seed:AdminPassword` 🔒 | string | – | | Admin password of the `empty` template; required outside Development when the seed is on. |

## Jobs

Every job has three keys: `Enabled` (bool), `IntervalSeconds` (int 1–86400), `BatchSize` (int 1–100000).

| Key | Default (Enabled / interval / batch) | Description |
|---|---|---|
| `Jobs:ReservationExpiry:Enabled`, `Jobs:ReservationExpiry:IntervalSeconds`, `Jobs:ReservationExpiry:BatchSize` | `true` / `30` / `100` | Releases expired reservations of unpaid orders and cancels the orders. |
| `Jobs:AcceptanceTimeout:Enabled`, `Jobs:AcceptanceTimeout:IntervalSeconds`, `Jobs:AcceptanceTimeout:BatchSize` | `true` / `20` / `50` | Moves on orders the pharmacy did not accept in time. |
| `Jobs:DispatchRetry:Enabled`, `Jobs:DispatchRetry:IntervalSeconds`, `Jobs:DispatchRetry:BatchSize` | `true` / `15` / `50` | Retries courier dispatch for packed orders without a courier. |
| `Jobs:PartitionMaintenance:Enabled`, `Jobs:PartitionMaintenance:IntervalSeconds`, `Jobs:PartitionMaintenance:BatchSize` | `true` / `3600` / `1` | Creates `courier_locations` partitions for future days and drops expired ones. |
| `Jobs:RetentionCleanup:Enabled`, `Jobs:RetentionCleanup:IntervalSeconds`, `Jobs:RetentionCleanup:BatchSize` | `true` / `3600` / `5000` | Deletes expired tokens/sessions and old audit entries. |
| `Jobs:StockSync:Enabled`, `Jobs:StockSync:IntervalSeconds`, `Jobs:StockSync:BatchSize` | `true` / `600` / `4` | Stock synchronization with the pharmacies' systems; `BatchSize` = degree of parallelism (`Parallel.ForEachAsync`). |

## Audit

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Audit:Enabled` | bool | `true` | | Records changes (old → new value) for API actions marked `[Audited]`. |
| `Audit:MaskedProperties` | string[] | `PasswordHash`, `TokenHash` | | Properties whose values never reach the audit (stored as `***`). |
| `Audit:RetentionDays` | int 1–3650 | `365` | | Retention of audit entries. |

## OpenTelemetry

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `OpenTelemetry:Enabled` | bool | `true` | | Tracing and metrics instrumentation. |
| `OpenTelemetry:ServiceName` | string | `pharmacy-delivery-api` | | `service.name` in the OTel resource. |
| `OpenTelemetry:OtlpEndpoint` | string | – | D: `http://localhost:4317`; T: `http://aspire-dashboard:18889` | OTLP collector. Empty = no export (the instrumentation still works, e.g. for tests). |
| `OpenTelemetry:OtlpProtocol` | `grpc` \| `http/protobuf` | `grpc` | | OTLP protocol. |
| `OpenTelemetry:SamplingRatio` | double 0–1 | `0.1` | D, T, S: `1.0`; P: `0.1` | Sampling of new traces `ParentBased(TraceIdRatio)`. |
| `OpenTelemetry:ExportLogs` | bool | `true` | | Export `ILogger` logs through OTLP (NLog stays the main sink). |

## Swagger, Cors, Security, ErrorHandling, Admin

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `Swagger:Enabled` | bool | `false` | D, T, S: `true` | Swagger UI at `/swagger` (a document for every API version). |
| `Cors:AllowedOrigins` | string[] | `[]` | D: `localhost:3000`, `localhost:5173` | Front-end origins; empty = same-origin only. |
| `Security:UseHsts` | bool | `true` | D, T: `false` | HSTS header (TLS terminates at the ingress). |
| `Security:TrustAllForwardedHeaders` | bool | `false` | | Trust `X-Forwarded-For/Proto` from any address. By default loopback only; turning it on without a proxy allows IP spoofing (rate limiting). Used in tests and behind a known ingress. |
| `ErrorHandling:IncludeExceptionDetails` | bool | `false` | D: `true` | Exception type and stack trace in ProblemDetails. **Development only.** |
| `Admin:MaintenanceEndpointsEnabled` | bool | `false` | D, T: `true` | Endpoint `POST /api/v1/admin/maintenance/seed/reset`. |
| `AllowedHosts` | string | `*` | | Host header filter (ASP.NET Core). |

## RateLimiting

Every rule has `PermitLimit` (int) and `WindowSeconds` (int, fixed window). When exceeded: 429 + `Retry-After` + code `request.rate_limited`.

| Key | Default | Partition | Description |
|---|---|---|---|
| `RateLimiting:Global:PermitLimit`, `RateLimiting:Global:WindowSeconds` | `300` / `60` | IP | General limit (without `/health`). |
| `RateLimiting:Auth:PermitLimit`, `RateLimiting:Auth:WindowSeconds` | `10` / `60` | IP | Login, registration, token refresh. |
| `RateLimiting:Checkout:PermitLimit`, `RateLimiting:Checkout:WindowSeconds` | `10` / `60` | user | Placing orders. |
| `RateLimiting:Tracking:PermitLimit`, `RateLimiting:Tracking:WindowSeconds` | `120` / `60` | courier | GPS point ingestion. |

## StartupReport

| Key | Type | Default | Environments | Description |
|---|---|---|---|---|
| `StartupReport:Enabled` | bool | `true` | | Startup report in the log (category `PharmacyDelivery.Startup`, level Information, console + file) as a tree: the host (environment, version, machine, OS, runtime, GC, time zone), **every configuration value of the application sections with its source** (`appsettings.json`, `appsettings.Test.json`, `env`, `memory`, `args` — the last one wins) and the environment variables. Secrets are masked (`***`): keys containing `password`, `secret`, `apikey`, `privatekey`, `credential`, or ending with `SigningKey`/`Pem`/`Token` (`true`/`false` values are always shown); environment variables with `PASSWORD`, `SECRET`, `TOKEN`, `API_KEY`, `PRIVATE_KEY`, `CREDENTIAL` in the name; passwords in connection strings (`Password=`) and credentials in URLs. |
| `StartupReport:IncludeEnvironmentVariables` | bool | `true` | P: `false` | The "Environment variables" branch of the report (all process variables). Off in Production: variables with unusual names could contain unmasked secrets. |

## Logging (NLog)

The NLog configuration lives in the `NLog` section of the `appsettings*.json` files (so it differs per environment), not in a separate `nlog.config`:

- target `console` — JSON (Test/Staging/Production), `textConsole` — plain text (Development, by overriding `NLog:rules`), `file` — **a folder per day and a separate file per level**: `logs/{yyyy-mm-dd}/{level}.log` (`logs/2026-10-21/info.log`, `warn.log`, `error.log`, `fatal.log`; in Development also `debug.log`), each file rotated on its own (10 MB × 10) — the errors of a given day can be reviewed without the noise of the Information level;
- every entry carries `traceId`/`spanId` (OpenTelemetry), version and environment; the message goes through `${mask-pii}` (e-mails, phone numbers, tokens, passwords);
- rules (the order matters; per-environment overrides by index, e.g. `NLog:rules:7:minLevel` in Development):
  - `[0]` `Microsoft.EntityFrameworkCore.Database.Command` — muted (SQL errors are logged by the interceptor without parameter values);
  - `[1]` `Microsoft.AspNetCore.DataProtection.*` — from Error (the API is stateless, JWT);
  - `[2]` `Microsoft.Hosting.Lifetime` — Information, **console only**;
  - `[3–4]` `Microsoft.*` — from Warning, **console only**;
  - `[5–6]` `System.*` (e.g. `System.Net.Http.HttpClient`) — from Warning, **console only**;
  - `[7]` everything else (application code `PharmacyDelivery.*`, including the startup report) — from Information (Development: Debug) to the console and the file.
- **Microsoft/System framework logs never reach the file** — the file holds application logs only; the framework is visible in the console (`docker compose logs`) and in the OTLP export.

The `Logging` section is kept for `ILogger` providers other than NLog (e.g. the OTLP export).

## Courier simulator (`src/PharmacyDelivery.CourierSimulator/appsettings.json`)

| Key | Type | Default | Description |
|---|---|---|---|
| `Simulator:ApiBaseUrl` | string | `http://localhost:5080` | API address (in compose `http://api:8080`). |
| `Simulator:Password` 🔒 | string | – (D: `Demo-Password-2026!`) | Password of the courier accounts (= the API's `Seed:UsersPassword`). |
| `Simulator:IntervalSeconds` | int 1–60 | `2` | Interval between GPS points. |
| `Simulator:SpeedKmh` | double 5–500 | `90` | Courier speed in the demo (deliberately fast). |
| `Simulator:IdleReportSeconds` | int 5–600 | `20` | How often an idle courier reports its position (dispatch ignores old positions). |
| `Simulator:Couriers` | list | 6 accounts `courierN@pharmacy-delivery.local` | E-mail and start position of every simulated courier. |
| `OpenTelemetry:OtlpEndpoint` | string | – | Export of the simulator's request traces. |

## docker-compose variables (`env/*.env.example`)

| Variable | Default (test only) | Description |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Test` | Environment of the API and the simulator. |
| `APP_VERSION` | `0.1.0-local` | Version of the images (tag + `MinVerVersionOverride`); from the git tag in CI. |
| `POSTGRES_PASSWORD` 🔒 | `pharmacy-delivery-test-only` | PostgreSQL password. |
| `RABBITMQ_PASSWORD` 🔒 | `pharmacy-delivery-test-only` | RabbitMQ password. |
| `PSP_WEBHOOK_SECRET` 🔒 | `test-only-webhook-secret-change-me` | → `Payments:WebhookSecret`. |
| `SEED_MODE` / `SEED_PROFILE` | `IfEmpty` / `demo` | → `Seed:Mode` / `Seed:Profile`. |
| `SEED_USERS_PASSWORD` 🔒 | `Demo-Password-2026!` | → `Seed:UsersPassword` and the simulator's password. |
| `API_PORT`, `POSTGRES_PORT`, `REDIS_PORT`, `RABBITMQ_PORT`, `RABBITMQ_MANAGEMENT_PORT`, `ASPIRE_UI_PORT`, `OTLP_GRPC_PORT` | `5080`, `5432`, `6379`, `5672`, `15672`, `18888`, `4317` | Ports on the host (change them on conflicts). |
