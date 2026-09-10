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
 └── Quotation         (1:N)
        ├── Customer       (N:1, restrict delete)
        └── QuotationItem  (1:N, cascade delete)
               └── Product (N:1, optional, set null on delete)
```

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
| QuotationItems | `QuotationId` | item loading |

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

## Extension points left open for V2

- `BusinessProfile.LogoUrl` is a plain string holding a data URI; switching to blob storage only
  changes the upload step and that field's value.
- `PdfFonts` registers any TTF dropped into `Pdf/Fonts`, so the PDF typeface can be branded
  without touching the layout code.
- Quotation items are already snapshots, which is what invoicing would need to convert a quotation
  into an invoice later.
