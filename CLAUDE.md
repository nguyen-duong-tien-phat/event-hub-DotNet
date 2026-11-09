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
- `docker-compose.yml` takes every setting from `.env` as `${VAR:-default}`, so change values in `.env`, never in the compose file. `.env` (gitignored; copy `.env.example`, which lists every variable) supplies `DB_CONNECTION` and, when running outside compose, the S3 settings (see Event images). `Jwt__Key`, `Tickets__Secret` and the two Stripe keys are not in `.env` or compose: they live in the `eventhub/app` secret (see Infrastructure and secrets), created from `infra/terraform.tfvars`, and compose points the API at it with `Aws__SecretId`. To run without Secrets Manager, set those four in `.env` and leave `Aws__SecretId` out (outside compose only). The app throws at startup if `Jwt:Key`, `Tickets:Secret`, `S3:Bucket` or `S3:PublicBaseUrl` is empty. `appsettings.Development.json` is also gitignored; `appsettings.json` has no connection strings.
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

A confirmed booking has one `Admission` row per admitted person (`Admissions` table; `Ticket` is the ticket *type*, so the per-person row has another name). `AdmissionRepository.IssueAsync` makes them when a booking becomes Confirmed (free create, `ConfirmPaymentAsync`, the Stripe webhook) under a Postgres advisory lock per booking, and does nothing if they exist; reading a Confirmed booking that has none issues them too. Each row has a random id, an 8 character `Code` (Crockford base32, typed by hand when a QR will not scan), and `CheckedInAt`/`CheckedInBy`: that is the only place check-in is stored. There is no ticket number. The QR value is `TicketCodeSigner.Sign(admissionId)` (`eh2.{id}.{hmac}`), built when a booking is sent to a client (`admissions[].code`). `CheckInService` takes the QR value or the short code, and `AdmissionRepository.TryCheckInAsync` is one `UPDATE ... WHERE "CheckedInAt" IS NULL` whose rows-affected count is the verdict. The signing secret (`Tickets:Secret`) is deliberately separate from `Jwt:Key`. A booking with a checked-in ticket cannot be cancelled (`BookingCheckedInException`, 409), and booking responses carry `checkedInCount` and `canCancel`. `GET /events/{id}/admissions` (`checkedIn`, `search`, paging; organizer or admin) lists who holds a ticket and whether they got in.

### Auth

