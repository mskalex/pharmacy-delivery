// Demo client of the PharmacyDelivery API: login -> cart -> checkout (pharmacy shown) -> fake payment ->
// (optional) pharmacy accepts/packs -> live courier tracking over SignalR (fallback: polling).
"use strict";

const api = "/api/v1";
const statuses = ["awaitingPayment", "paid", "assignedToPharmacy", "accepted", "readyForPickup", "courierAssigned", "inDelivery", "delivered"];
const statusLabels = {
  awaitingPayment: "awaiting payment", paymentFailed: "payment declined", paid: "paid",
  assignedToPharmacy: "sent to the pharmacy", accepted: "accepted by the pharmacy", readyForPickup: "packed",
  courierAssigned: "courier assigned", inDelivery: "on the way", delivered: "delivered", cancelled: "cancelled", refunded: "refunded",
};

const state = { token: null, profile: null, orderId: null, hub: null, poll: null, map: null, layers: {} };
const $ = (id) => document.getElementById(id);
const money = (m) => new Intl.NumberFormat("en-US", { style: "currency", currency: m.currency }).format(m.amount);
const time = (iso) => new Date(iso).toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" });

async function call(method, path, body, token = state.token, headers = {}) {
  const response = await fetch(api + path, {
    method,
    headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}), ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  const data = text ? JSON.parse(text) : null;
  if (!response.ok) throw new Error(data?.detail || data?.title || response.statusText);
  return data;
}

async function login(email, password) {
  const tokens = await call("POST", "/auth/login", { email, password }, null);
  return tokens.accessToken;
}

$("login-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  $("login-error").textContent = "";
  try {
    state.token = await login($("email").value, $("password").value);
    state.profile = await call("GET", "/auth/me");
    $("who").textContent = `${state.profile.displayName} (${state.profile.role})`;
    $("logout").hidden = false;
    $("login-panel").hidden = true;
    $("shop").hidden = state.profile.role !== "Customer";
    $("orders").hidden = state.profile.role !== "Customer";
    $("address").innerHTML = state.profile.addresses.map((a, i) => `<option value="${i}">${a.street}, ${a.city}</option>`).join("");
    await Promise.all([search(""), refreshCart(), refreshOrders()]);
  } catch (err) {
    $("login-error").textContent = err.message;
  }
});

document.querySelectorAll("button[data-email]").forEach((b) => b.addEventListener("click", () => { $("email").value = b.dataset.email; }));
$("logout").addEventListener("click", async () => { try { await call("POST", "/auth/logout"); } finally { location.reload(); } });

$("search-form").addEventListener("submit", (e) => { e.preventDefault(); search($("search").value); });

async function search(text) {
  const page = await call("GET", `/catalog/products?pageSize=8&search=${encodeURIComponent(text)}`, undefined, null);
  $("products").innerHTML = page.items.map((p) => `<li><span>${p.name}<br><small>${money(p.price)}</small></span><button data-add="${p.id}">Add</button></li>`).join("");
  document.querySelectorAll("[data-add]").forEach((b) => b.addEventListener("click", async () => {
    const cart = await call("GET", "/cart");
    const line = cart.lines.find((l) => l.productId === b.dataset.add);
    await call("PUT", `/cart/items/${b.dataset.add}`, { quantity: (line?.quantity ?? 0) + 1 });
    await refreshCart();
  }));
}

async function refreshCart() {
  const cart = await call("GET", "/cart");
  $("cart").innerHTML = cart.lines.map((l) => `<li><span>${l.quantity} × ${l.name}</span><span>${money(l.lineTotal)}</span></li>`).join("") || "<li class='hint'>empty</li>";
  $("cart-total").textContent = money(cart.total);
  $("checkout").disabled = cart.lines.length === 0;
}

async function refreshOrders() {
  const page = await call("GET", "/orders?pageSize=5");
  $("order-list").innerHTML = page.items.map((o) => `<li><span>${o.number}<br><small>${statusLabels[o.status] ?? o.status} · ${o.pharmacyName}</small></span><button data-track="${o.id}">Track</button></li>`).join("");
  document.querySelectorAll("[data-track]").forEach((b) => b.addEventListener("click", () => track(b.dataset.track)));
}

$("checkout").addEventListener("click", async () => {
  $("checkout-error").textContent = "";
  $("checkout").disabled = true;
  try {
    const a = state.profile.addresses[Number($("address").value)];
    const order = await call("POST", "/checkout", { address: { street: a.street, city: a.city, postalCode: a.postalCode, latitude: a.latitude, longitude: a.longitude } },
      state.token, { "Idempotency-Key": crypto.randomUUID().replaceAll("-", "") });
    await track(order.orderId);
    const payment = await call("POST", `/orders/${order.orderId}/payments`);
    await call("POST", `/dev/psp/payments/${payment.providerPaymentId}/complete?result=succeeded`, undefined, null);
    if ($("auto-pharmacy").checked) pharmacyDemo(order.orderId, order.pharmacy.code);
    await Promise.all([refreshCart(), refreshOrders()]);
  } catch (err) {
    $("checkout-error").textContent = err.message;
  } finally {
    $("checkout").disabled = false;
  }
});

