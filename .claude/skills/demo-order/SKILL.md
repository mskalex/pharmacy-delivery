---
name: demo-order
description: Run the end-to-end order scenario against a running PharmacyDelivery stand (cart, checkout with pharmacy and ETA, payment webhook, pharmacy accept/pack, courier dispatch and live tracking until delivered) and summarize the result. Use to demo or smoke-test the system.
---

# End-to-end demo order

1. Make sure the stand is up: `curl -s http://localhost:5080/health/ready` must be `Healthy`; otherwise use the `run-local` skill.
2. Make sure the data is the `demo` template (the script logs in as Emily and a pharmacist). If login fails with 401, reset:
   `POST /api/v1/admin/maintenance/seed/reset` `{"profile":"demo"}` as `admin@pharmacy-delivery.local`.
3. Run `python scripts/demo-order.py` (options: `--base-url`, `--password`; `--drive-courier` when the `courier-simulator` service is not running).
4. Expect, in order: order number + pharmacy + ETA → webhook 200 → `assignedToPharmacy` → accepted and packed → courier assigned →
   positions while `inDelivery` → `delivered` with the full status history.
5. Report the pharmacy chosen, ETA, courier and the final status history. For the visual demo point the user to <http://localhost:5080>
   (map with the moving courier) and to the trace of the order in Aspire Dashboard <http://localhost:18888>.

If the order stops in a status: check `docker compose logs api courier-simulator --tail=100` and the RabbitMQ `_error` queues.
