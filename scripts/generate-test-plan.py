#!/usr/bin/env python3
"""
Generates docs/test-plan.md from the structured specification below, so that the hierarchical numbering
(section N, area N.M, case N.M.K, step N.M.K.S) is always continuous. Edit the SPEC, then run:

    python scripts/generate-test-plan.py

Rules: never renumber an existing case — a removed case stays with auto="retired"; new cases go to the end of their
area. The test TestPlan_NumberingIsContinuous_AndMatchesAutomatedTests checks numbering and links to the code.
"""
from pathlib import Path

# (title, type, priority, data, automated: "yes"/"no"/"retired", test name or note, [(step, expected), ...])
SPEC = [
    ("Catalog and cache", [
        ("Search by name fragment and category", "integration", "medium", "minimal", "yes", "CatalogAndCartTests.Catalog_SearchesByNameFragmentAndCategory_Anonymously", [
            ("Without signing in, call `GET /api/v1/catalog/products?search=ACETAM`", "200, one product \"Test Acetaminophen\", price in USD"),
            ("Call `GET …?category=vitaminsAndSupplements`", "only products of that category, `totalCount = 1`"),
        ]),
        ("A price change invalidates the catalog cache", "integration", "high", "minimal", "yes", "CatalogAndCartTests.PriceChange_InvalidatesCatalogCache", [
            ("Get a product (it lands in HybridCache)", "price 10.00"),
            ("Admin: `PUT /api/v1/admin/products/{id}/price` with 12.50", "204"),
            ("Get the product again", "price 12.50 — cache invalidated by tag"),
        ]),
    ]),
    ("Cart", [
        ("Add, change and remove a line", "integration", "high", "minimal", "yes", "CatalogAndCartTests.Cart_AddUpdateRemove_TotalFollowsCatalogPrices", [
            ("Add 2 × product 1 and 1 × product 2", "total 40.00, two lines"),
            ("Remove product 1", "total 20.00, cart expiry time present"),
        ]),
        ("Quantity out of range and unknown product", "integration", "medium", "minimal", "yes", "CatalogAndCartTests.Cart_RejectsQuantityOutOfRangeAndUnknownProduct", [
            ("Set quantity 11", "400 `validation.failed`"),
            ("Add a product that does not exist", "404 `product.not_found`"),
        ]),
        ("Cart expiry (TTL)", "manual", "low", "demo", "no", "Redis TTL", [
            ("Add a product and check `TTL pharmacy-delivery:cart:{userId}` in Redis", "TTL ≈ 24 h (`Checkout:CartTtlHours`)"),
            ("Change the cart", "TTL renewed"),
        ]),
    ]),
    ("Checkout and pharmacy selection", [
        ("Geographic distance (Haversine)", "unit", "low", "–", "yes", "CommonUtilitiesTests.Haversine_DistanceMidtownToUpperEastSide_IsAboutFourKilometres", [
            ("Compute the distance Midtown → Upper East Side", "4.1–4.4 km"),
        ]),
        ("Opening hours in the America/New_York time zone", "unit", "high", "–", "yes", "PharmacyTests.OpeningHours_AreEvaluatedInNewYorkTime", [
            ("Pharmacy open 8–20 on Wednesday; check 12:30 and 11:30 UTC and 00:00 UTC the next day", "open / closed / closed (EDT = UTC−4)"),
            ("24/7 pharmacy at 05:00 UTC", "open"),
        ]),
        ("Pharmacy ranking: delivery time matters more than distance", "unit", "high", "–", "yes", "PharmacyTests.RankingPolicy_PrefersFasterDeliveryOverPureDistance_AndSkipsExcluded", [
            ("A near pharmacy with 10 queued orders, a farther idle one, an excluded one", "order: the farther idle one, then the near one; the excluded one is skipped"),
        ]),
        ("Empty cart", "unit", "medium", "–", "yes", "CheckoutServiceTests.EmptyCart_IsRejected", [
            ("Check out with an empty cart", "`BusinessRuleException` `cart.empty`"),
        ]),
        ("Address out of zone vs out of stock", "unit", "high", "–", "yes", "CheckoutServiceTests.AddressWithoutPharmacyInRadius_IsOutOfZone_OtherwiseNoPharmacyAvailable", [
            ("No pharmacy within the radius", "`delivery.out_of_zone`"),
            ("Pharmacies within the radius, none with the whole cart", "`checkout.no_pharmacy_available`"),
        ]),
        ("Falling back to the next pharmacy when the reservation fails", "unit", "critical", "–", "yes", "CheckoutServiceTests.WhenBestPharmacyCannotReserve_NextOneIsUsed_AndCartIsCleared", [
            ("The first pharmacy reports `stock.insufficient`", "the order is created at the second one with the correct total"),
            ("Check the cart", "the cart is deleted after success"),
        ]),
        ("Repeated Idempotency-Key (service)", "unit", "high", "–", "yes", "CheckoutServiceTests.RepeatedIdempotencyKey_ReturnsExistingOrderWithoutReserving", [
            ("Check out with the key of an existing order", "the existing order is returned, `IsReplay = true`, no reservation"),
        ]),
        ("Checkout returns the pharmacy and the ETA", "integration", "critical", "minimal", "yes", "CheckoutAndStockTests.Checkout_ReturnsFulfillingPharmacyAndEta_AndOrderShowsIt", [
            ("Cart 2 × P1 + 1 × P2, `POST /api/v1/checkout`", "201, pharmacy \"Test Pharmacy 1\", status `awaitingPayment`, total 40.00, ETA in the future"),
            ("`GET /api/v1/orders/{id}`", "contains the pharmacy address"),
            ("Check `stock_items`", "`reserved = 2` for P1"),
        ]),
        ("Checkout idempotency (HTTP)", "integration", "high", "minimal", "yes", "CheckoutAndStockTests.Checkout_WithSameIdempotencyKey_ReturnsTheSameOrder", [
            ("Two checkouts with the same `Idempotency-Key`", "201, then 200 with the same `orderId`"),
            ("Check the reservation", "reserved once"),
        ]),
        ("Address outside the delivery zone (HTTP)", "integration", "medium", "minimal", "yes", "CheckoutAndStockTests.Checkout_OutsideDeliveryZone_Is422", [
            ("Check out with an address in Philadelphia", "422 `delivery.out_of_zone`"),
        ]),
        ("Missing Idempotency-Key header", "integration", "medium", "minimal", "yes", "CheckoutAndStockTests.Checkout_WithoutIdempotencyKey_Is400", [
            ("Check out without the header", "400 `validation.failed`"),
        ]),
    ]),
    ("Stock reservation", [
        ("Concurrent reservations never oversell", "integration", "critical", "concurrency", "yes", "CheckoutAndStockTests.ParallelCheckouts_NeverOversellScarceStock", [
            ("Sign in 50 customers, each adds 1 unit of a product with stock 10", "carts ready"),
            ("Run 50 concurrent `POST /api/v1/checkout`", "exactly 10 × 201, the rest 422 `stock.insufficient` (or 409 `stock.busy`), no 5xx"),
            ("Check `stock_items`", "`reserved = 10`, `on_hand = 10`, no negative values"),
        ]),
        ("No deadlocks with carts holding the same products", "integration", "critical", "minimal", "yes", "CheckoutAndStockTests.ParallelCheckouts_WithOverlappingBaskets_DoNotDeadlock", [
            ("10 concurrent checkouts of P1+P2 and P2+P1 (reverse order)", "all 201/200 — locks are always taken in `product_id` order"),
        ]),
        ("lock_timeout exceeded", "integration", "high", "minimal", "yes", "CheckoutAndStockTests.Checkout_WhenStockRowIsLockedLongerThanLockTimeout_Returns409StockBusy", [
            ("In a separate transaction lock the stock row of P3 (`FOR UPDATE`)", "lock held"),
            ("Check out P3", "409 `stock.busy`"),
        ]),
        ("Expiry of an unpaid reservation", "integration", "critical", "minimal", "yes", "CheckoutAndStockTests.ExpiredUnpaidReservation_IsReleased_AndOrderCancelled", [
            ("Create an order and move the reservation's `expires_at` back", "reservation overdue"),
            ("Run `ExpireAsync`", "1 processed; order `cancelled`; `reserved = 0`"),
        ]),
        ("Reserve, commit and release keep the invariant", "unit", "high", "–", "yes", "StockTests.ReserveCommitRelease_KeepInvariant", [
            ("Stock 10: reserve 4, commit 3, release 1", "`on_hand = 7`, `reserved = 0`"),
        ]),
        ("Reserving more than available", "unit", "high", "–", "yes", "StockTests.Reserve_MoreThanAvailable_ThrowsStockInsufficient", [
            ("Stock 2, reserve 3", "`stock.insufficient`, `reserved` unchanged"),
        ]),
        ("All-or-nothing cart reservation", "unit", "critical", "–", "yes", "StockTests.StockAllocator_IsAllOrNothing", [
            ("One of the lines is unavailable", "exception, no line reserved"),
            ("Both available", "both reserved"),
        ]),
    ]),
    ("Payments", [
        ("Webhook HMAC signature", "unit", "critical", "–", "yes", "HmacSignatureTests.Verify_AcceptsValidSignature_RejectsTamperedBodyWrongSecretAndOldTimestamp", [
            ("Valid signature", "accepted"),
            ("Changed body / other secret / old timestamp / no signature", "rejected"),
        ]),
        ("Duplicate notification in the payment aggregate", "unit", "high", "–", "yes", "ValueObjectAndEntityTests.Payment_DuplicateNotificationIsIgnored_ConflictingOneThrows", [
            ("`MarkSucceeded` twice", "the second call returns false"),
            ("`MarkFailed` after success", "domain exception"),
        ]),
        ("Invalid signature (service)", "unit", "critical", "–", "yes", "PaymentServiceTests.InvalidSignature_IsRejected", [
            ("Webhook signed with `deadbeef`", "401 `payment.invalid_signature`"),
        ]),
        ("Duplicate webhook (service)", "unit", "critical", "–", "yes", "PaymentServiceTests.DuplicateNotification_IsProcessedOnce", [
            ("Two identical notifications", "`paid`, then `duplicate`; order `Paid`"),
        ]),
        ("Payment after the reservation expired", "unit", "critical", "–", "yes", "PaymentServiceTests.PaymentAfterReservationExpired_IsRefunded", [
            ("The order is cancelled, `succeeded` arrives", "refund at the PSP; order and payment `Refunded`"),
        ]),
        ("Payment through the webhook and the bus", "integration", "critical", "minimal", "yes", "OrderLifecycleTests.Payment_ThroughWebhookAndBus_AssignsOrderToPharmacy", [
            ("Checkout, payment start, signed webhook", "200"),
            ("Wait for the `OrderPaid` consumer (RabbitMQ)", "history: `awaitingPayment → paid → assignedToPharmacy`"),
        ]),
        ("Invalid signature (HTTP)", "integration", "critical", "minimal", "yes", "OrderLifecycleTests.Webhook_WithInvalidSignature_Is401", [
            ("`POST /api/v1/webhooks/psp` with a wrong signature", "401 `payment.invalid_signature`"),
        ]),
        ("Two concurrent webhooks", "integration", "critical", "minimal", "yes", "OrderLifecycleTests.Webhook_DeliveredTwice_IsProcessedOnce", [
            ("Send the same webhook twice concurrently", "both 200; results `paid` and `duplicate`"),
        ]),
    ]),
    ("Pharmacy", [
        ("Order reassignment in the domain", "unit", "high", "–", "yes", "OrderStateMachineTests.Reassign_MovesOrderToNextPharmacy_AndRemembersRejectedOne", [
            ("A paid order is reassigned to a second pharmacy", "new pharmacy, status `AssignedToPharmacy`, the first one remembered as rejected"),
            ("Reassign to the same pharmacy", "exception"),
        ]),
        ("Rejection by the pharmacy", "integration", "critical", "reassign", "yes", "OrderLifecycleTests.PharmacyRejects_OrderMovesToNextPharmacy", [
            ("Pharmacist of T-1 rejects a paid order", "204"),
            ("The customer gets the order", "pharmacy T-2, status `assignedToPharmacy`"),
            ("Pharmacist of T-2 gets the queue", "contains the order"),
        ]),
        ("Another pharmacy cannot see the order", "integration", "high", "reassign", "yes", "OrderLifecycleTests.OtherPharmacy_CannotSeeOrAcceptTheOrder", [
            ("Pharmacist of T-2 accepts / gets an order of T-1", "404 / 404"),
        ]),
        ("No acceptance in time", "integration", "critical", "reassign", "yes", "OrderLifecycleTests.AcceptanceTimeout_ReassignsOrder", [
            ("Move `assigned_at` back by 10 min, run the timeout job", "1 order processed"),
            ("Get the order", "pharmacy T-2"),
        ]),
    ]),
    ("Courier dispatch", [
        ("A courier has one delivery; delivery lifecycle", "unit", "high", "–", "yes", "ValueObjectAndEntityTests.Courier_CannotTakeTwoDeliveries_DeliveryFollowsItsLifecycle", [
            ("A busy courier gets a second delivery", "domain exception"),
            ("Deliver before pickup", "exception; after pickup — `Delivered`"),
        ]),
        ("Stale positions and locked couriers are skipped", "unit", "high", "–", "yes", "DispatchServiceTests.StalePositionsAndLockedCouriers_AreSkipped_ResultIsNoCourier", [
            ("A courier with a 10-minute-old position and a courier locked in Redis", "result `NoCourier`; the stale one is not checked"),
        ]),
        ("The nearest available courier is assigned", "integration", "critical", "dispatch", "yes", "OrderLifecycleTests.ReadyOrder_IsAssignedToNearestAvailableCourier_StalePositionsIgnored", [
            ("Pay, accept and pack the order", "`OrderReadyForPickup` on the bus"),
            ("Wait for status `courierAssigned`", "courier \"Near Courier\" (the nearest fresh one), not the courier with a stale position"),
        ]),
        ("The database rejects a second active delivery of a courier", "integration", "critical", "minimal", "yes", "OrderLifecycleTests.Database_RejectsSecondActiveDeliveryOfCourier", [
            ("Insert an active delivery for a courier", "OK"),
            ("Insert a second active delivery for the same courier", "23505 `ux_deliveries_active_courier`"),
        ]),
    ]),
    ("Tracking", [
        ("GPS point filtering", "unit", "high", "–", "yes", "TrackingServiceTests.Ingest_DropsFutureStaleOutOfOrderAndOutlierPoints", [
            ("Batch: 2 valid, future, stale, out of order, a 15 km jump in 10 s", "2 accepted, 4 dropped; latest position in Redis; 2 points in the history queue"),
        ]),
        ("Push only in status InDelivery", "unit", "critical", "–", "yes", "TrackingServiceTests.Ingest_PushesPositionOnlyWhileOrderIsInDelivery", [
            ("Point while the delivery is `Assigned`", "no push"),
            ("Point while `InDelivery`", "push with ETA"),
        ]),
        ("Batch too large", "unit", "medium", "–", "yes", "TrackingServiceTests.Ingest_RejectsTooLargeBatch", [
            ("51 points", "`ValidationFailedException`"),
        ]),
        ("Push and ETA throttling", "unit", "medium", "–", "yes", "TrackingServiceTests.PushThrottle_LimitsPushesPerOrder_AndRecalculatesEtaPeriodically", [
            ("Pushes after 0.3 s, 2 s and 31 s", "the second is blocked; the third without ETA recalculation; the fourth with recalculation"),
        ]),
        ("Position visible only on the way and only to the customer", "integration", "critical", "minimal", "yes", "OrderLifecycleTests.Tracking_ShowsCourierOnlyInDelivery_AndOnlyToTheCustomer", [
            ("Order packed, courier assigned: `GET …/tracking`", "`courier = null`"),
            ("The courier picks up and sends a point", "the customer sees the position; another customer gets 404"),
            ("The courier delivers", "status `delivered`"),
        ]),
        ("SignalR push to the customer", "integration", "critical", "minimal", "yes", "OrderLifecycleTests.SignalR_PushesCourierPositionToSubscribedCustomer", [
            ("The customer connects to the hub and calls `SubscribeToOrder`", "subscription accepted"),
            ("The courier sends a GPS point", "the customer receives `courierPosition` with that position"),
        ]),
        ("Polling fallback on the demo page", "manual", "medium", "demo", "no", "browser", [
            ("Block WebSocket and long polling (e.g. DevTools → block `/hubs`)", "the page shows \"channel: polling every 5 s\", the courier marker keeps moving"),
        ]),
    ]),
    ("Order state machine", [
        ("Happy path", "unit", "high", "–", "yes", "OrderStateMachineTests.HappyPath_GoesThroughAllStatuses_AndRecordsHistoryAndEvents", [
            ("Take an order through all statuses", "8 history entries and 8 domain events; total 32.97"),
        ]),
        ("Transition table", "unit", "critical", "–", "yes", "OrderStateMachineTests.Transitions_MatchTheDocumentedTable", [
            ("Check `CanTransition` for every pair of statuses", "matches the table (DESIGN.md §7)"),
        ]),
        ("Invalid transition", "unit", "high", "–", "yes", "OrderStateMachineTests.InvalidTransition_ThrowsDomainExceptionWithCode", [
            ("`Accept` in status `AwaitingPayment`", "`order.invalid_transition`"),
        ]),
    ]),
    ("Authentication and sessions", [
        ("Login stores only the refresh token hash", "unit", "critical", "–", "yes", "AuthServiceTests.Login_ReturnsTokens_StoresOnlyRefreshTokenHash", [
            ("Sign in", "tokens returned; the repository gets SHA-256, not the value"),
        ]),
        ("Refresh token reuse (service)", "unit", "critical", "–", "yes", "AuthServiceTests.Refresh_WithAlreadyUsedToken_RevokesSessionAndFails", [
            ("Refresh with a used token", "`auth.refresh_token_reused`; the session and its access tokens revoked (DB + Redis)"),
        ]),
        ("A password change ends other sessions", "unit", "high", "–", "yes", "AuthServiceTests.ChangePassword_RevokesOtherSessions", [
            ("Change the password", "all sessions except the current one revoked; the new hash works"),
        ]),
        ("Revocation check falls back without Redis", "unit", "high", "–", "yes", "AuthServiceTests.RevocationCheck_FallsBackToDatabase_WhenRedisIsUnavailable", [
            ("Redis unavailable, token revoked in the database", "the token is treated as revoked (fail-closed)"),
        ]),
        ("Refresh token rotation and theft detection (HTTP)", "integration", "critical", "minimal", "yes", "AuthAndSecurityTests.Refresh_RotatesTokens_ReuseRevokesWholeSession", [
            ("Refresh the token", "200, new pair"),
            ("Reuse the old token", "401 `auth.refresh_token_reused`"),
            ("Use the new token", "401 — the whole session is revoked"),
        ]),
        ("Logout revokes the access token immediately", "integration", "critical", "minimal", "yes", "AuthAndSecurityTests.Logout_RevokesAccessTokenImmediately", [
            ("`GET /auth/me`, then `POST /auth/logout`", "200, 204"),
            ("`GET /auth/me` again with the same (unexpired) token", "401"),
        ]),
        ("Tokens are stored only as hashes", "integration", "critical", "minimal", "yes", "AuthAndSecurityTests.Tokens_AreStoredOnlyAsHashes", [
            ("Sign in and read `refresh_tokens`", "the SHA-256 of the token is there, its value is not"),
        ]),
    ]),
    ("Password policy", [
        ("Weak passwords are rejected", "unit", "critical", "–", "yes", "PasswordPolicyValidatorTests.Validate_RejectsWeakPasswords", [
            ("Too short / common / containing the e-mail / containing the word \"pharmacy\"", "codes `password.too_short`, `password.common`, `password.contains_personal_data`"),
        ]),
        ("Long passphrase without composition rules", "unit", "high", "–", "yes", "PasswordPolicyValidatorTests.Validate_AcceptsLongPassphraseWithoutCompositionRules", [
            ("\"green bicycle rides along the river\"", "accepted"),
        ]),
        ("Composition rules and Unicode", "unit", "medium", "–", "yes", "PasswordPolicyValidatorTests.Validate_CompositionRulesAreConfigurable_AndLengthCountsUnicodeCharacters", [
            ("Require a digit and an uppercase letter", "`password.composition`"),
            ("12 Hebrew letters; normalization of \"ﬁ\"", "accepted; NFKC → \"fi\""),
        ]),
        ("Uniform response and account lockout", "unit", "critical", "–", "yes", "AuthServiceTests.Login_UnknownEmailWrongPasswordAndLockedAccount_GiveTheSameError", [
            ("5 wrong passwords", "account locked"),
            ("Correct password while locked, and an unknown e-mail", "the same response `auth.invalid_credentials`"),
            ("After 16 min", "login works"),
        ]),
        ("Password history", "unit", "high", "–", "yes", "AuthServiceTests.ChangePassword_RejectsReuseOfRecentPassword", [
            ("Change to a password from the history", "`password.reused`"),
        ]),
        ("Lockout counter and expiry", "unit", "high", "–", "yes", "ValueObjectAndEntityTests.User_LockoutAfterMaxAttempts_ExpiresAndCounterResets", [
            ("5 failures, check after 10 and 16 min", "locked / unlocked"),
            ("Another failure after the lockout expired", "counter = 1"),
        ]),
        ("Registration: weak password and e-mail taken (HTTP)", "integration", "high", "minimal", "yes", "AuthAndSecurityTests.Register_WeakPasswordIs400_DuplicateEmailIs409", [
            ("Register with the password \"unbelievable\"", "400, `errors.password` contains `password.common`"),
            ("Register with an existing e-mail", "409 `user.email_taken`"),
        ]),
        ("Lockout after 5 attempts (HTTP)", "integration", "critical", "minimal", "yes", "AuthAndSecurityTests.FiveFailedLogins_LockTheAccount_EvenForCorrectPassword", [
            ("5 wrong passwords, then the correct one", "401 `auth.invalid_credentials`"),
        ]),
    ]),
    ("Security", [
        ("Personal data is masked in logs", "unit", "high", "–", "yes", "ApiInfrastructureTests.MaskPii_HidesEmailsPhonesTokensAndPasswords", [
            ("Mask a text with an e-mail, a phone number, `access_token`, Bearer and a password", "none of the values remains"),
        ]),
        ("Anonymous endpoints are explicit", "architecture", "critical", "–", "yes", "ArchitectureTests.EveryController_RequiresAuthenticationByDefault_AnonymousEndpointsAreExplicit", [
            ("Collect actions with `[AllowAnonymous]`", "exactly the agreed list (login, catalog, webhook, JWKS, fake PSP)"),
        ]),
        ("401 / 403 / 404 for someone else's resource", "integration", "critical", "minimal", "yes", "AuthAndSecurityTests.Authorization_401WithoutToken_403ForWrongRole_404ForForeignOrder", [
            ("`GET /cart` without a token", "401 `auth.unauthorized`"),
            ("Customer → `/admin/audit`", "403"),
            ("Another customer → someone else's order", "404 `order.not_found`"),
        ]),
        ("Security headers", "integration", "high", "minimal", "yes", "AuthAndSecurityTests.Responses_CarrySecurityHeadersAndTraceId", [
            ("Any API request", "`nosniff`, `DENY`, CSP `default-src 'none'`, `X-Trace-Id` (32 characters)"),
        ]),
        ("Login rate limit", "integration", "high", "minimal", "yes", "AuthAndSecurityTests.LoginRateLimit_Returns429WithRetryAfter", [
            ("11 logins from one IP within a minute", "the 11th → 429 `request.rate_limited` + `Retry-After`"),
        ]),
    ]),
    ("Validation and error handling", [
        ("Exceptions map to status codes", "unit", "critical", "–", "yes", "ApiInfrastructureTests.ExceptionHandler_MapsExceptionTypesToStatusCodes", [
            ("Every exception type", "400/401/403/404/409/422/503/504/500; no details for 500"),
        ]),
        ("Validation and ProblemDetails (HTTP)", "integration", "critical", "minimal", "yes", "PlatformTests.Validation_UnknownFieldAndInvalidFields_Give400ProblemDetailsWithoutInternals", [
            ("Body with an unknown field", "400 `validation.failed`"),
            ("Empty street, ZIP code 1001, lat 95", "400, `errors` for `address.street/postalCode/latitude`, `traceId`, no `exception`"),
        ]),
    ]),
    ("Database", [
        ("SQLSTATE and constraint translation", "unit", "critical", "–", "yes", "PostgresErrorTranslatorTests.Translate_MapsSqlStateAndConstraint", [
            ("23505 with a constraint name, 23503, 23514, 55P03, 40P01, 40001, 57014", "the right exception and error code"),
        ]),
        ("Concurrency conflict and lost connection", "unit", "high", "–", "yes", "PostgresErrorTranslatorTests.Translate_ConcurrencyAndConnectivityFailures", [
            ("`DbUpdateConcurrencyException`, `NpgsqlException`, another exception", "`concurrency.conflict`, 503, not translated"),
        ]),
        ("Reads do not track entities", "integration", "high", "minimal", "yes", "PlatformTests.ReadServices_DoNotTrackEntities", [
            ("Call the read services in one scope", "`ChangeTracker.Entries()` is empty"),
        ]),
        ("Key queries use their indexes", "integration", "high", "minimal", "yes", "PlatformTests.KeyQueries_UseTheirIndexes", [
            ("`EXPLAIN` 6 key queries with `enable_seqscan = off`", "the expected index is in the plan"),
        ]),
        ("Database unavailable", "manual", "high", "demo", "no", "docker compose stop postgres", [
            ("Stop PostgreSQL and call a write endpoint", "after retries 503 `server.unavailable` + `Retry-After`"),
            ("`GET /health/ready`", "`Unhealthy`; `Healthy` again once the database is back"),
        ]),
    ]),
    ("Asynchronous messaging", [
        ("Domain events map to integration events", "unit", "high", "–", "yes", "IntegrationEventMapperTests.Map_AlwaysPublishesStatusChange_PlusSpecificEvent", [
            ("Changes to `Paid`, `Accepted`, `ReadyForPickup`, `Delivered`", "`OrderStatusChanged` + the specific event"),
        ]),
        ("Contracts use primitive types only", "unit", "medium", "–", "yes", "ContractTests.Contracts_UsePrimitivesOnly_NoDomainTypes", [
            ("Properties of the V1 contracts", "no types from the PharmacyDelivery namespaces"),
        ]),
        ("OrderPaid consumer (harness)", "unit", "high", "–", "yes", "ConsumerHarnessTests.OrderPaidConsumer_HandsOrderToPharmacyService", [
            ("Publish `OrderPaid`", "consumed; the pharmacy service is called"),
        ]),
        ("An unprocessable message goes to the DLQ", "manual", "medium", "demo", "no", "RabbitMQ Management", [
            ("Publish `OrderPaid` for an order that does not exist", "no retries for a non-transient error; the message is in `pharmacy-delivery-order-paid_error`"),
        ]),
    ]),
    ("JSON serialization", [
        ("Response format", "unit", "high", "–", "yes", "JsonOptionsTests.Serialize_UsesCamelCaseEnumStringsDecimalAndSkipsNulls", [
            ("Serialize a record with an enum, a decimal and a null", "camelCase, `\"inDelivery\"`, `6.49`, no `null`"),
        ]),
        ("Strict input, tolerant contracts", "unit", "high", "–", "yes", "JsonOptionsTests.Strict_RejectsUnknownMembers_TolerantSkipsThem", [
            ("Unknown field", "Strict: exception; Tolerant: skipped"),
        ]),
        ("Enums as numbers and JSON too deep", "unit", "medium", "–", "yes", "JsonOptionsTests.Deserialize_RejectsIntegerEnumsAndExcessiveDepth", [
            ("`\"status\": 3`; nesting depth 33", "rejected"),
        ]),
        ("Contracts round-trip", "unit", "high", "–", "yes", "ContractTests.Contracts_RoundTrip_AndTolerateUnknownFields", [
            ("Serialize and deserialize `OrderStatusChanged`, also with an extra field", "identical object"),
        ]),
    ]),
    ("Versioning", [
        ("API versions", "integration", "medium", "minimal", "yes", "PlatformTests.Versioning_ReportsSupportedVersions_RejectsUnknownVersion", [
            ("`GET /api/v1/…`", "header `api-supported-versions: 1.0`"),
            ("`GET /api/v9/…`", "404 `resource.not_found`"),
        ]),
        ("Health with version and environment", "integration", "medium", "minimal", "yes", "PlatformTests.Health_ReportsVersionEnvironmentAndDependencies", [
            ("`GET /health/ready`", "`Healthy`, environment, version, checks `postgres`, `redis`"),
        ]),
        ("Version from the git tag", "manual", "low", "–", "no", "MinVer", [
            ("`git tag v1.2.0`, build the image", "`/health` and Swagger show `1.2.0`; the image's OCI label is `1.2.0`"),
        ]),
    ]),
    ("Configuration and environments", [
        ("Every setting is documented", "documentation", "high", "–", "yes", "DocumentationTests.EverySetting_IsDocumented_AndEveryOptionsPropertyHasASetting", [
            ("Compare the keys of appsettings and the Options classes with docs/configuration.md", "no undocumented keys and no missing settings"),
        ]),
        ("Dangerous combinations are rejected", "unit", "critical", "–", "yes", "ApiInfrastructureTests.EnvironmentGuards_RejectDangerousCombinations", [
            ("Fake PSP in Production; ephemeral JWT key; Reset not allowed; reset with a real PSP", "validation fails"),
        ]),
        ("Invalid configuration stops the startup", "manual", "high", "–", "no", "dotnet run", [
            ("Start with `Checkout__DeliveryRadiusKm=0`", "the application exits at startup with a validation message"),
            ("Start with `ASPNETCORE_ENVIRONMENT=Production` and no JWT key", "startup aborted"),
        ]),
    ]),
    ("Seed data", [
        ("Order and several templates per class", "unit", "high", "–", "yes", "SeederRegistryTests.Registry_OrdersSeedersAndSupportsSeveralProfilesPerClass_CaseInsensitive", [
            ("Registry with classes in `alpha`/`beta`", "ordered by `Order`; template names are case-insensitive"),
        ]),
        ("Unknown template and duplicated Order", "unit", "medium", "–", "yes", "SeederRegistryTests.Registry_UnknownProfile_ListsAvailableOnes_DuplicateOrderIsRejected", [
            ("Template `gamma`", "error listing the available ones"),
            ("Two classes with the same `Order`", "registry build error"),
        ]),
        ("Application templates and deterministic ids", "unit", "medium", "–", "yes", "SeederRegistryTests.ApplicationRegistry_ContainsDocumentedTemplates_AndIdsAreDeterministic", [
            ("Application registry", "`demo`, `empty`, `large`; the same id for the same key"),
        ]),
        ("Reset through the admin endpoint", "integration", "high", "minimal", "yes", "PlatformTests.AdminSeedReset_ReplacesAllDataWithTheTemplate", [
            ("Create an order, call the `minimal` reset", "200; 0 orders, 3 products"),
        ]),
        ("Reset on startup of several instances", "manual", "medium", "–", "no", "docker compose", [
            ("`SEED_MODE=Reset docker compose up -d --scale api=3` (without the port mapping)", "one \"RESET done\" in the logs, the others \"Seed reset skipped\""),
        ]),
    ]),
    ("Observability", [
        ("Business spans and traceId", "integration", "high", "minimal", "yes", "PlatformTests.Checkout_ProducesTraceWithBusinessSpans_TraceIdMatchesResponse", [
            ("Check out, read `X-Trace-Id`", "the exporter has a `checkout` span with a `stock.reserve` child in that trace"),
        ]),
        ("End-to-end trace in the Aspire Dashboard", "manual", "medium", "demo", "no", "http://localhost:18888", [
            ("Place an order on the Test stand", "one trace: webhook → outbox → RabbitMQ → consumer → SignalR; the `pharmacy_delivery.*` metrics grow"),
        ]),
        ("Startup report: configuration tree with sources, environment variables, masked secrets", "unit", "high", "–", "yes", "StartupReportTests.Build_RendersTreeWithSourcesOfEveryValue_AndMasksSecrets", [
            ("Build the report from two configuration layers and environment variables", "a `├─`/`└─` tree: Host, Configuration (value ← source, the last layer wins), Environment variables"),
            ("Check the secrets", "passwords, secrets, tokens, the password in a connection string and credentials in a URL shown as `***`; `TokenRetentionDays`, `SigningKeyPath` shown in clear"),
            ("Turn off `IncludeEnvironmentVariables`", "no environment variables branch"),
        ]),
        ("Microsoft/System framework logs go to the console only", "unit", "high", "–", "yes", "NLogRulesTests.FrameworkLogs_NeverReachTheFile_ApplicationLogsDo", [
            ("Load the NLog rules from `appsettings.json` + `appsettings.{Test,Development,Production}.json`, log from the `Microsoft.*`, `System.*` and `PharmacyDelivery.*` categories", "only `PharmacyDelivery.*` logs reach the file (including the startup report)"),
            ("Check the console", "`Microsoft.Hosting.Lifetime` from Information, the other `Microsoft.*`/`System.*` from Warning"),
            ("Check the file name of the `file` target", "a directory per day and a file per level (`logs/{date}/{level}.log`)"),
        ]),
    ]),
    ("Architecture and DI", [
        ("Common has no dependencies", "architecture", "high", "–", "yes", "ArchitectureTests.Common_DependsOnNothing_AndHasNoPackages", [
            ("References of the PharmacyDelivery.Common assembly", "BCL only"),
        ]),
        ("Layers reference only the layers below", "architecture", "critical", "–", "yes", "ArchitectureTests.Layers_ReferenceOnlyTheLayersBelow", [
            ("Project references", "match DESIGN.md §4"),
        ]),
        ("The domain is framework-free", "architecture", "high", "–", "yes", "ArchitectureTests.Domain_IsPersistenceAndFrameworkIgnorant", [
            ("Dependencies of the domain types", "no EF, MassTransit, MediatR, Redis, ASP.NET"),
        ]),
        ("Services without EF Core", "architecture", "high", "–", "yes", "ArchitectureTests.Services_UseRepositoryAbstractionsOnly_NoEntityFramework", [
            ("Dependencies of the service layer", "repository abstractions only"),
        ]),
        ("Thin controllers", "architecture", "high", "–", "yes", "ArchitectureTests.Controllers_AreThin_NoDatabaseOrRepositoryAccess", [
            ("Dependencies of the controllers", "no DbContext, repositories, Redis"),
        ]),
        ("DI in one file", "architecture", "high", "–", "yes", "ArchitectureTests.DependencyInjection_IsConfiguredInOneFilePerHost", [
            ("Types that use `IServiceCollection`", "only `DependencyInjection` (API, simulator) and `Program`"),
        ]),
        ("The test plan matches the code", "documentation", "high", "–", "yes", "DocumentationTests.TestPlan_NumberingIsContinuous_AndMatchesAutomatedTests", [
            ("Parse this document and the test attributes", "continuous numbering, every \"yes\" case has a test, no tests pointing to unknown cases"),
        ]),
        ("Interface documentation", "architecture", "medium", "–", "yes", "InterfaceDocumentationTests.Interfaces_AndTheirMembers_AreDocumented", [
            ("XML docs of all interfaces in the `Abstract` folders", "every interface and member has a `<summary>`"),
        ]),
        ("Abstract/Concrete are grouped into area folders", "architecture", "medium", "–", "yes", "FolderLayoutTests.AbstractAndConcrete_AreGroupedIntoAreaFolders_NamespaceStaysAtLayerLevel", [
            ("`.cs` files in `Abstract`/`Concrete` of the product projects", "each in an area subfolder (`Concrete/Seed`, `Concrete/Jobs`, …)"),
            ("Namespace of those files", "`<Project>.Abstract` / `<Project>.Concrete` — the subfolder does not change the namespace"),
        ]),
    ]),
    ("Delivery", [
        ("Multistage image", "manual", "high", "–", "no", "CI: docker build", [
            ("`docker build -f src/PharmacyDelivery.Api/Dockerfile --build-arg APP_VERSION=1.0.0 .`", "chiseled image, non-root user, version label"),
            ("`docker build … --target test .`", "unit and architecture tests pass inside the build"),
        ]),
        ("Compose from scratch", "manual", "critical", "demo", "no", "README", [
            ("`docker compose up -d --build` on a clean machine", "all containers `healthy`, `/health/ready` = Healthy"),
        ]),
        ("Image healthcheck", "manual", "medium", "–", "no", "docker inspect", [
            ("`docker inspect --format '{{.State.Health.Status}}' pharmacy-delivery-api-1`", "`healthy` (`dotnet PharmacyDelivery.Api.dll --healthcheck`)"),
        ]),
        ("ReSharper code style", "manual", "medium", "–", "no", "CI", [
            ("`dotnet tool restore && dotnet jb cleanupcode …`", "no changes in `git diff`"),
            ("`dotnet jb inspectcode …`", "no warnings"),
        ]),
    ]),
    ("E2E scenario", [
        ("Demo script on the Test stand", "e2e", "critical", "demo", "no", "scripts/demo-order.py", [
            ("`python scripts/demo-order.py` with the simulator running", "the order goes through all statuses to `delivered`; the pharmacy and the ETA are shown"),
        ]),
        ("Demo page in the browser", "manual", "critical", "demo", "no", "http://localhost:5080", [
            ("Sign in as Emily, add products, place an order", "the pharmacy name and address and the ETA are shown"),
            ("Wait for the courier (pharmacy demo mode on)", "the courier marker moves on the map, the ETA updates, the status ends with \"delivered\""),
            ("Sign in as David", "the \"on the way\" order from the seed history — courier 6 drives live"),
        ]),
    ]),
    ("Audit", [
        ("Every state-changing action is audited or exempt", "architecture", "critical", "–", "yes", "ArchitectureTests.EveryStateChangingAction_IsAuditedOrExplicitlyExempt", [
            ("POST/PUT/DELETE actions of the controllers", "each has `[Audited]` or `[AuditExempt]`"),
        ]),
        ("Old and new values in the audit", "integration", "critical", "minimal", "yes", "PlatformTests.AuditedAction_RecordsOldAndNewValues_VisibleToAdmin", [
            ("The customer cancels an order", "200"),
            ("Admin: `GET /api/v1/admin/audit?entityType=Order&entityId=…`", "entry `order.cancel`: `Status` `AwaitingPayment → Cancelled`, user, traceId"),
        ]),
        ("Masking and rolled-back operations", "integration", "critical", "minimal", "yes", "PlatformTests.Audit_MasksSecrets_AndFailedRequestsLeaveNoEntry", [
            ("Register an account", "`PasswordHash` in the audit as `***`"),
            ("Cancel the same order a second time (422)", "no second `order.cancel` entry — the audit is rolled back with the transaction"),
        ]),
    ]),
]