// Demo shortcut: the pharmacist of the selected pharmacy accepts and packs the order (normally the pharmacy panel).
async function pharmacyDemo(orderId, code) {
  const token = await login(`pharmacist.${code.toLowerCase()}@pharmacy-delivery.local`, $("password").value);
  for (let i = 0; i < 30; i++) {
    const order = await call("GET", `/orders/${orderId}`);
    if (order.status === "assignedToPharmacy") break;
    await new Promise((r) => setTimeout(r, 500));
  }
  await new Promise((r) => setTimeout(r, 1500));
  await call("POST", `/pharmacy/orders/${orderId}/accept`, undefined, token);
  await new Promise((r) => setTimeout(r, 1500));
  await call("POST", `/pharmacy/orders/${orderId}/ready`, undefined, token);
}

async function track(orderId) {
  state.orderId = orderId;
  $("tracking").hidden = false;
  const order = await call("GET", `/orders/${orderId}`);
  $("order-number").textContent = order.number;
  $("pharmacy-name").textContent = order.pharmacy.name;
  $("pharmacy-address").textContent = `${order.pharmacy.street}, ${order.pharmacy.postalCode} ${order.pharmacy.city}`;
  showStatus(order.status, order.estimatedDeliveryAt);
  drawMap(order.pharmacy, order.deliveryAddress);
  await subscribe(orderId);
}

function showStatus(status, eta) {
  $("status").textContent = statusLabels[status] ?? status;
  if (eta) $("eta").textContent = time(eta);
  const reached = statuses.indexOf(status);
  $("timeline").innerHTML = statuses.map((s, i) => `<li class="${i <= reached ? "done" : ""}">${statusLabels[s]}</li>`).join("");
}

function drawMap(pharmacy, destination) {
  if (!state.map) {
    state.map = L.map("map");
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", { maxZoom: 19, attribution: "© OpenStreetMap contributors" }).addTo(state.map);
  }
  Object.values(state.layers).forEach((l) => l.remove());
  state.layers = {
    pharmacy: L.circleMarker([pharmacy.latitude, pharmacy.longitude], { radius: 9, color: "#0b7a53", fillOpacity: 0.9 }).bindTooltip(pharmacy.name).addTo(state.map),
    destination: L.circleMarker([destination.latitude, destination.longitude], { radius: 9, color: "#344054", fillOpacity: 0.9 }).bindTooltip("Delivery address").addTo(state.map),
  };
  state.map.fitBounds([[pharmacy.latitude, pharmacy.longitude], [destination.latitude, destination.longitude]], { padding: [40, 40] });
}

function moveCourier(lat, lng) {
  if (!state.layers.courier) {
    state.layers.courier = L.circleMarker([lat, lng], { radius: 8, color: "#d92d20", fillOpacity: 1 }).bindTooltip("Courier").addTo(state.map);
  } else {
    state.layers.courier.setLatLng([lat, lng]);
  }
}

async function subscribe(orderId) {
  if (state.hub) await state.hub.stop();
  clearInterval(state.poll);
  const hub = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/v1/tracking", { accessTokenFactory: () => state.token })
    .withAutomaticReconnect()
    .build();
  hub.on("orderStatusChanged", (m) => { if (m.orderId === state.orderId) { showStatus(m.status); refreshOrders(); } });
  hub.on("courierPosition", (m) => { if (m.orderId === state.orderId) { moveCourier(m.latitude, m.longitude); $("eta").textContent = time(m.estimatedDeliveryAt); } });
  try {
    await hub.start();
    const snapshot = await hub.invoke("SubscribeToOrder", orderId);
    if (snapshot.courier) moveCourier(snapshot.courier.latitude, snapshot.courier.longitude);
    state.hub = hub;
    $("channel").textContent = `channel: SignalR (${hub.connection.transport?.constructor?.name ?? "websocket"})`;
  } catch {
    // Fallback for clients without WebSockets: poll the tracking snapshot.
    $("channel").textContent = "channel: polling every 5 s";
    state.poll = setInterval(async () => {
      const t = await call("GET", `/orders/${orderId}/tracking`);
      showStatus(t.status, t.estimatedDeliveryAt);
      if (t.courier) moveCourier(t.courier.latitude, t.courier.longitude);
    }, 5000);
  }
}
