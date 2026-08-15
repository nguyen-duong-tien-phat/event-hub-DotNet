# EventHub

A backend API for event ticketing and booking, built with ASP.NET Core (.NET 10) and PostgreSQL. The project's core focus: **preventing overselling of tickets under concurrent load**, using an atomic conditional-update pattern, verified with both automated tests and real concurrent load testing (k6).

## Stack

- **ASP.NET Core 10** (Web API, controllers)
- **PostgreSQL 16** (via Npgsql)
- **Entity Framework Core** (ORM, migrations)
- **JWT Bearer Authentication** (role-based authorization)
- **xUnit + Moq** (unit tests)
- **Docker + Docker Compose** (containerized API + database)

## Architecture

The project follows a layered architecture with a strict dependency direction: `Api → Core ← Infrastructure`.

```
EventHub.Api             HTTP layer: controllers, DTOs, JWT config, validation attributes
    │
    ▼
EventHub.Core             Domain layer: entities, enums, repository interfaces,
    ▲                     services (business logic), request models
    │
EventHub.Infrastructure   Data layer: DbContext, repository implementations,
                           EF Core migrations
```

**Core** has zero dependency on EF Core, ASP.NET Core, or Infrastructure — it only depends on repository *interfaces* it defines itself. Infrastructure implements those interfaces. This means the business logic (`BookingService`, `EventService`, etc.) can be unit tested with mocked repositories, with no database required.

### Request flow

```
Controller (thin — HTTP in/out only)
    → Service (business logic)
        → Repository interface (Core)
            → Repository implementation (Infrastructure, backed by EF Core)
```

## The core problem: concurrency-safe ticket booking

The central challenge this project is built around: **when N tickets remain and M concurrent requests try to book, exactly N should succeed — never more (overselling), never fewer (false rejections).**

### The solution

Booking uses an **atomic conditional UPDATE** rather than a read-then-write pattern:

```sql
UPDATE "Tickets"
SET "RemainingQuantity" = "RemainingQuantity" - @quantity
WHERE "Id" = @ticketId AND "RemainingQuantity" >= @quantity
```

This single SQL statement checks and decrements inventory atomically. The database serializes writes to the same row by design, so this pattern is correct under concurrency without needing an explicit application-level lock. It also avoids the "false conflict" problem of naive optimistic concurrency (version-token based approaches), where any successful write invalidates every other concurrent reader's version token — even when there was genuinely enough inventory for all of them.

The reservation and the resulting `Booking` record insert are wrapped in a single database transaction, so a failure between the two steps can't leave an orphaned reservation with no corresponding booking.

### Why not other approaches?

- **Pessimistic locking** (`SELECT ... FOR UPDATE`) is also correct here, but holds a row lock for the full duration of the transaction, creating a queuing bottleneck under high contention. The atomic UPDATE avoids this since the lock is only held for the instant of the write.
- **Naive optimistic concurrency** (comparing a row-version/`xmin` token) causes false failures: with 100 tickets and 50 concurrent requests, only the first writer succeeds per round — the other 49 fail and must retry, even though inventory was never actually exhausted.

### Proof

The concurrency guarantee is verified two ways:

1. **Automated tests** (`EventHub.Tests`) — `BookingService`'s success, sold-out, and not-found paths are covered with mocked repositories.
2. **Real load test** (k6) — 30 concurrent requests fired at a ticket with known remaining inventory; verified the exact expected number succeeded (matching remaining stock) and the rest received `409 Conflict`, with zero overselling.

## Features

- JWT authentication, role-based authorization (Attendee / Organizer / Admin)
- Full CRUD for Events, Tickets, Users, with pagination
- Concurrency-safe Booking creation
- Input validation via data annotations, including a custom `[FutureDate]` attribute
- Password hashing via `IPasswordHasher<T>`

## Running locally

```bash
docker compose up --build
```

This starts both the PostgreSQL database and the API. Once running:

- API: `http://localhost:8080`
- Swagger UI: `http://localhost:8080/swagger`

### Running migrations

```bash
cd EventHub.Infrastructure
dotnet ef database update --startup-project ../EventHub.Api
```

### Running tests

```bash
dotnet test
```

### Running the load test

```bash
k6 run load-test.js
```

(Requires a valid JWT and a real `ticketId` with known `RemainingQuantity`, set at the top of `load-test.js`.)

## Project structure

```
EventHub/
├── EventHub.Api/              Controllers, DTOs, validation attributes, JWT config
├── EventHub.Core/             Entities, enums, service layer, repository interfaces
├── EventHub.Infrastructure/   DbContext, repository implementations, migrations
├── EventHub.Tests/            xUnit tests
├── docker-compose.yml
├── Dockerfile
└── load-test.js
```

## What's next

- Redis caching for event listings + rate limiting
- Stripe payment integration
- CI pipeline (GitHub Actions running `dotnet test` on push)