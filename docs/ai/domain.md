# Domain (for agents)

Bounded contexts and aggregates (DESIGN.md §5). Aggregates are created only through factory methods (`Create`, `Start`, `Assign`…) and changed
only through their methods; they validate invariants and throw `DomainException(code, message)`.

| Context | Aggregate / entity | Invariants worth knowing |
|---|---|---|
| Catalog | `Product` | price is `Money` (decimal, USD); inactive products cannot be added to a cart |
| Pharmacies | `Pharmacy`, `StockItem`, `StockReservation` | `0 ≤ reserved ≤ on_hand`; `Reserve` → `Commit` (goods leave the shelf, on `ReadyForPickup`) or `Release` (cancel, reassign, expiry); opening hours are evaluated in America/New_York |
| Ordering | `Order` (+ items, status history) | status changes only via `OrderStateMachine`; item prices are a snapshot from checkout; one pharmacy per order |
| Payments | `Payment` | a duplicate notification is ignored; a conflicting one throws |
| Deliveries | `Courier`, `Delivery` | a courier has at most one active delivery (also a partial unique index); `PickUp` before `Complete` |
| Identity | `User`, `AuthSession`, `RefreshToken`, `AccessToken` | lockout after N failures; refresh token reuse revokes the session |

Domain services (pure, no I/O): `PharmacyRankingPolicy` (rank candidate pharmacies by delivery time and queue), `StockAllocator`
(all-or-nothing reservation of a basket in one pharmacy).

## Order lifecycle

```
AwaitingPayment → Paid → AssignedToPharmacy → Accepted → ReadyForPickup → CourierAssigned → InDelivery → Delivered
AwaitingPayment|PaymentFailed → Paid | PaymentFailed | Cancelled
AssignedToPharmacy → AssignedToPharmacy (reassignment to the next pharmacy after reject/timeout)
CourierAssigned → ReadyForPickup (courier released, re-dispatch)
Cancelled → Refunded (payment arrived after the reservation expired, or a paid order was cancelled)
```

The table is `OrderStateMachine` in `PharmacyDelivery.Domain/Ordering/OrderStatus.cs`; `OrderStateMachineTests.Transitions_MatchTheDocumentedTable`
pins it to DESIGN.md §7 — change both together.

Why not a MassTransit saga: transitions are triggered by synchronous commands (pharmacist, courier, webhook) that must answer 409/422
immediately in the same transaction that holds the row locks; the aggregate is the single source of truth. Timeouts (pharmacy acceptance,
reservation expiry, dispatch retry) are polling jobs with `SKIP LOCKED` because RabbitMQ has no delayed delivery without a plugin.
A fulfillment saga with scheduled timeouts is the documented next step (DESIGN.md §21).

## Domain events

Raised inside aggregates (`AggregateRoot.Raise`), dispatched by the unit of work after `SaveChanges` through MediatR
(`DomainEventNotification<T>`), up to 5 rounds (handlers may change aggregates again). `OrderStatusChangedDomainEvent` →
`PublishOrderIntegrationEventsHandler` (integration events into the outbox) and `LogOrderStatusChangeHandler`.

## Glossary

Domain terms map 1:1 to code names: pharmacy (`Pharmacy`), stock item (`StockItem`), reservation (`StockReservation`), reassignment
(`Order.Reassign`), dispatch (`IDispatchService`). Full glossary: DESIGN.md, section "Glossary".
