# Gym Management API

.NET 8 ASP.NET Core Web API — **source of truth** for a single-tenant gym: staff operations (Flutter) and a read-only customer portal (Next.js).

[Overview](#overview) · [Getting started](#getting-started) · [Architecture](#architecture--tech-view) · [Domain rules](#domain-rules-that-shape-the-code) · [API](#api) · [Testing](#testing) · [Ops](#operations)

> [!NOTE]
> Full business rules live in [`AGENTS.md`](AGENTS.md) and [`BRD_PRD.md`](BRD_PRD.md). Copy-paste request samples: [`docs/API-TEST-SAMPLES.md`](docs/API-TEST-SAMPLES.md).

## Overview

Reception scans a customer QR; the API validates the subscription, deducts a session when needed, and appends an attendance row — under concurrency that cannot go negative. Customers can scan the same QR on their phone to see status **without** deducting anything.

| Client | Access | Responsibility |
|--------|--------|----------------|
| Flutter staff app | JWT (`Staff` / `Admin`) | CRUD, check-in, payments, card print |
| Next.js portal | Public + rate limit | Read-only status by QR token |
| This API | — | All business logic, persistence, auth |

### Features

- Time-based and session-credit subscriptions (session count chosen **per sale**, not on the plan)
- Cryptographic QR tokens; staff reprint without rotate; reset rotates intentionally
- Atomic check-in with SQL applock + conditional session `UPDATE`
- Offline payment recording (decoupled from activation)
- RFC 7807 errors with machine-readable `reason` codes
- Structured logging, correlation IDs, health checks — no extra NuGet logging stack

## Getting started

**Requires:** .NET 8 SDK, SQL Server LocalDB (`(localdb)\MSSQLLocalDB`).

```bash
dotnet restore
dotnet run
```

Migrations and admin seed run at startup.

| | |
|--|--|
| HTTP | `http://localhost:5168` |
| Swagger | `http://localhost:5168/swagger` |
| Health | `GET /health`, `GET /health/ready` |
| Admin | `admin` / `Admin#12345!` |

> [!WARNING]
> Change `AdminSeed:Password` and `Jwt:Key` before any real deployment. Keep secrets out of git.

```bash
dotnet test Gym-Management.sln   # LocalDB integration + unit tests
```

## Architecture & tech view

Deliberately **not** Clean Architecture / MediatR / CQRS. One web project, plain folders, controllers + focused services — optimized for a single gym on shared hosting (Plesk/IIS, no shell).

```
Controllers/     HTTP + validation attributes + auth attributes
Services/        Check-in engine, status/overlap rules, QR, settings, audit
Domain/          EF entities + enums (no stored subscription status)
Data/            GymDbContext, indexes, migrations
Auth/            JWT issue, ProblemDetails for 401/403, admin seed
Validation/      Custom attributes + ErrorReasons catalog
Observability/   Correlation, request timing, console formatter, health, Swagger copy
```

### Patterns and solutions we chose

| Concern | Approach | Why |
|---------|----------|-----|
| **Layering** | Controllers → application services → EF Core | Enough separation without ceremony; all logic stays server-side for Flutter/Next |
| **Derived state** | Subscription status computed at read time (`SubscriptionStatus`) | Avoids stale stored enums; one precedence matrix (Cancelled → Scheduled → Active/Expired/Exhausted) |
| **Domain rules as pure functions** | `SubscriptionRules`, `SubscriptionStatus` | Exhaustive unit tests without the web host; controllers stay thin |
| **Check-in as a use-case service** | `ICheckInService` / `CheckInService` | Transaction + applock + atomic deduct + append-only log in one place |
| **Concurrency** | `sp_getapplock` (Exclusive, Transaction) per customer + `UPDATE … WHERE UsedSessions < TotalSessions` | FR-006: parallel scans on 1 remaining session → exactly one grant, never negative |
| **Domain vs transport errors** | Check-in denials = HTTP **200** + `result`/`reason`; auth/validation = ProblemDetails | Desk UX treats “denied” as an outcome, not a failed request |
| **ProblemDetails pipeline** | `ApiException` + middleware + JWT events + `InvalidModelStateResponseFactory` | One `reason` contract everywhere (`unauthorized`, `validation`, `overlap_conflict`, …) |
| **Validation** | DataAnnotations + custom `NotBlank` / `NotEmptyGuid` / `PhoneNumber` | Field-level `errors` map on 422; clear messages for Flutter |
| **AuthN/Z** | JWT bearer + role claims (`Staff` / `Admin`) | `[Authorize(Roles = Admin)]` for plans & settings |
| **Passwords** | `PasswordHasher<StaffUser>` | No extra crypto libraries |
| **QR identity** | Random 128-bit Base64Url + SHA-256 `TokenHash` for lookup; plaintext stored for staff reprint | Hash for secure scan lookup; staff list/detail/card can reprint; **only reset rotates** |
| **Session products** | Plan type `Session` = generic credit SKU; `totalSessions` on subscription create | Customers buy different pack sizes without a plan per count |
| **Time products** | Plan carries `DurationDays`; sub can override `endDate` | Catalog for months; renewals use start = day after previous end |
| **Clock / timezone** | `IGymClock` + Settings `TimezoneId` | “Today” for status/overlap is gym-local, storage is UTC |
| **Idempotent startup** | `Database.Migrate()` + settings seed + admin seed | Shared hosting has no EF CLI shell |
| **Data Protection** | Keys under `App_Data/keys` | Survives IIS app-pool recycle |
| **Public surface** | Fixed-window rate limit (10/min/IP) + minimal DTO (`First L.`) | Brute-force / scrape resistance without leaking PII |
| **CORS** | Single configured portal origin | Least privilege for the Next.js app |
| **Observability** | Custom console formatter, scopes, `ActivitySource`, `/health` + `/health/ready` | Built-in only (package lock); correlation via `X-Correlation-ID` |
| **Testing** | xUnit + `WebApplicationFactory` + **real LocalDB** per fixture | Applock/SQL semantics must match production; no SQLite for check-in |
| **API docs** | Swagger description = scenario guide + XML remarks on actions | Developers discover flows in `/swagger`, not only in markdown |

### Check-in pipeline (core)

```
token → hash lookup
  → begin transaction + sp_getapplock(customerId)
  → inactive? / duplicate granted scan?
  → resolve Active sub (else deny with derived reason)
  → session: conditional UPDATE UsedSessions
  → append AttendanceLog (grant or deny)
  → commit
```

Attendance is **append-only**. Payments never gate this path.

### Error contract (short)

| HTTP | `reason` |
|------|----------|
| 401 / 403 / 404 / 409 / 422 / 429 / 500 | `unauthorized` / `forbidden` / `not_found` / `overlap_conflict` / `validation` / `rate_limited` / `internal_error` |

Check-in denials stay on **200**: `token_unknown`, `customer_inactive`, `duplicate_scan`, `not_started`, `expired`, `no_sessions_remaining`, `no_active_subscription`.

## Domain rules that shape the code

- At most one **non-terminal** subscription (Active/Scheduled), with a controlled renewal exception for time plans.
- Session subs have **no end date**; they run until exhausted or cancelled.
- Status is never stored — always derived.
- Soft-archive customers (`IsActive = false`); never hard-delete for attendance history.
- Public status never deducts and never logs attendance.

## API

| Area | Highlights |
|------|------------|
| Auth | `POST /api/auth/login`, `GET /api/auth/me` |
| Customers | CRUD, archive, card, **token reset** |
| Plans | Admin create/update; Session plans have no fixed session count |
| Subscriptions | Create/cancel/list; Session create requires `totalSessions` |
| Payments | Offline Cash/Card/Transfer |
| Check-ins | Staff scan engine |
| Reports | Dashboard aggregates |
| Settings | Admin only |
| Public | `GET /api/public/status/{token}` |

Interactive docs (scenarios + remarks): run the app and open **Swagger**.  
Manual checklist of all flows: [`docs/API-TEST-SAMPLES.md`](docs/API-TEST-SAMPLES.md) · [`Gym-Management.http`](Gym-Management.http).

## Configuration

| Key | Purpose |
|-----|---------|
| `ConnectionStrings:Default` | SQL Server / LocalDB |
| `Jwt:Key` (≥ 32 chars), `Issuer`, `Audience`, `ExpiresHours` | Bearer auth |
| `AdminSeed:*` | Idempotent admin user |
| `Cors:PortalOrigin` | Allowed Next.js origin (default `http://localhost:3000`) |
| `Observability:JsonConsole` | JSON logs (also default in Production) |

## Testing

```bash
dotnet test Gym-Management.sln
```

- **Unit:** status matrix + overlap rules (`SubscriptionLogicTests`)
- **Integration:** LocalDB `GymTests_{guid}` per fixture — login, customers/QR, plans, overlap, check-in denials, **FR-006 concurrency**
- No Testcontainers / SQLite for the check-in path (applock requires SQL Server)

## Operations

- **Migrations at startup** — required for Plesk/IIS without shell access.
- Persist DataProtection keys under `App_Data/keys`.
- Prefer environment / host config for production secrets.
- New EF migration in dev: `dotnet ef migrations add <Name>`

## Project layout

```
Auth/  Controllers/  Data/  Domain/  Migrations/
Observability/  Services/  Validation/
Gym-Management.Tests/   LocalDB-backed integration suite
docs/API-TEST-SAMPLES.md
```
