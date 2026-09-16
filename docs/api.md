# Quotely — API reference

Base URL (development): `http://localhost:5199`

All endpoints except `/api/auth/*` and `/health` require:

```
Authorization: Bearer <access token>
```

Requests and responses are JSON. Enums are serialised as strings. Dates use `yyyy-MM-dd`.

## Errors

Every failure uses the same envelope:

```json
{ "message": "Quotation was not found.", "status": 404 }
```

| Status | Meaning |
| ------ | ------- |
| 400 | Validation failed, or a referenced record isn't yours |
| 401 | Missing, expired or invalid token |
| 404 | Record does not exist **or** belongs to another user |
| 409 | Email already registered, or a concurrent update |
| 429 | Too many failed sign-in attempts |
| 500 | Unexpected failure (details are logged, never returned) |

## Auth

### `POST /api/auth/register`

```json
{
  "email": "you@business.com",
  "password": "Str0ngPass",
  "fullName": "Ravi Kumar",
  "businessName": "ABC Electricals"
}
```

`businessName` is optional; a business profile is created either way. Returns `200` with an auth
response. `409` if the email is taken.

### `POST /api/auth/login`

```json
{ "email": "you@business.com", "password": "Str0ngPass" }
```

Both endpoints return:

```json
{
  "accessToken": "eyJhbGciOi…",
  "expiresAtUtc": "2026-09-10T19:30:00Z",
  "user": { "id": "…", "email": "you@business.com", "fullName": "Ravi Kumar" }
}
```

Logout is client-side: discard the token.

## Business profile

| Method | Path | Notes |
| ------ | ---- | ----- |
| `GET` | `/api/business-profile` | Returns the caller's profile (empty object shape if not yet set) |
| `PUT` | `/api/business-profile` | Creates or updates it |

```json
{
  "businessName": "ABC Electricals",
  "businessEmail": "hello@abcelectricals.example",
  "phone": "+91 98765 43210",
  "addressLine": "123 Main Street",
  "city": "Chennai",
  "state": "Tamil Nadu",
  "postalCode": "600040",
  "country": "India",
  "taxNumber": "33ABCDE1234F1Z5",
  "logoUrl": "data:image/png;base64,…",
  "currency": "INR"
}
```

`businessName` and a three-letter `currency` are required. `logoUrl` accepts a PNG/JPEG data URI.

## Customers

| Method | Path | Notes |
| ------ | ---- | ----- |
| `GET` | `/api/customers?search=&page=1&pageSize=20` | Paged, searches name/company/email/phone |
| `GET` | `/api/customers/{id}` | |
| `POST` | `/api/customers` | `201 Created` |
| `PUT` | `/api/customers/{id}` | |
| `DELETE` | `/api/customers/{id}` | `400` if the customer still has quotations |

```json
{
  "name": "John Smith",
  "companyName": "John Smith Construction",
  "email": "john@example.com",
  "phone": "+91 91234 56780",
  "addressLine": "42 Beach Road",
  "city": "Chennai",
  "state": "Tamil Nadu",
  "postalCode": "600006",
  "country": "India",
  "notes": "Prefers weekday site visits."
}
```

Only `name` is required. Paged responses look like:

```json
{ "items": [], "page": 1, "pageSize": 20, "totalCount": 0, "totalPages": 0 }
```

### `GET /api/customers/{id}/summary`

One customer's financial standing, with their invoices paged server-side (`page`, `pageSize`,
default 10).

```json
{
  "customerId": "…", "customerName": "John Smith",
  "totalInvoiced": 50000.00, "totalPaid": 20000.00,
  "totalOutstanding": 30000.00, "totalOverdue": 30000.00,
  "invoiceCount": 1, "overdueCount": 1, "currency": "INR",
  "invoices": { "items": [ … ], "page": 1, "pageSize": 10, "totalCount": 1, "totalPages": 1 }
}
```

The money totals exclude drafts and cancelled invoices; the `invoices` listing includes them,
because the owner is looking at their own record of the customer. Another tenant's customer is a
`404`.

## Products & services

