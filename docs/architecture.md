# Quotely — Architecture

## Overview

Quotely is a two-tier application: a Next.js single-page dashboard talking to an ASP.NET Core
REST API over JSON. The API owns all business rules, money calculations and PDF rendering; the
frontend is a presentation layer that never decides what a quotation is worth.

```
Browser (Next.js)
   │  JSON + Bearer token
   ▼
ASP.NET Core Web API
   ├── Controllers      thin HTTP layer, DTOs in and out
   ├── Services         quotation rules, numbering, totals, PDF, tokens
   ├── Data             EF Core DbContext + migrations
   └── Pdf              QuestPDF document composition
   │
   ▼
SQL Server
```

There is deliberately no repository layer: EF Core's `DbSet` already is one, and adding another
indirection would not earn its keep at this size.

## Backend layout

```
backend/Quotely.Api/
├── Controllers/     AuthController, BusinessProfileController, CustomersController,
│                    ProductsController, QuotationsController
├── Services/        QuotationCalculator, QuotationService, PdfService, TokenService, CurrentUser
├── Data/            AppDbContext, DevSeeder, DesignTimeDbContextFactory
├── Models/          EF entities (AppUser, BusinessProfile, Customer, Product,
│                    Quotation, QuotationItem, QuotationStatus)
├── DTOs/            Request/response contracts — entities are never returned directly
├── Pdf/             QuotationDocument, CurrencyFormatter, PdfFonts
├── Middleware/      ExceptionHandlingMiddleware, ApiException
└── Program.cs       Composition root: config, Identity, JWT, CORS, DI, migrations, seeding
```

## Data model

```
AppUser (Identity)
 ├── BusinessProfile   (1:1)
 ├── Customer          (1:N)
 ├── Product           (1:N)
 ├── Quotation         (1:N)
 │      ├── Customer       (N:1, restrict delete)
 │      ├── QuotationItem  (1:N, cascade delete)
 │      │      └── Product (N:1, optional, set null on delete)
 │      └── Invoice        (1:1, optional, restrict delete)
 └── Invoice           (1:N)
        ├── Customer       (N:1, restrict delete — navigation only)
        └── InvoiceItem    (1:N, cascade delete)
```

`Quotations` additionally carries the V2.1 share-link columns — `PublicTokenHash` (unique),
`PublicLinkCreatedAt` — and the customer's answer: `RespondedAt`, `RespondedByName`,
`RespondedByEmail`, `ResponseComment`. The response lives on the quotation rather than on the
`Customer` record, because whoever replies is not necessarily the stored contact.

Every tenant-owned table carries a `UserId` foreign key. `CreatedAt` / `UpdatedAt` are stamped
centrally in `AppDbContext.SaveChanges`.

Indexes:

| Table | Index | Purpose |
| ----- | ----- | ------- |
| BusinessProfiles | `UserId` (unique) | one profile per user |
| Customers | `(UserId, Name)` | scoped list + search ordering |
| Products | `(UserId, Name)` | scoped list + search ordering |
| Quotations | `(UserId, QuotationNumber)` unique | numbers unique per business |
| Quotations | `(UserId, Sequence)` unique | allocation of the next number |
| Quotations | `(UserId, Status)` | dashboard counts and status filter |
| Quotations | `PublicTokenHash` unique, filtered | share-link lookup; many rows have no link |
| QuotationItems | `QuotationId` | item loading |
| Invoices | `(UserId, InvoiceNumber)` unique | numbers unique per business |
| Invoices | `(UserId, Sequence)` unique | allocation of the next number |
| Invoices | `(UserId, Status)` | status filter |
| Invoices | `QuotationId` unique | one invoice per quotation, enforced by the database |
| InvoiceItems | `InvoiceId` | item loading |

Money uses `decimal(18,2)`, quantities `decimal(18,3)` and tax rates `decimal(5,2)`. No monetary
value is ever a `float` or `double`, in the database or in C#.

## Quotation calculation

`QuotationCalculator` is the single source of truth and is a pure static class, which is what makes
it directly testable.

