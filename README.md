# Quotely

Quotely is a small-business quotation tool. Create customers and a service catalogue, build a
quotation, and download a professional PDF you can send to a customer.

```
Sign up → Business profile → Customer → Products/Services → Quotation → Totals → PDF → Download
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

## Documentation

- [docs/architecture.md](docs/architecture.md) — structure, data model, request flow, security
- [docs/api.md](docs/api.md) — endpoint reference with payloads