| Method | Path | Notes |
| ------ | ---- | ----- |
| `GET` | `/api/products?search=&page=1&pageSize=50` | Paged |
| `GET` | `/api/products/{id}` | |
| `POST` | `/api/products` | `201 Created` |
| `PUT` | `/api/products/{id}` | |
| `DELETE` | `/api/products/{id}` | Existing quotation lines keep their snapshot |

```json
{
  "name": "AC Installation",
  "description": "Split AC installation including mounting and gas charging.",
  "unit": "Service",
  "price": 5000,
  "taxRate": 18
}
```

`price` must be ≥ 0 and `taxRate` between 0 and 100.

## Quotations

| Method | Path | Notes |
| ------ | ---- | ----- |
| `GET` | `/api/quotations?search=&status=&page=1&pageSize=20` | Paged, newest first |
| `GET` | `/api/quotations/stats` | Dashboard counters |
| `GET` | `/api/quotations/{id}` | Full quotation with customer, business and items |
| `POST` | `/api/quotations` | `201 Created`, number assigned automatically |
| `PUT` | `/api/quotations/{id}` | Replaces the item set; the number never changes |
| `DELETE` | `/api/quotations/{id}` | `204 No Content` |
| `POST`/`GET` | `/api/quotations/{id}/pdf` | Returns `application/pdf` |
| `POST` | `/api/quotations/{id}/public-link` | Creates the customer share link (see below) |

### Create / update payload

```json
{
  "customerId": "8f1c…",
  "quotationDate": "2026-09-10",
  "validUntil": "2026-09-25",
  "status": "Draft",
  "notes": "Thank you for your business.",
  "terms": "Quotation is valid until the specified date.",
  "items": [
    {
      "productId": "9469…",
      "name": "AC Installation",
      "description": "Split AC installation.",
      "unit": "Service",
      "quantity": 2,
      "unitPrice": 5000,
      "discount": 500,
      "taxRate": 18
    }
  ]
}
```

Rules enforced by the server:

- at least one item
- `validUntil` on or after `quotationDate`
- `quantity` greater than zero; `unitPrice`, `discount` not negative; `taxRate` within 0–100
- `customerId` must belong to the caller (otherwise `400`)
- `productId` values that aren't the caller's are dropped and the line is stored as free text
- `status` is one of `Draft`, `Sent`, `Accepted`, `Rejected`, `Expired` (default `Draft`)

Totals in the request are ignored — the server always recalculates.

### Quotation response

```json
{
  "id": "0664…",
  "quotationNumber": "QT-000001",
  "customer": { "id": "…", "name": "John Smith", "companyName": "John Smith Construction" },
  "business": { "businessName": "ABC Electricals", "currency": "INR" },
  "quotationDate": "2026-09-10",
  "validUntil": "2026-09-25",
  "status": "Draft",
  "subtotal": 11500.00,
  "discountTotal": 500.00,
  "taxTotal": 1980.00,
  "grandTotal": 12980.00,
  "currency": "INR",
  "items": [
    {
      "id": "…",
      "name": "AC Installation",
      "unit": "Service",
      "quantity": 2,
      "unitPrice": 5000.00,
      "discount": 500.00,
      "taxRate": 18.00,
      "lineSubtotal": 10000.00,
      "lineTax": 1710.00,
      "lineTotal": 11210.00
    }
  ],
  "notes": "…",
  "terms": "…",
  "createdAt": "2026-09-10T17:00:00Z",
  "updatedAt": "2026-09-10T17:00:00Z"
}
```

### `GET /api/quotations/stats`

```json
{
  "totalQuotations": 3,
  "draftCount": 2,
  "sentCount": 1,
  "acceptedCount": 0,
  "totalValue": 41480.00,
  "currency": "INR"
}
```

### `POST /api/quotations/{id}/pdf`

Renders the PDF on demand — nothing is stored server-side. Responds with:

```
Content-Type: application/pdf
Content-Disposition: attachment; filename=QT-000001-John-Smith.pdf
```

`Content-Disposition` is included in the CORS exposed headers so the browser can read the filename.

### `POST /api/quotations/{id}/public-link`

Creates the customer-facing share link for one of the caller's own quotations.

```json
{ "url": "https://quotely.app/q/7f9c2a…", "createdAt": "2026-09-11T09:14:00Z" }
```

