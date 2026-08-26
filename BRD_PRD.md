# Document 1: Business Requirements Document (BRD)

## 1. Executive Summary
This document outlines the business requirements for a custom Gym Management & Subscription Management System tailored for a single gym location. The system aims to digitize and streamline reception operations, subscription management, and customer check-ins. It introduces a secure QR-code-based access system that differentiates between authenticated staff check-ins (which deduct sessions) and customer self-service lookups (which are strictly read-only). The solution comprises a .NET backend, a Flutter staff application, and a lightweight Next.js customer web portal.

## 2. Business Problem
Currently, gym reception staff rely on manual or fragmented systems to manage customer subscriptions, track session balances, and record attendance. This leads to operational bottlenecks, human error in session deduction, inability to handle concurrent check-ins safely, and poor customer experience due to a lack of self-service visibility. Furthermore, existing solutions often expose sensitive customer data via QR codes or require customers to install dedicated mobile apps.

## 3. Business Opportunity
By implementing a centralized, QR-driven system, the gym can eliminate reception friction, ensure 100% accuracy in session deduction, and provide customers with instant, app-free visibility into their subscription status. A clean, single-tenant architecture ensures low operational costs while setting the foundation for future multi-branch expansion or SaaS scaling.

## 4. Product Vision
To provide a seamless, secure, and fast gate-to-reception experience for gym customers and staff, ensuring absolute data integrity and privacy through a centralized backend system.

## 5. Business Objectives
*   **Streamline Operations:** Reduce check-in processing time at the reception to under 5 seconds.
*   **Data Integrity:** Ensure zero occurrences of negative session balances or unauthorized session deductions.
*   **Customer Transparency:** Allow customers to check their subscription status anytime without requiring a mobile app download.
*   **Privacy First:** Ensure no personally identifiable information (PII) is exposed via customer QR codes.

## 6. Stakeholders
*   **Gym Owner/Admin:** Holds overall business accountability, requires reporting and oversight.
*   **Receptionist/Staff:** Daily user of the Flutter application for customer management and check-ins.
*   **Customer:** End-user of the Next.js web portal and holder of the QR card.

## 7. User Types
*   **Staff (Authenticated):** Full access to Flutter app for CRUD operations, scanning, and reports.
*   **Customer (Unauthenticated/Public):** Read-only access to their own subscription status via the web portal.
*   **System Admin (Future):** Manages system configurations and multi-branch settings (Future).

## 8. Current/Potential Workflow vs. Proposed Future Workflow
*   **Current Workflow:** Customer arrives. Staff manually checks a paper card or a spreadsheet. Staff visually verifies expiration or counts remaining sessions. Staff manually logs attendance.
*   **Proposed Workflow:** Customer arrives. Staff scans customer’s QR card using the Flutter app. Backend instantly validates the active subscription, creates an attendance log, and deducts a session if applicable. If the customer wants to check their balance, they scan the QR with their phone camera, opening a web portal that displays their remaining balance without deducting a session.

## 9. Scope

### In Scope
*   Staff authentication via Flutter app.
*   Customer CRUD operations.
*   Subscription Plan creation and management.
*   Time-based and Session-based subscription support.
*   QR code generation with secure, non-sequential tokens.
*   Staff check-in flow (Flutter) with session deduction.
*   Customer self-service flow (Next.js) without session deduction.
*   Basic attendance history and dashboard reports.
*   Local payment recording (Option A).

### Out of Scope (MVP)
*   Online payment gateway integration (Option B).
*   Customer mobile applications (iOS/Android native apps).
*   Multi-tenant/SaaS architecture and multi-branch support.
*   Automated push notifications/emails.
*   Subscription "Freeze" functionality.

## 10. Business Requirements

