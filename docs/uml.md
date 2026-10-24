# UML diagrams (Mermaid)

All diagrams are Mermaid — they render on GitHub and in the VS Code Markdown preview. The key ones are also in [DESIGN.md](../DESIGN.md).

- [Use cases](#use-cases)
- [Components](#components)
- [Classes — domain model](#classes--domain-model)
- [Classes — service layer](#classes--service-layer)
- [Sequences](#sequences): checkout · concurrent reservations · payment → outbox → consumer · payment/expiry race · courier tracking
- [States](#states): Order · Delivery · StockReservation
- [Activities](#activities): pharmacy selection and reassignment · courier dispatch
- [Deployment](#deployment)
- [ER](#er)

## Use cases

```mermaid
flowchart LR
    customer([Customer])
    pharmacist([Pharmacist])
    courier([Courier])
    psp([PSP — payment gateway])
    admin([Administrator])

    subgraph System["Pharmacy q-commerce system"]
        uc1((Browse the catalog))
        uc2((Cart))
        uc3((Place an order<br/>+ pharmacy information))
        uc4((Payment))
        uc5((Live courier<br/>tracking))
        uc6((Cancel the order))
        uc7((Accept / reject<br/>/ pack the order))
        uc8((Pickup and delivery))
        uc9((Send GPS positions))
        uc10((Payment notification<br/>webhook))
        uc11((Prices, blocking accounts,<br/>audit, data reset))
    end

    customer --- uc1 & uc2 & uc3 & uc4 & uc5 & uc6
    pharmacist --- uc7
    courier --- uc8 & uc9
    psp --- uc10
    admin --- uc11
```

## Components

```mermaid
flowchart TB
    subgraph Clients["Clients"]
        web["Customer app / demo page"]
        panel["Pharmacy panel"]
        mobile["Courier app<br/>(CourierSimulator)"]
    end

    subgraph Api["PharmacyDelivery.Api (ASP.NET Core 10)"]
        ctrl["v1 controllers<br/>DTOs + FluentValidation"]
        hub["TrackingHub<br/>SignalR"]
        mw["Middleware: errors, audit,<br/>headers, rate limiting"]
    end

    subgraph Services["PharmacyDelivery.Services"]
        svc["Use case services"]
        med["MediatR<br/>domain events"]
        ports["Ports: ICartStore, IDistributedLock,<br/>IPaymentGateway, IEtaService, IOrderNotifier…"]
    end

    subgraph Domain["PharmacyDelivery.Domain"]
        agg["Aggregates: Order, StockItem, Pharmacy,<br/>Delivery, Courier, Payment, User"]
    end

    subgraph Repository["PharmacyDelivery.Repository"]
        repo["Repositories + queries (NoTracking)"]
        uow["UnitOfWork<br/>transactions, retry, lock_timeout"]
        ef["PharmacyDeliveryDbContext<br/>interceptors: audit, diagnostics"]
    end

    subgraph Infra["PharmacyDelivery.Infrastructure"]
        redisA["Redis adapters"]
        cons["MassTransit consumers"]
        jobs["Background jobs"]
        ext["PSP (Fake), ETA, JWT, seed"]
    end

    pg[("PostgreSQL + PostGIS")]
    redis[("Redis")]
    mq[["RabbitMQ"]]
    otel["OTLP collector<br/>(Aspire Dashboard)"]
    pspExt["PSP"]

    web & panel --> ctrl
    web <--> hub
    mobile --> ctrl
    pspExt -->|HMAC webhook| ctrl
    ctrl --> svc
    hub --> svc
    svc --> agg
    svc --> repo & uow
    svc --> med
    svc --> ports
    redisA & cons & jobs & ext -. implement / call .-> svc
    repo & uow & ef --> pg
    redisA --> redis
    hub <-->|backplane| redis
    cons <--> mq
    ef -->|outbox| mq
    Api & Infra -.-> otel
```

## Classes — domain model

```mermaid
classDiagram
    direction LR
    class Order {
        <<aggregate>>
        +OrderId Id
        +string Number
        +UserId CustomerId
        +PharmacyId PharmacyId
        +OrderStatus Status
        +Money Total
        +DeliveryAddress DeliveryAddress
        +DateTimeOffset EstimatedDeliveryAt
        +List~Guid~ RejectedPharmacyIds
        +MarkPaid()
        +AssignToPharmacy()
        +Reassign(PharmacyId)
        +Accept()
        +MarkReadyForPickup()
        +MarkCourierAssigned()
        +MarkInDelivery()
        +MarkDelivered()
        +Cancel(reason)
        +MarkRefunded()
    }
    class OrderItem {
        +ProductId ProductId
        +string ProductName
        +Money UnitPrice
        +int Quantity
    }
    class OrderStatusChange {
        +OrderStatus From
        +OrderStatus To
        +DateTimeOffset OccurredAt
    }
    class StockItem {
        <<aggregate>>
        +PharmacyId PharmacyId
        +ProductId ProductId
        +int OnHand
        +int Reserved
        +int Available
        +Reserve(qty)
        +Release(qty)
        +Commit(qty)
    }
    class StockReservation {
        +OrderId OrderId
        +int Quantity
        +ReservationStatus Status
        +DateTimeOffset? ExpiresAt
    }
    class Pharmacy {
        <<aggregate>>
        +string Code
        +GeoPoint Location
        +OpeningHours OpeningHours
        +IsOpenAt(utc, tz) bool
    }
    class Product {
        <<aggregate>>
        +string Name
        +string Ean
        +ProductCategory Category
        +Money Price
    }
    class Payment {
        <<aggregate>>
        +string Provider
        +string ProviderPaymentId
        +Money Amount
        +PaymentStatus Status
        +MarkSucceeded() bool
        +MarkRefunded()
    }
    class Delivery {
        <<aggregate>>
        +OrderId OrderId
        +CourierId CourierId
        +DeliveryStatus Status
        +PickUp()
        +Complete()
    }
    class Courier {
        <<aggregate>>
        +CourierStatus Status
        +MarkBusy()
        +MarkAvailable()
    }
    class User {
        <<aggregate>>
        +string Email
        +string Role
        +string PasswordHash
        +RegisterFailedLogin()
        +IsLockedOut(now) bool
    }
    class Money {
        <<value object>>
        +decimal Amount
        +string Currency
    }
    class GeoPoint {
        <<value object>>
        +double Latitude
        +double Longitude
    }
    class OrderStatusChangedDomainEvent {
        <<domain event>>
    }

    Order "1" *-- "1..*" OrderItem
    Order "1" *-- "1..*" OrderStatusChange
    Order ..> OrderStatusChangedDomainEvent : raises
    Order --> Pharmacy : fulfilled by
    StockReservation --> Order
    StockReservation --> StockItem : holds quantity of
    StockItem --> Pharmacy
    StockItem --> Product
    Payment --> Order
    Delivery --> Order
    Delivery --> Courier
    Courier --> User
    Order ..> Money
    Pharmacy ..> GeoPoint
```

## Classes — service layer

```mermaid
classDiagram
    direction TB
    class ICheckoutService { <<interface>> +CheckoutAsync() }
    class IStockReservationService { <<interface>> +ReserveAsync() +ReleaseForOrderAsync() +CommitForOrderAsync() +ExpireAsync() }
    class IPaymentService { <<interface>> +StartAsync() +HandleWebhookAsync() +RefundCancelledOrderAsync() }
    class IPharmacyOrderService { <<interface>> +AcceptAsync() +RejectAsync() +MarkReadyAsync() +ProcessAcceptanceTimeoutsAsync() }
    class IDispatchService { <<interface>> +DispatchAsync() +RetryPendingAsync() }
    class ITrackingService { <<interface>> +IngestAsync() +GetSnapshotAsync() }
    class IAuthService { <<interface>> +LoginAsync() +RefreshAsync() +ChangePasswordAsync() }
    class IPharmacySelector { <<interface>> +SelectAsync() }
    class IUnitOfWork { <<interface>> +ExecuteInTransactionAsync() +SaveChangesAsync() }
    class IOrderRepository { <<interface>> }
    class IStockRepository { <<interface>> +LockAsync() }
    class IDistributedLock { <<interface>> +TryAcquireAsync() }
    class ICourierLocationStore { <<interface>> +FindNearestAsync() }
    class IOrderNotifier { <<interface>> }
    class IPaymentGateway { <<interface>> }

    ICheckoutService ..> IPharmacySelector
    ICheckoutService ..> IStockReservationService
    ICheckoutService ..> IUnitOfWork
    IStockReservationService ..> IStockRepository
    IPaymentService ..> IPaymentGateway
    IPharmacyOrderService ..> IPharmacySelector
    IDispatchService ..> ICourierLocationStore
    IDispatchService ..> IDistributedLock
    ITrackingService ..> IOrderNotifier
    ICheckoutService ..> IOrderRepository

    note for IUnitOfWork "PharmacyDelivery.Repository.Abstract — implementation: UnitOfWork (EF Core)"
    note for IDistributedLock "PharmacyDelivery.Services.Abstract — implementation: RedisDistributedLock"
```

## Sequences

### Checkout with pharmacy selection and reservation

```mermaid
sequenceDiagram
    autonumber
    actor K as Customer
    participant API as CheckoutController
    participant CS as CheckoutService
    participant R as Redis
    participant PS as PharmacySelector
    participant DB as PostgreSQL
    K->>API: POST /api/v1/checkout + Idempotency-Key
    API->>CS: CheckoutAsync
    CS->>DB: an order with this key?
    CS->>R: GET cart
    CS->>DB: current prices (NoTracking)
    CS->>PS: SelectAsync(address, lines)
    PS->>DB: ST_DWithin + whole cart + load (no locks)
    PS-->>CS: ranking (ETA + queue), open ones only
    loop max. 3 pharmacies
        CS->>DB: BEGIN, SET LOCAL lock_timeout
        CS->>DB: SELECT stock_items FOR UPDATE ORDER BY product_id
        alt enough stock
            CS->>DB: reserved += qty, INSERT reservations (15 min TTL), INSERT order, outbox
            CS->>DB: COMMIT
        else out of stock or lock_timeout
            CS->>DB: ROLLBACK — next pharmacy
        end
    end
    CS->>R: DEL cart
    API-->>K: 201 pharmacy, ETA, reservation expiry
```

### Two concurrent checkouts for the same goods

```mermaid
sequenceDiagram
    participant A as Checkout A
    participant B as Checkout B
    participant DB as stock_items (P1 = 1 unit)
    A->>DB: SELECT … FOR UPDATE (P1)
    Note over DB: row P1 locked by A
    B->>DB: SELECT … FOR UPDATE (P1)
    Note over B: waits (≤ lock_timeout)
    A->>DB: reserved = 1, COMMIT
    Note over DB: lock released
    DB-->>B: row P1 as of A's COMMIT (available = 0)
    B->>B: StockItem.Reserve → DomainException stock.insufficient
    B->>DB: ROLLBACK, try the next pharmacy
```

### Payment → outbox → RabbitMQ → consumer

```mermaid
sequenceDiagram
    autonumber
    participant PSP
    participant API as PaymentWebhooksController
    participant PSv as PaymentService
    participant DB as PostgreSQL
    participant OB as Outbox delivery (MassTransit)
    participant MQ as RabbitMQ
    participant C as OrderPaidConsumer
    PSP->>API: POST webhook + X-Psp-Signature (HMAC)
    API->>PSv: HandleWebhookAsync
    PSv->>PSv: verify HMAC + timestamp
    PSv->>DB: BEGIN, SELECT order FOR UPDATE, payment
    PSv->>PSv: payment.MarkSucceeded, order.MarkPaid → domain event
    PSv->>DB: MediatR handler → INSERT outbox_message (OrderPaid, OrderStatusChanged)
    PSv->>DB: COMMIT (change and messages together)
    API-->>PSP: 200
    OB->>DB: read the outbox (FOR UPDATE SKIP LOCKED)
    OB->>MQ: publish OrderPaid
    MQ->>C: OrderPaid
    C->>DB: inbox (deduplication) + AssignToPharmacy in one transaction
```

### Race: payment vs reservation expiry

```mermaid
sequenceDiagram
    participant W as Payment webhook
    participant J as Expiry job
    participant O as orders row
    J->>O: SELECT … FOR UPDATE SKIP LOCKED
    W->>O: SELECT … FOR UPDATE (waits)
    J->>J: reservations released, order → Cancelled
    J->>O: COMMIT
    O-->>W: row (Cancelled)
    W->>W: payment Succeeded, but the order is cancelled
    W->>W: refund at the PSP (outside the transaction) → Refunded
```

### Real-time courier tracking

```mermaid
sequenceDiagram
    autonumber
    participant M as Courier app
    participant API as CourierController
    participant T as TrackingService
    participant R as Redis
    participant CH as Channel (memory)
    participant W as LocationHistoryWriter
    participant DB as PostgreSQL
    participant H as SignalR (Redis backplane)
    actor K as Customer
    K->>H: SubscribeToOrder(orderId) — access check
    M->>API: POST /courier/locations (batch of points)
    API->>T: IngestAsync
    T->>T: drop future, stale, out of order, > 120 km/h
    T->>R: GEOADD + HSET (latest position)
    T->>CH: TryWrite (history)
    T->>DB: active delivery of the courier (compiled query)
    alt delivery InDelivery and the throttling interval has passed
        T->>T: ETA (every 30 s)
        T->>H: courierPosition → group order:{id}
        H-->>K: position + ETA
    end
    W->>CH: read a batch (500 points / 1 s)
    W->>DB: COPY courier_locations (daily partition)
```

## States

### Order

```mermaid
stateDiagram-v2
    [*] --> AwaitingPayment : checkout
    AwaitingPayment --> Paid : webhook succeeded
    AwaitingPayment --> PaymentFailed : webhook failed
    AwaitingPayment --> Cancelled : expiry / customer
    PaymentFailed --> Paid : payment retried
    PaymentFailed --> Cancelled
    Paid --> AssignedToPharmacy : OrderPaid consumer
    Paid --> Cancelled
    AssignedToPharmacy --> Accepted : pharmacy accepts
    AssignedToPharmacy --> AssignedToPharmacy : rejection / timeout → next pharmacy
    AssignedToPharmacy --> Cancelled : no pharmacy / customer
    Accepted --> ReadyForPickup : packed (stock committed)
    Accepted --> Cancelled
    ReadyForPickup --> CourierAssigned : dispatch
    CourierAssigned --> InDelivery : pickup
    CourierAssigned --> ReadyForPickup : courier released
    InDelivery --> Delivered : delivery
    Cancelled --> Refunded : payment refunded
    Delivered --> [*]
    Refunded --> [*]
```

### Delivery

```mermaid
stateDiagram-v2
    [*] --> Assigned
    Assigned --> InDelivery : PickUp
    Assigned --> Cancelled : Cancel
    InDelivery --> Delivered : Complete
    Delivered --> [*]
    Cancelled --> [*]
```

### StockReservation

```mermaid
stateDiagram-v2
    [*] --> Active : checkout (ExpiresAt = +15 min)
    Active --> Active : payment (ExpiresAt = null)
    Active --> Committed : packed — the goods left the shelf
    Active --> Released : expiry / cancellation / reassignment to another pharmacy
    Committed --> [*]
    Released --> [*]
```

## Activities

### Pharmacy selection and order reassignment

```mermaid
flowchart TD
    start([Checkout or rejection/timeout]) --> q1[Pharmacies within 5 km<br/>with the whole cart — PostGIS]
    q1 --> f1{Any candidates?}
    f1 -- no, nobody within the radius --> e1[422 delivery.out_of_zone]
    f1 -- no, out of stock --> e2[422 checkout.no_pharmacy_available]
    f1 -- yes --> f2[Drop closed ones and those that rejected before]
    f2 --> r[Ranking: preparation + queue × 2 min + travel]
    r --> loop{Next pharmacy from the TOP 3?}
    loop -- yes --> lock[Lock stock FOR UPDATE<br/>in product_id order]
    lock --> ok{All lines available?}
    ok -- yes --> res[Reservation + order<br/>COMMIT]
    ok -- no --> loop
    loop -- none left --> cancel{Order paid?}
    cancel -- yes, reassignment --> c2[Cancel + refund the payment]
    cancel -- no, checkout --> e3[422 stock.insufficient / 409 stock.busy]
    res --> done([The customer sees the pharmacy and the ETA])
```

### Courier dispatch

```mermaid
flowchart TD
    s([OrderReadyForPickup]) --> st{Status = ReadyForPickup?}
    st -- no --> x([end: already handled])
    st -- yes --> geo[GEOSEARCH nearest couriers<br/>within 7 km]
    geo --> fresh[Drop positions older than 120 s]
    fresh --> avail[Available in the DB and without an active delivery]
    avail --> next{Next candidate?}
    next -- no --> none[Metric no_courier<br/>the job retries in 15 s]
    next -- yes --> lk{Redis lock courier:id?}
    lk -- taken --> next
    lk -- acquired --> tx[Transaction: order FOR UPDATE,<br/>courier.MarkBusy, Delivery.Assign]
    tx --> uq{Unique index of the active delivery OK?}
    uq -- 23505 --> next
    uq -- yes --> done([CourierAssigned])
```

## Deployment

```mermaid
flowchart LR
    subgraph Host["docker compose (Test environment)"]
        direction TB
        api["api ×N<br/>pharmacy-delivery-api:{version}<br/>:8080 → host :5080"]
        sim["courier-simulator"]
        keygen["jwt-keygen<br/>(one-off)"]
        pg[("postgres<br/>postgis/postgis:17-3.5<br/>vol: postgres-data")]
        rd[("redis 7.2<br/>vol: redis-data")]
        mq[["rabbitmq 4.1<br/>:5672, :15672"]]
        dash["aspire-dashboard<br/>:18888 UI, :4317 OTLP"]
        vol[/"vol: jwt-keys"/]
    end
    user([Browser]) -->|HTTP / WebSocket| api
    sim -->|HTTP| api
    keygen --> vol --> api
    api --> pg & rd & mq
    api & sim -->|OTLP| dash
```

In production the same image runs in Kubernetes behind an ingress (TLS), with managed PostgreSQL/Redis/RabbitMQ and an OpenTelemetry collector → Grafana (Tempo/Prometheus/Loki) — see DESIGN.md §17.

## ER

```mermaid
erDiagram
    pharmacies ||--o{ stock_items : "has stock"
    products ||--o{ stock_items : "at pharmacies"
    pharmacies ||--o{ orders : fulfills
    orders ||--|{ order_items : contains
    orders ||--|{ order_status_history : history
    orders ||--o{ stock_reservations : reserves
    orders ||--o{ payments : "paid by"
    orders ||--o{ deliveries : "delivered by"
    couriers ||--o{ deliveries : performs
    users ||--o{ auth_sessions : "signs in"
    auth_sessions ||--o{ refresh_tokens : rotates
    auth_sessions ||--o{ access_tokens : issues
    users ||--o{ password_history : history
    audit_entries ||--|{ audit_changes : contains

    pharmacies {
        uuid id PK
        string code UK
        geography location
        jsonb opening_hours
        bool is_active
    }
    products {
        uuid id PK
        string name
        string ean UK
        string category
        numeric price_amount
    }
    stock_items {
        uuid pharmacy_id PK
        uuid product_id PK
        int on_hand
        int reserved
    }
    stock_reservations {
        uuid id PK
        uuid order_id
        int quantity
        string status
        timestamptz expires_at
    }
    orders {
        uuid id PK
        string number UK
        uuid customer_id
        uuid pharmacy_id FK
        string status
        numeric total_amount
        jsonb delivery_address
        uuid_array rejected_pharmacy_ids
        string idempotency_key
    }
    order_items {
        uuid id PK
        uuid order_id FK
        uuid product_id
        int quantity
        numeric unit_price_amount
    }
    payments {
        uuid id PK
        uuid order_id FK
        string provider
        string provider_payment_id
        string status
    }
    deliveries {
        uuid id PK
        uuid order_id FK
        uuid courier_id FK
        string status
    }
    couriers {
        uuid id PK
        uuid user_id UK
        string status
    }
    users {
        uuid id PK
        string normalized_email UK
        string role
        string password_hash
        jsonb addresses
    }
    courier_locations {
        bigint id PK
        timestamptz recorded_at PK
        uuid courier_id
        uuid delivery_id
        float latitude
        float longitude
    }
    audit_entries {
        uuid id PK
        string action
        uuid user_id
        string trace_id
    }
    audit_changes {
        bigint id PK
        uuid audit_entry_id FK
        string entity_type
        string property
        string old_value
        string new_value
    }
```

The MassTransit tables (`inbox_state`, `outbox_message`, `outbox_state`) and the attributes of `access_tokens`/`refresh_tokens` are left out for readability.
