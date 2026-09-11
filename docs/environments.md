# Quotely — Environments, configuration and deployment

This is the beginner's guide to how Quotely is configured, where secrets live, and how code
travels from your laptop to a paying customer.

Read the three rules first. Everything else follows from them.

> ### The three rules
>
> 1. **PROD must never use Razorpay Test credentials**, and TEST/UAT must never use Live ones.
>    Test-mode money is imaginary; live-mode money is real. Crossing the two either takes real
>    money for a test, or silently fails to take real money for a real customer.
> 2. **TEST and UAT must never point at the PROD database.** A tester clicking "delete" should
>    destroy test data, not a customer's invoices.
> 3. **Never commit a secret to GitHub.** Not "temporarily", not in a branch you plan to delete.
>    Git remembers everything, and a pushed secret must be treated as burned and rotated.

---

## The four environments

An *environment* is a running copy of Quotely with its own configuration, its own database and its
own credentials. The same code runs in all four; only the configuration differs.

### DEV — your laptop

Where you write code. Runs on `localhost`, uses a local SQLite file, and talks to Razorpay in Test
Mode. Data here is disposable and is seeded with a demo account. Nothing about DEV is shared, and
nothing real is ever at stake.

### TEST — shared QA

The first environment other people see. Testers and automated checks exercise a real deployment
against a real SQL Server database, still with Razorpay Test Mode. Data is expected to be messy and
can be wiped and reseeded at any time.

### UAT — user acceptance / staging

A rehearsal for production. Configured exactly as PROD is — same database engine, same migration
discipline, same logging — with one deliberate exception: **Razorpay stays in Test Mode.** This is
where the business signs off on a release. If something works in UAT, it should work in PROD.

### PROD — real customers

Real businesses, real invoices, real money. Razorpay Live Mode, production database, production
secrets, no seed data, and no debugging conveniences.

---

## Environment matrix

All values below are **placeholders**. Real values are never written down in this repository.

| Setting | DEV | TEST | UAT | PROD |
| --- | --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Test` | `UAT` | `Production` |
| Config file (committed, no secrets) | `appsettings.Development.json` | `appsettings.Test.json` | `appsettings.UAT.json` | `appsettings.Production.json` |
| Database engine | SQLite file | SQL Server | SQL Server | SQL Server |
| Database instance | `quotely.dev.db` (local) | dedicated TEST database | dedicated UAT database | dedicated PROD database |
| Migrations applied | automatically on start | automatically on start | deliberate deploy step | deliberate deploy step |
| Demo seed data | yes | no | no | **never** |
| Razorpay mode | **Test** | **Test** | **Test** | **Live** |
| Razorpay key ID | test key | test key | test key | live key |
| Razorpay key secret | test secret | test secret | test secret | live secret |
| Razorpay webhook secret | its own | its own | its own | its own |
| JWT signing key | dev fallback (auto) | its own, ≥32 chars | its own, ≥32 chars | its own, ≥32 chars |
| API base URL (frontend) | `http://localhost:5199` | `https://<test-api-host>` | `https://<uat-api-host>` | `https://<prod-api-host>` |
| Public link base URL (API) | `http://localhost:3000` | `https://<test-web-host>` | `https://<uat-web-host>` | `https://<prod-web-host>` |
| OpenAPI / API explorer | enabled | disabled | disabled | disabled |
| HSTS | off | on | on | on |

**Every environment gets its own JWT key, its own webhook secret and its own database.** Sharing a
JWT key means a token minted in TEST would unlock PROD.

---

## Where each setting lives

Quotely uses the standard ASP.NET Core configuration hierarchy. Later sources win:

```
appsettings.json                      ← committed. Structure and safe defaults. Secret slots are EMPTY.
appsettings.{Environment}.json        ← committed. Non-secret, per-environment settings.
environment variables                 ← NOT committed. This is where every secret belongs.
```

