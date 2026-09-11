# Quotely

Quotely is a small-business quotation tool. Create customers and a service catalogue, build a
quotation, and download a professional PDF you can send to a customer.

```
Sign up → Business profile → Customer → Products/Services → Quotation → Totals → PDF → Download
                                                                  ↓
                                      Share link → Customer accepts → Invoice → Invoice PDF
                                                                          ↓
                                                      Payment link → Customer pays → Paid
```

## Tech stack

| Layer    | Choice |
| -------- | ------ |
| Frontend | Next.js 16 (App Router), TypeScript, React 19, Tailwind CSS 4 |
| Backend  | ASP.NET Core 10 Web API, C#, Entity Framework Core |
| Database | SQL Server (SQLite available for local development) |
| Auth     | ASP.NET Core Identity + JWT bearer tokens |
| PDF      | QuestPDF, generated server-side |

## Prerequisites

- .NET SDK 10.0+
- Node.js 20+
- One of:
  - Docker (for the bundled SQL Server container), or
  - an existing SQL Server instance, or
  - nothing extra — the development profile falls back to SQLite

## Project layout

```
quotely/
├── backend/
│   ├── Quotely.sln
│   ├── Quotely.Api/          # Controllers, Services, Data, DTOs, Models, Pdf, Middleware
│   └── Quotely.Tests/        # Unit + integration tests
├── frontend/
│   └── quotely-web/          # Next.js app, components, lib, types
├── docs/
│   ├── architecture.md
│   └── api.md
├── docker-compose.yml
└── README.md
```

## Configuration

No secrets are committed. The API reads configuration from `appsettings.json`, environment
variables, and (in development) user secrets. Environment variables use `__` for nesting.

| Setting | Environment variable | Notes |
| ------- | -------------------- | ----- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | Database connection string |
| `Database:Provider` | `Database__Provider` | `SqlServer` (default) or `Sqlite` |
| `Database:AutoMigrate` | `Database__AutoMigrate` | Apply migrations at startup (default: on in Development) |
| `Jwt:Key` | `JWT__KEY` | **Required outside Development.** 32+ characters |
| `Jwt:Issuer` / `Jwt:Audience` | `JWT__ISSUER` / `JWT__AUDIENCE` | Default `Quotely` / `QuotelyWeb` |
| `Jwt:AccessTokenMinutes` | `JWT__ACCESSTOKENMINUTES` | Access-token lifetime, default 120 |
| `Cors:AllowedOrigins:0` | `CORS__ALLOWEDORIGINS__0` | Frontend origin, default `http://localhost:3000` |
| `PublicLinks:BaseUrl` | `PUBLICLINKS__BASEURL` | Origin used to build customer share links, default `http://localhost:3000` |
| `Seed:Enabled` | `SEED__ENABLED` | Seed demo data (default: on in Development) |

The frontend needs one variable, in `frontend/quotely-web/.env.local`:

```
NEXT_PUBLIC_API_BASE_URL=http://localhost:5199
```

Copy it from the provided example:

```bash
cp frontend/quotely-web/.env.local.example frontend/quotely-web/.env.local
```

## Setting up the database

### Option A — SQL Server via Docker (recommended)

```bash
MSSQL_SA_PASSWORD='Your_strong_password123' docker compose up -d
```

Then point the API at it:

```bash
export Database__Provider=SqlServer
export ConnectionStrings__DefaultConnection="Server=localhost,1433;Database=Quotely;User Id=sa;Password=Your_strong_password123;TrustServerCertificate=True"
```

### Option B — SQLite (no Docker required)

The `Development` profile already defaults to SQLite (`backend/Quotely.Api/quotely.dev.db`), so
`dotnet run` works with no setup at all. Use this when you just want the app running locally.

> Migrations are authored for SQL Server, which is the supported production database. Under SQLite
> the schema is created directly from the EF model at startup instead of being migrated.

## Running migrations

Migrations live in `backend/Quotely.Api/Migrations` and target SQL Server. They are applied
automatically at startup in Development (`Database:AutoMigrate`). To run them by hand:

