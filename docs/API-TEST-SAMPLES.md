# Gym Management API — test samples (all flows)

Base URL (dev): `http://localhost:5168`  
Swagger: `http://localhost:5168/swagger`  
Default admin: `admin` / `Admin#12345!`

Authorize staff requests with:

```http
Authorization: Bearer <jwt-from-login>
```

Run flows top → bottom. Replace placeholders like `<customerId>` with values from earlier responses.

---

## Flow 0 — Auth

### 0.1 Admin login → 200
```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "admin",
  "password": "Admin#12345!"
}
```
Save `token` from the response.

### 0.2 Me → 200
```http
GET /api/auth/me
Authorization: Bearer <token>
```

### 0.3 Bad password → 401 `unauthorized`
```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "admin",
  "password": "wrong"
}
```

### 0.4 Missing fields → 422 `validation`
```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "admin"
}
```

---

## Flow 1 — Admin setup (plans + settings)

### 1.1 Create Time plan → 200
```http
POST /api/plans
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Monthly",
  "type": "Time",
  "durationDays": 30,
  "price": 500
}
```
Save `id` as `<timePlanId>`.

### 1.2 Create Session credits plan (no `sessions`) → 200
```http
POST /api/plans
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Session credits",
  "type": "Session",
  "price": 0
}
```
Save `id` as `<sessionPlanId>`. `sessions` in the response should be `null`.

### 1.3 List plans → 200
```http
GET /api/plans
Authorization: Bearer <token>
```

### 1.4 Update plan → 200
```http
PUT /api/plans/<timePlanId>
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Monthly",
  "durationDays": 30,
  "price": 550,
  "isActive": true
}
```

### 1.5 Get settings → 200 (Admin)
```http
GET /api/settings
Authorization: Bearer <token>
```

### 1.6 Update settings → 200 (Admin)
```http
PUT /api/settings
Authorization: Bearer <token>
Content-Type: application/json

{
  "GymName": "Iron Temple",
  "DuplicateScanThresholdMinutes": "15",
  "LowBalanceThreshold": "3",
  "TimezoneId": "UTC"
}
```

### 1.7 Session plan with fixed `sessions` → 422
```http
POST /api/plans
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Bad pack",
  "type": "Session",
  "sessions": 30,
  "price": 100
}
```

---

## Flow 2 — Time member

Create → card → subscribe → pay → check-in → duplicate → portal → renew → overlap.

### 2.1 Create customer → 200
```http
POST /api/customers
Authorization: Bearer <token>
Content-Type: application/json

{
  "firstName": "Ada",
  "lastName": "Lovelace",
  "phone": "01011112222",
  "notes": "Time member"
}
```
Save `id` as `<timeCustomerId>`, `token` as `<timeQrToken>`.

### 2.2 Search / list → 200
```http
GET /api/customers?query=Ada&page=1&pageSize=20
Authorization: Bearer <token>
```
Each item includes `token`.

### 2.3 Detail → 200
```http
GET /api/customers/<timeCustomerId>
Authorization: Bearer <token>
```

### 2.4 Card print (does **not** rotate) → 200
```http
GET /api/customers/<timeCustomerId>/card
Authorization: Bearer <token>
```
`{ "token", "customerName", "gymName" }`

### 2.5 Update profile → 200
```http
PUT /api/customers/<timeCustomerId>
Authorization: Bearer <token>
Content-Type: application/json

{
  "firstName": "Ada",
  "lastName": "Lovelace",
  "phone": "01011112222",
  "notes": "Updated notes"
}
```

### 2.6 Create time subscription → 200
```http
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "customerId": "<timeCustomerId>",
  "planId": "<timePlanId>",
  "startDate": "2026-08-26"
}
```
Save `id` as `<timeSubId>`, note `endDate`.

### 2.7 Record payment → 200
```http
POST /api/payments
Authorization: Bearer <token>
Content-Type: application/json

{
  "subscriptionId": "<timeSubId>",
  "amount": 500,
  "method": "Cash",
  "note": "Paid at desk"
}
```

### 2.8 Check-in → 200 `result: granted`
```http
POST /api/checkins
Authorization: Bearer <token>
Content-Type: application/json

{
  "token": "<timeQrToken>"
}
```

### 2.9 Check-in again (within duplicate window) → 200 `denied` / `duplicate_scan`
```http
POST /api/checkins
Authorization: Bearer <token>
Content-Type: application/json

{
  "token": "<timeQrToken>"
}
```

