# AGENTS.md — Gym Management Backend

Single-tenant Gym Management & Subscription backend (.NET REST API). It is the **source of truth** for a Flutter staff app (authenticated) and a Next.js read-only customer portal (public). Full business context lives in `BRD_PRD.md` — read it if a rule here seems arbitrary.

## Gates — must pass before reporting done

```bash
dotnet build Gym-Management.sln    # must succeed, zero warnings preferred
dotnet test Gym-Management.sln     # all green
```

**Never commit.** The orchestrator reviews and commits all work.

## Stack (fixed — do not substitute)

- .NET 8 (`net8.0`) ASP.NET Core Web API, controllers style. Existing scaffold: `Gym-Management.csproj` (Swashbuckle already referenced), solution `Gym-Management.sln`.
- EF Core with the `Microsoft.EntityFrameworkCore.SqlServer` provider. Migrations are applied **at startup** via `Database.Migrate()` (production is Plesk/IIS shared hosting with no shell).
- Dev/test database is **LocalDB**: `Server=(localdb)\MSSQLLocalDB`. There is **no Docker on this machine — never use Testcontainers**.
- JWT bearer auth, symmetric signing key from config.
- Test stack: **xUnit** in a new project `Gym-Management.Tests` (add to the solution).
- Allowed added packages only: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.AspNetCore.Authentication.JwtBearer`, xUnit stack (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`). Nothing else without orchestrator approval.
- Single web project with plain folders (`Domain/`, `Data/`, `Services/`, `Auth/`, `Controllers/`). **No** MediatR, CQRS, Clean Architecture layering, or additional projects.

## Locked business rules (implement exactly — do not reinterpret)

1. **Exactly one non-terminal subscription per customer.** Non-terminal = not cancelled, not expired (time-based), not exhausted (session-based).
2. **Time-based sub**: start date + end date (inclusive). **Session-based sub**: start date only, **no end date, no validity window** — lives until sessions run out or staff cancels it. The `Expired` state only applies to time-based subs; session subs go Active → Exhausted or Cancelled.
3. Creating a new subscription while a non-terminal one exists → reject with reason code `overlap_conflict`, **except** a future-dated renewal whose start date is on/after the day after the current *time-based* sub's end date (that sub is `Scheduled` and becomes usable when the old one expires). A live session-based sub blocks everything until staff explicitly cancels it.
4. **Statuses are derived at read time, never stored.** Precedence: Cancelled (`CancelledAtUtc != null`) → Scheduled (`StartDate > today` in gym timezone) → time-based: Expired (`today > EndDate`) | Active; session-based: Exhausted (`UsedSessions >= TotalSessions`) | Active.
5. **Check-in denial reason codes** (exact strings): `token_unknown`, `duplicate_scan`, `not_started`, `expired`, `no_sessions_remaining`, `no_active_subscription`. Also a customer-archive denial: `customer_inactive`.
6. **Session deduction is one conditional atomic UPDATE**: `UPDATE Subscriptions SET UsedSessions = UsedSessions + 1 WHERE Id = @id AND UsedSessions < TotalSessions` — 0 rows affected = deny `no_sessions_remaining`. Negative balances must be structurally impossible.
7. The whole check-in runs in one transaction serialized per customer via `sp_getapplock` (`@Resource` = customer id string, `@LockMode = 'Exclusive'`, `@LockOwner = 'Transaction'`).
8. **Attendance is append-only.** Every staff scan — granted AND denied — logs a row with result + reason. No updates, no deletes ever. Check-in order: token lookup → applock → duplicate check → resolve sub → deduct → insert log → commit.
9. Duplicate scan rule: deny `duplicate_scan` if the customer has a **granted** check-in within `DuplicateScanThresholdMinutes` (from Settings, default 15).
10. Renewal = a new subscription record; the old one simply becomes terminal. QR token belongs to the **customer** and survives renewals.
11. Payments are decoupled from activation — they never gate check-ins or subscription creation.
12. Customers are soft-archived (`IsActive = false`), never hard-deleted. Archived customer check-in → deny `customer_inactive`.
13. Timestamps stored UTC. All "today"/date comparisons use the IANA timezone from Settings (`TimezoneId`, default `UTC`).

## QR tokens

- 128-bit crypto-random (`RandomNumberGenerator`), Base64Url-encoded (~22 chars). No PII, non-sequential.
- Stored as **SHA-256 hash only** with a unique index; lookup by hash. Plaintext token is never persisted — returned exactly once per generation in the API response (create customer, reset token, card endpoint for printing).
- Reset = rotate (old token instantly dead). Raw token exposed **only** to authenticated staff endpoints, never in any public payload.

## Error & response contract

- RFC 7807 `ProblemDetails` for all errors, with an extra machine-readable string field `reason` (values above plus `overlap_conflict`, `validation`, `not_found`, `forbidden`, `unauthorized`).
- HTTP mapping: 401 unauthenticated, 403 wrong role, 404 unknown id (and unknown public token — with no detail leaked), 409 `overlap_conflict`, 422 `validation`, 429 rate limit.
- **Check-in domain denials return HTTP 200** with `{ result: "denied", reason, ... }` — a denial is a domain outcome, not a transport error. Only auth/infra failures use error statuses.
- Check-in success: `{ result: "granted", reason: null, customer: { fullName }, subscription: { type, status, remainingSessions?, endDate? } }`.

