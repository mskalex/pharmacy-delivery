# Messaging (for agents)

MassTransit 8.x (Apache-2.0; v9 is commercial — do not upgrade) over RabbitMQ, with the EF Core outbox/inbox in PostgreSQL.
MediatR 12.x (last Apache-2.0 line) carries **domain** events in-process. Design: DESIGN.md §11.

## Flow

```
aggregate.Raise(domain event)
  → UnitOfWork.SaveChanges → MediatR DomainEventNotification<T>
  → PublishOrderIntegrationEventsHandler → IntegrationEventMapper → IIntegrationEventPublisher (MassTransit, outbox)
  → commit (aggregate + outbox row in one transaction) → outbox delivery → RabbitMQ → consumer (inbox dedup) → service
```

## Contracts (`PharmacyDelivery.Messaging/Contracts/V1/OrderEvents.cs`)

| Event | Published when | Consumer → service |
|---|---|---|
| `OrderStatusChanged` | every status change | `OrderStatusChangedConsumer` → `IOrderNotifier` (SignalR push to customer and pharmacy) |
| `OrderPaid` | `Paid` | `OrderPaidConsumer` → `IPharmacyOrderService` (hand the order to the selected pharmacy) |
| `OrderAcceptedByPharmacy` | `Accepted` | — (published for other systems) |
| `OrderReadyForPickup` | `ReadyForPickup` | `OrderReadyForPickupConsumer` → `IDispatchService` (assign a courier) |
| `DeliveryCompleted` | `Delivered` | — (published for other systems) |

Rules:
- Contracts contain primitives only (`Guid`, `string`, `decimal`, `DateTimeOffset`) — no domain types (`ContractTests`).
- Versioning is additive: new nullable fields only. A breaking change = new contract in `Contracts/V2`, both published during migration.
- Serialization: `PharmacyDeliveryJsonOptions.Tolerant` (unknown fields ignored) — the tolerant reader principle.
- Endpoint names: kebab-case with prefix `pharmacy-delivery-` (`MessagingDefaults`), dead letters in `<queue>_error`.

## What does NOT go through the bus

- GPS points (high frequency, only the latest matters) → in-process `Channel` + Redis.
- Timeouts (pharmacy acceptance, reservation expiry, dispatch retry) → polling jobs with `SKIP LOCKED` (no delayed delivery without a
  RabbitMQ plugin). A fulfillment saga with scheduled messages is the documented next step (DESIGN.md §21).

## Consumers

Thin: one call into a service. They run inside the outbox/inbox transaction scope, so a redelivered message is processed once.
Retry policy (`MessagingDefaults`): exponential retries, then redelivery 15 s / 1 min / 5 min, then `_error`; non-retriable:
domain, business-rule, not-found, forbidden, validation and auth exceptions (list in `AddPharmacyDeliveryMessaging`).

Registration: `AddPharmacyDeliveryMessaging` in `PharmacyDelivery.Api/DependencyInjection.cs` (`bus.AddConsumer<…>()`). Testing: MassTransit test harness
(`ConsumerHarnessTests`) for unit, real RabbitMQ via Testcontainers in `OrderLifecycleTests`.

Add a new event: skill `.claude/skills/add-integration-event`.
