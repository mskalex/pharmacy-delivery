---
name: review-change
description: Review the current change in PharmacyDelivery against the project rules (layers, folders, authorization, audit, concurrency, data access, logging without PII, tests, docs, licences, style). Use before finishing a task or when asked for a review.
---

# Review a change

Collect the diff (or the list of changed files) and check each item; report findings as `file:line — problem — fix`, most severe first.

**Architecture**
- [ ] Dependencies follow the layer table in CLAUDE.md; controllers thin; services without EF Core; consumers call a service.
- [ ] Interfaces in `Abstract/<Area>/`, implementations in `Concrete/<Area>/`, interface models in `Models/`; namespace at layer level; one type per file.
- [ ] All registrations in `src/PharmacyDelivery.Api/DependencyInjection.cs`; correct lifetimes (scoped services/repositories, singletons stateless).

**Correctness and concurrency** (docs/ai/concurrency.md)
- [ ] Business rules in aggregates; order status only via `OrderStateMachine`.
- [ ] Row locks: order row before stock rows, stock rows `ORDER BY product_id`, inside `ExecuteInTransactionAsync`; nothing external inside.
- [ ] Retried operations are idempotent; integration events through the outbox, not published directly.
- [ ] Reads NoTracking with projections; every DB call gets the `CancellationToken`; new constraints have names + error-code mapping.

**Security**
- [ ] Endpoint has a policy or explicit `[AllowAnonymous]`; ownership checked (foreign resource → 404).
- [ ] State-changing action has `[Audited]` or `[AuditExempt]`; secrets in audit are masked.
- [ ] No tokens/passwords/PII in logs, exceptions or telemetry tags; no secrets committed (only test-only/dev-only marked values).
- [ ] Input validated (FluentValidation) with limits; rate limit for expensive or abusable endpoints.

**API/contracts**
- [ ] DTOs and contracts use primitives; breaking change → new API/contract version; `[ProducesResponseType]` complete.

**Tests and docs**
- [ ] Unit tests on `TestBase` (`Service<T>`, `MockOf<T>`) and integration tests where HTTP/DB/bus behaviour changed; `[Property("TestCase", …)]`
      ids exist in the regenerated test plan.
- [ ] docs/configuration.md for new settings, docs/database.md (schema regenerated, index rationale), DESIGN.md / docs/uml.md / README /
      docs/ai updated in the same change.
- [ ] New package: version in `Directory.Packages.props`, licence acceptable (no MassTransit 9+, MediatR 13+, AutoMapper 15+,
      FluentAssertions 8+), row in docs/third-party.md.

**Style**
- [ ] `format-code` produces no diff; one-line `if` bodies without braces on the next line, multi-line with braces; XML docs on interfaces.

Finish with the `run-tests` skill results.
