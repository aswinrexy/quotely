# Quotely — free-tier hosting

**This is temporary startup infrastructure, not the final architecture.** It exists so Quotely can
be put in front of real people at **zero monthly cost** while the product is still being validated.
Everything below runs on a free plan, and every free plan has teeth. They are listed honestly in
[Free-tier limitations](#free-tier-limitations) — read that section before promising anyone an SLA.

| Layer | Platform | Plan | Cost |
| --- | --- | --- | --- |
| Frontend (Next.js) | Vercel | Hobby | £0 — see the caveat below |
| Backend (ASP.NET Core) | Render | Free web service | £0 |
| Database (PostgreSQL) | Supabase | Free | £0 |
| Source + CI | GitHub | Free | £0 |
| Domain | `*.vercel.app` / `*.onrender.com` | — | £0 |

No custom domain, no paid add-on, no card on file. A domain is worth buying when there is revenue
to justify it; until then the platform domains are real, HTTPS-terminated URLs that work.

> **Vercel Hobby is licensed for non-commercial personal use only.** Vercel's fair-use guidelines
> name "any method of requesting or processing payment from visitors of the site" as commercial
> usage, and Quotely's public invoice page does exactly that. While the deployment is a private
> MVP in Razorpay TEST mode with no real money moving, it is defensible; the day a real customer
> pays a real invoice through it, Hobby is the wrong plan and Vercel may pause the project.
>
> Two ways out, both still £0: move the frontend to a **Render static site** (same account as the
> API, free, no commercial-use restriction) or to **Cloudflare Pages** (free, commercial use
> permitted). The third is to pay for Vercel Pro when there is revenue to pay it with. This is a
> licensing decision, not a technical one — the Next.js build is identical on all three.

---

## Status

**Nothing is deployed yet.** This repository is *prepared* for the deployment described here — the
Dockerfile, the blueprint, the PostgreSQL provider and the configuration all exist and have been
exercised locally — but no Render service, Vercel project or Supabase database has been created.
Creating them needs accounts this repository does not have.

There are therefore no live URLs to quote. The ones in this document are shaped like the real
thing (`https://quotely-api.onrender.com`) but they are **examples**, not addresses that resolve.
Replace them with the real ones the platforms hand you, and record them here.

[What you have to do yourself](#what-you-have-to-do-yourself) is the checklist.

---

## Why PostgreSQL, and what it cost

Quotely was built against SQL Server and still supports it. There is no free SQL Server worth
having, so the free-tier deployment runs on Supabase PostgreSQL instead.

Before a line of provider code was written, the model was audited for anything that would not
survive the move. The finding: **nothing in the data model is SQL Server specific.**

- No raw SQL, no stored procedures, no computed columns, no `HasDefaultValueSql`.
- Money is `decimal` with explicit precision (`18,2`), quantity `18,3`, tax `5,2` — these become
  `numeric(18,2)` etc. on PostgreSQL. Exact decimal on both engines. Never a float.
- Calendar dates are `DateOnly` → `date`. Instants are UTC `DateTime` → `timestamp with time zone`.
- Keys are `Guid` → native `uuid`, not text.
- Concurrency is an EF concurrency token, not `rowversion`.

The one genuinely interesting difference is **partial uniqueness**. Several columns must be unique
*only where they have a value*: a quotation's public token hash, an invoice's, an invoice's source
quotation, a payment's provider payment id, a payment's reservation slot. SQL Server compares NULLs
as equal, so EF emits a filtered index (`WHERE [QuotationId] IS NOT NULL`). PostgreSQL already
treats NULLs as distinct, so a plain unique index has exactly the same meaning and EF emits no
filter. **Same guarantee, different mechanism** — which is precisely the kind of thing that is easy
to assume and expensive to be wrong about, so it is asserted in
`backend/Quotely.Tests/PostgreSqlCompatibilityTests.cs` along with every type mapping above.

No data model was changed to achieve free hosting. One behaviour was tightened: the UTC value
converter now normalises on the way *in* as well as out, because `timestamptz` rejects a
`DateTime` whose `Kind` is not UTC. SQL Server and SQLite ignored the kind; PostgreSQL does not.

### Three providers, two migration histories

`Database:Provider` selects the engine:

| Value | Engine | Schema comes from |
| --- | --- | --- |
| `Sqlite` | SQLite file | the model (`EnsureCreated`) — DEV only |
| `SqlServer` | SQL Server | `backend/Quotely.Api/Migrations/` |
| `Postgres` | PostgreSQL | `backend/Quotely.Migrations.PostgreSql/Migrations/` |

The same model produces different DDL on each engine and one `__EFMigrationsHistory` cannot
describe both, so PostgreSQL migrations live in their own assembly. **The SQL Server migrations are
untouched and still work.** Nothing was destroyed to make room.

To add a migration after a model change, add it to *both* — they are two descriptions of one model:

```bash
cd backend
dotnet ef migrations add <Name> --project Quotely.Api --startup-project Quotely.Api
dotnet ef migrations add <Name> --project Quotely.Migrations.PostgreSql --startup-project Quotely.Migrations.PostgreSql
```

---

## Supabase — the database

1. Create a **new** Supabase project. Do not reuse an existing one: Quotely creates an
   `__EFMigrationsHistory` table, and a project already running another EF application has one of
   its own. Two applications sharing a migration history is a mess neither survives.
2. Region: pick the one nearest your customers (`ap-south-1` for India).
3. Save the database password when it is shown. It is shown once.
4. Take the **Session pooler** string: the **Connect** button at the top of the project, then
   *Session pooler*. Host `aws-<index>-<region>.pooler.supabase.com`, port **5432**, username
   `postgres.<project-ref>`.

   Not the other two, and the reason matters:

   - **Direct connection** (`db.<project-ref>.supabase.co`) is **IPv6-only** without a paid add-on,
     and Render's free tier has no IPv6 outbound. It will simply fail to connect.
   - **Transaction pooler** (port 6543) is built for serverless functions that open a connection
     per request. It does not support prepared statements, which is not what a long-running
     ASP.NET Core process with a connection pool wants.
   - **Session pooler** (port 5432) is IPv4 on every plan and keeps a real session per connection.
     That is this application.

5. Convert it to the .NET form and **require TLS**:

```
Host=aws-<index>-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
```

That string is a credential. It goes in Render's environment settings and nowhere else — never in
this repository, never in a commit message, never in a screenshot.

### Creating the schema

PROD does not migrate on start-up (`Database:AutoMigrate` is `false`), deliberately: a schema change
is a decision, not a side effect of a deploy. Apply migrations from your own machine:

```bash
cd backend
export ConnectionStrings__DefaultConnection='<the session pooler connection string>'
dotnet ef migrations script --idempotent \
  --project Quotely.Migrations.PostgreSql --startup-project Quotely.Migrations.PostgreSql \
  --output /tmp/quotely-pg.sql
```

Read `/tmp/quotely-pg.sql`, then apply it through the Supabase SQL editor or `psql`. The script is
idempotent, so it is safe against a database that is already partly up to date.

**Production is never seeded.** `Seed:Enabled` is `false` and there is no demo account, no fake
customer and no fake payment in a database that will hold real ones.

---

## Render — the API

The blueprint is [`render.yaml`](../render.yaml). Point Render at this repository and it reads the
shape of the service from there; the secrets are marked `sync: false`, so Render asks you for each
one in the dashboard instead of looking for it in git.

What the application does to be a well-behaved container:

- **Binds `0.0.0.0` on `$PORT`.** Render assigns the port; the app reads it. Locally `PORT` is
  unset and Kestrel's own configuration applies unchanged.
- **`GET /health`** returns `{"status":"ok"}` anonymously, and Render polls it.
- **Trusts the platform proxy** for `X-Forwarded-For` / `X-Forwarded-Proto`, so HTTPS is seen as
  HTTPS and the client address is the client's. TLS itself is Render's job.
- **Logs structured JSON** outside Development, so the log viewer is searchable. Secrets are read
  into options objects and never written out.
- **Runs as a non-root user**, and writes nothing to disk — PDFs are generated in memory.

Set these in Render → Environment:

```
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=<the Supabase session pooler string>
JWT__KEY=<openssl rand -base64 48>
Razorpay__KeyId=<TEST key id>
Razorpay__KeySecret=<TEST key secret>
Razorpay__WebhookSecret=<TEST webhook secret>
PublicLinks__BaseUrl=https://<your-project>.vercel.app
Cors__AllowedOrigins__0=https://<your-project>.vercel.app
```

`PublicLinks__BaseUrl` is what every public quotation and invoice URL is built from. Get it wrong
and the links you send customers resolve to nothing.

---

## Vercel — the web app

Zero configuration beyond two settings, so there is no `vercel.json` to go stale:

| Setting | Value |
| --- | --- |
| Root Directory | `frontend/quotely-web` |
| Framework | Next.js (detected) |
| Production branch | `main` |
| `NEXT_PUBLIC_API_BASE_URL` | `https://<your-service>.onrender.com` |

`NEXT_PUBLIC_API_BASE_URL` is inlined into the browser bundle at build time, so **changing it
requires a redeploy**, not just a restart.

Nothing secret is or may be exposed to the browser. Every `NEXT_PUBLIC_` variable is readable with
View Source, and Quotely has exactly one. In particular the Razorpay **Key Secret** is never sent
to the frontend: the browser receives only the Key ID, from the API, at payment time.

---

## CORS

The API allows an explicit list of origins and never `AllowAnyOrigin`. In a deployed environment
that list is exactly the Vercel URL:

```
Cors__AllowedOrigins__0=https://<your-project>.vercel.app
```

Add `Cors__AllowedOrigins__1`, `__2` … for preview deployments if you need them. Localhost stays
allowed in Development only, from `appsettings.json`.

A request from any other origin gets no `Access-Control-Allow-Origin` header back, which is what
stops another site from calling the API with a logged-in user's browser.

---

## Razorpay

The first public deployment runs in **TEST mode**. Real cards are not charged, and no customer can
lose money to a misconfiguration nobody has exercised yet. Going Live is a later, deliberate step:
replace the three Razorpay values in Render and change nothing else.

The webhook endpoint is:

```
POST https://<your-service>.onrender.com/api/webhooks/razorpay
```

Register that URL in the Razorpay dashboard for the `payment.captured` and `payment.failed` events,
generate a webhook secret there, and put it in `Razorpay__WebhookSecret`. The signature is verified
over the raw request body; a delivery that does not verify is rejected. **The secret is never in
source code.**

One free-tier wrinkle worth knowing: a sleeping Render service takes up to a minute to wake, and
Razorpay may time out and retry the delivery. That is safe — webhook events are de-duplicated by a
unique `(Provider, EventId)` index, so a retried delivery is acknowledged without being processed
twice — but it does mean a payment can take a minute to appear.

---

## Environments on free infrastructure

The four-environment model (DEV / TEST / UAT / PROD) is unchanged. What changes is that free
infrastructure will not carry four permanently running instances, so:

| Environment | Where it runs | Notes |
| --- | --- | --- |
| DEV | your laptop | SQLite, no cloud anything |
| TEST | a second free Render service + a second free Supabase project, tracking `develop` | optional; create it when you need somewhere shared to try things |
| UAT | Vercel preview deployments of the release branch | no permanent instance; a preview URL is enough for a sign-off |
| PROD | the free Render service + Supabase project, tracking `main` | the public deployment |

Every environment still gets **its own JWT key, its own webhook secret and its own database**. A
token minted in TEST must not unlock PROD, and that stays true on free tiers.

---

## Deployment

Both platforms deploy from git on their own. No GitHub Actions deployment workflow exists, because
one would need API tokens to do a worse job of what the platforms already do:

```
feature/*  →  PR  →  develop  →  (optional TEST service redeploys)
                        │
                 release/vX.Y.Z  →  PR  →  main  →  Render + Vercel redeploy PROD
                                              │
                                            tag  →  GitHub Release
```

CI is unchanged and still needs no secrets: backend tests run offline against in-memory SQLite and
a stand-in payment provider, and the frontend build uses a placeholder API URL.

A schema change is the one step that is not automatic. Apply the migration **before** the deploy
that needs it, so the new code never meets an old schema.

---

## Free-tier limitations

Stated plainly, because pretending otherwise is how people get let down.

**Render free web services sleep after about 15 minutes of inactivity.** The next request wakes the
container, which takes roughly 30–60 seconds. For a customer opening a quotation link, that is a
long blank moment. It affects the first request only; the service then stays warm while it is used.
There is no way around it on the free plan, and pinging the service to keep it awake is against
Render's terms.

**Supabase free projects pause after about a week of inactivity** and have to be restored from the
dashboard — a manual click, and the database is unreachable until someone makes it. The free
database is limited to **500 MB**, which is a great deal of quotations but not unlimited.

**There is no SLA, no high availability and no automated disaster recovery.** One container, one
database, no failover. If Render has an incident, Quotely is down until Render is not.

**Backups are not enterprise backups.** The free Supabase tier does not give you the point-in-time
recovery a paid plan does. Until it matters enough to pay for, take your own periodic dump:

```bash
pg_dump "<the direct, non-pooled connection string>" --file quotely-$(date +%F).sql
```

Keep it somewhere that is not the same laptop.

**Vercel Hobby does not permit commercial use.** Repeated here because it is the limit most
likely to be forgotten: it is a licensing limit rather than a technical one, so nothing will break
and no error will appear — the project is simply out of compliance the moment Quotely takes a real
payment, and the remedy is a Pro plan or a different host. See the caveat at the top.

**Cold starts, pauses and the 500 MB ceiling are acceptable for what this is**: an MVP for demos,
early testers and first-customer discovery. They are not acceptable for a business depending on
Quotely to get paid. The upgrade path is money, not a rewrite — a paid Render instance and a paid
Supabase plan remove the sleep, the pause and the backup gap without a line of code changing.

---

## Smoke test

Run this against the live deployment once, after the first deploy, and again after any change to
the hosting configuration. It takes about ten minutes.

Every step below has been exercised **locally** against the same build — the application behaves
correctly. What this checklist proves is that the *deployment* is wired up: right database, right
origins, right public base URL, right webhook.

**Wake the service first.** The first request after a sleep takes up to a minute; that is the free
tier, not a failure.

### Backend

```bash
API=https://<your-service>.onrender.com

curl -s $API/health                                   # {"status":"ok"}
curl -s -o /dev/null -w '%{http_code}\n' $API/api/quotations           # 401 — auth required
curl -s -o /dev/null -w '%{http_code}\n' $API/api/public/quotations/not-a-real-token-000000   # 404
```

CORS, from a browser console on the Vercel site — it must succeed there and fail from anywhere
else:

```js
await fetch("https://<your-service>.onrender.com/health").then(r => r.json())
```

### Frontend

Work through the product in one pass, in this order, because each step feeds the next:

1. Homepage loads over HTTPS.
2. Register a new account; then sign out and sign back in.
3. Dashboard renders (receivables read from the database, not zeros from an error).
4. Create a customer **with a phone number and an email** — sharing needs both to be interesting.
5. Create a quotation for that customer.
6. Open the quotation, **Share quotation** → check the URL, the WhatsApp link and the mailto link.
   Read the previewed message: the number, the business name and the total must be right.
7. Open the public quotation URL **in a private window** — no login, correct totals, no internal
   ids anywhere in the page or the JSON.
8. Download the PDF from the public page.
9. Accept the quotation as the customer would; confirm the owner's page shows who answered.
10. Convert it to an invoice.
11. Share the invoice; open the public invoice link in a private window.
12. Record a manual payment as the owner; confirm paid, outstanding and status all move, and that
    the public invoice page agrees.
13. Void that payment; confirm every figure moves back, and that the voided row is gone from the
    public page.

### Security

- Public quotation and invoice pages work with **no** authentication; every `/api/quotations`,
  `/api/invoices`, `/api/customers` route returns `401` without a token.
- A second account cannot see the first account's records — a cross-tenant id returns `404`, not
  `403`, so it does not confirm the record exists.
- No token, hash or internal id appears in any public URL or response body.
- View Source on the deployed frontend: the only `NEXT_PUBLIC_` value is the API base URL. No
  Razorpay key secret, no JWT key, no connection string.

### Payments

**Razorpay stays in TEST mode.** Do not run a live payment. Use a Razorpay test card, confirm the
payment appears against the invoice, and confirm the webhook delivery succeeds in the Razorpay
dashboard. If the delivery timed out because the service was asleep, retry it from the dashboard —
duplicate deliveries are de-duplicated, so retrying is safe.

---

## Observability

Free and lightweight, on purpose. No Datadog, no New Relic, no Application Insights.

- `GET /health` — liveness, polled by Render and usable by anything else.
- Structured JSON logs to stdout, collected by Render's own log viewer and searchable there.
- Start-up logging that names the environment and the provider, and never a secret.

---

## What you have to do yourself

None of this can be done from a repository, and none of it has been done:

1. Create a Supabase project (**new**, not an existing one) and keep the password.
2. Apply the PostgreSQL migration script to it.
3. Create the Render service from `render.yaml` and fill in the seven environment values.
4. Create the Vercel project with Root Directory `frontend/quotely-web` and set
   `NEXT_PUBLIC_API_BASE_URL` to the Render URL.
5. Go back to Render and set `PublicLinks__BaseUrl` and `Cors__AllowedOrigins__0` to the Vercel
   URL. Neither exists until step 4 is done, which is why this is a separate step.
6. Register the Razorpay TEST webhook at `/api/webhooks/razorpay` and set the webhook secret.
7. Record the real URLs at the top of this document, replacing the examples.
8. Run the [smoke test](#smoke-test) against the live deployment before telling anyone it is ready.

**Rotate any Razorpay test secret that has been pasted into a terminal, a chat or a screenshot.**
None has ever been committed to this repository — but a secret that has been seen outside a secret
store should be replaced, and a test secret costs nothing to rotate.

---

## Related documentation

- [`environments.md`](environments.md) — configuration, secrets and the environment matrix
- [`development-workflow.md`](development-workflow.md) — branches, releases and pull requests
- [`architecture.md`](architecture.md) — how the application itself is put together
