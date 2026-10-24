# Libraries and licenses

List of dependencies with the versions pinned in `Directory.Packages.props` (Central Package Management). Licenses were checked in the NuGet package metadata (`nuspec`) on the day each package was added. The repository is MIT-licensed — all licenses below are compatible with it (permissive: MIT, Apache-2.0, BSD, PostgreSQL).

> Rule: adding a package = an entry in this table in the same commit (checked by the `review-change` skill). Versions and licenses can be verified with `dotnet list src/PharmacyDelivery.slnx package --include-transitive`.

## NuGet — runtime

| Package | Version | License | Used for |
|---|---|---|---|
| Microsoft.EntityFrameworkCore (+ .Relational, .Design) | 10.0.13 | MIT | ORM, migrations |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | PostgreSQL | PostgreSQL provider for EF Core |
| Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite | 10.0.3 | PostgreSQL | PostGIS types (`geography`) |
| Npgsql (dependency) | 10.x | PostgreSQL | ADO.NET driver, binary COPY |
| NetTopologySuite (dependency) | 2.x | BSD-3-Clause | Geometry (Point) |
| Npgsql.OpenTelemetry | 10.0.3 | PostgreSQL | SQL query spans |
| EFCore.NamingConventions | 10.0.1 | Apache-2.0 | `snake_case` in the database schema |
| **MassTransit** (+ .RabbitMQ, .EntityFrameworkCore) | **8.5.11** | Apache-2.0 | Message bus, outbox/inbox. **Version 9+ is commercial — hence 8.x.** |
| RabbitMQ.Client (dependency) | 7.x | Apache-2.0 OR MPL-2.0 | AMQP client |
| **MediatR** | **12.5.0** | Apache-2.0 | Domain events (`INotification`). **Version 13+ is commercial — hence 12.x.** |
| MediatR.Contracts (dependency) | 2.0.1 | Apache-2.0 | MediatR contracts |
| StackExchange.Redis | 2.13.17 | MIT | Redis client |
| Microsoft.Extensions.Caching.StackExchangeRedis | 10.0.13 | MIT | `IDistributedCache` (L2 for HybridCache) |
| Microsoft.Extensions.Caching.Hybrid | 10.11.0 | MIT | HybridCache (L1 + L2, tags) |
| Microsoft.AspNetCore.SignalR.StackExchangeRedis | 10.0.13 | MIT | SignalR backplane |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.13 | MIT | JWT validation |
| Microsoft.IdentityModel.JsonWebTokens | 8.23.0 | MIT | Issuing JWTs (ES256) |
| Microsoft.Extensions.Identity.Core | 10.0.13 | MIT | `PasswordHasher<T>` (PBKDF2) |
| Microsoft.Extensions.Http, .Hosting, .Options.DataAnnotations | 10.0.13 | MIT | HttpClientFactory, simulator host, Options validation |
| Swashbuckle.AspNetCore | 10.3.1 | MIT | Swagger / OpenAPI |
| Microsoft.OpenApi (dependency) | 2.x | MIT | OpenAPI model |
| Asp.Versioning.Mvc (+ .ApiExplorer) | 10.2.1 | MIT | API versioning |
| FluentValidation (+ .DependencyInjectionExtensions) | 12.2.0 | Apache-2.0 | DTO validation |
| AspNetCore.HealthChecks.NpgSql, .Redis | 9.0.0 | Apache-2.0 | Health checks |
| NLog, NLog.Web.AspNetCore | 6.2.2 | BSD-3-Clause | Logging |
| NLog.Extensions.Logging | 6.2.2 | BSD-2-Clause | NLog in the simulator |
| OpenTelemetry (+ .Extensions.Hosting, .Exporter.OpenTelemetryProtocol, .Instrumentation.AspNetCore, .Instrumentation.Http) | 1.20.1 | Apache-2.0 | Tracing, metrics, OTLP export |
| OpenTelemetry.Instrumentation.StackExchangeRedis | 1.20.1-beta.1 | Apache-2.0 | Redis spans (beta — the only version available for this line) |

