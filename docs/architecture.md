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
 └── Invoice           (1:N)   ← two creation paths, one entity
        ├── Customer       (N:1, restrict delete — navigation only)
        ├── InvoiceItem    (1:N, cascade delete)
        └── Payment        (1:N, cascade delete — Gateway or Manual)

WebhookEvent          (standalone — provider event idempotency)
```

An `Invoice` is raised either by converting an accepted quotation (V2.2) or directly, without one
(V2.4). There is deliberately no `DirectInvoice` type, no origin column and no second table: the
path is recorded by nothing more than whether `QuotationId` is set, and every downstream
concern — PDF, public link, payments, list, dashboard — treats the two identically.

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
| Invoices | `QuotationId` unique, filtered | one invoice per quotation, enforced by the database; the filter lets every directly raised invoice hold `NULL` |
| InvoiceItems | `InvoiceId` | item loading |
| Invoices | `PublicTokenHash` unique, filtered | payment-link lookup |
| Payments | `ProviderPaymentId` unique, filtered | one provider payment, one record — enforced by the database |
| Payments | `(InvoiceId, Status, VoidedAt)` | the ledger read: captured, non-voided rows for one invoice |
| Payments | `ProviderOrderId` | order lookup; not unique, because an order may be retried |
| Payments | `InvoiceId`, `(InvoiceId, Status)` | summing captured payments |
| Payments | `(UserId, CreatedAt)` | tenant-scoped history |
| WebhookEvents | `(Provider, EventId)` unique | webhook idempotency |

Money uses `decimal(18,2)`, quantities `decimal(18,3)` and tax rates `decimal(5,2)`. No monetary
value is ever a `float` or `double`, in the database or in C#.

## The payment ledger (V2.3 → V2.5)

One table, two entrances, one calculation.

```
   Razorpay capture                    Owner records cash
   (webhook / checkout)                (POST …/payments)
           │                                   │
  ProcessOutcomeAsync()              RecordManualPaymentAsync()
           │                                   │
           └──────────────┬────────────────────┘
                          ▼
                    Payments table
              Source = Gateway | Manual
                          ▼
              RecalculateInvoiceAsync()
                          ▼
            Draft · Sent · PartiallyPaid · Paid
```

`Payment.Source` is an explicit column rather than something inferred from the provider string,
because "only a manual payment may be voided" has to be unambiguous in a query and in an
authorization check. A manual payment has `ProviderOrderId` and `ProviderPaymentId` genuinely null
— not a placeholder, which would make "has no order" indistinguishable from "we failed to record
one" — and is written as `Captured` immediately, since the owner is reporting money they already
hold.

**Voiding never deletes.** `VoidedAt` is set, the row stays, and it stops counting. That is the
whole mechanism: a voided payment is excluded by the ledger rule, and the invoice recalculates
through the same path everything else uses, so a `Paid` invoice can fall back to `PartiallyPaid` or
to `Sent`.

### InvoiceLedger — one definition of "paid"

`InvoiceLedger` is where the rule lives:

> A payment counts toward an invoice when it is **Captured** and **has not been voided**.

Everything that asks a money question composes from it — the invoice detail, the invoice list, the
overdue filter, the dashboard receivables and the customer summary. The rule appears there in
several mechanical forms (a filter, two scalar projections, a row projection, a single-invoice
lookup) because EF Core needs the subquery written inline at each point and cannot inline a shared
`Expression`; they sit adjacent in one file, and `Payment.CountsTowardPaid` is the in-memory twin.

V2.5 began with a second copy of this sum living privately in `InvoiceService`. It was harmless
until voiding existed, at which point the invoice detail disagreed with the ledger — the test suite
caught it immediately. That is why there is now one place.

Everything composes as `IQueryable`, so the sums and filters run in SQL. The dashboard never pages
invoices into the browser to add them up.

### Overdue is derived, never stored

`InvoiceStatus.Overdue` exists in the enum and is deliberately never assigned. An invoice reads as
overdue when it is issued, not cancelled, past its due date and still owing something — evaluated
per request.

Deriving it means an invoice becomes overdue because the date rolled over: no scheduler, no nightly
sweep, no background service, and no stored value that can drift out of step with the calendar.
Paying in full stops it being overdue on the very next read; voiding that payment makes it overdue
again. The enum member is kept because removing it would change the API's status vocabulary and
break an invoice an owner had set to `Overdue` by hand under V2.2 rules.

## Invoice sharing (V2.4)

`InvoiceShareBuilder` is a pure static class, like the calculators, and for the same reason: it can
be asserted on directly rather than through a browser. It turns an invoice, a business name, an
outstanding amount and a public URL into the message a customer receives plus a `wa.me` link and a
`mailto:` link, percent-encoding both.

It lives server-side because the wording and the figures are the business's own financial
communication — the amount is what is still outstanding, taken from the same derived summary the
rest of the system uses — and because the share material can only be composed at the one moment the
public URL exists, in the response to `POST /api/invoices/{id}/public-link`. The database keeps only
the token's SHA-256 hash, so there is no later opportunity.

Nothing is sent by Quotely. There is no WhatsApp Business API, no Meta Cloud API, no SMTP client and
no email provider in the codebase; WhatsApp and the owner's own mail client do the sending. The only
identifier that appears in any generated URL is the public invoice URL itself.

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

## Payments (V2.3)

An invoice can be shared as a payment link and settled online through Razorpay.

```
Invoice (Sent / PartiallyPaid / Overdue)
      │  POST /api/invoices/{id}/public-link
      ▼