```
per line:  gross = quantity × unitPrice
           net   = gross − discount          (discount is absolute, clamped to gross)
           tax   = net × taxRate / 100
           total = net + tax

per quote: subtotal      = Σ gross
           discountTotal = Σ discount
           taxTotal      = Σ tax
           grandTotal    = subtotal − discountTotal + taxTotal
```

Every value is rounded to two decimals with `MidpointRounding.AwayFromZero`. Discounts reduce the
taxable amount, so tax is charged on the discounted figure.

`lib/money.ts` in the frontend mirrors these rules so the form can show totals as the user types.
Those numbers are advisory: `QuotationService` recalculates from the submitted quantities and
prices on every create, update and PDF render, and only the server's figures are persisted.

## Quotation numbers

Numbers are `QT-000001`, allocated from a `Sequence` column that is unique per user, so each
business starts at 1 and numbers stay stable across edits.

## Request flow (create a quotation)

1. `POST /api/quotations` with a bearer token.
2. JWT middleware validates the signature, issuer, audience and lifetime.
3. `ICurrentUser` reads the user id **from the validated token claims only**.
4. `QuotationService` validates the request (items present, quantities positive, dates ordered),
   confirms the customer belongs to this user, and keeps only product references the user owns.
5. Line items are snapshotted (name, unit, price, tax) so later catalogue edits don't rewrite
   historical quotations.
6. Totals are calculated server-side and saved with the quotation in one transaction.
7. The saved quotation is returned as a DTO.

## Security

- **Password handling** — ASP.NET Core Identity (PBKDF2), minimum 8 characters, lockout after 10
  failed attempts.
- **Tokens** — short-lived signed JWTs. The signing key comes from configuration and the app
  refuses to start outside Development without one of at least 32 characters.
- **Tenant isolation** — the user id is only ever taken from token claims; a client-supplied id is
  never trusted. Every query is filtered by `UserId`, and a record owned by someone else returns
  `404` rather than `403`, so ids cannot be probed. This is covered by tests.
- **Validation** — DataAnnotations on DTOs plus explicit business-rule checks in the service layer.
  Frontend validation exists for feedback only.
- **Errors** — `ExceptionHandlingMiddleware` maps everything to `{ "message", "status" }`. Stack
  traces and EF exception details never reach the client; unexpected failures are logged
  server-side and reported as a generic message.
- **CORS** — an explicit origin allowlist; `Content-Disposition` is exposed so the browser can read
  the PDF filename.
- **Secrets** — nothing sensitive is committed. Configuration comes from environment variables.

## Frontend layout

```
frontend/quotely-web/
├── app/
│   ├── (auth)/        login, register — centred card layout
│   ├── (app)/         authenticated shell (sidebar) + dashboard, quotations,
│   │                  customers, products, business-profile
│   └── layout.tsx     ToastProvider + AuthProvider
├── components/ui/     button, field, card, table, dialog, badge, toast, states
├── components/app/    quotation-form, customer-form, product-form, page-header
├── lib/               api client, auth context, money mirror, formatting
└── types/             shared API contracts
```

The token and user are held in `localStorage` and attached by the API client. A `401` from any
request clears the session and redirects to `/login`. The `(app)` layout guards every
authenticated route.

## Customer-facing share links (V2.1)

A business owner can hand a customer one URL — `/q/{token}` — that shows the quotation and lets
them accept or reject it without a Quotely account.

```
Owner (JWT)                          Customer (no account)
   │                                        │
   ├─ POST /api/quotations/{id}/public-link │
   │     random 32 bytes → token            │
   │     SHA-256(token) → Quotations.PublicTokenHash
   │     returns https://…/q/{token} ───────┤
   │                                        ├─ GET  /api/public/quotations/{token}
   │                                        ├─ POST …/accept   or   …/reject
   │                                        │        server sets status + RespondedAt
   ├─ GET /api/quotations/{id} ─────────────┘        (never trusts the payload)
   └─ sees Accepted/Rejected, who answered, and their comment
```

**The token.** 32 bytes from `RandomNumberGenerator`, URL-safe base64 (43 characters, 256 bits of
entropy), so enumeration is not feasible. It is a bearer capability: anyone with the URL can view
and answer that one quotation, and nothing else.

