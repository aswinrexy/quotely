# Quotely — free-tier hosting

**This is temporary startup infrastructure, not the final architecture.** It exists so Quotely can
be put in front of real people at **zero monthly cost** while the product is still being validated.
Everything below runs on a free plan, and every free plan has teeth. They are listed honestly in
[Free-tier limitations](#free-tier-limitations) — read that section before promising anyone an SLA.

| Layer | Platform | Plan | Cost |
| --- | --- | --- | --- |
| Frontend (Next.js) | Cloudflare Pages | Free | £0 |
| Backend (ASP.NET Core) | Render | Free web service | £0 |
| Database (PostgreSQL) | Supabase | Free | £0 |
| Source + CI | GitHub | Free | £0 |
| Domain | `*.pages.dev` / `*.onrender.com` | — | £0 |

No custom domain, no paid add-on, no card on file. A domain is worth buying when there is revenue
to justify it; until then the platform domains are real, HTTPS-terminated URLs that work.

> **Why not Vercel.** Vercel's Hobby plan is licensed for non-commercial personal use only, and
> its fair-use guidelines name "any method of requesting or processing payment from visitors of
> the site" as commercial usage. Quotely's public invoice page does exactly that, so Hobby would
> be the wrong plan the day a real customer pays — and Vercel Pro is not £0. Cloudflare Pages
> permits commercial use on its free tier, so it is the honest choice here rather than the
> convenient one. Vercel is not part of this architecture.

---

## Status

**All three layers are live.** The Razorpay webhook is the last thing to register.

| Layer | State | URL |
| --- | --- | --- |
| Supabase PostgreSQL | **Live**, migrated, empty | `ap-southeast-2` (Sydney) |
| Render API | **Live**, smoke-tested | https://api.quotely4you.org |
| Health | **Live** | https://api.quotely4you.org/health |
| Cloudflare Pages | **Live** | https://quotely4you.org |
| Origin hosts (fallback) | **Live** | `quotely-api-yiul.onrender.com`, `quotely-4j2.pages.dev` |

### Webhook routes changed in V2.7

`/api/webhooks/razorpay` **no longer exists.** It assumed one Razorpay account for everybody, which
is exactly what V2.7 removed. It is replaced by two routes that cannot be mistaken for each other:

| Route | What it carries | Secret |
| --- | --- | --- |
| `/api/webhooks/razorpay/m/{routeToken}` | A business's invoice payments | That business's own, shown once when they connect |
| `/api/webhooks/razorpay/billing` | Quotely's ₹150/month subscriptions | `Razorpay__WebhookSecret` |

**The webhook currently registered in the founder's Razorpay dashboard points at the old path and
will now be rejected.** It has to be re-pointed at whichever of the two it was actually for — see
"Deploying V2.7" below.

### Deploy branches

Render and Cloudflare Pages must both track **`main`**. They were pointed at
`feature/v2.6-quotation-sharing`, which was deleted when that branch merged, so until they are
repointed neither can redeploy — the live sites are serving their last successful build.

- Render → **quotely-api** → Settings → Build & Deploy → Branch → `main` → Manual Deploy
- Cloudflare Pages → **quotely** → Settings → Builds & deployments → Production branch → `main`

After that, every merge to `main` redeploys both automatically. Supabase is not automatic and never
will be: `Database__AutoMigrate` is `false` in production, so a schema change is a deliberate step
you take before the deploy that needs it.

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

## What the database looks like, verified

The PostgreSQL migration has been applied to the Supabase project and the result inspected. This
is what was checked, because "the migration ran" is not the same as "the schema means what the
model means":

| Check | Result |
| --- | --- |
| Tables | 17 — Identity (8), business profile, customers, products, quotations + items, invoices + items, payments, webhook events |
| Indexes | 49 |
| Foreign keys | 19, with the intended `CASCADE` / `RESTRICT` / `SET NULL` behaviours |
| Money | `numeric(18,2)` — exact decimal, never a float |
| Quantity / tax | `numeric(18,3)` / `numeric(5,2)` |
| Calendar dates | `date` (`DueDate`, `ValidUntil`, `InvoiceDate`) |
| Instants | `timestamp with time zone` (`PaidAt`, `VoidedAt`, `PublicLinkCreatedAt`) |
| Keys | native `uuid`, not text |
| Seed data | **none** — every table is empty, as production must be |

The interesting one was **partial uniqueness**, tested rather than assumed by writing rows into
the real tables inside a transaction that was then deliberately aborted:

- Two invoices with `QuotationId` NULL under a `UNIQUE` index — **accepted**, so any number of
  directly raised invoices can coexist.
- A second invoice reusing a non-null `PublicTokenHash` — **rejected**, so a share token can never
  resolve to two invoices.

That is exactly the guarantee SQL Server gets from a filtered index, reached on PostgreSQL by its
treating NULLs as distinct. Nothing was committed by the probe.

### Row-level security

Supabase enables RLS on every table created in `public` (via its own `rls_auto_enable` event
trigger), so all 17 tables have RLS **on with zero policies**. For Quotely that is the right
answer, and it is worth understanding why before someone "fixes" it:

- The **Data API** (PostgREST, the `anon` and `authenticated` roles) is denied everything. Quotely
  does not use PostgREST at all, so denying it costs nothing and closes the hole that a public
  `anon` key would otherwise open.
- The **application** connects as `postgres` through the session pooler. That role has
  `rolbypassrls`, and the tables are owned by it, so the API reads and writes normally.

**Do not add RLS policies to make something work.** If a query is refused, the cause is the
connection, not the policy set. Tenant isolation in Quotely is enforced in the application by
`ICurrentUser` and a `UserId` filter on every query — that is the boundary, and it is tested.

For belt and braces you can also switch the Data API off entirely in
**Project Settings → Data API**, since nothing uses it.

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
PublicLinks__BaseUrl=https://<your-project>.pages.dev
Cors__AllowedOrigins__0=https://<your-project>.pages.dev
```

`PublicLinks__BaseUrl` is the **frontend** origin — the Cloudflare Pages URL — because that is
where `/q/{token}` and `/i/{token}` live. It is what every public quotation and invoice URL is
built from, so pointing it at the API instead produces links that resolve to nothing.

---

## Cloudflare Pages — the web app

Quotely's frontend is a **static site**. Every page is a client component that fetches from the
API in the browser: no server rendering, no route handlers, no server actions. So it is exported
to plain HTML/CSS/JS and served from Cloudflare's CDN — which on the free tier means unlimited
bandwidth, unlimited static requests, no cold start, and no Workers quota to run out of.

| Setting | Value |
| --- | --- |
| Build command | `npm run build` |
| Build output directory | `out` |
| Root directory | `frontend/quotely-web` |
| Production branch | `main` |
| `NEXT_PUBLIC_API_BASE_URL` | `https://<your-service>.onrender.com` |

`NEXT_PUBLIC_API_BASE_URL` is inlined into the JavaScript bundle **at build time**, so changing it
requires a rebuild, not a restart. It is the only environment variable the frontend has.

Nothing secret is or may be exposed to the browser. Every `NEXT_PUBLIC_` variable is readable with
View Source. In particular the Razorpay **Key Secret** is never sent to the frontend: the browser
receives only the Key ID, from the API, at payment time.

### How the dynamic routes work

This is the one part worth understanding before changing it.

A static export has no server, so Next requires every dynamic segment to be enumerated at build
time. Quotely's cannot be: a share token is 256 bits of randomness and an invoice id is a GUID.
So each dynamic route is exported **once** under a placeholder — `/q/token.html`,
`/customers/id.html` — and [`public/_redirects`](../frontend/quotely-web/public/_redirects)
rewrites every real URL onto that file with status `200`. A `200` is a rewrite rather than a
redirect, so the address bar keeps the real URL.

The page then reads the segment out of the address bar via
[`lib/route-param.ts`](../frontend/quotely-web/lib/route-param.ts), **not** via Next's
`useParams()`. That distinction is not cosmetic: `useParams()` returns the placeholder that was
baked into the prerendered page and never revisits it, so a page using it would ask the API for a
quotation called "token". This was found by testing, not by reading, and it is the single thing
most likely to be broken by a well-meaning refactor.

Two consequences for `_redirects`:

- Cloudflare follows a matching rule **even when a static asset matches the request**, so literal
  routes like `/customers/new` must be listed *before* the `/customers/:id` rule that would
  otherwise swallow them.
- The first matching rule wins, and `:name` matches exactly one path segment.

Add a route with a dynamic segment and you must add a rule here too, or it will 404 in production
while working perfectly in `next dev`.

---

## CORS

The API allows an explicit list of origins and never `AllowAnyOrigin`. In a deployed environment
that list is exactly the Cloudflare Pages URL:

```
Cors__AllowedOrigins__0=https://<your-project>.pages.dev
```

Add `Cors__AllowedOrigins__1`, `__2` … for Cloudflare preview deployments if you need them. Localhost stays
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
| UAT | Cloudflare Pages preview deployments of the release branch | no permanent instance; a preview URL is enough for a sign-off |
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
                 release/vX.Y.Z  →  PR  →  main  →  Render + Cloudflare redeploy PROD
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

**Cloudflare Pages is the least limited piece here.** Static assets are unmetered, bandwidth is
uncapped and commercial use is permitted, so the frontend is the one layer that will not fall over
or fall foul of a licence. The cap that exists is **500 builds per month**, which is a lot of
deploys but not infinite if something starts pushing in a loop.

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

CORS, from a browser console on the Cloudflare Pages site — it must succeed there and fail from anywhere
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

The database is done. What remains needs a browser and an account:

1. ~~Create a Supabase project and apply the migration.~~ **Done.**
2. Create the Render service from `render.yaml` and fill in its environment values.
3. Create the Cloudflare Pages project — root directory `frontend/quotely-web`, build command
   `npm run build`, output directory `out` — and set `NEXT_PUBLIC_API_BASE_URL` to the Render URL.
4. Go back to Render and set `PublicLinks__BaseUrl` and `Cors__AllowedOrigins__0` to the
   Cloudflare Pages URL. Neither exists until step 3 is done, which is why this is separate.
5. Register the Razorpay TEST webhook at `/api/webhooks/razorpay` and set the webhook secret.
6. Record the real URLs at the top of this document, replacing the examples.
7. Run the [smoke test](#smoke-test) against the live deployment before telling anyone it is ready.

**Rotate any Razorpay test secret that has been pasted into a terminal, a chat or a screenshot.**
None has ever been committed to this repository — but a secret that has been seen outside a secret
store should be replaced, and a test secret costs nothing to rotate.

---

## Related documentation

- [`environments.md`](environments.md) — configuration, secrets and the environment matrix
- [`development-workflow.md`](development-workflow.md) — branches, releases and pull requests
- [`architecture.md`](architecture.md) — how the application itself is put together


---

## Deploying V2.7

V2.7 adds two things a deployment has to be told about: a key that encrypts merchant credentials,
and a schema that did not exist before. Neither has a safe default, so both are steps rather than
assumptions.

### 1. Generate the encryption key

```bash
openssl rand -base64 32
```

Set it in Render as **`Encryption__Key`**. Do not put it in this repository, in a commit message,
or in a chat window — including this one.

Without it, production **refuses to start**. That is deliberate: the alternative is a deployment
that boots, accepts a merchant's Razorpay secret, and cannot encrypt it.

Losing it is not recoverable. Every merchant's stored credentials become unreadable and every
business has to reconnect their payment account. Keep a copy wherever you keep the database
password.

### 2. Apply the migrations

Two migrations, both additive and forward-only. Nothing is dropped and no existing row is rewritten.

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$HOME/.dotnet:$HOME/.dotnet/tools:$PATH
export DOTNET_ROLL_FORWARD=Major

# Against the production database. Take a snapshot first — the free tier has no backups.
export ConnectionStrings__DefaultConnection='<the Supabase session pooler string>'
export Database__Provider=Postgres

dotnet ef database update \
  --project backend/Quotely.Migrations.PostgreSql \
  --startup-project backend/Quotely.Migrations.PostgreSql
```

`AddMerchantPaymentConnections` creates the connections table, adds `MerchantConnectionId` to
`Payments`, and widens `WebhookEvents.EventId` to hold a connection-scoped key.
`AddSaasSubscriptions` creates `Subscriptions`, `Coupons` and `CouponRedemptions`.

Existing payments keep a null `MerchantConnectionId`. That is the truthful record: they were
collected before there was more than one account to collect into.

### 3. Set the rest of the environment

| Variable | Value |
| --- | --- |
| `Razorpay__Mode` | `Test` |
| `Billing__Enabled` | `false` |
| `Billing__EnforceEntitlements` | `false` |
| `Admin__Emails__0` | your email, or leave unset for no admin view |

`Razorpay__Oauth__*` stay empty until Razorpay approves the partner application.

### 4. Connect your own Razorpay account

**Online payments now stop working until you do this** — for you and for every other business.
That is the milestone, not a regression: an invoice can no longer be paid into an account that does
not belong to whoever issued it.

1. Sign in to Quotely → **Settings → Payments**
2. Paste your Razorpay **test** Key ID and Key Secret
3. Copy the webhook URL and secret from the card that appears — **the secret is shown once**
4. In Razorpay → **Account & Settings → Webhooks**, either update the existing webhook to the new
   URL and secret or add a new one, with `payment.authorized`, `payment.captured`, `payment.failed`
   and `order.paid`

### 5. Verify

```bash
curl -s https://api.quotely4you.org/health
```

Then, signed in: Settings → Payments shows **Connected**; Settings → Billing shows a trial and
₹150/month; a public invoice link offers a Pay button again. A test payment should reach **your**
Razorpay dashboard.

---

## Turning on real money

Nothing here happens automatically, and none of it is done by the application. Work top to bottom;
each step assumes the one above it succeeded.

| | Step | Who | Done when |
| --- | --- | --- | --- |
| **A** | Database migration applied to the Quotely Supabase database | You | `__EFMigrationsHistory` lists `AddMerchantPaymentConnections` and `AddSaasSubscriptions` |
| **B** | Production deployment of V2.7 from `main` | You | `/health` returns 200 **and** `POST /api/webhooks/razorpay` returns **404** — that route only exists on V2.6, so a 400 means the old build is still serving |
| **C** | Merchant connection completed | You | Settings → Payments shows **Connected**; the webhook is registered in your Razorpay dashboard with its new URL and secret |
| **D** | Test payment completed | You | A payment on a public invoice link succeeds in Razorpay **test** mode |
| **E** | Test webhook verified | You | The delivery shows 200 in Razorpay's webhook log, not 400 |
| **F** | Payment ledger verified | You | `/api/invoices/{id}/payments` shows the capture once, not twice |
| **G** | Invoice balance verified | You | Outstanding reaches 0 and status moves to **Paid** |
| **H** | SaaS subscription test completed | You | Only after `Billing__Enabled=true`; a subscription is created and Settings → Billing reflects it |
| **I** | SaaS webhook verified | You | A `subscription.*` delivery to `/api/webhooks/razorpay/billing` returns 200 |
| **J** | Live credentials configured | You | `Razorpay__Mode=Live` **and** all three `Razorpay__*` values replaced **in one edit** |
| **K** | Live webhook configured | You | Live-mode webhooks registered for both the merchant route and the billing route |
| **L** | **First real transaction manually approved** | **You, by hand** | — |

**Step L is yours alone.** It is not automated, not scripted, and nothing in this repository will
perform it. Do it with the smallest amount your account permits, and verify the money landed in the
**business's** account rather than Quotely's before doing anything else.

### Why J must be a single edit

Production refuses to start on a half-switched configuration — `Razorpay__Mode=Live` with test keys
fails, and live keys with `Razorpay__Mode=Test` fails just as firmly. That is intended. If you save
the mode and the keys separately, the service will fail to boot in between. Set all four in one go.

### After J, every business must reconnect

Test connections are refused in live mode, in both directions, on purpose. Each business — you
included — reconnects in Settings → Payments with **live** keys and re-registers their webhook.
Until they do, their invoices show the balance without a Pay button, which is the correct and safe
state rather than a failure.

---

## Custom domain

**`quotely4you.org` — registered 21 September 2026 at Cloudflare Registrar, and live.**

Nothing is hard-coded, and nothing should be. The domain lives entirely in configuration:

| Setting | Value |
| --- | --- |
| `PublicLinks__BaseUrl` | `https://quotely4you.org` — the **frontend**, never the API |
| `Cors__AllowedOrigins__0` | `https://quotely4you.org` |
| `Cors__AllowedOrigins__1` | `https://quotely-4j2.pages.dev` — cutover fallback, remove once unused |
| `NEXT_PUBLIC_API_BASE_URL` (Pages) | `https://api.quotely4you.org` |
| `Razorpay__Oauth__RedirectUri` | `https://quotely4you.org/settings/payments` — only once OAuth is approved |

Architecture as built:

```
https://quotely4you.org            → Cloudflare Pages   (proxied, apex via CNAME flattening)
https://www.quotely4you.org        → 301 to the apex    (Redirect Rule, query string preserved)
https://api.quotely4you.org        → Render             (DNS only — see below)
https://quotely4you.org/q/{token}  → public quotation   (a frontend route)
https://quotely4you.org/i/{token}  → public invoice     (a frontend route)
support@quotely4you.org            → Cloudflare Email Routing
```

`PublicLinks__BaseUrl` must point at the **frontend**. Pointing it at `api.` produces share links
that resolve to nothing — `/q/{token}` and `/i/{token}` are Next.js routes, not API routes.

### The api. record must stay unproxied

`api` is a **grey-cloud (DNS only)** CNAME to the Render host, and the opposite of what Pages sets
up for the site itself. With Cloudflare's proxy on, Render's ACME challenge terminates at Cloudflare
instead of reaching Render, the certificate is never issued, and the failure reads like a Render
outage rather than a DNS setting.

### Changing the domain requires a frontend REBUILD

`NEXT_PUBLIC_API_BASE_URL` is inlined into the JavaScript bundle at build time, because the app is
a static export. Editing the variable in the Pages dashboard changes nothing on its own — the old
URL stays compiled into the shipped chunks until a new build runs. Set the variable, then retry the
deployment, then confirm from outside:

```bash
# the live bundle must contain the new host and not the old one
curl -s https://quotely4you.org/login \
  | grep -o '/_next/static/chunks/[A-Za-z0-9_.-]*\.js' | sort -u \
  | while read f; do curl -s "https://quotely4you.org$f"; done \
  | grep -o 'https://api\.quotely4you\.org' | head -1
```

A green "Success" in the Pages dashboard does not prove the bundle changed; an unchanged chunk hash
proves it did not.

### Order of operations

Widen CORS on the API **before** pointing the frontend at the new host. Reverse that order and the
app is broken for the length of the Render deploy — every request blocked by the browser, with a
CORS error in the console and nothing useful on screen.

### Availability is not clearance

**Domain availability does not establish trademark clearance.** Someone has held `quotely.com`
since 2006, and at least four other parties registered Quotely-shaped domains in the last two
years. Registering `quotely4you.org` says the registry had it free; it says nothing about the name.
Get a lawyer's view before putting the name on anything you would find expensive to change.