/i/{token}                        ← bearer capability, hash-only storage
      │  POST …/create-payment-order   (no amount in the request)
      ▼
Razorpay order, priced by the server
      │
      ├── checkout → …/verify-payment ──┐
      │                                  ├──►  IPaymentService.ProcessOutcomeAsync
      └── webhook → /api/webhooks/razorpay ─┘        (the only route into the ledger)
                                                              │
                                                              ▼
                                             Payment rows → paid / outstanding → status
```

**One path into the ledger.** Checkout verification and webhook processing both reduce a provider
event to a `PaymentOutcome` and call `ProcessOutcomeAsync`. Neither does arithmetic of its own, so
there is no second payment state machine that can disagree with the first.

**The browser is never the source of truth.** The create-order endpoint takes no amount; the
server computes `total − captured` itself. Checkout's callback is only a claim: the signature is
verified, the order is confirmed to belong to *this* invoice, and then the provider is asked what
actually happened before anything is recorded.

**Idempotency is a database constraint, not a check.** `Payments.ProviderPaymentId` is uniquely
indexed, so a webhook and a checkout callback racing each other end with one row — the loser
re-reads the winner's result. `WebhookEvents(Provider, EventId)` does the same for redeliveries.

**One live attempt per invoice, also a constraint.** `Payments.ReservationSlot` holds the invoice
id while an attempt is live and is null once it settles, under a filtered unique index. Opening an
order therefore *reserves* the balance: a second concurrent request loses the insert and is handed
the winner's order rather than a second way to pay the same money. Without this, six simultaneous
requests produce six independently payable orders — a test demonstrates exactly that when the
constraint is removed. Reservations lapse (15 minutes for an untouched order, 24 hours for an
authorised payment) so an abandoned tab cannot lock an invoice out of being paid, and a part
payment retires a stale order so the next one is repriced to what is actually owed.

**order.paid is reconciliation, never invention.** Both `payment.captured` and `order.paid` are
read for their `payload.payment.entity`, so `order.paid` names the *actual* payment by id and
collapses onto the same record through the unique index — in either arrival order. An order-level
event carrying no payment entity produces no outcome at all: there would be no payment id to
deduplicate on, so we wait for `payment.captured` rather than inventing a successful payment from
"the order is paid".

**Overpayment is recorded, not hidden.** Normal use cannot exceed the balance. If a provider-side
condition ever captures more than the total, the payment is recorded truthfully, `outstanding`
clamps to zero for display, the invoice reads `Paid`, and the excess is reported as `overpaidBy`
on the summary and shown to the owner. V2.3 does not attempt an automatic refund.

**Ordering is not assumed.** Razorpay does not guarantee webhook order, so `PaymentStatus` is
ranked and an incoming state may only be applied if it is at least as authoritative. A late
`payment.authorized` cannot demote a captured payment.

**Money stays decimal.** Amounts are `decimal(18,2)` throughout; conversion to paise is
`decimal.Round(amount * 100m, 0, AwayFromZero)`. No `double` touches a monetary value at any point.

**Paid is derived, never stored.** `paid = SUM(Payment.Amount WHERE Status = Captured)`, computed
on read. Only `Captured` counts — an authorised-but-uncaptured payment is money the business does
not have yet. Invoice status follows the balance, but only once money has actually arrived: an
invoice with no payments keeps whatever status its owner set, so V2.2's manual lifecycle is intact.

**Provider isolation.** `IPaymentProvider` is the seam. Razorpay's REST calls, both HMAC schemes
and its status vocabulary live in `RazorpayPaymentProvider`; nothing outside `Payments/` knows what
a Razorpay response looks like. The test suite substitutes a fake implementation and never touches
the network.

Not built for V2.3, by instruction: refunds, subscriptions, manual payment entry, and any second
provider.

## Extension points left open for V2

- `BusinessProfile.LogoUrl` is a plain string holding a data URI; switching to blob storage only
  changes the upload step and that field's value.
- `PdfFonts` registers any TTF dropped into `Pdf/Fonts`, so the PDF typeface can be branded
  without touching the layout code.
- Quotation items are already snapshots, which is what invoicing needed to convert a quotation
  into an invoice (delivered in V2.2).