```bash
dotnet tool install --global dotnet-ef
export DOTNET_ROOT="$HOME/.dotnet"   # only if the dotnet SDK is not on the default path
cd backend
dotnet ef database update --project Quotely.Api -- --provider SqlServer
```

To add a migration after changing the model:

```bash
dotnet ef migrations add <Name> --project Quotely.Api --output-dir Migrations -- --provider SqlServer
```

## Start the backend

```bash
cd backend
dotnet run --project Quotely.Api --urls http://localhost:5199
```

- API: <http://localhost:5199>
- Health check: <http://localhost:5199/health>
- OpenAPI document (Development): <http://localhost:5199/openapi/v1.json>

## Start the frontend

```bash
cd frontend/quotely-web
npm install
npm run dev
```

Open <http://localhost:3000>.

## Running tests

```bash
cd backend
dotnet test
```

Covers quotation totals, tax and discount maths, rounding, per-user quotation numbering, data
isolation between users, request validation, error envelopes, and PDF generation (including
multi-page documents and long descriptions).

Frontend checks:

```bash
cd frontend/quotely-web
npx tsc --noEmit
npm run lint
npm run build
```

## Demo account

With seeding enabled (the default in Development), the API creates a demo business on first run:

```
Email:    demo@quotely.app
Password: Demo@12345
```

It comes with a business profile (ABC Electricals), two customers, five services, and one
quotation (`QT-000001`). To create your own account instead, use **Create an account** on the
sign-in page.

To reseed from scratch under SQLite, delete `backend/Quotely.Api/quotely.dev.db` and restart the API.

## How PDF generation works

1. The frontend calls `POST /api/quotations/{id}/pdf`.
2. The API loads the quotation **scoped to the authenticated user** and its business profile.
3. `QuotationDocument` (QuestPDF) composes an A4 layout: business header and optional logo,
   quotation number and dates, bill-to block, an item table whose header repeats across pages,
   a totals panel, and notes/terms. Long descriptions wrap and long quotations paginate.
4. The bytes are returned as `application/pdf` with a `Content-Disposition` filename such as
   `QT-000001-John-Smith.pdf`; the browser saves it via a blob download.

PDFs are never stored — they are rendered on demand from the current quotation data, so a PDF
always matches what is in the database. Money is stored and calculated with `decimal`
(never floating point), and totals are always recalculated server-side before saving or printing.

## Customer-facing quotation links

From a quotation's details page, **Generate share link** produces a URL such as
`http://localhost:3000/q/7f9c2a…`. The customer opens it with no account, reviews the quotation and
accepts or rejects it; the owner sees the outcome, who answered and their comment.

The token is 32 cryptographically random bytes and only its SHA-256 hash is stored, so the URL is
shown exactly once — copy it when it appears. Creating a new link retires the previous one, which
is how a link is revoked today. A `Draft` quotation becomes `Sent` when it is shared, an answered
quotation cannot be answered again, and a quotation past its valid-until date can be viewed but not
accepted or rejected.

See [docs/api.md](docs/api.md) for the endpoints and [docs/architecture.md](docs/architecture.md)
for the security model.

## Invoicing

There are two ways to raise an invoice, and both produce the same document.

**Directly.** **Invoices → Create invoice** bills a customer who was never quoted: pick the
customer, add lines, set the dates, save. This is the shorter path for work that was never
quoted — a call-out, a repair, a repeat job.

**From a quotation.** Once a customer has accepted a quotation, its details page offers **Convert
to Invoice**.

Either way you get a numbered invoice (`INV-000001`, sequential per business, one sequence shared
by both paths), which lives under **Invoices** with its own list, detail page, edit screen and PDF.
The list shows where each came from — the source quotation's number, or "Direct invoice".

The invoice is a separate document, not a flag on the quotation. It stores its own copy of the line
items, the billing details and the currency, so a later change to a product's price or a customer's
address cannot restate an invoice that has already been issued. A quotation converts once — a
second attempt reports the existing invoice — and an invoiced quotation cannot be deleted until its
invoice is.

There is one `Invoice` entity and one `Invoices` table. A directly raised invoice is not a
different type — it simply has no source quotation, and everything downstream (PDF, public link,
payments, dashboard) treats the two identically.