## Endpoints

Staff (JWT required):

| Method & path | Notes |
|---|---|
| `POST /api/auth/login` | `{username, password}` → `{token, role, fullName}` |
| `GET /api/customers?query=&page=&pageSize=` | search by name or phone, paged |
| `POST /api/customers` | response includes the raw token (once) for QR printing |
| `GET /api/customers/{id}` | detail: profile + subs + recent attendance + payments |
| `PUT /api/customers/{id}` | name/phone/notes |
| `POST /api/customers/{id}/archive` | soft archive |
| `POST /api/customers/{id}/token/reset` | rotate token, returns new raw token; writes audit log |
| `GET /api/customers/{id}/card` | `{ token, customerName, gymName }` for Flutter QR card printing |
| `GET/POST /api/plans`, `PUT /api/plans/{id}` | Time: `durationDays`. Session: generic credit product (no fixed `sessions` on the plan) |
| `POST /api/subscriptions` | `{customerId, planId, startDate, endDate?, totalSessions?, overridePrice?}`; Time: endDate defaults to start + DurationDays − 1; Session: **totalSessions required** |
| `POST /api/subscriptions/{id}/cancel` | body reason **required** → writes audit log |
| `GET /api/subscriptions?customerId=&status=` | status is the derived status |
| `POST /api/payments` | `{subscriptionId, amount, method, note?}` |
| `POST /api/checkins` | `{token}` — the core engine, rules 5–9 |
| `GET /api/reports/dashboard` | today's granted/denied counts, active / expired / exhausted sub counts, low-balance list (< `LowBalanceThreshold` sessions) |
| `GET/PUT /api/settings` | **Admin only** |

Public (no auth):

| `GET /api/public/status/{token}` | Rate-limited: fixed window **10 req/min per IP** (ASP.NET Core rate limiting middleware). Payload contains **only**: `gymName`, `customerName` ("First L." format), `subscriptionType`, `status`, `remainingSessions` (session subs), `endDate` (time subs). No IDs, no phone, no email, no staff data. Unknown/rotated token → 404 with no detail. Must never deduct or log attendance. |

## Roles

- **Staff**: all operational endpoints (check-ins, customers, subscriptions, payments, token reset, card).
- **Admin**: everything Staff does plus settings and plan management. JWT role claim. Seeded at startup from `appsettings.json` `AdminSeed` section (username `admin`; document the default password in README; idempotent seed).

## Data model

```
StaffUser(Id, FullName, Username unique, PasswordHash, Role, IsActive)
Customer(Id, FirstName, LastName, Phone, TokenHash unique, TokenRotatedAtUtc, IsActive, Notes, CreatedAtUtc, CreatedByStaffId?)
Plan(Id, Name, Type: Time|Session, DurationDays int?, Sessions int? unused, Price decimal, IsActive)
  // Time: DurationDays required. Session: generic credit product — TotalSessions is set on the Subscription, not the Plan.
Subscription(Id, CustomerId FK, PlanId FK, Type snapshot, StartDate date, EndDate date?, TotalSessions int?, UsedSessions int default 0, PricePaid decimal?, CancelledAtUtc?, CancelReason?, CreatedAtUtc, CreatedByStaffId)
AttendanceLog(Id, CustomerId, SubscriptionId?, StaffId, AtUtc, Result: Granted|Denied, Reason?, RemainingSessionsAfter?)
Payment(Id, SubscriptionId, CustomerId, Amount, Method: Cash|Card|Transfer, Note?, RecordedAtUtc, RecordedByStaffId)
Setting(Id, Key unique, Value)   // DuplicateScanThresholdMinutes, GymName, TimezoneId, LowBalanceThreshold
AuditLog(Id, AtUtc, StaffId, Action, EntityType, EntityId, Details?)
```

Indexes: `Customer.TokenHash` unique, `Customer.Phone`, `AttendanceLog (CustomerId, AtUtc desc)`, `Subscription (CustomerId)`.

## Testing rules

- Integration tests use a **real LocalDB** database created per test fixture (`Database=GymTests_{guid}`), migrated, seeded, dropped on dispose. **No SQLite** for anything touching check-in/concurrency/applock.
- The spec's FR-006 concurrency test is mandatory: N parallel `POST /api/checkins` against a session sub with 1 session remaining → exactly 1 granted, N−1 denied, `UsedSessions == TotalSessions`, never negative.
- Unit-test the derived-status matrix and the overlap rule exhaustively (every branch).
- Passwords hashed with ASP.NET Core `PasswordHasher<T>` — no extra libraries.

## Operational notes

- `Program.cs` must: run migrations at startup, persist DataProtection keys to `App_Data/keys` (shared-hosting app-pool recycles), enable CORS only for the configured portal origin (`Cors:PortalOrigin`, default `http://localhost:3000`), serve Swagger in Development.
- Config in `appsettings.json`: `ConnectionStrings:Default` (LocalDB in dev), `Jwt:Key` (≥ 32 chars) + `Jwt:Issuer` + `Jwt:Audience`, `AdminSeed`, `Cors:PortalOrigin`. Production values come from environment/appsettings on the host — keep secrets out of git.