MATRIX = [
    ("FR-1 Catalog and cart", ["7.1.1", "7.1.2", "7.2.1", "7.2.2"]),
    ("FR-2 Checkout with pharmacy and ETA", ["7.3.6", "7.3.8", "7.3.9", "7.4.1"]),
    ("FR-3 Payment and refund", ["7.5.5", "7.5.6", "7.5.8"]),
    ("FR-4 Pharmacy panel and reassignment", ["7.6.2", "7.6.3", "7.6.4"]),
    ("FR-5 Courier dispatch", ["7.7.2", "7.7.3", "7.7.4"]),
    ("FR-6 Live tracking", ["7.8.2", "7.8.5", "7.8.6", "7.23.2"]),
    ("FR-7 Accounts and sessions", ["7.10.5", "7.10.6", "7.11.7", "7.11.8"]),
    ("FR-8 Administration", ["7.1.2", "7.19.4", "7.24.2"]),
    ("NFR-1 No overselling", ["7.4.1", "7.4.2", "7.4.7"]),
    ("NFR-2 Tracking latency", ["7.8.4", "7.8.6"]),
    ("NFR-3 Availability", ["7.14.2", "7.14.5", "7.22.2"]),
    ("NFR-4 Idempotency", ["7.3.9", "7.5.8"]),
    ("NFR-5 Security", ["7.10.5", "7.11.1", "7.12.3", "7.12.5"]),
    ("NFR-6 Audit", ["7.24.1", "7.24.2", "7.24.3"]),
    ("NFR-7 Observability", ["7.20.1", "7.20.2", "7.20.3", "7.20.4"]),
    ("NFR-8 Privacy (GDPR/CCPA)", ["7.12.1", "7.24.3", "7.8.5"]),
]

