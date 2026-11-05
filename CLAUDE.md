# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

.NET 10 solution (`EventHub.sln`). Run everything from the repo root unless noted.

```bash
docker compose up -d postgres redis mailpit floci   # dependencies only (run the API from the IDE / dotnet run)
docker compose up --build                     # full stack; API on :8080, Swagger at /swagger (Development, or Swagger__Enabled=true, which compose sets)

dotnet build
dotnet test                                   # CI runs: restore, build -c Release, test -c Release
dotnet test --filter "FullyQualifiedName~BookingFlowTests"              # one class
dotnet test --filter "FullyQualifiedName~BookingFlowTests.CreateAsync_AddsOnePercentServiceFee_AndChargesTheTotal"  # one test

# EF migrations: the DbContext lives in Infrastructure, the startup project is the API
cd EventHub.Infrastructure
dotnet ef migrations add <Name> --startup-project ../EventHub.API
dotnet ef database update --startup-project ../EventHub.API

k6 run load-test.js                           # concurrency/oversell test; needs a JWT and ticketId edited at the top of the file
```

- `dotnet run --project EventHub.API` must be started from a directory where `../.env` resolves (`Program.cs` calls `Env.Load("../.env")`), i.e. from `EventHub.API/`, as the IDE launch profiles do (`http://localhost:8080`).
- `docker-compose.yml` takes every setting from `.env` as `${VAR:-default}`, so change values in `.env`, never in the compose file. `.env` (gitignored; copy `.env.example`, which lists every variable) supplies `Jwt__Key`, `Tickets__Secret`, `Stripe__SecretKey`, `Stripe__WebhookSecret`, `DB_CONNECTION`, and, when running outside compose, the S3 settings (see Event images). The app throws at startup if `Jwt:Key`, `Tickets:Secret`, `S3:Bucket` or `S3:PublicBaseUrl` is empty. `appsettings.Development.json` is also gitignored; `appsettings.json` has no connection strings.
- `RedisRateLimiterTests` use `[RedisFact]`, which skips unless Redis is on `localhost:6379`. All other tests mock repositories and need no infrastructure.

## Architecture

Three layers, dependency direction `API → Core ← Infrastructure` (the API references both; Core references neither). Core holds entities, services, and the interfaces (`IRepository<>`, `IUnitOfWork`, `ICacheService`, `IRateLimiter`, `IPaymentService`, `IEmailSender`) that Infrastructure implements. Services are therefore unit-tested with Moq and no database. Controllers are thin; all business rules live in Core services.

Files are organized by feature folder in each project (`Bookings/`, `Auth/`, `Tickets/`, ...) and named `<Feature>.<Role>.cs` (`Booking.Service.cs`, `Booking.Repository.cs`, `Booking.Controller.cs`). Note the API project folder is `EventHub.API`.

All wiring is in `EventHub.API/Program.cs`. Several services (`TokenService`, `RefreshTokenService`, `PasswordResetService`, `TicketCodeSigner`) take plain config values (secrets, TTLs) via constructor and are registered with factory lambdas rather than `IOptions`.

### Errors and logging

- Controllers turn the errors they expect into status codes (`{ message }` bodies: 400/403/404/409/429). Anything else reaches `GlobalExceptionHandler` (`EventHub.API/Errors`): it logs the exception with method, path and request id, and answers 500 `{ message, traceId }`; the exception text never goes to the client. A client that hung up (`RequestAborted`) is logged at Information, not as an error. In Development the developer exception page runs first and shows the details.
- `UseHttpLogging` writes one line per request (method, path, status, duration; no query string, headers or bodies). It sits outside the exception handler so a 500 is logged too. Its category is raised to Information in `appsettings.json`.
- `EventHub.Core` has no logger. `BookingService.ExpireAbandonedBookingAsync` returns an `ExpiryResult` (how many it cancelled, and a failure per booking with the step that failed: `Cancel`, still Pending and retried next run, or `StopPayment`, cancelled but the payment may be open or paid), and `BookingExpiryJob` logs them.

### Health and proxies

