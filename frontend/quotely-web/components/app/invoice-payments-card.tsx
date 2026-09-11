"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { Pill } from "@/components/ui/badge";
import { Icon } from "@/components/ui/icons";
import { Mono } from "@/components/ui/table";
import type { Invoice, InvoicePayments, PaymentStatus, PublicInvoiceLink } from "@/types";

const PAYMENT_TONES: Record<PaymentStatus, "neutral" | "blue" | "green" | "amber" | "rose"> = {
  Created: "neutral",
  Pending: "amber",
  Captured: "green",
  Failed: "rose",
  Cancelled: "neutral",
};

/**
 * The owner's read-only view of what a customer has paid. Amounts come from the server, which
 * derives them from captured payments — nothing here is entered by hand.
 */
export function PaymentHistoryCard({ payments }: { payments: InvoicePayments | null }) {
  if (!payments || payments.payments.length === 0) return null;

  const overpaid = payments.summary.overpaidBy > 0;

  return (
    <Card>
      <CardHeader title="Payment history" description="Recorded automatically as payments settle." />

      {/* An overpayment needs a person to resolve it, so it is stated rather than left in a log. */}
      {overpaid && (
        <div className="border-b border-ash bg-amber-wash px-4 py-3">
          <p className="flex items-center gap-1.5 text-body font-medium text-amber-ink">
            <Icon.alert className="h-4 w-4 shrink-0" />
            Overpaid by {formatMoney(payments.summary.overpaidBy, payments.summary.currency)}
          </p>
          <p className="mt-0.5 text-caption text-amber-ink">
            More was captured than this invoice is for. Refund the difference through your Razorpay
            dashboard.
          </p>
        </div>
      )}
      <ul className="divide-y divide-ash">
        {payments.payments.map((payment) => (
          <li key={payment.id} className="px-4 py-3">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <span className="font-medium tabular-nums text-charcoal">
                {formatMoney(payment.amount, payment.currency)}
              </span>
              <Pill tone={PAYMENT_TONES[payment.status] ?? "neutral"}>{payment.status}</Pill>
            </div>
            <p className="mt-1 text-caption text-fog">
              {formatDate(payment.paidAt ?? payment.createdAt)}
              {payment.method ? ` · ${payment.method.toUpperCase()}` : ""}
            </p>
            {payment.reference && (
              <p className="mt-0.5 break-all text-caption text-fog">
                <Mono className="text-caption">{payment.reference}</Mono>
              </p>
            )}
            {payment.failureReason && (
              <p className="mt-1 text-caption text-rose-ink">{payment.failureReason}</p>
            )}
          </li>
        ))}
      </ul>
    </Card>
  );
}

/**
 * Share panel for the payment link.
 *
 * Only a hash of the token is stored, so the URL can be shown exactly once — at the moment it is
 * created. That is why the URL lives in component state here, and why asking again is presented
 * as "replace": the previous link stops working immediately.
 */
export function InvoiceShareCard({
  invoice,
  onChanged,
}: {
  invoice: Invoice;
  onChanged: () => void;
}) {
  const toast = useToast();
  const [url, setUrl] = useState<string | null>(null);
  const [generating, setGenerating] = useState(false);
  const [confirmReplace, setConfirmReplace] = useState(false);

  // A cancelled invoice cannot be paid, so it is not shareable either.
  if (invoice.status === "Cancelled") return null;

  const hasExistingLink = invoice.hasPublicLink;

  async function generate() {
    setGenerating(true);
    try {
      const link = await api.post<PublicInvoiceLink>(`/api/invoices/${invoice.id}/public-link`, undefined);
      setUrl(link.url);
      setConfirmReplace(false);
      toast(hasExistingLink ? "New payment link created" : "Payment link created", "success");
      onChanged();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not create the payment link.", "error");
    } finally {
      setGenerating(false);
    }
  }

  async function copy() {
    if (!url) return;
    try {
      await navigator.clipboard.writeText(url);
      toast("Link copied", "success");
    } catch {
      // Clipboard access can be blocked; the URL stays selectable on screen.
      toast("Copy failed — select the link and copy it manually.", "error");
    }
  }

  return (
    <>
      <Card>
        <CardHeader
          title="Share for payment"
          description="A link your customer opens to view this invoice and pay it. No account needed."
        />
        <CardBody className="space-y-3">
          {url ? (
            <>
              <div className="rounded-input border border-ash bg-paper p-2.5">
                <p className="break-all font-mono text-caption text-charcoal">{url}</p>
              </div>
              <p className="text-caption text-fog">
                Copy it now — this link is shown once and cannot be displayed again.
              </p>
              <div className="flex flex-wrap gap-2">
                <Button size="sm" onClick={copy}>
                  <Icon.copy className="h-3.5 w-3.5" />
                  Copy link
                </Button>
                <a href={url} target="_blank" rel="noopener noreferrer">
                  <Button variant="secondary" size="sm">
                    <Icon.external className="h-3.5 w-3.5" />
                    Open
                  </Button>
                </a>
              </div>
            </>
          ) : hasExistingLink ? (
            <>
              <div className="flex items-center gap-2 text-body text-charcoal">
                <Icon.link className="h-4 w-4 shrink-0 text-electric" />
                <span>
                  Link active
                  {invoice.publicLinkCreatedAt
                    ? ` since ${formatDate(invoice.publicLinkCreatedAt)}`
                    : ""}
                </span>
              </div>
              <p className="text-caption text-fog">
                Only a hash of the link is stored, so the URL cannot be shown again. Creating a new
                one immediately stops the old link from working.
              </p>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => setConfirmReplace(true)}
                loading={generating}
              >
                Replace link
              </Button>
            </>
          ) : (
            <>
              <p className="text-body text-fog">
                Anyone with the link can view this invoice and pay it, so share it only with your
                customer.
              </p>
              <Button size="sm" onClick={generate} loading={generating}>
                <Icon.link className="h-3.5 w-3.5" />
                Create payment link
              </Button>
            </>
          )}
        </CardBody>
      </Card>

      <ConfirmDialog
        open={confirmReplace}
        title="Replace payment link"
        description="The link you shared previously will stop working immediately, and the new URL is shown only once. Continue?"
        confirmLabel="Replace link"
        tone="primary"
        loading={generating}
        onConfirm={generate}
        onCancel={() => setConfirmReplace(false)}
      />
    </>
  );
}