Only a SHA-256 hash of the token is stored, so **this response is the one and only time the URL
exists**. Calling the endpoint again mints a new token and the previously shared URL stops working
— which is also how a link is revoked. A `Draft` quotation becomes `Sent`; any other status is left
as it is. Another user's quotation returns `404`.

The owner's quotation DTO carries `hasPublicLink` and `publicLinkCreatedAt` so the UI can show that
a link is active, plus `respondedAt`, `respondedByName`, `respondedByEmail` and `responseComment`
once the customer has answered.

### `POST /api/quotations/{id}/convert-to-invoice`

Raises the invoice for an accepted quotation. Returns `201 Created` with the full invoice DTO and a
`Location` of `/api/invoices/{id}`.

The server checks, in order: the quotation exists and belongs to the caller (`404` otherwise), its
status is `Accepted` (`409` otherwise), it has at least one item (`400` otherwise), and no invoice
exists for it yet (`409`, naming the existing invoice). Items and billing details are copied onto
the invoice; the totals are recomputed from those copies and asserted to equal the accepted
quotation's. Number allocation and the insert share one transaction.

The quotation DTO carries `invoiceId`, `invoiceNumber` and `canConvertToInvoice` so the UI knows
which of the three states to show.

## Invoices

All endpoints require `Authorization: Bearer <token>` and are scoped to the caller. Another user's
invoice returns `404`.

| Method | Path | Purpose |
| ------ | ---- | ------- |
| `GET` | `/api/invoices` | paged list; `search`, `status`, `page`, `pageSize` |
| `POST` | `/api/invoices` | raise an invoice directly, without a quotation |
| `GET` | `/api/invoices/stats` | receivables across every issued invoice |
| `GET` | `/api/invoices/{id}` | one invoice with items |
| `PUT` | `/api/invoices/{id}` | update dates, status, notes, terms and (draft only) items |
| `DELETE` | `/api/invoices/{id}` | delete, unless `Paid` or `PartiallyPaid` |
| `GET`/`POST` | `/api/invoices/{id}/pdf` | render the invoice PDF |

`search` matches the invoice number, the snapshotted customer name or company, and the source
quotation number where there is one.

`status` is one of `Draft`, `Sent`, `PartiallyPaid`, `Paid`, `Overdue`, `Cancelled`. All but
`Overdue` match the stored status. **`Overdue` is derived, not stored** — it selects invoices that
are issued, not cancelled, past their due date and still owing something. The filter runs in the
database and pages normally.

Every row carries `paid`, `outstanding` and `isOverdue`, all derived from the payment ledger.

### `POST /api/invoices`

Raises an invoice directly, for a customer who was never quoted. Returns `201 Created` with the
full invoice DTO and a `Location` of `/api/invoices/{id}`.

```json
{
  "customerId": "8f1c…",
  "invoiceDate": "2026-09-12",
  "dueDate": "2026-09-27",
  "notes": "Thank you for your business.",
  "terms": "Payment due by the date shown above.",
  "items": [
    { "name": "AC Installation", "description": "Split AC, wall mounted",
      "unit": "Service", "quantity": 2, "unitPrice": 5000, "discount": 0, "taxRate": 18 }
  ]
}
```

`dueDate` is optional and defaults to the invoice date plus 15 days; a due date before the invoice
date returns `400`. `customerId` must belong to the caller — anything else returns `404`, the same
answer an unknown id gets, so a rejected request cannot confirm that a customer exists. At least one
item is required, and the usual line rules apply (quantity above zero, price and discount not
negative, tax between 0 and 100).

The invoice number, the totals, the billing snapshot and the status are the server's: the number is
allocated from the same per-business sequence a converted invoice uses, the totals are computed from
the submitted lines by the same calculator, and a new invoice is always `Draft`. Any of these sent
in the body is ignored.

What comes back is an ordinary invoice — same entity, same table, same lifecycle, same PDF, same
payment link and same Razorpay flow. The only difference is that `quotationId` is `null` and
`quotationNumber` is empty.

### Update payload

```json
{
  "invoiceDate": "2026-09-11",
  "dueDate": "2026-09-26",
  "status": "Sent",
  "notes": "…",
  "terms": "…",
  "items": [
    { "name": "AC Installation", "description": "…", "unit": "Service",
      "quantity": 2, "unitPrice": 5000, "discount": 0, "taxRate": 18 }
  ]
}
```