Invoice status (`Draft`, `Sent`, `Partially paid`, `Paid`, `Overdue`, `Cancelled`) is separate from
quotation status. Line items can only be changed while the invoice is a draft; a `Paid` or
`Cancelled` invoice can no longer be edited, and a `Paid` or partly paid one cannot be deleted. The
due date defaults to 15 days after the invoice date and is editable while the invoice is a draft.

## Sharing an invoice

An invoice's details page has **Share invoice**. It mints the customer-facing link and offers three
ways to hand it over:

- **WhatsApp** — opens WhatsApp (the app on a phone, WhatsApp Web on a desktop) with a message
  already written: the customer's name, the invoice number, your business name, the outstanding
  amount, the due date and the link.
- **Email** — opens your own mail client with the same message as a draft, addressed to the
  customer and with the subject filled in.
- **Copy link** — copies the URL.

**Quotely does not send anything.** Both actions are deep links that hand the message to WhatsApp
or to your mail app, with you in the loop; nothing leaves the server. This milestone deliberately
introduces no WhatsApp Business API, no Meta Cloud API, no SMTP server and no email provider —
there is nothing to configure, and no delivery to guarantee. Automated sending and reminders are a
later milestone.

The message and both links are composed server-side, where the authoritative figures are, and the
amount shown is what is still outstanding rather than the original total. The only identifier any
of it carries is the public invoice URL.

Sharing a draft issues it (`Draft` becomes `Sent`), the same as before. Because only a hash of the
token is stored, the URL is shown once: reopening the page later offers **Share again**, which
mints a new link and immediately retires the old one.

## Online payments (Razorpay)

An issued invoice can be shared as a payment link. The customer opens `/i/{token}`, sees the
outstanding balance and pays through Razorpay Checkout; the invoice updates from the server's own
record of what was captured, never from the browser.

### Configuration

Three values, all server-side. Never commit them.

| Setting | Environment variable | Purpose |
| ------- | -------------------- | ------- |
| `Razorpay:KeyId` | `Razorpay__KeyId` | Publishable key. Sent to the browser to open checkout. |
| `Razorpay:KeySecret` | `Razorpay__KeySecret` | Signs API calls and verifies checkout signatures. **Server only.** |
| `Razorpay:WebhookSecret` | `Razorpay__WebhookSecret` | Verifies webhook signatures. **Server only.** |

In development, use user secrets rather than a file:

```bash
cd backend/Quotely.Api
dotnet user-secrets init
dotnet user-secrets set "Razorpay:KeyId" "rzp_test_xxxxxxxx"
dotnet user-secrets set "Razorpay:KeySecret" "xxxxxxxx"
dotnet user-secrets set "Razorpay:WebhookSecret" "xxxxxxxx"
```

With no keys configured the app runs normally and the public invoice page simply shows the balance
without a Pay button — nothing breaks.

### Testing a payment end to end

1. Create a Razorpay account and copy the **test mode** Key ID and Key Secret from
   Dashboard → Account & Settings → API Keys.
2. In Dashboard → Account & Settings → Webhooks, add a webhook pointing at
   `https://<your-host>/api/webhooks/razorpay`. Locally, expose the API with a tunnel first.
   Set a webhook secret and configure it as `Razorpay__WebhookSecret`.
3. Subscribe the webhook to `payment.authorized`, `payment.captured`, `payment.failed` and
   `order.paid`.
4. Start the API and the web app, then sign in.
5. Create a quotation, mark it Accepted and convert it to an invoice.
6. On the invoice, choose **Create payment link** and copy the URL.
7. Open `/i/{token}` — in a private window, to prove no session is involved.
8. Choose **Pay**, and use a Razorpay test instrument (for example the test card
   `4111 1111 1111 1111` with any future expiry and any CVV, or the `success@razorpay` test UPI id).
9. The page shows the server-verified result, and the owner's invoice shows the payment under
   **Payment history** with the balance and status updated.

The test suite never contacts Razorpay: `IPaymentProvider` is replaced by a fake that uses the same
HMAC schemes, so signature handling is genuinely exercised offline.

## Documentation

- [docs/architecture.md](docs/architecture.md) — structure, data model, request flow, security
- [docs/api.md](docs/api.md) — endpoint reference with payloads
