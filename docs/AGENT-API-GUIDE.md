# Gym Management API — Agent Integration Guide

> **For AI coding agents.** This file is the complete integration contract for the Gym Management backend. It is self-contained: everything an agent needs to wire up a frontend (Flutter staff app or Next.js public portal) without reading the C# source. If this file and the API ever disagree, the API wins — report the discrepancy.

- **Base URL (dev):** `http://localhost:5168`
- **Swagger UI:** `http://localhost:5168/swagger` · **OpenAPI JSON:** `/swagger/v1/swagger.json`
- **Health:** `GET /health` (liveness) · `GET /health/ready` (readiness, checks DB)
- **API version:** v1 · **Protocol:** HTTPS in prod, HTTP in dev

---

## 1. Auth (JWT Bearer)

All endpoints require auth **except**: `POST /api/auth/login`, `GET /api/public/status/{token}`, `/health*`.

```http
POST /api/auth/login
Content-Type: application/json

{ "username": "admin", "password": "Admin#12345!" }
```

**200** →
```json
{ "token": "<jwt>", "role": "Admin", "fullName": "System Administrator" }
```

Then send on every request:
```http
Authorization: Bearer <token>
```

- Tokens are JWTs with a short lifetime; there is **no refresh endpoint** — just log in again on 401.
- `GET /api/auth/me` validates the current token (returns `{ id, username, role, fullName }`).
- Default seeded admin: `admin` / `Admin#12345!` (dev).

### Roles

| Role | Access |
|------|--------|
| `Staff` | Customers, check-ins, subscriptions, payments, reports |
| `Admin` | Everything Staff can, **plus** `POST/PUT /api/plans` and `GET/PUT /api/settings` |

Staff calling an Admin endpoint gets `403` with `reason: "forbidden"`.

---

## 2. Conventions (read this before sending requests)

- **JSON casing:** camelCase (`firstName`, `startDate`). Exception: Settings API uses literal string keys (`"GymName"`).
- **Dates:** `yyyy-MM-dd` for all date fields (`startDate`, `endDate`). Timestamps are ISO 8601 UTC (`createdAtUtc`, `atUtc`).
- **Enums are strings, exact casing:** plan `type`: `Time` \| `Session` · payment `method`: `Cash` \| `Card` \| `Transfer`.
- **IDs:** GUIDs.
- **Debugging:** send `X-Correlation-ID` header — it's echoed back and appears in server logs.

---

## 3. Error contract (every failure)

All errors are RFC 7807 `application/problem+json` with a machine-readable `reason`:

```json
{
  "title": "Conflict",
  "status": 409,
  "detail": "Customer already has an active subscription.",
  "instance": "/api/subscriptions",
  "reason": "overlap_conflict"
}
```

| HTTP | `reason` | Typical cause |
|------|-------------|----------------|
| 401 | `unauthorized` | Missing/expired token |
| 403 | `forbidden` | Role too low (Admin-only endpoint) |
| 404 | `not_found` | Unknown id/token (public portal: deliberately no detail) |
| 409 | `overlap_conflict` | Subscription overlap (see §6) |
| 422 | `validation` | Field validation failed |
| 429 | `rate_limited` | Public portal: >10 req/min/IP |

**422 adds an `errors` map:**
```json
{
  "status": 422,
  "reason": "validation",
  "errors": { "phone": ["phone must be a valid phone number (5–30 characters; digits and + - ( ) spaces)."] }
}
```

---

## 4. ⚠️ Check-in is the #1 integration gotcha

`POST /api/checkins` returns **HTTP 200 even when the scan is DENIED**. Auth failures are still 401/422 — but every domain outcome is 200. **Read `result`, not the status code.**

```http
POST /api/checkins
Authorization: Bearer <token>
Content-Type: application/json

{ "token": "<customer QR token>" }
```

