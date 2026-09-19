"use client";

import { useCallback, useEffect, useState } from "react";
import { api, ApiError } from "@/lib/api";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader, Panel, SectionCard } from "@/components/ui/card";
import { Field, Input } from "@/components/ui/field";
import { ConfirmDialog } from "@/components/ui/dialog";
import { DetailSkeleton, ErrorState } from "@/components/ui/states";
import { PageHeader } from "@/components/app/page-header";
import { Icon } from "@/components/ui/icons";
import { cn } from "@/lib/cn";
import type { MerchantConnection, MerchantConnectionStart } from "@/types";

const ENDPOINT = "/api/settings/payments/connection";

/**
 * Settings → Payments.
 *
 * The page where a business attaches its OWN Razorpay account, so that money its customers pay
 * lands in its own bank account. Everything here is about that one idea, and the copy says so in
 * those words rather than in the vocabulary of API credentials.
 */
export default function PaymentsSettingsPage() {
  const toast = useToast();
  const [connection, setConnection] = useState<MerchantConnection | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [disconnecting, setDisconnecting] = useState(false);
  const [confirmDisconnect, setConfirmDisconnect] = useState(false);

  /**
   * The webhook secret, held in memory for as long as this page is open and never fetched again.
   * The server keeps only an encrypted copy, so this really is the only moment it exists in
   * readable form — the same show-once contract as a public share link.
   */
  const [freshSecret, setFreshSecret] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setConnection(await api.get<MerchantConnection>(ENDPOINT));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load your payment settings.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  function onConnected(result: MerchantConnection) {
    setConnection(result);
    setFreshSecret(result.webhookSecret ?? null);
    toast("Your Razorpay account is connected.", "success");
  }

  async function onDisconnect() {
    setDisconnecting(true);
    try {
      setConnection(await api.delete<MerchantConnection>("/api/settings/payments/razorpay"));
      setFreshSecret(null);
      setConfirmDisconnect(false);
      toast("Your Razorpay account has been disconnected.", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not disconnect the account.", "error");
    } finally {
      setDisconnecting(false);
    }
  }

  if (loading) return <DetailSkeleton />;
  if (error) return <ErrorState message={error} onRetry={load} />;
  if (!connection) return null;

  const connected = connection.status === "Connected";

  return (
    <>
      <PageHeader
        title="Payments"
        description="Connect your own payment account so your customers can pay your invoices directly to your business."
      />

      <div className="space-y-4">
        <StatusCard connection={connection} />

        {freshSecret && connection.webhookUrl && (
          <WebhookSetupCard url={connection.webhookUrl} secret={freshSecret} mode={connection.mode} />
        )}

        {!connected && <ConnectCard connection={connection} onConnected={onConnected} />}

        {connected && (
          <SectionCard
            title="Disconnect"
            description="Your invoices stop offering online payment straight away. Payments you have already received are not affected."
            footer={
              <Button variant="danger" onClick={() => setConfirmDisconnect(true)}>
                Disconnect Razorpay
              </Button>
            }
          >
            <p className="text-body text-fog">
              Disconnecting removes every credential Quotely holds for this account. You can connect
              again at any time — you will be given a new webhook address when you do.
            </p>
          </SectionCard>
        )}
      </div>

      <ConfirmDialog
        open={confirmDisconnect}
        title="Disconnect Razorpay?"
        description="Your invoices will stop offering online payment until you connect an account again. Payments already received are unaffected."
        confirmLabel="Disconnect"
        loading={disconnecting}
        onConfirm={onDisconnect}
        onCancel={() => setConfirmDisconnect(false)}
      />
    </>
  );
}

// ---- status ----------------------------------------------------------------

const TONE: Record<MerchantConnection["status"], { label: string; className: string }> = {
  Connected: { label: "Connected", className: "bg-mint-wash text-mint-ink" },
  Disconnected: { label: "Not connected", className: "bg-paper text-steel" },
  Error: { label: "Needs attention", className: "bg-rose-wash text-rose-ink" },
  Expired: { label: "Expired", className: "bg-amber-wash text-amber-ink" },
  Pending: { label: "Waiting for Razorpay", className: "bg-amber-wash text-amber-ink" },
};

function StatusCard({ connection }: { connection: MerchantConnection }) {
  const tone = TONE[connection.status];

  return (
    <Card>
      <CardHeader
        title="Razorpay"
        description="Where your customers' invoice payments are collected."
        action={
          <span className={cn("rounded-full px-2.5 py-1 text-caption font-medium", tone.className)}>
            {tone.label}
          </span>
        }
      />
      <CardBody className="space-y-3">
        {connection.statusMessage && (
          <p className="text-body text-rose-ink">{connection.statusMessage}</p>
        )}

        {connection.status === "Connected" ? (
          <dl className="grid gap-x-8 gap-y-2 sm:grid-cols-2">
            <Fact label="Account" value={connection.displayName || connection.accountLabel || "—"} />
            <Fact
              label="Connected using"
              value={connection.mode === "Oauth" ? "Razorpay authorisation" : "Your API keys"}
            />
            <Fact label="Mode" value={connection.environment === "Live" ? "Live — real money" : "Test"} />
            <Fact label="Connected" value={formatDate(connection.connectedAt)} />
            {connection.accessTokenExpiresAt && (
              <Fact label="Authorisation renews before" value={formatDate(connection.accessTokenExpiresAt)} />
            )}
          </dl>
        ) : (
          <p className="text-body text-fog">
            Until you connect an account, your invoices show the amount due but cannot be paid
            online. Nothing else about them changes.
          </p>
        )}

        {connection.environment === "Test" && connection.status === "Connected" && (
          <Panel className="text-body text-steel">
            This is a <strong className="font-semibold text-charcoal">test</strong> account. Payments
            made here are not real money and never reach your bank.
          </Panel>
        )}
      </CardBody>
    </Card>
  );
}

function Fact({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-caption text-fog">{label}</dt>
      <dd className="truncate text-body text-charcoal">{value}</dd>
    </div>
  );
}

function formatDate(value?: string | null) {
  if (!value) return "—";
  return new Date(value).toLocaleDateString(undefined, {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}

// ---- connecting ------------------------------------------------------------

function ConnectCard({
  connection,
  onConnected,
}: {
  connection: MerchantConnection;
  onConnected: (result: MerchantConnection) => void;
}) {
  // Connecting with Razorpay is the better option and leads when it is available. It is not yet:
  // it needs a Razorpay partner application, which needs Razorpay's approval. Rather than show a
  // button that cannot work, the page offers the route that does and says why.
  const [showKeys, setShowKeys] = useState(!connection.oauthAvailable);

  return (
    <SectionCard
      title="Connect your Razorpay account"
      description="Your customers pay you directly. Quotely never holds your money — it only asks Razorpay to collect it on your behalf."
    >
      {connection.oauthAvailable ? (
        <div className="space-y-4">
          <OauthConnect onConnected={onConnected} />
          <button
            type="button"
            onClick={() => setShowKeys((open) => !open)}
            className="text-caption text-steel underline underline-offset-2 hover:text-charcoal"
          >
            {showKeys ? "Hide" : "Connect with your Razorpay API keys instead"}
          </button>
          {showKeys && <KeyPairForm onConnected={onConnected} />}
        </div>
      ) : (
        <div className="space-y-4">
          <Panel className="space-y-1 text-body text-steel">
            <p className="font-medium text-charcoal">One-click connect is coming.</p>
            <p>
              It needs Razorpay to approve Quotely as a technology partner. Until that is done, you
              can connect with your own Razorpay API keys — the money still goes straight to your
              account, and you can revoke the keys from your Razorpay dashboard at any time.
            </p>
          </Panel>
          <KeyPairForm onConnected={onConnected} />
        </div>
      )}
    </SectionCard>
  );
}

function OauthConnect({ onConnected }: { onConnected: (result: MerchantConnection) => void }) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);

  async function start() {
    setBusy(true);
    try {
      const start = await api.post<MerchantConnectionStart>("/api/settings/payments/razorpay/oauth/start");
      // Navigating rather than opening a popup: Razorpay's authorisation asks the merchant to
      // sign in, and a popup that a browser blocks or that loses its opener is a worse failure
      // than a full-page redirect the back button can undo.
      window.location.href = start.authorizationUrl;
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not start the connection.", "error");
      setBusy(false);
    }
  }

  // Present so the callback path has somewhere to complete; see completeFromCallback below.
  useCompleteOauthFromCallback(onConnected);

  return (
    <Button onClick={start} loading={busy}>
      <Icon.external className="h-4 w-4" />
      Connect with Razorpay
    </Button>
  );
}

/**
 * Completes the authorisation when Razorpay sends the merchant back here with ?code= and ?state=.
 *
 * The exchange is done by the API, as the signed-in owner. The browser only carries the values
 * across; it never sees a token. The query string is cleared afterwards so a refresh cannot
 * replay a code that has already been spent.
 */
function useCompleteOauthFromCallback(onConnected: (result: MerchantConnection) => void) {
  const toast = useToast();

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const code = params.get("code");
    const state = params.get("state");
    if (!code || !state) return;

    window.history.replaceState({}, "", window.location.pathname);

    void (async () => {
      try {
        onConnected(
          await api.post<MerchantConnection>("/api/settings/payments/razorpay/oauth/complete", {
            code,
            state,
          }),
        );
      } catch (err) {
        toast(err instanceof Error ? err.message : "Could not complete the connection.", "error");
      }
    })();
    // Runs once, on the load that carries the callback parameters.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
}

function KeyPairForm({ onConnected }: { onConnected: (result: MerchantConnection) => void }) {
  const toast = useToast();
  const [keyId, setKeyId] = useState("");
  const [keySecret, setKeySecret] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [saving, setSaving] = useState(false);
  const [fieldError, setFieldError] = useState<string | null>(null);

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    setFieldError(null);
    setSaving(true);

    try {
      const result = await api.post<MerchantConnection>("/api/settings/payments/razorpay/keys", {
        keyId: keyId.trim(),
        keySecret: keySecret.trim(),
        displayName: displayName.trim() || undefined,
      });

      // Cleared the moment it has been sent. The secret has no reason to sit in a form field
      // afterwards, where a shared screen or a password manager might pick it up.
      setKeyId("");
      setKeySecret("");
      setDisplayName("");
      onConnected(result);
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : "Could not connect this account. Please try again.";
      setFieldError(message);
      toast(message, "error");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={onSubmit} className="space-y-4">
      <p className="text-body text-fog">
        In your Razorpay dashboard, go to <strong className="text-charcoal">Account &amp; Settings
        → API Keys</strong> and generate a key. Paste both halves here.
      </p>

      <Field label="Key ID" htmlFor="rzp-key-id" required hint="Begins with rzp_">
        <Input
          id="rzp-key-id"
          value={keyId}
          onChange={(e) => setKeyId(e.target.value)}
          placeholder="rzp_test_…"
          autoComplete="off"
          spellCheck={false}
          required
        />
      </Field>

      <Field
        label="Key Secret"
        htmlFor="rzp-key-secret"
        required
        error={fieldError}
        hint="Stored encrypted. Quotely never shows it again, and never sends it to your customers."
      >
        <Input
          id="rzp-key-secret"
          // A password field, so it is masked on a shared screen and kept out of autofill.
          type="password"
          value={keySecret}
          onChange={(e) => setKeySecret(e.target.value)}
          autoComplete="new-password"
          spellCheck={false}
          required
        />
      </Field>

      <Field label="Label" htmlFor="rzp-label" hint="Optional — helps if you have more than one account.">
        <Input
          id="rzp-label"
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          placeholder="Main business account"
          maxLength={100}
        />
      </Field>

      <Button type="submit" loading={saving}>
        Connect account
      </Button>
    </form>
  );
}

// ---- webhook setup ---------------------------------------------------------

/**
 * Shown once, immediately after connecting with API keys.
 *
 * Razorpay tells Quotely about a payment by calling back, and it will only do that if the
 * merchant registers this address and secret in their own dashboard. Under OAuth we register it
 * for them, so this is only ever needed for the key-pair route.
 */
function WebhookSetupCard({
  url,
  secret,
  mode,
}: {
  url: string;
  secret: string;
  mode?: MerchantConnection["mode"] | null;
}) {
  if (mode === "Oauth") return null;

  const absolute = `${apiOrigin()}${url}`;

  return (
    <Card className="border-amber-ink/30">
      <CardHeader
        title="One more step — add the webhook"
        description="Razorpay uses this to tell Quotely when a customer has paid. Copy both values now: the secret is shown only this once."
      />
      <CardBody className="space-y-4">
        <CopyRow label="Webhook URL" value={absolute} />
        <CopyRow label="Webhook secret" value={secret} />

        <ol className="ml-4 list-decimal space-y-1 text-body text-steel">
          <li>
            In Razorpay, open <strong className="text-charcoal">Account &amp; Settings → Webhooks</strong>.
          </li>
          <li>Add a webhook with the URL and secret above.</li>
          <li>
            Select the events{" "}
            <strong className="text-charcoal">
              payment.authorized, payment.captured, payment.failed
            </strong>{" "}
            and <strong className="text-charcoal">order.paid</strong>.
          </li>
        </ol>

        <Panel className="text-body text-steel">
          Without this, payments still reach your bank — Quotely just will not know about them
          until the customer&rsquo;s browser reports back, so an invoice may look unpaid for longer than
          it should.
        </Panel>
      </CardBody>
    </Card>
  );
}

function CopyRow({ label, value }: { label: string; value: string }) {
  const toast = useToast();
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      toast("Could not copy. Select the text and copy it manually.", "error");
    }
  }

  return (
    <div>
      <p className="mb-1 text-caption text-fog">{label}</p>
      <div className="flex items-stretch gap-2">
        <code className="min-w-0 flex-1 truncate rounded-btn bg-paper px-3 py-2 font-mono text-caption text-charcoal">
          {value}
        </code>
        <Button type="button" variant="secondary" size="sm" onClick={copy} className="h-auto">
          {copied ? <Icon.check className="h-4 w-4" /> : <Icon.copy className="h-4 w-4" />}
          {copied ? "Copied" : "Copy"}
        </Button>
      </div>
    </div>
  );
}

/**
 * The webhook URL the API returns is a path, because the API does not know which host it is
 * reached on. The absolute address is built here from the base URL the browser already uses.
 */
function apiOrigin() {
  const base = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";
  return base.replace(/\/+$/, "");
}
