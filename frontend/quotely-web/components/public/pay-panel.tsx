"use client";

import { useState } from "react";
import { publicApi } from "@/lib/api";
import { openCheckout } from "@/lib/razorpay";
import { formatMoney } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Icon } from "@/components/ui/icons";
import { Mono } from "@/components/ui/table";
import type { PaymentOrder, PublicInvoice, VerifyPaymentResponse } from "@/types";

type Phase =
  | { kind: "idle" }
  | { kind: "creating" }
  | { kind: "checkout" }
  | { kind: "verifying" }
  | { kind: "settled"; result: VerifyPaymentResponse }
  | { kind: "pending"; result: VerifyPaymentResponse }
  | { kind: "failed"; message: string };

/**
 * The payment action on the customer-facing invoice.
 *
 * Every figure shown here came from the server, and the outcome shown after checkout is the
 * server's verdict — the provider's callback alone is never treated as proof that money moved.
 */
export function PayPanel({
  token,
  invoice,
  onPaid,
}: {
  token: string;
  invoice: PublicInvoice;
  onPaid: () => void;
}) {
  const [phase, setPhase] = useState<Phase>({ kind: "idle" });

  const busy = phase.kind === "creating" || phase.kind === "checkout" || phase.kind === "verifying";

  async function pay() {
    setPhase({ kind: "creating" });

    let order: PaymentOrder;
    try {
      // No body: the amount is the server's decision, not ours.
      order = await publicApi.post<PaymentOrder>(
        `/api/public/invoices/${encodeURIComponent(token)}/create-payment-order`,
        {},
      );
    } catch (err) {
      setPhase({
        kind: "failed",
        message: err instanceof Error ? err.message : "The payment could not be started.",
      });
      return;
    }

    setPhase({ kind: "checkout" });

    let outcome;
    try {
      outcome = await openCheckout(order);
    } catch (err) {
      setPhase({
        kind: "failed",
        message: err instanceof Error ? err.message : "The payment window could not be opened.",
      });
      return;
    }

    // Closing the window is not a failure: nothing was charged, so the invoice is untouched.
    if (outcome.dismissed) {
      setPhase({ kind: "idle" });
      return;
    }

    if (outcome.failure || !outcome.result) {
      setPhase({ kind: "failed", message: outcome.failure ?? "The payment could not be completed." });
      return;
    }

    setPhase({ kind: "verifying" });

    try {
      const result = await publicApi.post<VerifyPaymentResponse>(
        `/api/public/invoices/${encodeURIComponent(token)}/verify-payment`,
        outcome.result,
      );

      setPhase(result.success ? { kind: "settled", result } : { kind: "pending", result });
      onPaid();
    } catch (err) {
      // The money may well have been taken; the server could not confirm it yet. Say exactly
      // that rather than inviting the customer to pay a second time.
      setPhase({
        kind: "failed",
        message:
          err instanceof Error
            ? err.message
            : "We could not confirm this payment. Please contact the business before trying again.",
      });
    }
  }

  if (phase.kind === "settled") {
    return (
      <Notice tone="success" title="Payment received">
        <p className="text-body text-steel">
          <span className="font-medium text-charcoal">
            {formatMoney(phase.result.amountPaid, phase.result.currency)}
          </span>{" "}
          paid.{" "}
          {phase.result.outstanding > 0
            ? `${formatMoney(phase.result.outstanding, phase.result.currency)} remaining.`
            : "Paid in full."}
        </p>
        {phase.result.paymentReference && (
          <p className="mt-2 text-caption text-fog">
            Reference <Mono className="text-caption">{phase.result.paymentReference}</Mono>
          </p>
        )}
      </Notice>
    );
  }

  if (phase.kind === "pending") {
    return (
      <Notice tone="pending" title="Payment is being confirmed">
        <p className="text-body text-steel">
          {phase.result.message ??
            "Your bank has not confirmed this payment yet. There is no need to pay again — this page will show the balance once it settles."}
        </p>
      </Notice>
    );
  }

  return (
    <div>
      {phase.kind === "failed" && (
        <div
          role="alert"
          className="mb-3 rounded-card border border-ash bg-rose-wash px-3.5 py-3 text-body text-rose-ink"
        >
          <p className="font-medium">Payment could not be completed</p>
          <p className="mt-0.5">{phase.message}</p>
          <p className="mt-1 text-caption">
            Nothing has been charged and the outstanding balance is unchanged.
          </p>
        </div>
      )}

      <Button
        onClick={pay}
        loading={busy}
        // Full width and 48px tall on a phone; the dominant action on the page.
        className="h-12 w-full text-body-lg sm:h-11"
      >
        {!busy && <Icon.check className="h-4 w-4" />}
        {phase.kind === "creating"
          ? "Creating secure payment…"
          : phase.kind === "checkout"
            ? "Complete the payment…"
            : phase.kind === "verifying"
              ? "Verifying payment…"
              : `Pay ${formatMoney(invoice.outstanding, invoice.currency)}`}
      </Button>

      <p className="mt-2.5 text-center text-caption text-fog">
        Payments are processed securely by Razorpay. {invoice.business.businessName} never sees your
        card details.
      </p>
    </div>
  );
}

function Notice({
  tone,
  title,
  children,
}: {
  tone: "success" | "pending";
  title: string;
  children: React.ReactNode;
}) {
  return (
    <div
      role="status"
      className={`rounded-card border border-ash px-4 py-3.5 ${
        tone === "success" ? "bg-mint" : "bg-amber-wash"
      }`}
    >
      <p
        className={`flex items-center gap-2 font-semibold ${
          tone === "success" ? "text-green-ink" : "text-amber-ink"
        }`}
      >
        {tone === "success" ? <Icon.check className="h-4 w-4" /> : <Icon.alert className="h-4 w-4" />}
        {title}
      </p>
      <div className="mt-1">{children}</div>
    </div>
  );
}