### 2.10 Public portal (no auth) → 200
```http
GET /api/public/status/<timeQrToken>
```

### 2.11 List subscriptions → 200
```http
GET /api/subscriptions?customerId=<timeCustomerId>
Authorization: Bearer <token>
```

### 2.12 Filter by status → 200
```http
GET /api/subscriptions?customerId=<timeCustomerId>&status=Active
Authorization: Bearer <token>
```

### 2.13 Renew (start = day after current `endDate`) → 200 `Scheduled`
```http
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "customerId": "<timeCustomerId>",
  "planId": "<timePlanId>",
  "startDate": "2026-09-25"
}
```
Adjust `startDate` to be **the day after** the active sub’s `endDate`.

### 2.14 Overlapping time sub → 409 `overlap_conflict`
```http
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "customerId": "<timeCustomerId>",
  "planId": "<timePlanId>",
  "startDate": "2026-08-26"
}
```

---

## Flow 3 — Session member

Custom `totalSessions` → pay → check-ins → exhaust → cancel → new pack.

### 3.1 Create customer → 200
```http
POST /api/customers
Authorization: Bearer <token>
Content-Type: application/json

{
  "firstName": "Grace",
  "lastName": "Hopper",
  "phone": "01033334444"
}
```
Save `id` as `<sessionCustomerId>`, `token` as `<sessionQrToken>`.

### 3.2 Session sub without `totalSessions` → 422
```http
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "customerId": "<sessionCustomerId>",
  "planId": "<sessionPlanId>",
  "startDate": "2026-08-26"
}
```

### 3.3 Session sub with `totalSessions: 2` → 200
```http
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "customerId": "<sessionCustomerId>",
  "planId": "<sessionPlanId>",
  "startDate": "2026-08-26",
  "totalSessions": 2,
  "overridePrice": 80
}
```
Save `id` as `<sessionSubId>`.

### 3.4 Pay → 200
```http
POST /api/payments
Authorization: Bearer <token>
Content-Type: application/json

{
  "subscriptionId": "<sessionSubId>",
  "amount": 80,
  "method": "Card"
}
```

### 3.5 Disable duplicate window (so you can scan twice) → 200
```http
PUT /api/settings
Authorization: Bearer <token>
Content-Type: application/json

{
  "DuplicateScanThresholdMinutes": "0"
}
```

### 3.6 Check-in #1 → 200 `granted` (1 left)
```http
POST /api/checkins
Authorization: Bearer <token>
Content-Type: application/json

{
  "token": "<sessionQrToken>"
}
```

### 3.7 Check-in #2 → 200 `granted` (0 left)
```http
POST /api/checkins
Authorization: Bearer <token>
Content-Type: application/json

{
  "token": "<sessionQrToken>"
}
```

### 3.8 Check-in #3 → 200 `denied` / `no_sessions_remaining`
```http
POST /api/checkins
Authorization: Bearer <token>
Content-Type: application/json

{
  "token": "<sessionQrToken>"
}
```

### 3.9 Restore duplicate window → 200
```http
PUT /api/settings
Authorization: Bearer <token>
Content-Type: application/json

{
  "DuplicateScanThresholdMinutes": "15"
}
```

### 3.10 New sub while session sub still non-terminal → 409  
(If already exhausted, this may succeed — cancel first if needed.)
```http
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "customerId": "<sessionCustomerId>",
  "planId": "<sessionPlanId>",
  "startDate": "2026-08-26",
  "totalSessions": 5
}
```

### 3.11 Cancel → 200
```http
POST /api/subscriptions/<sessionSubId>/cancel
Authorization: Bearer <token>
Content-Type: application/json

{
  "reason": "Sold a new pack"
}
```

### 3.12 New session pack after cancel → 200
```http
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "customerId": "<sessionCustomerId>",
  "planId": "<sessionPlanId>",
  "startDate": "2026-08-26",
  "totalSessions": 10,
  "overridePrice": 150
}
```

---

## Flow 4 — QR reset (only rotate path)

### 4.1 Reset token → 200 (new `token`)
```http
POST /api/customers/<sessionCustomerId>/token/reset
Authorization: Bearer <token>
```
Save new token as `<newQrToken>`. Old card is dead.

### 4.2 Old token on public portal → 404
```http
GET /api/public/status/<sessionQrToken>
```

### 4.3 New token on public portal → 200
```http
GET /api/public/status/<newQrToken>
```

