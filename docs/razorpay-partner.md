# Razorpay: what is verified, what needs approval

Every claim in this document is marked **VERIFIED** (read in Razorpay's published documentation on
the date below), **NOT VERIFIED** (not published, or not found), or **REQUIRES APPROVAL** (real,
but gated behind a Razorpay decision we have not yet obtained).

Nothing in the codebase invents a Razorpay endpoint, parameter, scope or event name. Where the
implementation could not be exercised because approval is missing, this file says so plainly
rather than the code implying otherwise.

**Checked on 19 September 2026.** Razorpay changes its documentation; re-read before relying on it.

---

## 1. The three identities

Read this first. Almost every mistake available in this area is a confusion between them.

| | Who | Money | Whose Razorpay account | Where it lives in the code |
| --- | --- | --- | --- | --- |
| **Platform** | Quotely | ₹150/month subscriptions **to us** | Quotely's own | `RazorpayOptions`, `ISaasBillingProvider` |
| **Tenant** | A business using Quotely | Invoice payments **to them** | Theirs | `MerchantPaymentConnection`, `IMerchantPaymentProvider` |
| **Customer** | The tenant's customer | Pays the tenant's invoice | — | — |

```
Tenant's customer ──pays invoice──▶ TENANT'S Razorpay ──▶ tenant's bank
Tenant ───────────pays ₹150/mo───▶ QUOTELY'S Razorpay ──▶ Quotely's bank
```

These never meet. Nothing implements both provider interfaces, they verify webhooks against
different secrets on different routes, and there is a test that a merchant's webhook secret cannot
move a subscription.

---

## 2. OAuth — the preferred way a business connects

**REQUIRES APPROVAL.** Implemented in full; cannot be switched on yet.

### The blocker

> Only an "Owner" user can create applications on the Razorpay Partner Dashboard. You must first
> register as a Technology Partner and create an application to obtain OAuth credentials.

Technology Partner eligibility is stated as *"a marketplace or a platform that connects buyers with
sellers"*. The onboarding path Razorpay publishes is:

1. Create a partner account and request to switch to Technology Partner
2. Complete KYC by submitting the required documents
3. Integrate using OAuth
4. Test with a sample sub-merchant account
5. Go live

**No `client_id` exists until Razorpay approves the application.** Until then `Razorpay__Oauth__*`
is empty, `IRazorpayOauthClient.IsConfigured` is false, the settings page reports
`oauthAvailable: false` and the start endpoint answers 503. Nothing is stubbed or faked.

- Approval duration — **NOT VERIFIED** (not published).
- Whether an invoicing SaaS qualifies under "marketplace or platform" — **NOT VERIFIED**. Ask
  `integrations@razorpay.com` before assuming.
- Required KYC documents — **NOT VERIFIED** (the docs point at a separate KYC page).

### What is implemented against it — all VERIFIED

| Thing | Value |
| --- | --- |
| Authorize | `https://auth.razorpay.com/authorize` |
| Parameters | `client_id`, `response_type=code`, `redirect_uri`, `scope`, `state` |
| Scopes | `read_only`, `read_write`, `rx_read_only`, `rx_read_write`, `rx_partner_read_write` |
| Token / refresh | `POST https://auth.razorpay.com/token` |
| Revoke | `POST https://auth.razorpay.com/revoke` |
| Access token life | **7,862,400 s ≈ 91 days** |
| Refresh token life | **180 days** |
| Returned | `access_token`, `public_token` (`rzp_test_oauth_…`), `refresh_token`, `razorpay_account_id` |
| Acting as a sub-merchant | `Authorization: Bearer <access_token>` + `X-Razorpay-Account: acc_…` |
| Revocation event | `account.app.authorization_revoked` |

Quotely requests **`read_write` only**. No `rx_*` scope is ever requested: those reach RazorpayX
banking, and Quotely has no business touching a merchant's bank account.

`public_token` — not `access_token` — is what the browser is given. Handing a browser the access
token would give it the merchant's whole API.

### Two things still to do when approval lands

1. **Register the webhook automatically.** `POST https://api.razorpay.com/v2/accounts/{account_id}/webhooks`
   (**VERIFIED**) creates a webhook on a sub-merchant's account with a secret we choose; max 30 per
   account. `MerchantConnectionService.CompleteOauthAsync` already generates and stores that secret
   — the call that registers it at Razorpay is the missing step, and until it is made an OAuth
   merchant would have to add the webhook by hand exactly as a key-pair merchant does.
2. **Handle `account.app.authorization_revoked`,** so a merchant who revokes access at Razorpay is
   marked `Error` here instead of failing on their next customer's payment.

---

## 3. API keys — how a business connects **today**

**VERIFIED and working.** No Razorpay programme required.

The merchant generates a key in their own dashboard (**Account & Settings → API Keys**) and pastes
both halves into Settings → Payments. The secret is encrypted with AES-256-GCM before storage and
is never returned by any endpoint.