- Short-lived JWT access tokens (`TokenService`) plus DB-backed refresh tokens with rotation and reuse detection (`RefreshTokenService`). `JsonWebTokenHandler.DefaultInboundClaimTypeMap` is cleared in `Program.cs`, so claim names are the raw JWT ones (not the .NET URI claim types).
- Password reset: 6-digit code emailed through `IEmailSender`. `SmtpEmailSender` is used if `Smtp:Host` is set (docker-compose points it at Mailpit, inbox at http://localhost:8025); otherwise `LogEmailSender` writes the email to the log.
- `ExpiredCredentialsCleanupJob` hourly deletes expired refresh tokens and reset codes via `ExpiredCredentialsCleaner`.
- Roles: Attendee / Organizer / Admin. `EventAccess.EnsureCanManage` (Core/Events) is the one rule for "an admin, or the event's own organizer"; events, tickets and an event's booking list all use it. Services throw `UnauthorizedAccessException` and controllers answer 403. An attendee sees only their own bookings.
- `POST /users` (Admin only) creates an account with an optional `role` (name, any letter case); left out it is an Attendee. `UserRole.Admin` is 0, so the field is nullable and `CreateAsync` falls back to Attendee, never to the enum default. Public `POST /auth/register` has no role field and always makes an Attendee.
- `POST /events` takes an optional `organizerId`: only an admin may name another account, which must be an active Organizer or Admin (`EventService.ResolveOrganizerAsync`: 403 for a non-admin, 400 for an account that cannot own events). `GET /users` also takes `role`, which the dashboard uses to list the organizers to pick from.
- `PATCH /users/{id}/role` (Admin) sets any role; an admin cannot lower their own, so one admin always remains. The role is baked into the access token, so a change reaches the user when the token is next refreshed.
- `PATCH /users/{id}/active` (Admin) blocks or unblocks an account (`User.IsActive`); an admin cannot block themselves. Blocking revokes the refresh tokens, login answers 403 (after a correct password) and refresh answers 401, but an access token already issued works until it expires. `GET /users` takes `search` (name or email) and `isActive`.
- `GET /bookings/managed` (Admin, Organizer) lists bookings across events, newest first with buyers, filtered by `eventId`, `userId` (one buyer's history) and `status`: an admin sees every booking, an organizer only those of their own events (403 when `eventId` is someone else's). The by-event list stays for one event.
- `PATCH /bookings/{id}/cancel`: the buyer, an admin (any booking) or the event's organizer (`EventAccess.EnsureCanManage`). A paid booking is refunded.
- `GET /events/sales?organizerId=&page=&pageSize=` (organizer or admin) answers `{ summary: { events, upcoming, past, ticketsSold, ticketRevenue, checkedIn, ticketsOfPastEvents, ticketsSoldOfPastEvents }, events: PagedResult<{ id, title, startsAt, location, imageUrl, category, totalQuantity, remainingQuantity, ticketsSold, ticketRevenue, checkedIn }> }` in one SQL pass (`EventRepository.GetSalesAsync`, CTEs per event, no booking loaded). An organizer gets only their own events (403 when naming someone else); an admin any organizer, or everyone's when `organizerId` is left out. The numbers mean what `/events/{id}/stats` means.
- **Keyset paging.** `GET /events`, `GET /bookings/managed` and `GET /events/{id}/admissions` take an optional `cursor` (the `nextCursor` of the previous page, opaque base64url of the last row's sort time and id: `Cursor` in Core/Common). With it the query is `WHERE (time, id) is past the cursor` with no OFFSET and `page` is ignored; without it the OFFSET page works as before, for old clients. Repositories fetch `pageSize + 1` rows and `PagedResult.From` drops the extra one and sets `nextCursor` (null on the last page); `totalCount` is still counted. A malformed cursor is a 400 `{ message }`. The events cache key includes the cursor.
- `GET /events/{id}/stats` (organizer or admin): bookings by status, ticket revenue (without the service fee), fees, tickets sold (confirmed bookings), held (in stock taken by pending bookings), check-ins. `GET /events` takes `sort` (`StartsAt`, `StartsAtDesc`, `Newest`) and `PATCH /events/{id}` takes `highlights`.

### Email

- `IEmailSender.SendAsync(to, subject, body, key)`: the `key` says what the email is about (`password-reset:{id}`, `booking-confirmed:{bookingId}`, `checked-in:{admissionId}`). Three registrations in `EventHub.API/Startup/Email.Setup.cs`: keyed `"raw"` really sends (`SmtpEmailSender`, or `LogEmailSender`), keyed `"deliver"` is `DeduplicatingEmailSender` over `"raw"` (`SentEmails.TryClaimAsync` = `INSERT ... ON CONFLICT DO NOTHING`, released when sending fails), and the unkeyed one is what services ask for.
- With `Sqs:EmailQueue` set, the unkeyed sender is `SqsEmailSender` (queues a JSON `QueuedEmail`) and the `EmailQueueWorker` background job receives, sends through `"deliver"`, then deletes the message; a failure leaves it for SQS to redeliver, and after 5 receives it goes to the dead-letter queue (`infra/sqs.tf`). Empty: `"deliver"` sends inside the request. The worker must ask for `"deliver"`, never the unkeyed sender (that would re-queue the email forever).
- **Every email lives in `EmailService` (Core/Email/Email.Service.cs), one method per email**: `PasswordResetAsync` (throws if it cannot be sent), `BookingConfirmedAsync` (called from `BookingService.OnConfirmedAsync`: free create, `ConfirmPaymentAsync`, the Stripe webhook) and `CheckedInAsync` (`CheckInService`). The subject, text and key are in the method; callers only say `emails.BookingConfirmedAsync(booking)`. A new email is a new method there. The booking and check-in ones swallow a failure to queue (Core has no logger) so they never fail the booking or the scan.

### Retry queues (SQS)

- `QueueWorker` (Infrastructure/Queues) is the base of every queue worker: long-polls a queue, runs `HandleAsync` in a scope per message, deletes the message only after it succeeded; a throw leaves it for SQS to redeliver, and after `maxReceiveCount = 5` it goes to the queue's DLQ (all in `infra/sqs.tf`). Delivery is at least once, so handlers must be safe to run twice. `EmailQueueWorker`, `PaymentRetryWorker` and `FileDeleteWorker` derive from it. `SqsSetup` (API/Startup) builds the client and wires the optional retry queues; each queue name in config (`Sqs__EmailQueue`, `Sqs__PaymentRetryQueue`, `Sqs__FileDeleteQueue`) is optional, and a name that does not exist stops the API at startup.
- **Stripe stop/refund.** `BookingService.StopPaymentOrQueueRetryAsync` runs after a booking is cancelled (`CancelAsync`, `ExpireAbandonedBookingAsync`); if Stripe fails and `IPaymentRetryQueue` exists, the booking id is queued instead of failing (no queue: the error is thrown as before). `PaymentRetryWorker` calls `RetryStopPaymentAsync`, which repeats `StopPaymentAsync` for a Cancelled booking and throws while Stripe still fails (the refund uses the Stripe idempotency key `refund-{bookingId}`).
- **Poster delete.** When `S3FileStorage.DeleteAsync` fails and `IFileDeleteQueue` exists, the key is queued; `FileDeleteWorker` deletes it (deleting a missing key succeeds).
- `ExpiredCredentialsCleaner` also deletes `SentEmails` keys older than 30 days (`SentEmailRetention`, longer than SQS keeps a message).
- Compose gives the Floci service the network alias `localhost.floci.io`, so the API container reaches Floci by the same host name the browser uses (`S3__ServiceUrl`); without it server-side S3 calls from the container fail.

### Event images (S3)

- `IFileStorage` (Core) is implemented by `S3FileStorage` (Infrastructure, `AWSSDK.S3`). `UploadService` holds the rules: `Rules` maps each `UploadKind` to a folder and its allowed content types, so a new kind of upload is one more line there.
- Flow: `POST /events/image-upload` (Admin/Organizer) returns a presigned PUT URL (5 min) and the final `fileUrl`. The client PUTs the bytes straight to S3, then sends `fileUrl` as `imageUrl` on `POST`/`PATCH /events`. `EventService` rejects any `imageUrl` that is not under our storage (`UploadService.IsUploadedUrl`). The `Content-Type` is part of the signature, so the PUT must send exactly the type that was asked for, and no `Authorization` header. There is no size limit (a presigned POST would allow one).
- `DELETE /events/{id}` answers 409 if the event has any booking (`Booking.EventId` has no foreign key, so deleting would orphan paid bookings). Otherwise it deletes the event, clears the event cache, then deletes the poster *after* the commit. `IFileStorage.DeleteAsync` logs an S3 failure instead of throwing, and files not under our storage (seed data, external URLs) are never deleted. `PATCH /events/{id}` that changes `imageUrl` deletes the old poster the same way (after the commit and the cache clear, our uploads only, never when the URL is unchanged).
- Config: `S3__Bucket` and `S3__PublicBaseUrl` (required), `S3__ServiceUrl` (set: an S3-compatible emulator such as Floci; empty: real AWS), and `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`/`AWS_REGION`, which the SDK reads itself (`test`/`test` for Floci; compose reads all of these from `.env` and falls back to the Floci values). The host in `S3__PublicBaseUrl` is saved into every event's `imageUrl`, so use an address the clients can reach *before* uploading (a phone or Android emulator cannot reach `localhost.floci.io`). `S3FileStorage` signs `http://` when `ServiceURL` is plain http; the SDK would otherwise sign `https://`.
- Floci (a local AWS emulator) runs from compose, with its data in the `floci-data` volume. Running `floci aws start` as well clashes on port 4566; that one keeps its data in `~/.floci/data/aws-dev` instead: point the `aws` CLI at it with `eval "$(floci aws env -o text)"`, then create the bucket and its CORS rule as in the README. Floci does not enforce bucket read policies. Browsers need CORS twice: `Cors__AllowedOrigins` for the API (default `http://localhost:5173`) and the bucket's CORS rule for the PUT.

### Infrastructure and secrets (`infra/`)

- `infra/` is Terraform pointed at Floci only (`providers.tf` redirects every endpoint to localhost; never real AWS). Run it from `infra/`: `terraform apply`. It describes the image bucket and its CORS rule, the `eventhub-api` IAM role and policy, and the `eventhub/app` secret. State (`terraform.tfstate`) and `terraform.tfvars` (the secret values) are gitignored; `.terraform.lock.hcl` is committed. The state holds secrets in plain text.
- At startup `Program.cs` reads the secret named by `Aws__SecretId` from Secrets Manager (`Aws__ServiceUrl`: Floci; empty: real AWS) and adds it as the last configuration source, so it overrides `.env`. Its keys use `:` (`Jwt:Key`, `Tickets:Secret`, `Stripe:SecretKey`, `Stripe:WebhookSecret`). Unset: nothing is read and the keys must come from `.env`. Read once, so a changed secret needs a restart. Compose always sets `Aws__SecretId` (default `eventhub/app`) and points `Aws__ServiceUrl` at `http://floci:4566`, so the API container does not start before `terraform apply` has created the secret.

### Redis

- `RedisCacheService`: cache-aside for the events list (the key holds every filter, including `organizerId`, under `EventService.CacheKeyPrefix`). Event writes and ticket create/update/delete clear it.
- `RedisRateLimiter.IsAllowedAsync(key, maxAttempts, window)`: token bucket. Callers in controllers build the keys (`rateLimit:login:ip:*`, `rateLimit:login:email:*`, `rateLimit:booking:{userId}`, forgot/reset password) and return 429 when it says no.

### Database

PostgreSQL via EF Core/Npgsql. `Program.cs` builds the `NpgsqlDataSource` once and shares it as a singleton (building it per request throws under concurrent requests); keep that when touching DbContext setup. Migrations are in `EventHub.Infrastructure/Migrations`. `seed-events.sql` and `seed/` contain sample data.

## Docs caveat

`README.md` is a portfolio overview and skips detail, so trust the code when they differ. It does cover running locally, the one-time Floci bucket and CORS setup, and who may do what.

## Commits

Do not add attribution lines to commit messages or pull request descriptions: no `Co-Authored-By: Claude ...`, no "Generated with Claude Code" or similar. Write them as plain text.

## Code lookup: graphify first

- Read the graph first (`graphify-out/GRAPH_REPORT.md` and the graphify skill: query, path, explain) every time you need to understand, locate or check source code; open the files it points to afterwards. No blind grep/find sweeps, and tell subagents to use graphify too.
- Compare the graph date (`graphify-out/graph.json`) with `git log -1`; if code changed since, run `graphify update .` first (code only, no LLM cost).
- After finishing any new feature (endpoint, service, entity, job, screen, or a change in how pieces connect), run `graphify update .` before the final reply. `graphify-out/` is gitignored; never commit it.