`items` is optional and only accepted while the invoice is `Draft`; sending it against an issued
invoice returns `409`. The invoice number, totals and ownership are never taken from the request:
the number is immutable and the totals are recomputed from the submitted lines. `dueDate` before
`invoiceDate` returns `400`. A `Paid` or `Cancelled` invoice rejects the whole update with `409`.

### Invoice response

```json
{
  "id": "…",
  "invoiceNumber": "INV-000001",
  "quotationId": "…",
  "quotationNumber": "QT-000001",
  "customer": { "name": "John Smith", "companyName": "…", "city": "Chennai", "…": "…" },
  "business": { "businessName": "ABC Electricals", "…": "…" },
  "invoiceDate": "2026-09-11",
  "dueDate": "2026-09-26",
  "status": "Draft",
  "isOverdue": false,
  "canEdit": true,
  "canEditItems": true,
  "canDelete": true,
  "subtotal": 12000.00,
  "discountTotal": 0.00,
  "taxTotal": 2160.00,
  "grandTotal": 14160.00,
  "currency": "INR",
  "items": [ { "id": "…", "name": "AC Installation", "lineTotal": 11800.00, "…": "…" } ]
}
```

`customer` is the snapshot stored on the invoice, not the live `Customer` record — editing the
customer afterwards does not change it. `isOverdue` is a display hint computed from `dueDate`; the
stored `status` stays authoritative.

### `GET /api/invoices/{id}/pdf`

Renders the invoice from its own snapshot. Responds with:

```
Content-Type: application/pdf
Content-Disposition: attachment; filename=INV-000001-John-Smith.pdf
```

### `POST /api/invoices/{id}/public-link`

Creates the customer-facing payment link for one of the caller's own invoices.

```json
{
  "url": "https://quotely.app/i/7f9c2a…",
  "createdAt": "2026-09-12T09:14:00Z",
  "share": {
    "url": "https://quotely.app/i/7f9c2a…",
    "message": "Hi John,\n\nYour invoice INV-000001 from ABC Electrical is ready.\n\nAmount: ₹12,980.00\nDue date: 20 Sep 2026\n\nView and pay your invoice:\nhttps://quotely.app/i/7f9c2a…\n\nThank you.",
    "emailSubject": "Invoice INV-000001 from ABC Electrical",
    "whatsAppUrl": "https://wa.me/919123456780?text=…",
    "mailtoUrl": "mailto:john%40example.com?subject=…&body=…",
    "customerPhone": "919123456780",
    "customerEmail": "john@example.com"
  }
}
```

Only a SHA-256 hash of the token is stored, so **this response is the one and only time the URL
exists**. Calling again mints a new token and the previous URL stops working — which is also how a
link is revoked. A `Draft` invoice becomes `Sent`; a `Cancelled` invoice returns `409`.

`share` (V2.4) is ready-made material for handing the invoice over, composed here because this is
the only moment the URL exists. **Quotely sends nothing**: `whatsAppUrl` is a `wa.me` deep link and
`mailtoUrl` opens the owner's own mail client with a draft. There is no WhatsApp Business API, no
Cloud API, no SMTP, and no email provider anywhere in the codebase.

`message` states the outstanding balance, not the original total, so a part-paid invoice is not
chased for the full amount. `customerPhone` is null when the billing snapshot holds no usable
number, and `whatsAppUrl` then carries the message without a recipient so WhatsApp asks the owner to
choose a contact; `mailtoUrl` behaves the same way when there is no email. The only identifier any
of this carries is the public invoice URL — never a token on its own, a JWT, or a user, customer,
invoice or payment id.

### `GET /api/invoices/stats`

What the business is owed, aggregated by the database. `needsAttention` (default 5, max 20) caps
the list of invoices to chase.

```json
{
  "totalOutstanding": 30000.00, "totalOverdue": 30000.00,
  "countOutstanding": 1, "countOverdue": 1, "currency": "INR",
  "needsAttention": [
    { "id": "…", "invoiceNumber": "INV-000001", "customerName": "John Smith",
      "dueDate": "2026-09-01", "outstanding": 30000.00, "currency": "INR", "isOverdue": true }
  ]
}
```