**Storage.** Only `SHA-256(token)` is persisted, in `Quotations.PublicTokenHash` behind a unique
index. A stolen database backup therefore yields no working links. The consequence, accepted
deliberately, is that the URL cannot be shown twice — the owner copies it when it is created, and
asking for a link again issues a fresh one and retires the previous URL. That doubles as link
revocation, and the UI warns before replacing.

**Lookup.** The incoming token is hashed and matched against that unique index, so a token can
never resolve to a different quotation. Anything unknown is a plain `404`.

**What the customer may do.** Nothing but answer once. The decision comes from the route, not the
payload; amounts, line items and status are never read from the request. `Accepted` and `Rejected`
are terminal (`409` on a second attempt), and a quotation past its `ValidUntil` can be viewed but
not answered — reusing the existing date rather than adding a second expiry mechanism. Editing a
quotation leaves its link and any recorded response intact.

**Data shown.** `PublicQuotationDto` is a separate contract from the owner's `QuotationDto`: no
user, business, customer or quotation ids, and no token material. Totals come from the stored
server-calculated columns.

**Logging.** `ExceptionHandlingMiddleware` redacts the token segment of `/api/public/quotations/…`
before any path reaches the log, so log readers never obtain working links.

Not built for V2.1, and reasonable next steps: a link expiry or explicit revoke button separate
from replacement, and rate limiting on the public endpoints.

## Invoicing (V2.2)

An accepted quotation converts into an invoice — a separate financial document with its own table,
its own numbering (`INV-000001`, sequential per user) and its own status enum (`Draft`, `Sent`,
`PartiallyPaid`, `Paid`, `Overdue`, `Cancelled`). Quotation status is untouched by any of it.

```
Quotation (Accepted)
      │  POST /api/quotations/{id}/convert-to-invoice
      ▼
Invoice  ──►  InvoiceItems      (snapshot of the quotation lines)
      │       customer snapshot (name, company, address, contact)
      │       currency snapshot
      ▼
GET /api/invoices/{id}/pdf      (rendered from the snapshot alone)
```

**Why a snapshot, and how it is enforced.** An invoice has to stay true to what was agreed. The
quotation already snapshots its own lines, but it renders the customer block from the live
`Customer` row and the currency from the live `BusinessProfile` — fine for an offer, wrong for a
financial record. So `Invoice` stores the billing details and the currency, and `InvoiceItem` has
deliberately **no** `ProductId`: there is no path by which a line could resolve its price through
today's catalogue. Only the letterhead is read live, because that is the issuer's own identity.

**Totals.** `InvoiceCalculator` applies the same rules as `QuotationCalculator` to invoice
entities. Conversion therefore recomputes the totals from the copied lines rather than trusting a
copied number, and then asserts the result equals the accepted quotation's grand total — a mismatch
aborts the conversion instead of silently issuing a wrong invoice. `InvoiceCalculatorTests` pins the
two calculators to each other so a future divergence fails a test rather than an invoice.

**One invoice per quotation.** `Invoices.QuotationId` carries a unique index, so a duplicate cannot
be written even under a race; the service checks first and returns `409` naming the existing
invoice. The FK back to `Quotations` is `Restrict`, so deleting an invoiced quotation is refused
with a message pointing at the invoice.

**Editing.** `Invoice.AllowsFinancialEdits` (draft only) gates the line items and totals;
`Invoice.IsLocked` (`Paid` or `Cancelled`) gates the whole update. Both are properties on the
entity rather than checks scattered through the controller, so V2.3 can widen `IsLocked` to "has
payments" in one place. `Paid` and `PartiallyPaid` invoices also refuse deletion — cancel instead.

**Overdue** is derived from `DueDate` for display (`isOverdue`) and is also a status the owner can
set. Nothing runs in the background and no second expiry mechanism exists.

Not built for V2.2, by instruction: payments, part-payments and receipts — V2.3.

## Extension points left open for V2

- `BusinessProfile.LogoUrl` is a plain string holding a data URI; switching to blob storage only
  changes the upload step and that field's value.
- `PdfFonts` registers any TTF dropped into `Pdf/Fonts`, so the PDF typeface can be branded
  without touching the layout code.
- Quotation items are already snapshots, which is what invoicing needed to convert a quotation
  into an invoice (delivered in V2.2).
