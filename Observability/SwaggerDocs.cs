using System.Reflection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Gym_Management.Observability;

public static class SwaggerDocs
{
    public const string Description = """
## Welcome

Backend for the gym staff app (Flutter) and the customer status portal (Next.js).
Use **Authorize** with the JWT from `POST /api/auth/login`.

**Dev URLs:** `http://localhost:5168` · Swagger is this page · Health: `/health`

---

## Quick start (happy path)

1. **Login** — `POST /api/auth/login` with `admin` / `Admin#12345!` (or a Staff user).
2. Click **Authorize** and paste: `Bearer <token>` (or just the token — Swagger adds Bearer).
3. **Create a plan** (Admin) — `Time` needs `durationDays`; `Session` is a generic credit product (no fixed `sessions`).
4. **Create a customer** — `POST /api/customers` → response includes `token` (use this for the QR).
5. **Create a subscription** — Time: `customerId`, `planId`, `startDate`. Session: also send `totalSessions` (and optional `overridePrice`).
6. **Check in** — `POST /api/checkins` with `{ "token": "<customer token>" }`.
7. **Customer portal** — `GET /api/public/status/{token}` (no auth; read-only).

---

## Roles

| Role | Can do |
|------|--------|
| **Staff** | Customers, check-ins, subscriptions, payments, reports, card print |
| **Admin** | Everything Staff can, plus **plans** and **settings** |

---

## QR token (staff only)

- Returned on: list customers, customer detail, create, card, and token reset.
- **Only** `POST /api/customers/{id}/token/reset` rotates it (old QR stops working).
- Card / list / detail **never** rotate — safe to reopen and reprint.
- Public portal never returns the raw token in a way that leaks extras; path uses the token value.

---

## Scenarios you'll hit

| Goal | What to call |
|------|----------------|
| Search members | `GET /api/customers?query=name-or-phone` |
| Edit profile | `PUT /api/customers/{id}` |
| Soft-delete member | `POST /api/customers/{id}/archive` → check-in becomes `customer_inactive` |
| Lost / compromised QR | `POST /api/customers/{id}/token/reset` → print new card |
| Print card layout | `GET /api/customers/{id}/card` → `{ token, customerName, gymName }` |
| Renew after a time plan | New sub with `startDate` = day after current `endDate` (else `409 overlap_conflict`) |
| Sell N sessions to a member | Session plan + `POST /api/subscriptions` with `totalSessions: N` (and `overridePrice` if needed) |
| Cancel a live session plan | `POST /api/subscriptions/{id}/cancel` with `{ "reason": "..." }` first |
| Record cash/card at desk | `POST /api/payments` (does **not** gate check-in) |
| Desk scan | `POST /api/checkins` — **200** even when denied; read `result` + `reason` |
| Today’s numbers | `GET /api/reports/dashboard` |
| Gym name / duplicate window | `GET`/`PUT /api/settings` (Admin) |
| Customer self-check | `GET /api/public/status/{token}` (10 req/min/IP) |

---

## Errors (keep these handy)

Transport errors → `application/problem+json` with `reason` (+ `errors` on validation):

| HTTP | reason |
|------|--------|
| 401 | `unauthorized` |
| 403 | `forbidden` |
| 404 | `not_found` |
| 409 | `overlap_conflict` |
| 422 | `validation` |
| 429 | `rate_limited` |

**Check-in denials are HTTP 200:** `{ "result": "denied", "reason": "..." }`  
Reasons: `token_unknown`, `customer_inactive`, `duplicate_scan`, `not_started`, `expired`, `no_sessions_remaining`, `no_active_subscription`.

---

## Tips

- Dates are `yyyy-MM-dd`. Enums are strings: `Time` / `Session`, `Cash` / `Card` / `Transfer`.
- Subscription **status** is computed (Active, Scheduled, Expired, Exhausted, Cancelled) — not stored.
- Prefer sending `X-Correlation-ID` when debugging; it comes back on the response.
""";

    public static void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Gym Management API",
            Version = "v1",
            Description = Description,
            Contact = new OpenApiContact { Name = "Gym Management backend" }
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT from POST /api/auth/login. Paste the token only — Swagger prefixes Bearer."
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });

        options.TagActionsBy(api =>
        {
            if (api.GroupName is { Length: > 0 } group)
            {
                return [group];
            }

            var controller = api.ActionDescriptor.RouteValues.TryGetValue("controller", out var name)
                ? name
                : "API";
            return [controller ?? "API"];
        });

        options.OrderActionsBy(api => $"{api.ActionDescriptor.RouteValues["controller"]}_{api.HttpMethod}_{api.RelativePath}");

        var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
        if (File.Exists(xml))
        {
            options.IncludeXmlComments(xml, includeControllerXmlComments: true);
        }

        options.SupportNonNullableReferenceTypes();
    }
}