A nested setting becomes an environment variable by replacing `:` with a **double underscore**:

| Configuration key | Environment variable |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `Jwt:Key` | `Jwt__Key` (`JWT__KEY` also works) |
| `Razorpay:KeyId` | `Razorpay__KeyId` |
| `Razorpay:KeySecret` | `Razorpay__KeySecret` |
| `Razorpay:WebhookSecret` | `Razorpay__WebhookSecret` |
| `PublicLinks:BaseUrl` | `PublicLinks__BaseUrl` |
| `Cors:AllowedOrigins:0` | `Cors__AllowedOrigins__0` |

### Required environment variables per deployed environment

Set these in your hosting platform's configuration/secrets UI — never in a file in the repository.

```
ASPNETCORE_ENVIRONMENT=Test|UAT|Production
ConnectionStrings__DefaultConnection=<this environment's own database>
Jwt__Key=<random string, 32+ characters, unique per environment>
Razorpay__KeyId=<this environment's Razorpay key id>
Razorpay__KeySecret=<this environment's Razorpay key secret>
Razorpay__WebhookSecret=<this environment's Razorpay webhook secret>
PublicLinks__BaseUrl=<the customer-facing web host, e.g. https://app.example.com>
Cors__AllowedOrigins__0=<the same web host>
```

The API **refuses to start** in any non-Development environment if `Jwt:Key` is missing or shorter
than 32 characters. That is deliberate: failing loudly at boot is far better than running
production on a guessable signing key.

To generate a JWT key:

```bash
openssl rand -base64 48
```

### Local development secrets

DEV needs no secrets at all until you want to test payments. When you do, use .NET user secrets,
which stores values **outside the repository** in your home directory:

```bash
cd backend/Quotely.Api
dotnet user-secrets init
dotnet user-secrets set "Razorpay:KeyId" "<your test key id>"
dotnet user-secrets set "Razorpay:KeySecret" "<your test key secret>"
dotnet user-secrets set "Razorpay:WebhookSecret" "<your test webhook secret>"
```

Never put these in `appsettings.Development.json`, which **is** committed.

---

## Frontend configuration

The frontend has exactly one setting: `NEXT_PUBLIC_API_BASE_URL`.

Locally it lives in `frontend/quotely-web/.env.local` (git-ignored — copy `.env.local.example`).
In TEST/UAT/PROD it is set in the hosting platform, and it must be present **at build time**,
because Next.js inlines `NEXT_PUBLIC_` values into the JavaScript bundle.

> **The `NEXT_PUBLIC_` prefix means "public".** Anything carrying it is shipped to the browser and
> readable by anyone. Never give a secret that prefix. The Razorpay *Key ID* the checkout needs is
> public by design and is handed to the browser by the API at payment time, which is why there is
> no Razorpay setting in the frontend at all.

---

## Database strategy

Each environment owns its own database. They are never shared, and a connection string is never
copied from one environment to another.

**DEV — SQLite.** `Database:Provider` is `Sqlite` and the schema is created directly from the EF
Core model on start-up (`EnsureCreated`), so no Docker or SQL Server install is needed to work on
the app. Delete `quotely.dev.db` to start fresh. A local SQL Server is available via
`docker compose up -d` if you want to rehearse against the real engine.

**TEST / UAT / PROD — SQL Server.** `Database:Provider` is `SqlServer` and the schema comes from EF
Core migrations.

### Are the migrations SQL Server compatible?

**Yes.** The five migrations in `backend/Quotely.Api/Migrations/` are authored for SQL Server and
use its types throughout — `uniqueidentifier`, `nvarchar`, `datetime2`, `bit` — along with filtered
unique indexes such as `filter: "[QuotationId] IS NOT NULL"`, which SQL Server needs so that many
directly raised invoices can each hold `NULL`.

No migration work is outstanding. SQLite never runs a migration; it is built from the model, which
is why the two providers can coexist without a second set of migration files.