HEADER = """# Test plan

The whole document uses hierarchical numbering: section `N`, area `N.M`, test case `N.M.K`, step `N.M.K.S`. A case identifier is its number with the `TC-` prefix (e.g. `TC-7.4.1`). Numbers are stable: a removed case stays marked as "retired", new cases are appended to the end of their area. The document is generated by `scripts/generate-test-plan.py`; the test `TC-7.21.7` checks that it matches the code.

## 1. Objective

Confirm that the system meets the requirements of [DESIGN.md](../DESIGN.md) §3 — in particular no overselling, correct pharmacy selection, safe payments, live courier tracking and account security — and that these properties are checked automatically on every change.

## 2. Scope

2.1 **In scope:** API v1, the SignalR hub, the service and domain layers, the database (locks, indexes, constraints), the bus (outbox/inbox), cache, background jobs, configuration and environments, seed data, the Docker image, the demo page.

2.2 **Out of scope:** performance tests under production load (query plans are measured on the `large` template — [database.md](database.md)), a real PSP adapter, mobile apps.

## 3. Test strategy

3.1 **Unit** — domain, services with Moq, utilities; no Docker, seconds.

3.2 **Architecture and documentation** — NetArchTest (layer dependencies, DI, audit), documentation matches the code.

3.3 **Integration** — Testcontainers: PostgreSQL 17 + PostGIS, Redis 7.2, RabbitMQ 4.1 and the API host (`WebApplicationFactory`); base class `IntegrationTestBase`, data through `[UseSeed("…")]`.

3.4 **E2E on the Test stand** — `scripts/demo-order.py` against docker compose.

3.5 **Manual** — demo page (map, live courier), infrastructure failures, Docker image.

All automated tests live in one project `PharmacyDelivery.Tests` (solution `src/PharmacyDelivery.Tests.slnx`); each has a `[Property("TestCase", "TC-…")]` attribute.

## 4. Environments

4.1 **`IntegrationTests`** — the environment of the automated tests: containers created for the run, background jobs off, high request limits (except in the rate limit test), ephemeral JWT key.

4.2 **`Test` stand** — docker compose with the `demo` data, the courier simulator and the Aspire Dashboard (E2E and manual tests).

## 5. Test data

5.1 `minimal` — pharmacy T-1 (24/7), 3 products with 50 units each, a customer, a second customer, a pharmacist, a courier, an admin.

5.2 `concurrency` — `minimal` + a scarce product (10 units, only at T-1) + 50 customers.

5.3 `reassign` — `minimal` + pharmacy T-2 (≈1 km away) with products 1–2 and its pharmacist.

5.4 `dispatch` — `minimal` + a courier 200 m from the pharmacy and a courier 3 km away; the base courier has a stale position.

5.5 `demo` — 8 pharmacies in New York City, 60 products, customers Emily and David, pharmacists, 6 couriers, order history.

5.6 `large` — 200 pharmacies, 5,000 products, 1M stock rows, 100k orders, 1M GPS points.

Password of every test account: `Fixture-Password-2026!` (automated tests), `Demo-Password-2026!` (stand).

## 6. Entry and exit criteria

6.1 **Entry:** the code builds without warnings (warnings as errors), Docker is available for the integration tests.

6.2 **Exit:** all automated tests are green; manual cases of critical priority are run on the Test stand; no open defects of critical or high priority; coverage does not drop compared to the previous run.

6.3 **Reports:** building the API image runs the unit/architecture/documentation tests and writes `junit.xml`, `tests.trx` and `coverage.cobertura.xml` (Dockerfile stage `test-results`); GitLab shows them in the MR test tab and in the diff (`.gitlab-ci.yml`). Unit test coverage (layers Common, Domain, Services, Messaging — `src/PharmacyDelivery.Tests/coverage.runsettings`) is currently about 54% of lines: Common 88%, Domain 78%, Messaging 57%, Services 42%; the rest of Services as well as Repository, Infrastructure and Api are covered by the integration tests.

## 7. Test cases

Each case: a title, a metadata line (identifier · type · priority · data · automation) and steps `N.M.K.S` with the expected result.
"""

