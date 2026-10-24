# Conventions (for agents)

## Code style

- ReSharper/Rider conventions, encoded in `.editorconfig` (+ `src/*.slnx.DotSettings` for the cleanup profile). Build has
  `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`.
- File-scoped namespaces, `var` where the type is evident, primary constructors for services, `_camelCase` private fields, `Async` suffix,
  max line length 140.
- **Braces**: a single-line body of `if`/`else`/`for`/`foreach`/`while` has no braces and goes on the next line; a body (or condition) that
  spans several lines gets braces.

  ```csharp
  if (order is null)
      throw new NotFoundException(ErrorCodes.OrderNotFound, "Order not found.");
  ```
- One type per file. Interfaces in `Abstract/<Area>/`, implementations in `Concrete/<Area>/`, models used by interface signatures in
  `Models/`; area subfolders keep the namespace `<Project>.Abstract`/`<Project>.Concrete` (`FolderLayoutTests`).
- XML `<summary>` on every interface and interface member; comments explain *why*. Everything is in English: code, comments, docs, UI texts and demo data.
- Format before finishing: skill `format-code` (`dotnet jb cleanupcode … --profile="PharmacyDelivery: Full Cleanup"`).
  `.DotSettings` files must not contain XML comments (ReSharper then ignores the file and the profile is "not found").
  Unused usings are build errors (IDE0005); the test solution's cleanup profile leaves usings alone (see the skill).

## Errors

| Situation | Throw | HTTP |
|---|---|---|
| request shape (FluentValidation) | automatic | 400 `validation.failed` + `errors` |
| domain invariant | `DomainException(code, msg)` | 422 |
| scenario rule (no pharmacy, empty cart, out of zone) | `BusinessRuleException` | 422 |
| missing or foreign resource | `NotFoundException` (never reveal that a foreign resource exists) | 404 |
| role/ownership | `ForbiddenException` | 403 |
| duplicate / concurrency / lock timeout | `ConflictException` (from `PostgresErrorTranslator`) | 409 |
| DB timeout / unavailable | `DatabaseTimeoutException` / `ServiceUnavailableException` | 504 / 503 + `Retry-After` |

Codes are constants in `PharmacyDelivery.Common/Errors/ErrorCodes.cs` (`area.reason`, e.g. `stock.insufficient`). Responses are RFC 9457
ProblemDetails with `code` and `traceId` (= OpenTelemetry trace id); exception details only when `ErrorHandling:IncludeExceptionDetails`.
Constraint names are explicit in EF configurations; `PostgresErrorTranslator` maps SQLSTATE + constraint → error code.

## Validation

1. DTO shape — FluentValidation validators next to DTOs in `PharmacyDelivery.Api/Contracts/V1/` (limits from options, US ZIP code, coordinates).
2. Invariants — in aggregates.
3. Scenario rules — in services.

## Data access

- Writes: repository (tracking) → aggregate methods → `IUnitOfWork.SaveChangesAsync`; row locks inside `ExecuteInTransactionAsync`.
- Reads: `*Queries` classes, `AsNoTracking()` + `Select` projection into `Repository/Models` read models; compiled queries on hot paths.
  `PlatformTests.ReadServices_DoNotTrackEntities` checks the change tracker stays empty.
- Every query takes the request `CancellationToken`.
- New index → justify it with the query it serves in `docs/database.md`; production index changes: `CREATE INDEX CONCURRENTLY` in a
  migration without transaction.

## JSON

System.Text.Json only, options from `PharmacyDeliveryJsonOptions`: camelCase, enums as camelCase strings, nulls omitted, `decimal` money as
`{ amount, currency }`, `DateTimeOffset` UTC ISO 8601. API input is strict (`UnmappedMemberHandling.Disallow` → 400 on unknown fields);
bus contracts and Redis are tolerant. Domain types never cross the API/bus boundary — DTOs and contracts use primitives.

## Logging and telemetry

- NLog (config in the `NLog` section of appsettings). Structured messages, no string interpolation in log calls.
- Framework categories (`Microsoft.*`, `System.*`) go to the console only, never to the NLog file (rules in appsettings.json,
  pinned by `NLogRulesTests`). Rules are overridden per environment **by index** — keep `appsettings.Development.json` in sync when
  reordering rules.
- Startup report (`StartupReport`): host, every configuration value with its source, environment variables, secrets masked.
  A new secret setting must have a name the masking recognises (`…Password`, `…Secret`, `…Token`, `…SigningKey`, `…Pem`, `ApiKey`…);
  extend the regex in `StartupReport` and `StartupReportTests` otherwise.
- Never log tokens, passwords, e-mails, phone numbers or addresses; `${mask-pii}` is a safety net, not a licence.
- 4xx → `Warning`, 5xx → `Error` with the exception (done by the exception handler).
- Business spans and metrics: `PharmacyDeliveryTelemetry` (`ActivitySource`/`Meter` "PharmacyDelivery"); tags without PII (`order.id`, `pharmacy.id`, `result`).

## API

- URL versioning `/api/v1/…`; compatible changes stay in v1, breaking ones need v2.
- Controllers: thin, inherit `ApiControllerBase`, policies from `Policies`, rate limits from `RateLimits`, `[ProducesResponseType]` for Swagger.
- State-changing actions: `[Audited("area.action")]` (old → new values stored) or `[AuditExempt("reason")]`.
- New settings: Options class property + `appsettings.json` value + row in `docs/configuration.md` (test fails otherwise).

## Third-party packages

Versions only in `Directory.Packages.props`. Before adding a package check its licence (MIT/Apache/BSD; no commercial-licence versions:
MassTransit 9+, MediatR 13+, AutoMapper 15+, FluentAssertions 8+) and add it to `docs/third-party.md`.
