# Runbook (for agents)

## Run

| Goal | Command |
|---|---|
| Test stand (everything in Docker) | `docker compose up -d --build` → <http://localhost:5080>, Swagger `/swagger`, Aspire <http://localhost:18888> |
| Local API (Development) | `docker compose up -d postgres redis rabbitmq aspire-dashboard` then `dotnet run --project src/PharmacyDelivery.Api` |
| Courier simulator locally | `dotnet run --project src/PharmacyDelivery.CourierSimulator` |
| E2E smoke | `python scripts/demo-order.py` (`--drive-courier` without the simulator) |
| Health | `curl http://localhost:5080/health/ready` (postgres, redis, masstransit-bus) |

Environment for `dotnet run`: `ASPNETCORE_ENVIRONMENT=Test dotnet run …` (the `--environment` switch is not honoured).
Demo accounts: password `Demo-Password-2026!`; `emily.johnson@example.com`, `david.cohen@example.com`, `pharmacist.nyc-mid@pharmacy-delivery.local`,
`courier1@pharmacy-delivery.local`, `admin@pharmacy-delivery.local`.

## Data

- `Seed:Mode` = `None` | `IfEmpty` | `Reset`; `Seed:Profile` = `demo` | `empty` | `large`. Compose: `SEED_MODE=Reset SEED_PROFILE=demo docker compose up -d api`.
- Reset at runtime (Development/Test): `POST /api/v1/admin/maintenance/seed/reset` `{"profile":"demo"}` as admin.
- `large` (~1M stock rows, 100k orders, 1M GPS points) takes minutes; used for `EXPLAIN` in `docs/database.md`. Switch back to `demo` afterwards —
  the demo page logins only exist in `demo`.

## Migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/PharmacyDelivery.Repository --output-dir Migrations
```

Applied at startup when `Database:ApplyMigrationsOnStartup=true` (under `pg_advisory_lock`). Raw SQL that EF cannot express (partitions,
`CONCURRENTLY` indexes) goes into the migration by hand. After a schema change: `python scripts/generate-db-schema.py` (needs the compose
stand with the new image) and update the index table in `docs/database.md`. Skill: `add-migration`.

## Build the images

```bash
APP_VERSION=0.1.0-local docker compose build api courier-simulator      # API build runs the unit tests
docker build -f src/PharmacyDelivery.Api/Dockerfile --target test-results --output type=local,dest=test-results .
```

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Login on the demo page: "Invalid email or password" | the stand was reseeded with `empty`/`large` → reset to `demo` |
| Map tiles do not load | OSM requires a Referer: page `Referrer-Policy` must be `strict-origin-when-cross-origin`, CSP `img-src` must allow `tile.openstreetmap.org`; rebuild the image after changing `Web.cs`/`app.js` |
| Integration tests hang or Postgres container killed | Docker memory exhausted → `docker compose stop`, close other containers |
| `55P03` / 409 `stock.busy` | a row lock held longer than `Database:LockTimeoutMs` — look for long transactions |
| `DbUpdateConcurrencyException` on insert of child rows | a Guid key without `ValueGeneratedNever` |
| GPS points ignored | outlier filter (> `Tracking:MaxSpeedKmh`) or timestamps older/newer than allowed |
| `jb cleanupcode`: profile not found | the `.DotSettings` file is not valid for ReSharper (no XML comments allowed there) |
| Console noise from EF probing `__ef_migrations_history` | expected on an empty database; filtered by NLog rules |
