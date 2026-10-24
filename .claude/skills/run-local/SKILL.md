---
name: run-local
description: Start the PharmacyDelivery stand (docker compose, environment Test) or the API locally with dotnet run, wait until healthy and report the URLs. Use when asked to run, start, restart or reset the app or its data.
---

# Run the PharmacyDelivery stand

1. Check Docker: `docker version`. If the user's other containers use most of Docker's memory, say so before starting.
2. Choose the mode:
   - **Stand (default)**: `docker compose up -d --build` from the repository root. Image build includes the unit tests (~3–5 min first time).
   - **Local API**: `docker compose up -d postgres redis rabbitmq aspire-dashboard`, then `dotnet run --project src/PharmacyDelivery.Api`
     (run in background) and optionally `dotnet run --project src/PharmacyDelivery.CourierSimulator`. Do not also start the `api` service — port 5080.
   - **Clean data**: add `SEED_MODE=Reset` (and `SEED_PROFILE=demo|empty|large`) to the compose command, e.g.
     `SEED_MODE=Reset docker compose up -d api`; at runtime: `POST /api/v1/admin/maintenance/seed/reset` `{"profile":"demo"}` as admin.
3. Wait for health — poll `curl -s http://localhost:5080/health/ready` until `"status":"Healthy"` (postgres, redis, masstransit-bus), at most ~2 min.
   On failure show `docker compose logs --tail=80 api`.
4. Report:
   - demo page <http://localhost:5080> (login `emily.johnson@example.com` / `Demo-Password-2026!`), Swagger `/swagger`;
   - Aspire Dashboard <http://localhost:18888>, RabbitMQ <http://localhost:15672> (`pharmacy_delivery` / `pharmacy-delivery-test-only`);
   - environment and version from the health response.

Troubleshooting: docs/ai/runbook.md.