- `GET /health/live` answers 200 while the process runs (no dependencies checked). `GET /health/ready` also checks PostgreSQL and Redis (`EventHub.API/Health`, 5 s timeout each) and answers 503 if either is down. Both are anonymous and are left out of the request log.
- `UseForwardedHeaders` runs first and reads `X-Forwarded-For` and `X-Forwarded-Proto`, but only from loopback and the addresses in `ForwardedHeaders__KnownProxies` / `ForwardedHeaders__KnownNetworks` (CIDR). Anyone else could fake their address and dodge the per-IP rate limits, so do not trust all proxies. Without a proxy, leave both empty.

### Booking and inventory (the core of the project)

- `TicketRepository.TryReserveAsync` oversell protection is a single atomic `UPDATE "Tickets" SET "RemainingQuantity" = "RemainingQuantity" - n WHERE "Id" = x AND "RemainingQuantity" >= n`, with the rows-affected count as the verdict. Don't replace it with read-then-write, a version token, or app-level locking (README explains why). `ReleaseAsync` is the inverse.
- `BookingService.CreateAsync` runs reserve + booking insert inside one `IUnitOfWork` transaction, then creates the Stripe PaymentIntent *after* commit so no DB transaction is held across a network call. If payment creation fails, the booking is cancelled and tickets released. Free bookings (total 0) skip payment and are `Confirmed` immediately. A 1% service fee is added on top.
- Idempotency: `(UserId, IdempotencyKey)` has a unique index; a replay or a race loser returns the original booking (`BookingInProgressException` if the winner has not yet attached a PaymentIntent).
- Booking status moves `Pending → Confirmed | Cancelled`. Confirmation comes from the Stripe webhook (`Webhook.Controller.cs`, signature-verified) or from the client-driven `ConfirmPaymentAsync`; both are idempotent. If a payment succeeds after the booking was cancelled, `RefundIfPaidAsync` refunds it.
- `BookingExpiryJob` (a `BackgroundService`, every minute) cancels `Pending` bookings older than `BookingService.HoldDuration` (15 min) and releases their tickets. Both background jobs resolve scoped services through `IServiceScopeFactory`. They live in Infrastructure under the namespace `EventHub.BackgroundJobs`.
- Ticket stock is only ever changed by atomic SQL: `TryReserveAsync`, `ReleaseAsync`, `TryAdjustTotalAsync` (editing the total) and `TryDeleteUnsoldAsync`. Never call `Update()` on a `Ticket` and save the whole row: it writes `RemainingQuantity` back from an earlier read and undoes sales made in between. `TicketService.UpdateAsync` relies on EF writing only the changed columns.
- Repositories share one scoped `DbContext` per request, so never run repository calls in parallel (`Task.WhenAll`).

### Tickets and check-in

Confirmed bookings expose one HMAC-signed code per admitted person (`TicketCodeSigner.Sign(bookingId, n)`, `TryVerify`). `CheckInService` verifies the code and admits each `(booking, number)` once, enforced by the `CheckIn` entity. The signing secret (`Tickets:Secret`) is deliberately separate from `Jwt:Key`.

### Auth