Drafts and cancelled invoices are excluded from every figure. `needsAttention` is ordered by due
date, longest overdue first, and includes not-yet-due unpaid invoices after them.

### `POST /api/invoices/{id}/payments`

Records money received outside the gateway (V2.5) — cash, a bank transfer, a UPI transfer, a
cheque. Returns `201 Created` with the payment.

```json
{
  "amount": 20000.00,
  "method": "cash",
  "paymentDate": "2026-09-10",
  "reference": "Receipt 4471",
  "notes": "Collected on site"
}
```

`method` is one of `cash`, `bank_transfer`, `upi`, `cheque`, `other`; anything else is `400`.
`paymentDate` defaults to today, may be back-dated, and may not be in the future (`400`).
`reference` (100 chars) and `notes` (500) are optional.

**The amount is validated against a balance the server computes**, never one the client supplies:
more than the outstanding balance is `400`, as is zero or negative. A `Draft` or `Cancelled`
invoice returns `409`. Another tenant's invoice returns `404`.

The payment is recorded as `Captured` with `source: "Manual"` — there is nothing left to confirm —
and the invoice is recalculated through the same path a Razorpay capture uses.

### `POST /api/invoices/{id}/payments/{paymentId}/void`

Stops a manual payment counting toward the balance, keeping the row as an audit record. Returns
`200` with the voided payment.

Only `source: "Manual"` payments can be voided; a gateway payment returns `409`, as does one
already voided. A payment belonging to a different invoice — even one the caller owns — returns
`404`. Voiding recalculates the invoice, so a `Paid` invoice can return to `PartiallyPaid`, or to
`Sent` if nothing counts any more.

### `GET /api/invoices/{id}/payments`

Payment history and the derived financial summary.

```json
{
  "summary": {
    "total": 12980.00, "paid": 3000.00, "outstanding": 9980.00,
    "currency": "INR", "invoiceStatus": "PartiallyPaid",
    "canPay": true, "hasPendingPayment": false
  },
  "payments": [
    { "id": "…", "amount": 3000.00, "currency": "INR", "status": "Captured",
      "provider": "Razorpay", "reference": "pay_…", "orderReference": "order_…",
      "method": "upi", "paidAt": "2026-09-12T09:20:11Z", "createdAt": "…" }
  ]
}
```

`paid` is summed from captured payments; there is no stored, editable paid column. `overpaidBy` is
zero except in the anomaly case where more has been recorded than the invoice total. Each payment
carries `source` (`"Gateway"` or `"Manual"`), `isVoided`, `voidedAt` and `canVoid` — `canVoid` is
true only for a manual payment that still stands, so one action can be rendered without the client
re-deriving the rule. `reference` is the provider's payment id for gateway money and the owner's
own reference for a manual payment. Payment
`status` is our own vocabulary — `Created`, `Pending`, `Captured`, `Failed`, `Cancelled` — not the
provider's.

## Public invoice & payment API (no authentication)

The payment token in the route is the authorization. Unknown, malformed and revoked tokens all
return the same `404`, so a caller cannot learn whether an invoice exists. A `Draft` invoice is
also `404`: it has not been issued.

### `GET /api/public/invoices/{token}`

Returns the invoice as a document plus its payment state: `paid`, `outstanding`, `canPay`,
`hasPendingPayment` and the settled `payments`. Carries no user, business, customer or invoice
identifier of any kind.

### `POST /api/public/invoices/{token}/create-payment-order`

Registers a provider order for the current outstanding balance. **The request body is empty on
purpose** — the amount is the server's to decide. Anything sent in the body is ignored.

```json
{
  "keyId": "rzp_test_…", "orderId": "order_…", "amount": 998000,
  "currency": "INR", "invoiceNumber": "INV-000001",
  "businessName": "ABC Electricals", "customerName": "John Smith", "…": "…"
}
```

`amount` is in minor units (paise), converted from the decimal total with `decimal` arithmetic
only. Opening an order reserves the invoice's balance under a unique index, so repeated and even
simultaneous calls return the **same** order: a double-click, a refresh and a second tab converge
on one payment attempt, and two attempts can never both consume the same balance. Returns `409`
when the invoice is a draft, cancelled, already settled, or has an authorised payment still being
confirmed, and `502` when the provider is unreachable.