Granted:
```json
{
  "result": "granted",
  "reason": null,
  "customer": { "fullName": "Ada Lovelace" },
  "subscription": { "type": "Session", "status": "Active", "remainingSessions": 9, "endDate": null }
}
```

Denied (still HTTP 200):
```json
{ "result": "denied", "reason": "duplicate_scan", "customer": null, "subscription": null }
```

**Denial reasons (exact strings):**
`token_unknown` · `customer_inactive` · `duplicate_scan` · `not_started` · `expired` · `no_sessions_remaining` · `no_active_subscription`

`duplicate_scan` fires when the same customer had a **granted** check-in within `DuplicateScanThresholdMinutes` (setting, default 15). Staff UI should show the denial reason prominently — it's a normal flow, not an error.

---

## 5. Endpoint reference

### Auth — `Auth`
| # | Method & path | Auth | Purpose |
|---|---|---|---|
| 1 | `POST /api/auth/login` | — | Get JWT. 401 bad creds · 422 blank fields |
| 2 | `GET /api/auth/me` | Any | Current user; token health-check. 401 if invalid |

### Customers — `Customers` (Staff+)
| # | Method & path | Notes |
|---|---|---|
| 3 | `GET /api/customers?query=&page=1&pageSize=20` | Search by name/phone. `pageSize` 1–100. Returns `{ items[], page, pageSize, total }` |
| 4 | `POST /api/customers` | Body: `{ firstName, lastName, phone, notes? }` → **includes QR `token`**. 422 on bad fields |
| 5 | `GET /api/customers/{id}` | Detail: profile + `subscriptions[]` + `recentAttendance[]` + `payments[]` + `token`. 404 unknown |
| 6 | `PUT /api/customers/{id}` | Full update: `{ firstName, lastName, phone, notes? }`. Does **not** touch token |
| 7 | `POST /api/customers/{id}/archive` | Soft-delete. Check-ins afterwards → `denied / customer_inactive`. 200 returns updated summary |
| 8 | `POST /api/customers/{id}/token/reset` | **Only** rotation path. Old QR dies instantly. 200 → `{ "token": "<new>" }` |
| 9 | `GET /api/customers/{id}/card` | Card print payload `{ token, customerName, gymName }`. **Never rotates** |

**Customer shape:**
```json
{ "id": "guid", "firstName": "Ada", "lastName": "Lovelace", "phone": "01011112222",
  "isActive": true, "token": "<qr-token>", "createdAtUtc": "2026-08-27T15:30:00Z" }
```
Detail adds `notes` and the three nested lists. Subscription items inside detail:
```json
{ "id": "guid", "type": "Time", "status": "Active", "startDate": "2026-08-01",
  "endDate": "2026-08-31", "totalSessions": null, "usedSessions": null,
  "remainingSessions": null, "pricePaid": 500, "createdAtUtc": "..." }
```

### QR token rules (critical for the staff app)
- `token` is returned by: list, detail, create, card, reset.
- **Only `token/reset` changes it.** Reopening detail or reprinting a card is always safe.
- The raw token is the QR payload. Store it; it doubles as the portal URL segment (§5 Public).

### Plans — `Plans`
| # | Method & path | Auth | Notes |
|---|---|---|---|
| 10 | `GET /api/plans` | Staff+ | List all |
| 11 | `POST /api/plans` | **Admin** | `{ name, type, durationDays?, sessions?, price }` |
| 12 | `PUT /api/plans/{id}` | **Admin** | `{ name, durationDays?, sessions?, price, isActive }` — set `isActive: false` to retire |

**Plan-shape rules (422 if violated):**
- `Time` plan → **requires** `durationDays` (1–3650). `sessions` ignored.
- `Session` plan → **must NOT** have `sessions` or `durationDays`. It's a generic credit product; the count is chosen per subscription, not per plan. Send `price` (can be 0).

**Plan shape:** `{ "id": "guid", "name": "Monthly", "type": "Time", "durationDays": 30, "sessions": null, "price": 500, "isActive": true }`

