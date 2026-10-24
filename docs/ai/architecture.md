# Architecture (for agents)

Modular monolith: one API process + background jobs + bus consumers, one PostgreSQL database. Decision records: DESIGN.md §4.

## Request path

```
HTTP → Controller (PharmacyDelivery.Api/Controllers, thin: DTO → service → DTO)
     → FluentValidation (endpoint filter, 400 ValidationProblemDetails)
     → Service (PharmacyDelivery.Services/Concrete/<Area>)        ← business scenario, transaction boundary
         → Repository / *Queries (PharmacyDelivery.Repository)     ← EF Core; reads are NoTracking projections
         → Domain aggregate methods (PharmacyDelivery.Domain)      ← invariants, raise domain events
         → IUnitOfWork.SaveChangesAsync / ExecuteInTransactionAsync
              → domain events → MediatR handlers (PharmacyDelivery.Services/Concrete/Events)
              → integration events → MassTransit EF outbox (same transaction)
     → exceptions → ProblemDetails (PharmacyDelivery.Api/Infrastructure/ErrorHandling.cs)
     → [Audited] → AuditMiddleware + AuditSaveChangesInterceptor → audit_entries/audit_changes (old → new)
```

Asynchronous path: outbox → RabbitMQ → consumer (`PharmacyDelivery.Infrastructure/Concrete/Messaging`, thin: calls a service) → inbox dedup.
Real time: `TrackingService` → Redis (last position, GEO) → `IOrderNotifier` → SignalR hub `/hubs/v1/tracking` (Redis backplane).

## Where things are

| Need | Location |
|---|---|
| New endpoint | `PharmacyDelivery.Api/Controllers/<Name>Controller.cs` (one controller per file, base `ApiControllerBase`), DTOs + validators + mapping in `PharmacyDelivery.Api/Contracts/V1/` |
| New use case | interface `PharmacyDelivery.Services/Abstract/<Area>/I<Name>Service.cs`, implementation `PharmacyDelivery.Services/Concrete/<Area>/<Name>Service.cs`, result models `PharmacyDelivery.Services/Models/` |
| New port to infrastructure | interface in `PharmacyDelivery.Services/Abstract/<Area>/`, adapter in `PharmacyDelivery.Infrastructure/Concrete/<Area>/` |
| DB access | repository/queries interface `PharmacyDelivery.Repository/Abstract/<Area>/`, implementation `PharmacyDelivery.Repository/Concrete/<Area>/`, read models `PharmacyDelivery.Repository/Models/`, EF configuration `PharmacyDelivery.Repository/Configurations/` |
| Error codes | `PharmacyDelivery.Common/Errors/ErrorCodes.cs`; exceptions `PharmacyDelivery.Common/Exceptions/AppExceptions.cs` |
| Settings | Options class with `const string SectionName` (Services/Options, Repository/Options, Infrastructure/Options, Api/Configuration) + `appsettings*.json` + `docs/configuration.md` |
| Registration | `PharmacyDelivery.Api/DependencyInjection.cs` only (`AddPharmacyDeliveryOptions`, `AddPharmacyDeliveryServices`, `AddPharmacyDeliveryPersistence`, `AddPharmacyDeliveryRedis`, `AddPharmacyDeliveryMessaging`, `AddPharmacyDeliveryBackgroundJobs`, `AddPharmacyDeliveryExternalServices`, `AddPharmacyDeliverySeeding`, `AddPharmacyDeliveryAuth`, `AddPharmacyDeliveryWeb`, `AddPharmacyDeliveryObservability`) |
| Background job | `PharmacyDelivery.Infrastructure/Concrete/Jobs/` deriving `PeriodicJob` (scope per tick, `SKIP LOCKED` claims) + `Jobs:<Name>` settings |
| Seed data | application templates `PharmacyDelivery.Infrastructure/Concrete/Seed/` + `Seed/Data/*.json`; test templates `PharmacyDelivery.Tests/Seed/` |
| Demo page | `PharmacyDelivery.Api/wwwroot/` (index.html, app.js, app.css; Leaflet + SignalR from cdnjs; CSP in `Infrastructure/Web.cs`) |

## API surface (v1, prefix `/api/v1`)

`auth` (register, login, refresh, logout, logout-all, me, sessions, change-password) · `catalog/products` · `pharmacies` · `cart` ·
`checkout` (header `Idempotency-Key`) · `orders` (list, get, payments, cancel, tracking) · `webhooks/psp` (HMAC) · `pharmacy/orders`
(queue, accept, reject, ready) · `courier` (current delivery, pickup, deliver, locations) · `admin` (price, block user, audit, seed reset) ·
`dev/psp/payments` (fake PSP, Development/Test) · `/.well-known/jwks.json` · hub `/hubs/v1/tracking` · `/health/live`, `/health/ready`.

## Environments

`Development` (dotnet run), `Test` (compose stand — default), `Staging`, `Production`, plus `IntegrationTests` for the test host.
Behaviour is switched by settings only, never by `env.IsProduction()` outside the composition root; dangerous combinations are rejected at
startup by `EnvironmentGuards` (`PharmacyDelivery.Api/Configuration/ApiOptions.cs`).