### `POST /api/public/invoices/{token}/verify-payment`

```json
{ "razorpayPaymentId": "pay_…", "razorpayOrderId": "order_…", "razorpaySignature": "…" }
```

The server checks that the order belongs to this invoice, verifies the HMAC signature, then asks
the provider what actually happened rather than trusting the callback. Returns the authoritative
balance:

```json
{
  "success": true, "paymentStatus": "Captured", "invoiceStatus": "Paid",
  "total": 12980.00, "paid": 12980.00, "outstanding": 0.00,
  "amountPaid": 12980.00, "currency": "INR", "paymentReference": "pay_…"
}
```

`success` is false for a `Pending` or `Failed` payment, with `message` explaining what to tell the
customer. Repeating the call is safe: the same provider payment can only be recorded once.

### `GET /api/public/invoices/{token}/pdf`

The same invoice PDF the owner downloads, reached through the payment token.

## Webhooks

### `POST /api/webhooks/razorpay`

Anonymous to the JWT scheme; authenticated by `X-Razorpay-Signature`, an HMAC-SHA256 over the
**raw request body** using the webhook secret. Handles `payment.authorized`, `payment.captured`,
`payment.failed` and `order.paid`.

`X-Razorpay-Event-Id` is persisted under a unique index, so a redelivery is acknowledged without
being applied twice. Responses are deliberately terse — `{ "status": "ok" }` or
`{ "status": "rejected" }` with `400` — and never describe internal state.

## Public quotation API (no authentication)

These endpoints are anonymous by design. The share token in the path **is** the authorization: it
grants access to exactly one quotation. There is no listing endpoint, and no route accepts an
internal identifier.

| Method | Path | Notes |
| ------ | ---- | ----- |
| `GET` | `/api/public/quotations/{token}` | The customer-safe view of the quotation |
| `POST` | `/api/public/quotations/{token}/accept` | Records an acceptance |
| `POST` | `/api/public/quotations/{token}/reject` | Records a rejection |
| `GET` | `/api/public/quotations/{token}/pdf` | The same PDF the owner downloads |

### `GET /api/public/quotations/{token}`

```json
{
  "quotationNumber": "QT-000001",
  "quotationDate": "2026-09-11",
  "validUntil": "2026-09-25",
  "business": { "businessName": "ABC Electricals", "phone": "…", "email": "…", "logoUrl": null },
  "customer": { "name": "John Smith", "companyName": "John Smith Construction" },
  "items": [
    {
      "name": "AC Installation",
      "unit": "Service",
      "quantity": 2,
      "unitPrice": 5000.00,
      "discount": 500.00,
      "taxRate": 18.00,
      "lineTotal": 11210.00
    }
  ],
  "subtotal": 11500.00,
  "discountTotal": 500.00,
  "taxTotal": 1980.00,
  "grandTotal": 12980.00,
  "currency": "INR",
  "notes": "…",
  "terms": "…",
  "status": "Sent",
  "isExpired": false,
  "canRespond": true,
  "respondedAt": null,
  "respondedByName": null
}
```

The payload deliberately contains **no** database identifiers — no quotation id, user id or
customer id — and nothing about the token. Totals are the stored, server-calculated values; the
customer page renders them as-is and never recalculates.

An unknown, malformed or retired token returns a plain `404` that reveals nothing about which
quotations exist.

### `POST /api/public/quotations/{token}/accept` and `/reject`

```json
{
  "name": "John Smith",
  "email": "john@example.com",
  "comment": "Approved. Please proceed."
}
```

`name` is required (max 150). `email` is optional but must be well formed. `comment` is optional,
max 1000 characters. The decision comes from the route — a `status`, total or item list in the body
is ignored. On success the updated public DTO is returned.

| Case | Response |
| ---- | -------- |
| Already accepted or rejected | `409` — the first answer stands |
| Past its `validUntil` date | `409` — expired quotations can be viewed but not answered |
| Missing name / bad email / oversized comment | `400` |

## Health

`GET /health` → `{ "status": "ok" }` (anonymous).