This is a deliberate decision, and it is worth being explicit about why. OAuth is better: the
merchant never hands us a secret, and they can revoke us without rotating a key. But OAuth is
blocked on an approval of unknown duration that may not be granted, and shipping OAuth-only would
mean **no business could accept a single payment until Razorpay approved us**. Both modes live
behind one `MerchantConnectionMode` discriminator, so approval changes a configuration value and
not a line of payment code.

The merchant's money goes to the merchant's account in both modes. That is the property that
mattered; the mode is only how they authorised it.

---

## 4. Webhooks

| | Merchant invoice payments | Quotely subscriptions |
| --- | --- | --- |
| Route | `/api/webhooks/razorpay/m/{routeToken}` | `/api/webhooks/razorpay/billing` |
| Secret | Per connection, encrypted | `Razorpay__WebhookSecret` |
| Handler | `WebhookService` | `SubscriptionWebhookService` |
| Idempotency key | `{connectionId}:{eventId}` | `{eventId}` under provider `Razorpay:Billing` |

**Why the route token is in the URL.** A webhook body is attacker-controlled until its signature
has been verified, and the signature cannot be verified until a secret — and therefore a merchant —
has been chosen. Choosing that merchant by reading `account_id` out of the unverified body would be
letting the attacker pick the key their forgery is checked against. So the URL selects the
connection, the connection's own secret authenticates the body, and only then is `account_id` read
— as a cross-check that must agree, never as the thing that decides.

The route token is a 32-byte random value, not the connection's row id, and it is reissued whenever
a business disconnects.

**Why idempotency keys are scoped per connection.** Razorpay numbers events per account, so two
merchants can legitimately receive events with the same id. A global key would treat the second as
a duplicate and silently discard a real payment. There is a test for this.

Events handled for merchant payments (**VERIFIED**): `payment.authorized`, `payment.captured`,
`payment.failed`, `order.paid`.

---

## 5. Subscriptions — Quotely's own billing

**VERIFIED.**

### The trial mechanism

> When creating a Subscription using APIs, you can add a trial period by passing a future start
> date in the `start_at` parameter.

That is the whole of it. A subscription created today with `start_at` six months out is six free
months followed by ₹150/month. Razorpay's documented behaviour, not a trick — and it is what makes
`QUOTELY6` work without a separate free-plan concept.

### Events — all VERIFIED, none guessed

| Event | Meaning |
| --- | --- |
| `subscription.authenticated` | First payment made — the mandate is signed |
| `subscription.activated` | Moved to active |
| `subscription.charged` | A successful charge |
| `subscription.completed` | All invoices generated |
| `subscription.updated` | Updated, no state change |
| `subscription.pending` | A charge failed; retries are running |
| `subscription.halted` | All retries exhausted |
| `subscription.cancelled` | Cancelled |
| `subscription.paused` / `subscription.resumed` | Paused and resumed |

Statuses: `created`, `authenticated`, `active`, `pending`, `halted`, `completed`, `cancelled`,
`paused`, `expired`.

Razorpay sends **no** `subscription.payment_failed`. A failed charge appears as `pending` and then
`halted`, which is why `SubscriptionNotification.PaymentFailed` is derived from those two.

### The signature trap

| Flow | Signed over |
| --- | --- |
| Invoice payment (order) | `{order_id}\|{payment_id}` |
| Subscription (mandate) | `{payment_id}\|{subscription_id}` |

**The order is reversed.** Both are Razorpay's documented formulas. Getting the subscription one
backwards produces a check that rejects every legitimate mandate, so the two are written out
separately rather than sharing a helper that would hide the difference.

---

## 6. What you actually need to do

### To unlock OAuth (optional — key-pair mode works without it)

1. Sign in to the Razorpay Dashboard as the account **Owner**.
2. Go to **Partners** and request to switch to **Technology Partner**.
3. Complete KYC. Expect to be asked what Quotely does; "an invoicing platform whose business
   customers collect payments from their own customers" is the accurate description.
4. Once approved: **Partners → Applications → Create Application**. You get a Development client
   (non-HTTPS redirect URIs, test + live) and a Production client (HTTPS only, live).
5. Whitelist the redirect URI `https://<your-domain>/settings/payments`.
6. Put the credentials in Render — **never in git**:
   - `Razorpay__Oauth__ClientId`
   - `Razorpay__Oauth__ClientSecret`
   - `Razorpay__Oauth__RedirectUri`
7. Redeploy. "Connect with Razorpay" appears on its own; no code change.

### To enable subscription billing

1. `Billing__Enabled=true` in Render.
2. Add a webhook in **Quotely's own** Razorpay dashboard pointing at
   `https://<api-domain>/api/webhooks/razorpay/billing`, with the subscription events above, and
   put its secret in `Razorpay__WebhookSecret`.
3. Leave `Billing__EnforceEntitlements=false` until you actually intend to restrict anyone.

### Before real money

See the real-money checklist in `docs/hosting.md`. Nothing switches to live automatically, and
production refuses to start if `Razorpay__Mode=Live` without live credentials.
