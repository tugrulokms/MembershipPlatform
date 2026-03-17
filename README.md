# Membership Platform

A distributed Membership & Entitlement service built with .NET 10, demonstrating production-grade patterns for reliable event-driven systems.

## Architecture

```
                          ┌──────────────┐
                          │   API Layer  │  POST /subscriptions
                          │  (Minimal)   │  POST /subscriptions/{id}/cancel
                          └──────┬───────┘  GET  /users/{id}/entitlements
                                 │
                 ┌───────────────┼───────────────┐
                 │               │               │
          ┌──────▼──────┐ ┌─────▼──────┐ ┌──────▼──────┐
          │  Application│ │   Domain   │ │Infrastructure│
          │  (Handlers) │ │ (Entities) │ │ (EF/Rabbit/ │
          └─────────────┘ └────────────┘ │  Redis/PG)  │
                                         └──────┬──────┘
                                                │
            ┌───────────────┬───────────────┬────┘
            │               │               │
     ┌──────▼──────┐ ┌─────▼──────┐ ┌──────▼──────┐
     │  PostgreSQL  │ │  RabbitMQ  │ │    Redis    │
     │  (State +   │ │  (Events)  │ │  (Cache)    │
     │   Outbox)   │ │            │ │             │
     └─────────────┘ └────────────┘ └─────────────┘
```

## Distributed Patterns Implemented

### Transactional Outbox
Domain events are serialized into `OutboxMessage` rows within the same `SaveChanges()` transaction as the aggregate mutation. A background `OutboxProcessor` polls with `SELECT ... FOR UPDATE SKIP LOCKED` to publish messages to RabbitMQ, preventing the dual-write problem where a crash between "save" and "publish" loses the event.

### Inbox Deduplication
`BillingEventConsumer` checks `InboxMessage` by `EventId` before processing. With at-least-once delivery guarantees from RabbitMQ, duplicate messages are silently discarded, making consumers idempotent.

### Stale Event Detection
`Subscription.IsStaleEvent()` compares the incoming event's timestamp against `LastBillingEventTimestamp`. Out-of-order events (e.g., a delayed `PaymentSucceeded` arriving after a newer `PaymentFailed`) are rejected to prevent incorrect state transitions.

### Optimistic Concurrency with Retry
EF Core `RowVersion` on `Subscription` prevents lost updates when multiple consumer instances process events for the same aggregate concurrently. On `DbUpdateConcurrencyException`, the entity is detached, reloaded, and retried (up to 3 times).

### Dead Letter Queue with Replay
Three-tier failure handling: main queue -> retry with exponential backoff (3 attempts with jitter) -> DLQ -> replay (3x) -> PermanentlyFailed. DLQ messages capture the failure reason for debugging.

### Cache-Aside with Graceful Degradation
`GetEntitlementsHandler` checks Redis first (10-min TTL), falls back to PostgreSQL on miss. Cache failures are caught and logged without breaking business logic -- stale data expires via TTL at worst.

### Health Checks (K8s-Native)
- `/health` -- Liveness probe: process alive, no dependency checks
- `/ready` -- Readiness probe: verifies PostgreSQL, Redis, and RabbitMQ connectivity

### Correlation ID Propagation
HTTP `X-Correlation-Id` header -> Serilog structured log context -> RabbitMQ message properties. Enables tracing a request across synchronous API calls and asynchronous message processing.

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10 |
| Database | PostgreSQL 16 + EF Core |
| Messaging | RabbitMQ 3 (via RabbitMQ.Client 7.x) |
| Caching | Redis 7 (via StackExchange.Redis) |
| Logging | Serilog (structured JSON) |
| Tracing | OpenTelemetry + Jaeger |
| Testing | xUnit + NSubstitute + FluentAssertions + Testcontainers |
| CI | GitHub Actions |

## Getting Started

### Prerequisites
- [Docker](https://docs.docker.com/get-docker/) and Docker Compose

### Run everything with Docker Compose

```bash
cd infra
docker compose up --build
```

This starts PostgreSQL, Redis, RabbitMQ, Jaeger, and the Membership API. The API applies EF Core migrations automatically on startup.

| Service | URL |
|---------|-----|
| Membership API | http://localhost:8080 |
| RabbitMQ Management | http://localhost:15672 (guest/guest) |
| Jaeger UI | http://localhost:16686 |

### Run locally (without Docker for the app)

Start infrastructure only:
```bash
cd infra
docker compose up postgres redis rabbitmq jaeger
```

Then run the API:
```bash
cd src/MembershipService.Api
dotnet run
```

### API Endpoints

```bash
# Create a subscription
curl -X POST http://localhost:8080/subscriptions \
  -H "Content-Type: application/json" \
  -d '{"userId": 1, "planId": 1}'

# Get entitlements (cache-aside: Redis -> PostgreSQL)
curl http://localhost:8080/users/1/entitlements

# Cancel a subscription
curl -X POST http://localhost:8080/subscriptions/{id}/cancel

# Health checks
curl http://localhost:8080/health   # liveness
curl http://localhost:8080/ready    # readiness (checks all deps)
```

## Running Tests

```bash
# Unit tests (no infrastructure needed)
dotnet test tests/MembershipService.UnitTests

# Integration tests (requires Docker -- Testcontainers spins up Postgres, Redis, RabbitMQ)
dotnet test tests/MembershipService.IntegrationTests
```

## Project Structure

```
MembershipPlatform.sln
├── src/
│   ├── MembershipService.Domain/           # Entities, enums, domain events (zero dependencies)
│   ├── MembershipService.Application/      # Handlers, interfaces, DTOs
│   ├── MembershipService.Infrastructure/   # EF Core, RabbitMQ, Redis, health checks
│   └── MembershipService.Api/              # Minimal API endpoints, middleware
├── tests/
│   ├── MembershipService.UnitTests/        # Domain + handler tests (NSubstitute)
│   └── MembershipService.IntegrationTests/ # API tests (Testcontainers)
├── infra/
│   ├── docker-compose.yml                  # Full stack: PG + Redis + RabbitMQ + Jaeger + API
│   └── k8s/deployment.yaml                 # K8s manifest with liveness/readiness probes
├── benchmarks/                             # BenchmarkDotNet: cache hit vs miss latency
└── load-tests/                             # k6 scripts: subscription creation + entitlement reads
```

## Design Decisions

**Why Outbox over CDC?** Change Data Capture (Debezium) requires infrastructure overhead (Kafka Connect, schema registry). The outbox pattern achieves the same reliability guarantee with `FOR UPDATE SKIP LOCKED` and is self-contained within the application.

**Why Inbox over exactly-once?** RabbitMQ provides at-least-once delivery. True exactly-once requires distributed transactions (2PC) or idempotent consumers. The inbox pattern is simpler and performs better -- one `SELECT` per message vs. coordinated two-phase commit.

**Why RowVersion over pessimistic locking?** Optimistic concurrency (RowVersion) allows reads to proceed without blocking. Under low contention (typical for subscription updates), retries are rare. Pessimistic locking (`SELECT FOR UPDATE`) would hold row locks across the entire processing pipeline.

**Why background processor over event-driven outbox?** Postgres `LISTEN/NOTIFY` could replace polling, but adds complexity for marginal latency improvement. The 1-second poll interval is acceptable for eventual consistency patterns where events are processed asynchronously anyway.