FOOTER_RISKS = """
## 8. Risks

8.1 Integration tests depend on Docker and host memory — when memory is short, PostgreSQL in the container can be killed (OOM), which looks like hanging queries. Run them on a machine with ≥ 4 GB of free memory for Docker.

8.2 Asynchronous flows (RabbitMQ) — tests wait for a state with a timeout (`Eventually`), not for fixed delays.

8.3 Time — unit tests use a fixed "now" (`TestBase.Now`); integration tests use 24/7 pharmacies, so they do not depend on the time of day.

8.4 External resources of the demo page (OSM tiles, CDN) — verified manually only.

## 9. Requirements coverage matrix

| Requirement | Cases |
|---|---|
"""


def main():
    out = [HEADER]
    for m, (area, cases) in enumerate(SPEC, start=1):
        out.append(f"\n### 7.{m} {area}\n")
        for k, (title, kind, priority, data, auto, ref, steps) in enumerate(cases, start=1):
            number = f"7.{m}.{k}"
            reference = f"`{ref}`" if auto == "yes" else ref
            out.append(f"\n#### {number} {title}\n\n`TC-{number}` · {kind} · priority: {priority} · data: {data} · automation: {auto} ({reference})\n")
            for s, (step, expected) in enumerate(steps, start=1):
                out.append(f"- {number}.{s} {step} → {expected}")
    out.append(FOOTER_RISKS.rstrip())
    for requirement, cases in MATRIX:
        out.append(f"| {requirement} | " + ", ".join(f"`TC-{c}`" for c in cases) + " |")
    target = Path(__file__).resolve().parent.parent / "docs" / "test-plan.md"
    target.write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")
    total = sum(len(c) for _, c in SPEC)
    automated = sum(1 for _, c in SPEC for x in c if x[4] == "yes")
    print(f"{target}: {len(SPEC)} areas, {total} cases ({automated} automated)")


if __name__ == "__main__":
    main()
