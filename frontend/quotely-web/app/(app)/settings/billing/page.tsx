"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader, Panel, SectionCard } from "@/components/ui/card";
import { Field, Input } from "@/components/ui/field";
import { ConfirmDialog } from "@/components/ui/dialog";
import { DetailSkeleton, ErrorState } from "@/components/ui/states";
import { PageHeader } from "@/components/app/page-header";
import { cn } from "@/lib/cn";
import type { Subscription, SubscriptionStatus } from "@/types";

const ENDPOINT = "/api/billing/subscription";

/**
 * Settings → Billing. What this business pays QUOTELY.
 *
 * Not to be confused with Settings → Payments, which is what their customers pay THEM. The two
 * pages are deliberately worded so that nobody has to work out which is which: this one says
 * "your Quotely subscription", that one says "your customers pay you".
 */
export default function BillingSettingsPage() {
  const toast = useToast();
  const [subscription, setSubscription] = useState<Subscription | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const [cancelling, setCancelling] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setSubscription(await api.get<Subscription>(ENDPOINT));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load your billing details.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  async function onCancel() {
    setCancelling(true);
    try {
      setSubscription(await api.delete<Subscription>(ENDPOINT));
      setConfirmCancel(false);
      toast("Your subscription has been cancelled.", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not cancel your subscription.", "error");
    } finally {
      setCancelling(false);
    }
  }

  if (loading) return <DetailSkeleton />;
  if (error) return <ErrorState message={error} onRetry={load} />;
  if (!subscription) return null;

  return (
    <>
      <PageHeader title="Billing" description="Your Quotely subscription." />

      <div className="space-y-4">
        <PlanCard subscription={subscription} />

        {!subscription.couponCode && <CouponCard onRedeemed={setSubscription} />}

        {subscription.billingEnabled && !subscription.hasActiveMandate && (
          <StartCard onStarted={load} />
        )}

        {subscription.hasActiveMandate && !subscription.cancelRequestedAt && (
          <SectionCard
            title="Cancel subscription"
            description="You keep access until the end of the period you have already paid for."
            footer={
              <Button variant="danger" onClick={() => setConfirmCancel(true)}>
                Cancel subscription
              </Button>
            }
          >
            <p className="text-body text-fog">
              Your quotations, invoices and customers stay exactly where they are, and you can
              export everything at any time.
            </p>
          </SectionCard>
        )}
      </div>

      <ConfirmDialog
        open={confirmCancel}
        title="Cancel your subscription?"
        description="You will keep full access until the end of the period you have already paid for. Nothing is deleted."
        confirmLabel="Cancel subscription"
        loading={cancelling}
        onConfirm={onCancel}
        onCancel={() => setConfirmCancel(false)}
      />
    </>
  );
}

// ---- the plan --------------------------------------------------------------

const TONE: Record<SubscriptionStatus, { label: string; className: string }> = {
  Trialing: { label: "Free trial", className: "bg-blue-wash text-electric" },
  Active: { label: "Active", className: "bg-mint-wash text-mint-ink" },
  PastDue: { label: "Payment failed", className: "bg-amber-wash text-amber-ink" },
  Cancelled: { label: "Cancelled", className: "bg-paper text-steel" },
  Expired: { label: "Ended", className: "bg-rose-wash text-rose-ink" },
};

function PlanCard({ subscription }: { subscription: Subscription }) {
  const tone = TONE[subscription.status];

  return (
    <Card>
      <CardHeader
        title={subscription.planName}
        description={subscription.planDescription ?? undefined}
        action={
          <span className={cn("rounded-full px-2.5 py-1 text-caption font-medium", tone.className)}>
            {tone.label}
          </span>
        }
      />
      <CardBody className="space-y-4">
        <div className="flex items-baseline gap-1.5">
          <span className="font-display text-heading-sm text-charcoal">
            {formatMoney(subscription.price, subscription.currency)}
          </span>
          <span className="text-body text-fog">
            / {subscription.interval === "monthly" ? "month" : subscription.interval}
          </span>
        </div>

        <p className="text-body text-charcoal">{headline(subscription)}</p>

        {subscription.statusMessage && (
          <Panel className="text-body text-amber-ink">{subscription.statusMessage}</Panel>
        )}

        {subscription.couponCode && (
          <Panel className="text-body text-steel">
            Coupon <strong className="font-mono text-charcoal">{subscription.couponCode}</strong>{" "}
            redeemed{subscription.couponFreeMonths ? ` — ${subscription.couponFreeMonths} months free` : ""}.
          </Panel>
        )}

        <dl className="grid gap-x-8 gap-y-2 sm:grid-cols-2">
          {subscription.trialEnd && <Fact label="Free until" value={date(subscription.trialEnd)} />}
          {subscription.nextPaymentAt && (
            <Fact
              label="Next payment"
              value={`${formatMoney(subscription.price, subscription.currency)} on ${date(subscription.nextPaymentAt)}`}
            />
          )}
          {subscription.lastPaymentAt && (
            <Fact label="Last payment" value={date(subscription.lastPaymentAt)} />
          )}
          {subscription.accessEndsAt && (
            <Fact label="Access ends" value={date(subscription.accessEndsAt)} />
          )}
        </dl>

        {!subscription.billingEnabled && (
          <Panel className="text-body text-steel">
            Quotely is free while we are in early access. You will be told well before that
            changes, and nothing is charged until you set up a payment yourself.
          </Panel>
        )}
      </CardBody>
    </Card>
  );
}

/** One sentence saying where this business stands, in the words a person would use. */
function headline(subscription: Subscription): string {
  switch (subscription.status) {
    case "Trialing":
      return subscription.trialEnd
        ? `Free until ${date(subscription.trialEnd)}. You will not be charged before then.`
        : "You are on a free trial.";
    case "Active":
      return subscription.cancelRequestedAt
        ? `Cancelled. You keep full access until ${date(subscription.currentPeriodEnd)}.`
        : "Your subscription is active.";
    case "PastDue":
      return "We could not take your last payment. Your account still works while we retry.";
    case "Cancelled":
      return subscription.accessEndsAt
        ? `Cancelled. You keep full access until ${date(subscription.accessEndsAt)}.`
        : "Your subscription has been cancelled.";
    case "Expired":
      return "Your subscription has ended. You can still view and export everything you have.";
  }
}

function Fact({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-caption text-fog">{label}</dt>
      <dd className="truncate text-body text-charcoal">{value}</dd>
    </div>
  );
}

function date(value?: string | null) {
  if (!value) return "—";
  return new Date(value).toLocaleDateString(undefined, {
    day: "numeric",
    month: "long",
    year: "numeric",
  });
}

// ---- coupons ---------------------------------------------------------------

function CouponCard({ onRedeemed }: { onRedeemed: (subscription: Subscription) => void }) {
  const toast = useToast();
  const [code, setCode] = useState("");
  const [saving, setSaving] = useState(false);
  const [fieldError, setFieldError] = useState<string | null>(null);

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    setFieldError(null);
    setSaving(true);

    try {
      // The server decides what the code is worth. Nothing here changes a price — this only
      // carries the characters that were typed.
      const subscription = await api.post<Subscription>("/api/billing/coupon", { code });
      setCode("");
      onRedeemed(subscription);
      toast(
        subscription.couponFreeMonths
          ? `${subscription.couponFreeMonths} months added. Free until ${date(subscription.trialEnd)}.`
          : "Coupon applied.",
        "success",
      );
    } catch (err) {
      const message = err instanceof Error ? err.message : "That coupon could not be applied.";
      setFieldError(message);
    } finally {
      setSaving(false);
    }
  }

  return (
    <SectionCard
      title="Have a coupon?"
      description="Enter it here and your free period is extended straight away."
    >
      <form onSubmit={onSubmit} className="flex flex-wrap items-start gap-2">
        <Field label="Coupon code" htmlFor="coupon" error={fieldError} className="min-w-0 flex-1">
          <Input
            id="coupon"
            value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            className="font-mono uppercase"
            autoComplete="off"
            spellCheck={false}
            maxLength={40}
            required
          />
        </Field>
        <Button type="submit" loading={saving} className="mt-6">
          Apply
        </Button>
      </form>
    </SectionCard>
  );
}

// ---- starting to pay -------------------------------------------------------

function StartCard({ onStarted }: { onStarted: () => void }) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);

  async function start() {
    setBusy(true);
    try {
      const checkout = await api.post<{ shortUrl?: string | null }>(ENDPOINT);

      if (checkout.shortUrl) {
        // Razorpay's own hosted authorisation page. Using it rather than embedding checkout means
        // the mandate is set up on Razorpay's page, where the bank redirects land reliably.
        window.location.href = checkout.shortUrl;
        return;
      }

      onStarted();
      toast("Your subscription has been set up.", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not set up your subscription.", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <SectionCard
      title="Set up payment"
      description="Nothing is charged until your free period ends. You can cancel at any time before then."
    >
      <Button onClick={start} loading={busy}>
        Set up payment
      </Button>
    </SectionCard>
  );
}