### 4.4 Card returns current token (no rotate) → 200
```http
GET /api/customers/<sessionCustomerId>/card
Authorization: Bearer <token>
```

---

## Flow 5 — Scheduled + archive denials

### 5.1 Create customer → 200
```http
POST /api/customers
Authorization: Bearer <token>
Content-Type: application/json

{
  "firstName": "Future",
  "lastName": "Member",
  "phone": "01055556666"
}
```
Save `id` / `token` as `<futureCustomerId>` / `<futureQrToken>`.

### 5.2 Future-dated time sub → 200 `Scheduled`
```http
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "customerId": "<futureCustomerId>",
  "planId": "<timePlanId>",
  "startDate": "2027-01-01"
}
```

### 5.3 Check-in → 200 `denied` / `not_started`
```http
POST /api/checkins
Authorization: Bearer <token>
Content-Type: application/json

{
  "token": "<futureQrToken>"
}
```

### 5.4 Archive → 200
```http
POST /api/customers/<futureCustomerId>/archive
Authorization: Bearer <token>
```

### 5.5 Check-in archived → 200 `denied` / `customer_inactive`
```http
POST /api/checkins
Authorization: Bearer <token>
Content-Type: application/json

{
  "token": "<futureQrToken>"
}
```

### 5.6 Unknown QR → 200 `denied` / `token_unknown`
```http
POST /api/checkins
Authorization: Bearer <token>
Content-Type: application/json

{
  "token": "not-a-real-token"
}
```

---

## Flow 6 — Reports + health

### 6.1 Dashboard → 200
```http
GET /api/reports/dashboard
Authorization: Bearer <token>
```

### 6.2 Liveness → 200
```http
GET /health
```

### 6.3 Readiness (DB) → 200
```http
GET /health/ready
```

---

## Flow 7 — AuthZ + validation edges

### 7.1 Anonymous me → 401
```http
GET /api/auth/me
```

### 7.2 Staff cannot create plans → 403  
(Needs a Staff JWT; skip if you only have admin.)
```http
POST /api/plans
Authorization: Bearer <staffToken>
Content-Type: application/json

{
  "name": "Nope",
  "type": "Time",
  "durationDays": 7,
  "price": 1
}
```

### 7.3 Staff cannot read settings → 403
```http
GET /api/settings
Authorization: Bearer <staffToken>
```

### 7.4 Blank name → 422
```http
POST /api/customers
Authorization: Bearer <token>
Content-Type: application/json

{
  "firstName": "   ",
  "lastName": "X",
  "phone": "01099998888"
}
```

### 7.5 Bad phone → 422
```http
POST /api/customers
Authorization: Bearer <token>
Content-Type: application/json

{
  "firstName": "Bad",
  "lastName": "Phone",
  "phone": "01092abc"
}
```

### 7.6 Payment amount 0 → 422
```http
POST /api/payments
Authorization: Bearer <token>
Content-Type: application/json

{
  "subscriptionId": "00000000-0000-0000-0000-000000000001",
  "amount": 0,
  "method": "Cash"
}
```

### 7.7 Cancel blank reason → 422
```http
POST /api/subscriptions/00000000-0000-0000-0000-000000000001/cancel
Authorization: Bearer <token>
Content-Type: application/json

{
  "reason": " "
}
```

### 7.8 Check-in blank token → 422
```http
POST /api/checkins
Authorization: Bearer <token>
Content-Type: application/json

{
  "token": "  "
}
```

### 7.9 Unknown customer → 404
```http
GET /api/customers/00000000-0000-0000-0000-000000000099
Authorization: Bearer <token>
```

### 7.10 Unknown public token → 404
```http
GET /api/public/status/totally-unknown-token
```

---

## Quick reference — expected outcomes

| Situation | HTTP | `reason` / result |
|-----------|------|-------------------|
| Bad login | 401 | `unauthorized` |
| Wrong role | 403 | `forbidden` |
| Missing id / public token | 404 | `not_found` |
| Sub overlap | 409 | `overlap_conflict` |
| Bad body | 422 | `validation` (+ `errors`) |
| Check-in grant/deny | **200** | `result: granted` or `denied` |
| Public rate limit | 429 | `rate_limited` |

**Check-in deny reasons:** `token_unknown`, `customer_inactive`, `duplicate_scan`, `not_started`, `expired`, `no_sessions_remaining`, `no_active_subscription`

**QR token:** returned on list / detail / create / card / reset. **Only** `POST .../token/reset` rotates it.
