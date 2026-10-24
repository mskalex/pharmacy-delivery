---
name: add-integration-event
description: Add a new integration event (contract in PharmacyDelivery.Messaging, publication through the MassTransit outbox, consumer, tests, docs). Use when another module or system must react asynchronously to a domain change.
---

# Add an integration event

1. **Contract**: record in `src/PharmacyDelivery.Messaging/Contracts/V1/` with an XML `<summary>` saying when it is published and who consumes it.
   Primitives only (`Guid`, `string`, `decimal`, `DateTimeOffset`) — `ContractTests` rejects domain types. Changing an existing contract:
   additive nullable fields only; a breaking change goes to `Contracts/V2` and both versions are published during migration.
2. **Publication**: the domain change must raise a domain event in the aggregate. Map it in
   `src/PharmacyDelivery.Services/Concrete/Events/IntegrationEventMapper.cs` (or a new MediatR handler in the same folder) and publish through
   `IIntegrationEventPublisher` — this writes to the EF outbox in the same transaction. Never publish from controllers or after commit.
3. **Consumer** (if this system reacts): `src/PharmacyDelivery.Infrastructure/Concrete/Messaging/<Event>Consumer.cs`, thin — one call into a service
   from `PharmacyDelivery.Services`. The service must be idempotent (redelivery is possible; the inbox deduplicates the same message id).
4. **Registration**: `bus.AddConsumer<…>()` in `AddPharmacyDeliveryMessaging` (`src/PharmacyDelivery.Api/DependencyInjection.cs`). Endpoint name follows
   `MessagingDefaults` (`pharmacy-delivery-<kebab-name>`); dead letters go to `<queue>_error`.
5. **Tests**: mapper unit test (`IntegrationEventMapperTests`), consumer on the MassTransit test harness (`ConsumerHarnessTests` style),
   and an integration test proving the effect through real RabbitMQ (wait with `Eventually`/`WaitForStatusAsync`, never fixed delays).
   Add the cases to the test plan (`scripts/generate-test-plan.py`).
6. **Docs**: event table in `docs/ai/messaging.md`, DESIGN.md §11, sequence diagram in docs/uml.md if the flow changed.