### Subscriptions — `Subscriptions` (Staff+)
| # | Method & path | Notes |
|---|---|---|
| 13 | `GET /api/subscriptions?customerId=&status=` | `status` filter is the derived string (`Active`, `Scheduled`, `Expired`, `Exhausted`, `Cancelled`) |
| 14 | `POST /api/subscriptions` | Create/renew. 404 unknown customer/plan · 409 overlap · 422 shape |
| 15 | `POST /api/subscriptions/{id}/cancel` | `{ "reason": "..." }` (required, ≤500 chars). Writes audit log. Idempotency: cancelling twice → 422 |

**Create body:**
```json
{ "customerId": "<guid>", "planId": "<guid>", "startDate": "2026-08-27",
  "totalSessions": 10, "overridePrice": 80 }
```
- **Time plan:** do **not** send `totalSessions`. `endDate` = start + `durationDays` − 1 (inclusive), or send explicit `endDate`.
- **Session plan:** **requires** `totalSessions` (1–10000). Must **not** have `endDate`.
- `overridePrice` (optional) replaces the plan price for this sale.

**Subscription shape:** `{ id, customerId, planId, type, status, startDate, endDate, totalSessions, usedSessions, remainingSessions, pricePaid, createdAtUtc }` — `remainingSessions` only for Session type; `endDate` only for Time.

### Payments — `Payments` (Staff+)
| # | Method & path | Notes |
|---|---|---|
| 16 | `POST /api/payments` | `{ subscriptionId, amount, method, note? }`. `amount` 0.01–1,000,000. **Never gates check-in** — payments are bookkeeping only |

**Payment shape:** `{ id, subscriptionId, customerId, amount, method, note, recordedAtUtc }`

### Reports — `Reports` (Staff+)
| # | Method & path | Notes |
|---|---|---|
| 17 | `GET /api/reports/dashboard` | `{ todayGranted, todayDenied, activeSubscriptions, expiredSubscriptions, exhaustedSubscriptions, lowBalance[] }` |

`lowBalance[]` items: `{ customerId, customerName, subscriptionId, remainingSessions }` — active session subs under the low-balance threshold, sorted ascending.

### Settings — `Settings` (**Admin only**)
| # | Method & path | Notes |
|---|---|---|
| 18 | `GET /api/settings` | Key/value map (all values are strings) |
| 19 | `PUT /api/settings` | Partial — send only keys you want to change |

```json
{ "GymName": "Iron Temple", "TimezoneId": "Africa/Cairo",
  "DuplicateScanThresholdMinutes": "15", "LowBalanceThreshold": "3" }
```

### Public portal — `Public` (no auth)
| # | Method & path | Notes |
|---|---|---|
| 20 | `GET /api/public/status/{token}` | Customer self-check. **Rate limit 10 req/min/IP → 429** |

**200:**
```json
{ "gymName": "Iron Temple", "customerName": "Ada L.",
  "subscriptionType": "Time", "status": "Active",
  "remainingSessions": null, "endDate": "2026-08-31" }
```
- Unknown/expired token → `404` with **no detail** (deliberate: no enumeration).
- `customerName` is `First L.` — no phone, no full last name, no ids.
- Never mutates anything (no session deduction, no attendance row).

### Health
| # | Method & path | Notes |
|---|---|---|
| 21 | `GET /health`, `GET /health/ready` | Unauthenticated probes for uptime monitors |

---

## 6. Business rules the frontend must respect

1. **One live subscription per customer.** Creating another while one is non-terminal → `409 overlap_conflict`.
   - **Exception (renewal):** a Time sub starting **on/after the day after** the current Time sub's `endDate` is allowed — it comes back `status: "Scheduled"` and activates automatically when the old one expires.
   - **A live Session sub blocks everything** (even future-dated). To sell a new pack: `POST /api/subscriptions/{id}/cancel` with a reason first, then create.
