"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { openSubscriptionCheckout } from "@/lib/razorpay";
import { formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader, Panel, SectionCard } from "@/components/ui/card";
import { Field, Input } from "@/components/ui/field";
import { ConfirmDialog } from "@/components/ui/dialog";
import { DetailSkeleton, ErrorState } from "@/components/ui/states";
import { PageHeader } from "@/components/app/page-header";
import { cn } from "@/lib/cn";
import type { Subscription, SubscriptionCheckout, SubscriptionStatus } from "@/types";

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
          <StartCard subscription={subscription} onFinished={setSubscription} />
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
          {/*
            Stated plainly, because "Quotely Pro" and "Free trial" together are easy to read as
            "I already have Pro and something is paying for it". Nothing is, until this says so.
          */}
          {subscription.billingEnabled && (
            <Fact
              label="Payment method"
              value={
                subscription.hasActiveMandate ? (
                  "Set up"
                ) : (
                  <span className="text-amber-ink">Not set up yet</span>
                )
              }
            />
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

/**
 * Setting up the mandate that lets Quotely charge this business ₹150 a month.
 *
 * The money flow, stated once because it is easy to confuse with the other one: this payment
 * goes from the business TO QUOTELY, collected with Quotely's own Razorpay account. It is not
 * the flow in Settings → Payments, which is the business's customers paying THEM.
 *
 * Embedded checkout, not a redirect. Razorpay subscriptions take no callback_url, so sending
 * someone to the hosted short_url strands them on Razorpay's page with nothing telling Quotely
 * what happened. Here the signed result comes straight back and is handed to the server to
 * verify. The hosted page remains the fallback for when the script cannot load at all.
 */
function StartCard({
  subscription,
  onFinished,
}: {
  subscription: Subscription;
  onFinished: (subscription: Subscription) => void;
}) {
  const toast = useToast();
  const { user } = useAuth();
  const [busy, setBusy] = useState(false);

  async function start() {
    setBusy(true);
    try {
      // The server creates the subscription at Razorpay and decides when billing starts — after
      // the free period, including any months a coupon added. Nothing here sets a price.
      const checkout = await api.post<SubscriptionCheckout>(ENDPOINT);

      let outcome;
      try {
        outcome = await openSubscriptionCheckout(checkout, {
          name: user?.fullName,
          email: user?.email,
        });
      } catch {
        // The checkout script could not load — an ad blocker, or no connection to Razorpay.
        // The hosted page is the honest fallback rather than a dead button.
        if (checkout.shortUrl) {
          window.location.href = checkout.shortUrl;
          return;
        }
        throw new Error("The payment window could not be opened. Please try again.");
      }

      if (outcome.dismissed) {
        // Nothing was authorised and nothing charged. Not an error, so not an error message.
        toast("No payment method was set up.", "info");
        return;
      }

      if (outcome.failure || !outcome.result) {
        toast(outcome.failure ?? "The payment method could not be set up.", "error");
        return;
      }

      // Verified server-side against Razorpay's signature, then confirmed with Razorpay directly.
      // The browser's word that it worked is never enough on its own.
      const confirmed = await api.post<Subscription>(`${ENDPOINT}/confirm`, outcome.result);
      onFinished(confirmed);
      toast("Your payment method is set up.", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not set up your subscription.", "error");
    } finally {
      setBusy(false);
    }
  }

  const firstCharge = subscription.trialEnd ?? subscription.nextPaymentAt;

  return (
    <SectionCard
      title="Set up payment"
      description="Authorise the monthly payment now. Nothing is taken until your free period ends, and you can cancel any time before then."
    >
      <div className="space-y-4">
        <Panel className="space-y-1 text-body text-steel">
          <p>
            You will be charged{" "}
            <strong className="font-semibold text-charcoal">
              {formatMoney(subscription.price, subscription.currency)} per month
            </strong>
            {firstCharge ? (
              <>
                , starting <strong className="font-semibold text-charcoal">{date(firstCharge)}</strong>.
              </>
            ) : (
              "."
            )}
          </p>
          <p>
            Razorpay collects it automatically each month. You can cancel from this page at any
            time, and you keep access until the end of the period you have paid for.
          </p>
        </Panel>

        <Button onClick={start} loading={busy}>
          Set up payment
        </Button>
      </div>
    </SectionCard>
  );
}