- Short-lived JWT access tokens (`TokenService`) plus DB-backed refresh tokens with rotation and reuse detection (`RefreshTokenService`). `JsonWebTokenHandler.DefaultInboundClaimTypeMap` is cleared in `Program.cs`, so claim names are the raw JWT ones (not the .NET URI claim types).
- Password reset: 6-digit code emailed through `IEmailSender`. `SmtpEmailSender` is used if `Smtp:Host` is set (docker-compose points it at Mailpit, inbox at http://localhost:8025); otherwise `LogEmailSender` writes the email to the log.
- `ExpiredCredentialsCleanupJob` hourly deletes expired refresh tokens and reset codes via `ExpiredCredentialsCleaner`.
- Roles: Attendee / Organizer / Admin. `EventAccess.EnsureCanManage` (Core/Events) is the one rule for "an admin, or the event's own organizer"; events, tickets and an event's booking list all use it. Services throw `UnauthorizedAccessException` and controllers answer 403. An attendee sees only their own bookings.
- `PATCH /users/{id}/role` (Admin) sets any role; an admin cannot lower their own, so one admin always remains. The role is baked into the access token, so a change reaches the user when the token is next refreshed.
- `PATCH /users/{id}/active` (Admin) blocks or unblocks an account (`User.IsActive`); an admin cannot block themselves. Blocking revokes the refresh tokens, login answers 403 (after a correct password) and refresh answers 401, but an access token already issued works until it expires. `GET /users` takes `search` (name or email) and `isActive`.
- `PATCH /bookings/{id}/cancel`: the buyer, an admin (any booking) or the event's organizer (`EventAccess.EnsureCanManage`). A paid booking is refunded.
- `GET /events/{id}/stats` (organizer or admin): bookings by status, ticket revenue (without the service fee), fees, tickets sold (confirmed bookings), held (in stock taken by pending bookings), check-ins. `GET /events` takes `sort` (`StartsAt`, `StartsAtDesc`, `Newest`) and `PATCH /events/{id}` takes `highlights`.

### Event images (S3)

- `IFileStorage` (Core) is implemented by `S3FileStorage` (Infrastructure, `AWSSDK.S3`). `UploadService` holds the rules: `Rules` maps each `UploadKind` to a folder and its allowed content types, so a new kind of upload is one more line there.
- Flow: `POST /events/image-upload` (Admin/Organizer) returns a presigned PUT URL (5 min) and the final `fileUrl`. The client PUTs the bytes straight to S3, then sends `fileUrl` as `imageUrl` on `POST`/`PATCH /events`. `EventService` rejects any `imageUrl` that is not under our storage (`UploadService.IsUploadedUrl`). The `Content-Type` is part of the signature, so the PUT must send exactly the type that was asked for, and no `Authorization` header. There is no size limit (a presigned POST would allow one).
- `DELETE /events/{id}` answers 409 if the event has any booking (`Booking.EventId` has no foreign key, so deleting would orphan paid bookings). Otherwise it deletes the event, clears the event cache, then deletes the poster *after* the commit. `IFileStorage.DeleteAsync` logs an S3 failure instead of throwing, and files not under our storage (seed data, external URLs) are never deleted.
- Config: `S3__Bucket` and `S3__PublicBaseUrl` (required), `S3__ServiceUrl` (set: an S3-compatible emulator such as Floci; empty: real AWS), and `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`/`AWS_REGION`, which the SDK reads itself (`test`/`test` for Floci; compose reads all of these from `.env` and falls back to the Floci values). The host in `S3__PublicBaseUrl` is saved into every event's `imageUrl`, so use an address the clients can reach *before* uploading (a phone or Android emulator cannot reach `localhost.floci.io`). `S3FileStorage` signs `http://` when `ServiceURL` is plain http; the SDK would otherwise sign `https://`.
- Floci (a local AWS emulator) runs from compose, with its data in the `floci-data` volume. Running `floci aws start` as well clashes on port 4566; that one keeps its data in `~/.floci/data/aws-dev` instead: point the `aws` CLI at it with `eval "$(floci aws env -o text)"`, then create the bucket and its CORS rule as in the README. Floci does not enforce bucket read policies. Browsers need CORS twice: `Cors__AllowedOrigins` for the API (default `http://localhost:5173`) and the bucket's CORS rule for the PUT.

### Redis

- `RedisCacheService`: cache-aside for the events list (the key holds every filter, including `organizerId`, under `EventService.CacheKeyPrefix`). Event writes and ticket create/update/delete clear it.
- `RedisRateLimiter.IsAllowedAsync(key, maxAttempts, window)`: token bucket. Callers in controllers build the keys (`rateLimit:login:ip:*`, `rateLimit:login:email:*`, `rateLimit:booking:{userId}`, forgot/reset password) and return 429 when it says no.

### Database

PostgreSQL via EF Core/Npgsql. `Program.cs` builds the `NpgsqlDataSource` once and shares it as a singleton (building it per request throws under concurrent requests); keep that when touching DbContext setup. Migrations are in `EventHub.Infrastructure/Migrations`. `seed-events.sql` and `seed/` contain sample data.

## Docs caveat

`README.md` is a portfolio overview and skips detail, so trust the code when they differ. It does cover running locally, the one-time Floci bucket and CORS setup, and who may do what.

## Commits

Do not add attribution lines to commit messages or pull request descriptions: no `Co-Authored-By: Claude ...`, no "Generated with Claude Code" or similar. Write them as plain text.