2. **Status is computed, never stored:** `Active`, `Scheduled`, `Expired` (Time only), `Exhausted` (Session only), `Cancelled`. Precedence: Cancelled → Scheduled → Expired/Exhausted → Active. Don't try to persist status client-side.
3. **Time subs expire by date; Session subs only by usage or cancellation** — a Session sub has no end date and no validity window.
4. **Customers are soft-archived, never deleted.** Archived → check-ins deny `customer_inactive`. There is no un-archive endpoint yet.
5. **QR token belongs to the customer**, survives renewals and profile edits. Rotating it invalidates the old QR *and* the old portal link instantly.
6. **Payments never gate anything.** A customer can check in with zero payments recorded.
7. **Dates are evaluated in the gym's timezone** (`TimezoneId` setting), not the client's. Send plain `yyyy-MM-dd`; don't pre-convert.

---

## 7. CORS (web frontends only)

The API allows exactly one origin: the Next.js portal (`Cors:PortalOrigin`, default `http://localhost:3000`), all headers/methods, no credentials. Requests from any other origin will fail in the browser. Dev tools like Postman/curl are unaffected.

---

## 8. Agent quick-start (copy-paste sequence)

```bash
BASE=http://localhost:5168

# 1. Login
TOKEN=$(curl -s $BASE/api/auth/login -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"Admin#12345!"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')

AUTH="Authorization: Bearer $TOKEN"

# 2. Create a Time plan (Admin)
PLAN=$(curl -s $BASE/api/plans -H "$AUTH" -H 'Content-Type: application/json' \
  -d '{"name":"Monthly","type":"Time","durationDays":30,"price":500}')
PLAN_ID=$(echo $PLAN | python -c 'import sys,json;print(json.load(sys.stdin)["id"])')

# 3. Create a customer (response includes the QR token)
CUST=$(curl -s $BASE/api/customers -H "$AUTH" -H 'Content-Type: application/json' \
  -d '{"firstName":"Ada","lastName":"Lovelace","phone":"01011112222"}')
CUST_ID=$(echo $CUST | python -c 'import sys,json;print(json.load(sys.stdin)["id"])')
QR_TOKEN=$(echo $CUST | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')

# 4. Subscribe
SUB=$(curl -s $BASE/api/subscriptions -H "$AUTH" -H 'Content-Type: application/json' \
  -d "{\"customerId\":\"$CUST_ID\",\"planId\":\"$PLAN_ID\",\"startDate\":\"2026-09-01\"}")

# 5. Check in (200 even when denied — read result)
curl -s $BASE/api/checkins -H "$AUTH" -H 'Content-Type: application/json' \
  -d "{\"token\":\"$QR_TOKEN\"}"

# 6. Public portal (no auth)
curl -s $BASE/api/public/status/$QR_TOKEN
```

Full request/response samples for every flow (including error cases): [`docs/API-TEST-SAMPLES.md`](API-TEST-SAMPLES.md).

---

## 9. FAQ for agents

**Q: Check-in returned 200 — did they get in?**
A: Check `result`. `granted` = in. `denied` + `reason` = not in; show the reason.

**Q: Why 409 on subscription create?**
A: Overlap. For Time subs, start the new one the day after the current `endDate`. For Session subs, cancel the live one first.

**Q: Can I set how many sessions a Session plan includes?**
A: No. Plans don't carry session counts. Pass `totalSessions` per subscription (per sale).

**Q: Token expired — refresh?**
A: No refresh endpoint. `POST /api/auth/login` again.

**Q: Where's delete customer?**
A: Doesn't exist by design. Use `POST /api/customers/{id}/archive`.

**Q: My enum value got 422.**
A: Enums are exact-case strings: `Time`/`Session`, `Cash`/`Card`/`Transfer`. Lowercase `time` fails.

**Q: Public portal returns 429.**
A: Rate limit is 10/min/IP. Back off; it resets within a minute.