| ID | Requirement | Description | Priority |
| :--- | :--- | :--- | :--- |
| BR-001 | Customer Management | System must allow staff to create, edit, view, and search customer profiles. | High |
| BR-002 | Subscription Management | System must support time-based and session-based subscription plans. | High |
| BR-003 | QR Code Generation | System must generate a secure, random, non-sequential public token per customer for QR creation. | High |
| BR-004 | Staff Check-in | Flutter app must allow staff to scan QR and validate check-ins via the backend. | High |
| BR-005 | Session Deduction | Backend must atomically deduct exactly one session per successful staff check-in for session-based plans. | High |
| BR-006 | Customer Web Portal | Next.js web portal must display safe, read-only subscription status based on the QR token. | High |
| BR-006 | Payment Recording | System must allow staff to record offline payments made at the gym. | Medium |
| BR-007 | Reporting | System must provide basic reports (today's check-ins, active/expired subs). | Medium |
| BR-008 | QR Card Printing | Flutter app must allow staff to print a physical card containing the QR code and basic branding. | Medium |

## 11. Business Rules
1.  (Confirmed) Only valid subscriptions can be used for check-in.
2.  (Confirmed) Time-based subscriptions are validated by date.
3.  (Confirmed) Session-based subscriptions are validated by remaining sessions.
4.  (Confirmed) Successful session-based check-in deducts exactly one session.
5.  (Confirmed) Customer QR self-service does not deduct sessions.
6.  (Confirmed) Remaining sessions can never be negative.
7.  (Confirmed) QR token must not expose sensitive data.
8.  (Confirmed) QR should ideally remain stable across renewals.
9.  (Confirmed) Backend is the source of truth.
10. (Confirmed) Check-in and session deduction must be atomic.
11. (Confirmed) Invalid check-ins must not create successful attendance records.
12. (Confirmed) Customer portal is read-only.
13. (Confirmed) Staff actions require authentication.
14. (Confirmed) Public customer information must be minimized.
15. (Recommended) Duplicate scans within a configurable time frame (e.g., 15 minutes) should be blocked to prevent accidental double deductions.

## 12. Success Metrics / KPIs
*   **Check-in Processing Time:** Target < 5 seconds from scan to screen display. (Proposed)
*   **Data Integrity:** 0 instances of negative session balances. (Proposed)
*   **Customer Adoption:** > 50% of customers use the self-service web portal at least once a month. (Proposed)
*   **Revenue Tracking:** 100% of offline payments recorded in the system. (Proposed)

## 13. Risks
*   **Token Compromise:** If a customer’s QR token is leaked, anyone can view their basic subscription status. (Mitigation: No sensitive PII exposed on the portal; tokens can be revoked by staff).
*   **Connectivity Loss:** Flutter app requires internet to validate check-ins via backend. (Risk: Reception stall during outage).
*   **Concurrency Bottlenecks:** High load during peak hours might cause delays if backend transactions are not optimized.

## 14. Assumptions
*   The gym has stable internet connectivity at the reception desk.
*   Staff have dedicated tablets/devices running the Flutter app.
*   Customers have smartphones with standard QR-scanning capabilities in their native camera apps.

## 15. Constraints
*   **Budget:** Must not over-engineer; cloud and infrastructure costs should be minimized for a single gym.
*   **Technology:** Must use .NET (ASP.NET Core) for backend, Flutter for staff app, Next.js for customer portal.

## 16. Dependencies
*   Printing hardware compatibility for QR card generation.
*   Domain name and SSL certificate for the Next.js public portal.

## 17. Open Questions / Decisions Required
1.  **Simultaneous Subscriptions:** Can a customer have an active time-based AND an active session-based subscription at the same time? If yes, which takes priority at check-in?
2.  **Session Expiry:** Do unused sessions roll over if a session-based subscription expires?
3.  **Payment to Activation:** Must a subscription be paid in full before it is activated, or can staff activate a pending payment subscription?

---
---

# Document 2: Product Requirements Document (PRD)

## 1. Product Overview
A modular gym management system consisting of a .NET REST API (source of truth), a Flutter app for staff to manage operations and perform check-ins, and a Next.js web portal for customers to view subscription status via a secure QR token.

## 2. Product Goals
*   Provide a fast, error-free check-in process for staff.
*   Prevent unauthorized session deduction and negative balances.
*   Offer customers an app-free way to check their remaining sessions or expiration dates.

## 3. Non-Goals
*   Building a multi-tenant SaaS platform in the MVP.
*   Integrating online payment gateways (e.g., Stripe, PayPal) in the MVP.
*   Developing a customer-facing mobile application.
*   Implementing complex fitness tracking or workout planning features.

## 4. Personas
*   **Receptionist/Staff:** Needs quick, reliable tools to check in customers, create subscriptions, and print replacement QR cards. Does not want to deal with complex software training.
*   **Gym Owner:** Needs visibility into gym attendance and subscription revenue.
*   **Customer:** Wants to know if their subscription is valid and how many sessions they have left without asking the receptionist.

## 5. User Journeys

1.  **Create Customer:** Staff logs in → Opens Customer tab → Adds new customer (Name, Phone) → System generates secure QR token → Staff prints QR card.
2.  **Create Subscription:** Staff selects customer → Adds Subscription → Selects Plan (e.g., 30 Sessions) → Sets start date → Records payment → Subscription becomes Active.
3.  **Staff Check-in:** Customer arrives → Staff scans QR → Flutter sends token to Backend → Backend validates active sub → Backend deducts session (if applicable) & logs attendance → Flutter displays "Access Granted. 22 sessions remaining."
4.  **Customer Scans QR:** Customer scans their card with phone → Browser opens `https://gym-domain.com/s/[token]` → Next.js fetches data → Customer sees "23/30 Sessions Remaining."
5.  **Failed Check-in:** Staff scans QR → Backend finds 0 sessions remaining → Backend rejects → Flutter displays "Access Denied. No sessions remaining."

## 6. Functional Requirements (FR)

### Customer Management
*   **FR-001:** Staff can create, read, update, and search customers.
    *   *Acceptance Criteria:* Given a receptionist is logged in, When they search for a phone number, Then the corresponding customer profile is displayed.
*   **FR-002:** System auto-generates a secure public access token upon customer creation.

### Subscription Management
*   **FR-003:** Staff can create time-based and session-based subscriptions for customers.
    *   *Acceptance Criteria:* Given a customer purchases a 30-session plan, When the staff creates the subscription, Then total sessions = 30, used = 0, remaining = 30.

### QR Code & Scanning
*   **FR-004:** Flutter app contains a QR scanner that reads the public token and calls the backend Check-in API.
*   **FR-005:** Flutter app can trigger printing of the QR card.
    *   *Acceptance Criteria:* Given a customer is created, When the staff clicks "Print QR", Then a layout containing the QR, customer name, and gym logo is sent to the printer.

### Check-in & Session Deduction (Backend Logic)
*   **FR-006:** Backend must process check-ins atomically.
    *   *Acceptance Criteria (Concurrency):* Given a customer has 1 remaining session, When two staff scan the QR simultaneously, Then the first request succeeds (remaining = 0), and the second request is rejected (no negative balance).
*   **FR-007:** Backend must reject read-only portal requests from deducting sessions.
    *   *Acceptance Criteria:* Given a customer scans their QR via phone, When the portal loads, Then attendance is not created and sessions are not deducted.

### Customer Portal
*   **FR-008:** Next.js dynamic route `/s/[token]` displays subscription status.
    *   *Acceptance Criteria:* Given a valid token is scanned, When the page loads, Then the customer sees their name, status, and remaining sessions/days, with no PII (email, phone) displayed.

### Payments
*   **FR-009:** Staff can record offline payments (cash, card) against a subscription.
    *   *Acceptance Criteria:* Given a subscription is created, When staff records a $100 cash payment, Then the payment is logged in the payment history.

## 7. Detailed Feature Specifications

### Subscription State Model (Recommended)
*   **Draft:** Created but not active.
*   **Scheduled:** Created with a future start date.
*   **Active:** Current date is between start/end dates (Time), or remaining sessions > 0 (Session).
*   **Expired:** End date has passed (Time).
*   **Exhausted:** Remaining sessions = 0 (Session).
*   **Cancelled:** Manually voided by staff.

### QR Lifecycle
*   **Generation:** Created via backend cryptographically secure random string upon Customer creation.
*   **Storage:** Mapped to Customer ID in the database. Next.js and Flutter only read this token.
*   **Usage:** Passed to backend endpoints to resolve customer context.
*   **Revocation/Replacement:** Staff can revoke a compromised token and generate a new one (rendering the old QR invalid).

### API Requirements (Conceptual)
*   **Staff (Authenticated):**
    *   `POST /api/auth/login`
    *   `POST /api/customers`
    *   `GET /api/customers/{id}`
    *   `POST /api/subscriptions`
    *   `POST /api/attendance/checkin` (Payload: `{ token }`) -> Returns validation result.
*   **Public (Unauthenticated):**
    *   `GET /api/public/subscriptions/{token}` -> Returns strictly minimal safe data: `customerName`, `subscriptionType`, `status`, `remainingSessions`, `endDate`.

## 8. Non-Functional Requirements (NFR)
*   **Security:** Backend must enforce JWT authentication for staff endpoints. Public endpoints must be rate-limited to prevent token brute-forcing.
*   **Performance:** Check-in API response must be < 200ms under normal load.
*   **Reliability & Data Integrity:** Backend must use database transactions (e.g., Row Locking / `SERIALIZABLE` isolation level) during session deduction to prevent race conditions.
*   **Privacy:** Public API must not leak staff details, customer PII, or internal IDs.

## 9. Error Handling (User-Facing)
*   **Invalid QR:** Flutter displays "QR not recognized. Please see reception."
*   **Expired Subscription:** Flutter displays "Access Denied. Subscription expired on [Date]."
*   **No Sessions Remaining:** Flutter displays "Access Denied. No sessions remaining."
*   **Subscription Not Started:** Flutter displays "Access Denied. Subscription starts on [Date]."
*   **Server Unavailable:** Flutter displays "Network error. Please try again."
*   **Duplicate Scan:** Flutter displays "Duplicate scan detected. Please wait X minutes."

## 10. Reporting Requirements
*   **MVP:**
    *   Today's check-ins count and list.
    *   Active vs. Expired subscriptions count.
    *   Subscriptions with low session balance (< 3).
*   **Phase 2:**
    *   Revenue summary (daily/weekly/monthly).
    *   Session utilization metrics (attendance trends).

## 11. Notifications
*   **MVP:** None.
*   **Phase 2:** SMS/Email reminders for expiring time-based subscriptions or low-session balances.

## 12. MVP vs Phase 2 vs Future

**MVP:**
*   Customer, Subscription, and Payment (offline) CRUD.
*   Flutter check-in with atomic deduction.
*   Next.js read-only portal.
*   Basic dashboard.

**Phase 2:**
*   Reporting & Analytics dashboard.
*   Token revocation UI.
*   Configurable duplicate-scan blocking time.
*   Manual session adjustments with audit logs.

**Future Enhancements:**
*   Online payment gateways (Stripe).
*   Subscription freeze functionality.
*   Multi-branch architecture.
*   Automated email/SMS notifications.

---

## 13. Important Product Decisions Analysis

1.  **Should the QR token belong to the customer or subscription?**
    *   *Recommended Decision:* Belong to the Customer. This prevents reprinting QR cards upon renewal and simplifies the physical card lifecycle.
2.  **What happens when a customer has multiple subscriptions?**
    *   *Recommended Decision:* The backend should resolve to the "Current Active" subscription based on priority rules (e.g., active > scheduled > expired).
3.  **Can a customer have an active time-based subscription and an active session subscription simultaneously?**
    *   *Open Question:* Requires business confirmation. If yes, *Recommended:* Prioritize time-based check-in, as it doesn't consume paid sessions.
4.  **What happens when a subscription expires with unused sessions?**
    *   *Recommended Decision:* Sessions are forfeited. The status becomes "Expired" (if a hard end date exists) or "Exhausted".
5.  **Can staff manually adjust remaining sessions?**
    *   *Recommended Decision:* Yes, for fixing mistakes, but only in Phase 2.
6.  **Should manual adjustments require a reason?**
    *   *Recommended Decision:* Yes, mandatory reason + audit log entry to prevent internal fraud.
7.  **Should check-ins be reversible?**
    *   *Recommended Decision:* No. To maintain strict audit trails, accidental deductions should be fixed via a "Credit Session" manual adjustment, not by deleting attendance records.
8.  **What happens if staff scans the same QR twice within a short period?**
    *   *Recommended Decision:* Block it. Implement a configurable "Duplicate Scan Threshold" (e.g., 15 minutes) to prevent accidental double-deduction.
9.  **Should duplicate scans be blocked for a configurable time?**
    *   *Recommended Decision:* Yes, configurable by the Gym Owner in system settings.
10. **What happens if a subscription starts in the future?**
    *   *Recommended Decision:* Check-in is rejected with message "Subscription not started."
11. **What happens if the subscription is cancelled?**
    *   *Recommended Decision:* Check-in is rejected. Sessions cannot be used.
12. **What happens if the QR token is compromised?**
    *   *Recommended Decision:* Staff can revoke the token and generate a new one. The compromised QR becomes a dead link.
13. **How can a QR be revoked/replaced?**
    *   *Recommended Decision:* A "Reset Token" button in the customer profile on the Flutter app.
14. **Should customer name be displayed publicly?**
    *   *Recommended Decision:* Yes, First Name and Last Initial (e.g., "Ahmed M.") to confirm to the customer they are viewing the right card, without exposing full identity.
15. **Should the customer portal expose any additional information?**
    *   *Recommended Decision:* No. Only gym name, customer first name/initial, status, and remaining balance/days.
16. **How should subscription renewals work?**
    *   *Recommended Decision:* A new subscription record is created and linked to the customer. The backend seamlessly transitions from the old expired/exhausted sub to the new active sub.
17. **Should a new subscription start immediately or after the current one?**
    *   *Open Question:* If bought in advance, *Recommended:* Allow staff to set a "Start Date" in the future to align with the expiration of the current sub.
18. **How should payments relate to subscription activation?**
    *   *Recommended Decision:* Decouple them. Subscriptions can be created and activated regardless of payment status to avoid blocking a customer at the gate. Staff records the payment alongside it. (Strict "pay-to-activate" logic can be added if requested).

## 14. Architecture Principle (Constraint)
The .NET backend owns 100% of the business logic.
*   Flutter is a presentation and input-capture layer. It does not calculate dates, statuses, or sessions.
*   Next.js is a read-only presentation layer. It renders JSON from the .NET public API.
*   No business logic is duplicated across clients.