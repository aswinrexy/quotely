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
| `GET` | `/api/invoices/{id}` | one invoice with items |
| `PUT` | `/api/invoices/{id}` | update dates, status, notes, terms and (draft only) items |
| `DELETE` | `/api/invoices/{id}` | delete, unless `Paid` or `PartiallyPaid` |
| `GET`/`POST` | `/api/invoices/{id}/pdf` | render the invoice PDF |

`search` matches the invoice number, the snapshotted customer name or company, and the source
quotation number. `status` is one of `Draft`, `Sent`, `PartiallyPaid`, `Paid`, `Overdue`,
`Cancelled`.

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