| Migration | Adds |
| --- | --- |
| `InitialCreate` | identity, business profile, customers, products, quotations |
| `AddPublicQuotationLinks` | V2.1 share tokens |
| `AddInvoices` | V2.2 invoices and invoice items |
| `AddPayments` | V2.3 payments, webhook events, invoice payment links |
| `AddPaymentReservations` | V2.3 hardening — the live-attempt reservation slot |
| `AddDirectInvoices` | V2.4 — nullable `QuotationId` + filtered unique index |

### Applying migrations

TEST applies migrations automatically on start-up (`Database:AutoMigrate` is `true`), so a QA
database always matches the build being tested.

UAT and PROD do **not**. A schema change there is a deliberate, reviewed step:

```bash
# 1. Back up the database first. Always. A migration can be one-way.
# 2. Generate a SQL script and read it before anything touches the server:
cd backend/Quotely.Api
dotnet ef migrations script --idempotent --output migration.sql

# 3. Review migration.sql, then apply it through your normal database tooling.
```

An idempotent script can be run safely against a database that is already partly up to date, which
is what makes it the right tool for an environment you cannot simply recreate.

Never point `dotnet ef database update` at production from a laptop.

---

## Branching and how code reaches production

Two long-lived branches. Environments are deployment targets, **not** branches — four branches for
four environments means four-way merges and drift.

```
feature/<something>   short-lived, branched from develop, deleted after merge
        │
        ▼
develop ──────────────► deploys to DEV and TEST
        │
        ▼
main    ──────────────► deploys to UAT, then PROD after sign-off
```

1. **Start work.** `git switch develop && git pull && git switch -c feature/manual-payments`
2. **Open a pull request into `develop`.** CI runs the backend tests and the frontend
   typecheck/lint/build on every push and PR.
3. **Merge into `develop`.** TEST picks it up; QA exercises it there.
4. **Release.** Open a PR from `develop` into `main`. Merging publishes a release candidate to UAT.
5. **Sign-off in UAT**, with Razorpay still in Test Mode.
6. **Promote to PROD** from `main`, as a deliberate action — and only then does Razorpay Live Mode
   come into play.

A fix that cannot wait branches from `main`, merges back into `main`, and is then merged down into
`develop` so the two do not diverge.

---

## Setting up a new environment

1. **Create its database.** Dedicated, never shared with another environment.
2. **Create its Razorpay credentials.** Test Mode for TEST and UAT; Live Mode for PROD only.
3. **Generate a fresh JWT key** — `openssl rand -base64 48`. Never reuse another environment's.
4. **Set the environment variables** listed above in the hosting platform's secret store.
5. **Set `ASPNETCORE_ENVIRONMENT`** to `Test`, `UAT` or `Production`.
6. **Deploy the API**, then confirm `GET /health` returns `{"status":"ok"}`.
7. **Apply migrations** — automatic in TEST, a reviewed script in UAT and PROD.
8. **Deploy the frontend** with `NEXT_PUBLIC_API_BASE_URL` set to that environment's API host.
9. **Register the Razorpay webhook** at `https://<that-api-host>/api/webhooks/razorpay`, using that
   environment's own webhook secret.
10. **Verify the pairing**: a test payment in TEST/UAT must appear in the *test* Razorpay
    dashboard. If it appears in the live one, stop and re-check the credentials.

---

## If a secret is ever committed

Treat it as compromised the moment it is pushed — assume it has been scraped.

1. **Rotate it immediately** in the Razorpay dashboard or wherever it came from. This is the step
   that actually makes you safe.
2. Remove it from the code and move it to an environment variable.
3. Rewriting git history is optional cleanup, not a fix. A pushed secret is already out.

---

## Related documentation

- [`README.md`](../README.md) — running Quotely locally
- [`docs/api.md`](api.md) — API reference
- [`docs/architecture.md`](architecture.md) — how the system fits together
