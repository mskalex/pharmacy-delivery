#!/usr/bin/env python3
"""
End-to-end demo of the order flow against a running API (no third-party packages needed).

    python scripts/demo-order.py                       # API on http://localhost:5080
    python scripts/demo-order.py --base-url http://localhost:5080 --password "Demo-Password-2026!"

Steps: customer login -> catalog -> cart -> checkout (pharmacy shown) -> payment (fake PSP webhook)
-> pharmacist accept + ready -> courier assigned (courier simulator or this script drives the courier)
-> tracking snapshot -> delivered.
"""
import argparse
import json
import sys
import time
import urllib.error
import urllib.request
import uuid


def call(base, method, path, body=None, token=None, headers=None, expect=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(base + path, data=data, method=method)
    request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("Authorization", "Bearer " + token)
    for key, value in (headers or {}).items():
        request.add_header(key, value)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read()
            status = response.status
    except urllib.error.HTTPError as error:
        raw = error.read()
        status = error.code
    payload = json.loads(raw) if raw and raw[:1] in (b"{", b"[") else None
    if expect and status not in expect:
        print(f"!! {method} {path} -> {status}: {payload}")
        sys.exit(1)
    return status, payload


def login(base, email, password):
    _, tokens = call(base, "POST", "/api/v1/auth/login", {"email": email, "password": password}, expect=[200])
    return tokens["accessToken"]


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://localhost:5080")
    parser.add_argument("--password", default="Demo-Password-2026!")
    parser.add_argument("--drive-courier", action="store_true", help="drive the courier from this script (no simulator running)")
    args = parser.parse_args()
    base = args.base_url.rstrip("/")

    status, health = call(base, "GET", "/health/ready")
    print(f"health: {status} {health['status']} version={health['version']} env={health['environment']}")

    customer = login(base, "emily.johnson@example.com", args.password)
    _, me = call(base, "GET", "/api/v1/auth/me", token=customer, expect=[200])
    address = me["addresses"][0]
    print(f"customer: {me['displayName']}, address: {address['street']}")

    _, page = call(base, "GET", "/api/v1/catalog/products?search=acetaminophen", token=customer, expect=[200])
    product = page["items"][0]
    print(f"product: {product['name']} {product['price']['amount']} {product['price']['currency']}")

    call(base, "PUT", f"/api/v1/cart/items/{product['id']}", {"quantity": 2}, token=customer, expect=[200])
    key = uuid.uuid4().hex
    _, order = call(base, "POST", "/api/v1/checkout", {"address": {k: address[k] for k in ("street", "city", "postalCode", "latitude", "longitude")}},
                    token=customer, headers={"Idempotency-Key": key}, expect=[201])
    print(f"order {order['orderNumber']}: pharmacy '{order['pharmacy']['name']}' ({order['pharmacy']['street']}), ETA {order['estimatedDeliveryAt']}")

    _, payment = call(base, "POST", f"/api/v1/orders/{order['orderId']}/payments", token=customer, expect=[201])
    status, result = call(base, "POST", f"/api/v1/dev/psp/payments/{payment['providerPaymentId']}/complete?result=succeeded", expect=[200])
    print(f"payment: webhook status {result['webhookStatus']}")

    pharmacist = login(base, f"pharmacist.{order['pharmacy']['code'].lower()}@pharmacy-delivery.local", args.password)
    for _ in range(20):  # OrderPaid -> consumer assigns the order to the pharmacy (asynchronously, via RabbitMQ)
        _, details = call(base, "GET", f"/api/v1/orders/{order['orderId']}", token=customer, expect=[200])
        if details["status"] == "assignedToPharmacy":
            break
        time.sleep(0.5)
    print(f"status: {details['status']}")
    call(base, "POST", f"/api/v1/pharmacy/orders/{order['orderId']}/accept", token=pharmacist, expect=[204])
    call(base, "POST", f"/api/v1/pharmacy/orders/{order['orderId']}/ready", token=pharmacist, expect=[204])
    print("pharmacy: accepted and packed")

    courier_name = None
    for _ in range(40):  # OrderReadyForPickup -> dispatch consumer assigns the nearest courier
        _, details = call(base, "GET", f"/api/v1/orders/{order['orderId']}", token=customer, expect=[200])
        if details["status"] in ("courierAssigned", "inDelivery", "delivered"):
            courier_name = details["courierName"]
            break
        time.sleep(0.5)
    print(f"courier: {courier_name} (status {details['status']})")
    if courier_name is None:
        print("no courier assigned (are courier positions fresh? run the courier simulator)")
        sys.exit(1)

    if args.drive_courier:
        number = courier_name.split()[-1]
        courier = login(base, f"courier{number}@pharmacy-delivery.local", args.password)
        _, current = call(base, "GET", "/api/v1/courier/deliveries/current", token=courier, expect=[200])
        call(base, "POST", f"/api/v1/courier/deliveries/{current['deliveryId']}/pickup", token=courier, expect=[204])
        destination = current["destination"]
        pharmacy = current["pharmacy"]
        # ~30 m every 1.2 s (~90 km/h): faster jumps would be dropped by the API as GPS outliers (Tracking:MaxSpeedKmh).
        # One degree is ~111 km of latitude and ~84 km of longitude at New York's latitude.
        lat_span = destination["latitude"] - pharmacy["latitude"]
        lng_span = destination["longitude"] - pharmacy["longitude"]
        steps = max(5, min(60, int((abs(lat_span) * 111 + abs(lng_span) * 84) / 0.03) + 1))
        for i in range(1, steps + 1):
            point = {"latitude": pharmacy["latitude"] + lat_span * i / steps, "longitude": pharmacy["longitude"] + lng_span * i / steps,
                     "recordedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "speedKmh": 30}
            _, ingest = call(base, "POST", "/api/v1/courier/locations", {"points": [point]}, token=courier, expect=[202])
            if i % 5 == 0 or i == steps:
                _, tracking = call(base, "GET", f"/api/v1/orders/{order['orderId']}/tracking", token=customer, expect=[200])
                print(f"tracking: courier at {tracking['courier']['latitude']:.5f},{tracking['courier']['longitude']:.5f} "
                      f"ETA {tracking['estimatedDeliveryAt']} (accepted {ingest['accepted']})")
            time.sleep(1.2)
        call(base, "POST", f"/api/v1/courier/deliveries/{current['deliveryId']}/deliver", token=courier, expect=[204])

    for _ in range(240):
        _, details = call(base, "GET", f"/api/v1/orders/{order['orderId']}", token=customer, expect=[200])
        if details["status"] == "delivered":
            break
        time.sleep(1)
    print(f"final status: {details['status']}; history: " + " -> ".join(h["to"] for h in details["history"]))
    sys.exit(0 if details["status"] == "delivered" else 1)


if __name__ == "__main__":
    main()