## NuGet — build and tests

| Package | Version | License | Used for |
|---|---|---|---|
| MinVer | 8.0.0 | Apache-2.0 | SemVer from git tags |
| NUnit, NUnit3TestAdapter, NUnit.Analyzers | 4.6.1 / 6.3.1 / 4.15.0 | MIT | Tests |
| Microsoft.NET.Test.Sdk | 18.11.0 | MIT | Test runner |
| coverlet.collector | 10.1.0 | MIT | Code coverage (Cobertura format for the GitLab report) |
| JunitXml.TestLogger | 7.1.0 | MIT | Test results in JUnit XML (GitLab test report: `artifacts:reports:junit`) |
| Moq | 4.20.72 | BSD-3-Clause | Mocks (version without SponsorLink) |
| NetArchTest.Rules | 1.3.2 | MIT | Architecture tests |
| Testcontainers.PostgreSql, .Redis, .RabbitMq | 4.16.0 | MIT | Containers in the integration tests |
| Microsoft.AspNetCore.Mvc.Testing, Microsoft.AspNetCore.SignalR.Client | 10.0.13 | MIT | WebApplicationFactory, hub client |
| OpenTelemetry.Exporter.InMemory | 1.20.1 | Apache-2.0 | Assertions on spans in tests |

## Tools (dotnet tools, `.config/dotnet-tools.json`)

| Tool | Version | License | Used for |
|---|---|---|---|
| dotnet-ef | 10.0.13 | MIT | EF Core migrations |
| JetBrains.ReSharper.GlobalTools (`jb cleanupcode`, `jb inspectcode`) | 2026.2.3.1 | JetBrains proprietary, free of charge | ReSharper code style in CI; a developer tool only, not distributed with the application |

## Docker images

| Image | License | Notes |
|---|---|---|
| `mcr.microsoft.com/dotnet/sdk:10.0`, `aspnet:10.0-noble-chiseled-extra`, `runtime:10.0-noble-chiseled-extra` | MIT (.NET), Ubuntu (packages) | Runtime image: chiseled (no shell), non-root user; the `extra` variant includes ICU and tzdata |
| `postgis/postgis:17-3.5` | PostgreSQL License + GPL-2.0 (PostGIS) | PostGIS runs as a separate database process — no effect on the license of the application code |
| `redis:7.2-alpine` | BSD-3-Clause | **7.2 is the last BSD-licensed Redis release**; from 7.4 RSALv2/SSPLv1, from 8.0 also AGPLv3. Alternative: Valkey (BSD) |
| `rabbitmq:4.1-management-alpine` | MPL-2.0 | Broker |
| `mcr.microsoft.com/dotnet/aspire-dashboard:9.5` | MIT | OpenTelemetry viewer (Development/Test) |
| `alpine:3.20` (+ openssl) | MIT / Apache-2.0 | One-off generation of the JWT key |

## Front end (demo page `wwwroot`, cdnjs CDN)

| Library | Version | License |
|---|---|---|
| Leaflet | 1.9.4 | BSD-2-Clause |
| @microsoft/signalr | 8.0.7 | MIT |

## Data

| Source | License | Requirement |
|---|---|---|
| OpenStreetMap map tiles | ODbL (data), CC BY-SA (tiles) | Attribution "© OpenStreetMap contributors" — it is in the footer of the demo page |
| List of common passwords (`common-passwords.txt`, 10,000 entries) — SecLists | MIT | Source described in the file header |

## Deliberately not used

| Library | Reason |
|---|---|
| MediatR 13+, MassTransit 9+ | Commercial licenses — the last Apache-2.0 versions are pinned |
| AutoMapper 15+ | Commercial license; explicit mapping (`V1Mapping`) is easier to review |
| FluentAssertions 8+ | Commercial license; the NUnit assertion model (`Assert.That`) is enough |
| Newtonsoft.Json | One serializer across the whole system: System.Text.Json |
| Redis ≥ 7.4 | License change (see Docker images) |
