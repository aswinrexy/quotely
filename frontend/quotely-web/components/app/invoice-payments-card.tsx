"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { Pill } from "@/components/ui/badge";
import { cn } from "@/lib/cn";
import { Icon } from "@/components/ui/icons";
import { Mono } from "@/components/ui/table";
import { Dialog } from "@/components/ui/dialog";
import type {
  Invoice,
  InvoiceShare,
  InvoicePayments,
  Payment,
  PaymentStatus,
  PublicInvoiceLink,
} from "@/types";
import { MANUAL_PAYMENT_METHOD_LABELS, type ManualPaymentMethod } from "@/types";

const PAYMENT_TONES: Record<PaymentStatus, "neutral" | "blue" | "green" | "amber" | "rose"> = {
  Created: "neutral",
  Pending: "amber",
  Captured: "green",
  Failed: "rose",
  Cancelled: "neutral",
};

/** "bank_transfer" reads as a database value; "Bank transfer" reads as a payment. */
function methodLabel(method?: string | null) {
  if (!method) return null;
  return MANUAL_PAYMENT_METHOD_LABELS[method as ManualPaymentMethod] ?? method.replace(/_/g, " ").toUpperCase();
}

/**
 * The owner's view of every payment against an invoice, gateway and manual together in one
 * chronological list. Amounts come from the server, which derives them from the ledger — nothing
 * here is added up in the browser.
 *
 * Void appears only on manual rows the server says are voidable. Gateway money is the provider's
 * record: correcting it means a refund, which this version does not perform.
 */
export function PaymentHistoryCard({
  payments,
  invoiceId,
  onChanged,
}: {
  payments: InvoicePayments | null;
  invoiceId: string;
  onChanged: () => void;
}) {
  const toast = useToast();
  const [voiding, setVoiding] = useState<Payment | null>(null);
  const [working, setWorking] = useState(false);

  if (!payments || payments.payments.length === 0) return null;

  const overpaid = payments.summary.overpaidBy > 0;

  async function confirmVoid() {
    if (!voiding) return;
    setWorking(true);
    try {
      await api.post(`/api/invoices/${invoiceId}/payments/${voiding.id}/void`, undefined);
      toast("Payment voided", "success");
      setVoiding(null);
      onChanged();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not void the payment.", "error");
    } finally {
      setWorking(false);
    }
  }

  return (
    <>
      <Card>
        <CardHeader
          title="Payment history"
          description="Money received online and by hand, in one place."
        />

        {/* An overpayment needs a person to resolve it, so it is stated rather than left in a log. */}
        {overpaid && (
          <div className="border-b border-ash bg-amber-wash px-4 py-3">
            <p className="flex items-center gap-1.5 text-body font-medium text-amber-ink">
              <Icon.alert className="h-4 w-4 shrink-0" />
              Overpaid by {formatMoney(payments.summary.overpaidBy, payments.summary.currency)}
            </p>
            <p className="mt-0.5 text-caption text-amber-ink">
              More has been recorded than this invoice is for. Void a manual payment if one was
              entered twice, or refund the difference through your Razorpay dashboard.
            </p>
          </div>
        )}

        <ul className="divide-y divide-ash">
          {payments.payments.map((payment) => {
            const label = methodLabel(payment.method);
            return (
              <li key={payment.id} className="px-4 py-3">
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <span
                    className={cn(
                      "font-medium tabular-nums",
                      payment.isVoided ? "text-fog line-through" : "text-charcoal",
                    )}
                  >
                    {formatMoney(payment.amount, payment.currency)}
                  </span>
                  <div className="flex items-center gap-1.5">
                    {payment.isVoided ? (
                      <Pill tone="neutral">Voided</Pill>
                    ) : (
                      <>
                        <Pill tone={payment.source === "Manual" ? "blue" : "neutral"}>
                          {payment.source === "Manual" ? "Manual" : payment.provider}
                        </Pill>
                        <Pill tone={PAYMENT_TONES[payment.status] ?? "neutral"}>{payment.status}</Pill>
                      </>
                    )}
                  </div>
                </div>

                <p className="mt-1 text-caption text-fog">
                  {formatDate(payment.paidAt ?? payment.createdAt)}
                  {label ? ` · ${label}` : ""}
                </p>

                {payment.reference && (
                  <p className="mt-0.5 break-all text-caption text-fog">
                    <Mono className="text-caption">{payment.reference}</Mono>
                  </p>
                )}

                {payment.notes && (
                  <p className="mt-1 whitespace-pre-line text-caption text-steel">{payment.notes}</p>
                )}

                {payment.failureReason && (
                  <p className="mt-1 text-caption text-rose-ink">{payment.failureReason}</p>
                )}

                {payment.isVoided && payment.voidedAt && (
                  <p className="mt-1 text-caption text-fog">
                    Voided {formatDate(payment.voidedAt)} · kept for your records
                  </p>
                )}

                {payment.canVoid && (
                  <button
                    type="button"
                    onClick={() => setVoiding(payment)}
                    className="mt-2 rounded-input text-caption font-medium text-fog transition-colors duration-150 ease-out hover:text-rose-ink"
                  >
                    Void
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      </Card>

      <ConfirmDialog
        open={voiding !== null}
        title="Void payment?"
        description={
          voiding
            ? `${formatMoney(voiding.amount, voiding.currency)}${
                methodLabel(voiding.method) ? ` · ${methodLabel(voiding.method)}` : ""
              }. This removes the payment from the invoice balance while keeping the record for audit purposes.`
            : ""
        }
        confirmLabel="Void payment"
        loading={working}
        onConfirm={confirmVoid}
        onCancel={() => setVoiding(null)}
      />
    </>
  );
}

/**
 * Share panel for the payment link.
 *
 * Only a hash of the token is stored, so the URL can be shown exactly once — at the moment it is
 * created. That is why the share material lives in component state here, and why asking again is
 * presented as "replace": the previous link stops working immediately.
 *
 * The WhatsApp and email actions are deep links. Quotely sends nothing itself — WhatsApp opens
 * with the message written, and the mail action opens the owner's own mail client with a draft.
 * The message text, the amount and both URLs are composed by the server, which is where the
 * authoritative figures live; the browser only opens what it was handed.
 */
export function InvoiceShareCard({
  invoice,
  onChanged,
}: {
  invoice: Invoice;
  onChanged: () => void;
}) {
  const toast = useToast();
  const [share, setShare] = useState<InvoiceShare | null>(null);
  const [open, setOpen] = useState(false);
  const [generating, setGenerating] = useState(false);
  const [confirmReplace, setConfirmReplace] = useState(false);

  // A cancelled invoice cannot be paid, so it is not shareable either.
  if (invoice.status === "Cancelled") return null;

  const hasExistingLink = invoice.hasPublicLink;

  async function generate() {
    setGenerating(true);
    try {
      const link = await api.post<PublicInvoiceLink>(`/api/invoices/${invoice.id}/public-link`, undefined);
      setShare(link.share);
      setConfirmReplace(false);
      setOpen(true);
      onChanged();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not create the payment link.", "error");
    } finally {
      setGenerating(false);
    }
  }

  /** Reopens the panel on material already in memory, rather than silently minting a new link. */
  function openExisting() {
    if (share) setOpen(true);
    else if (hasExistingLink) setConfirmReplace(true);
    else void generate();
  }

  return (
    <>
      <Card>
        <CardHeader
          title="Share invoice"
          description="Send your customer a link to view this invoice and pay it. No account needed."
        />
        <CardBody className="space-y-3">
          {share ? (
            <>
              <div className="flex items-center gap-2 text-body text-charcoal">
                <Icon.check className="h-4 w-4 shrink-0 text-electric" />
                <span>Link ready to send</span>
              </div>
              <Button size="sm" onClick={() => setOpen(true)}>
                <Icon.link className="h-3.5 w-3.5" />
                Share invoice
              </Button>
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
                Only a hash of the link is stored, so the URL cannot be shown again. Sharing again
                issues a new link and immediately stops the old one from working.
              </p>
              <Button variant="secondary" size="sm" onClick={openExisting} loading={generating}>
                <Icon.link className="h-3.5 w-3.5" />
                Share again
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
                Share invoice
              </Button>
            </>
          )}
        </CardBody>
      </Card>

      <ShareDialog
        open={open && share !== null}
        share={share}
        onClose={() => setOpen(false)}
      />

      <ConfirmDialog
        open={confirmReplace}
        title="Create a new link"
        description="The link you shared previously will stop working immediately. Continue?"
        confirmLabel="Create new link"
        tone="primary"
        loading={generating}
        onConfirm={generate}
        onCancel={() => setConfirmReplace(false)}
      />
    </>
  );
}

/**
 * The three ways to hand the link over. On a phone the underlying Dialog is already a bottom
 * sheet, so the actions sit within thumb reach without a separate mobile treatment.
 */
function ShareDialog({
  open,
  share,
  onClose,
}: {
  open: boolean;
  share: InvoiceShare | null;
  onClose: () => void;
}) {
  const toast = useToast();

  async function copy() {
    if (!share) return;
    try {
      await navigator.clipboard.writeText(share.url);
      toast("Invoice link copied", "success");
    } catch {
      // Clipboard access can be blocked; the URL stays selectable on screen.
      toast("Copy failed — select the link and copy it manually.", "error");
    }
  }

  if (!share) return null;

  return (
    <Dialog
      open={open}
      title="Share invoice"
      description="Copy it now — this link is shown once and cannot be displayed again."
      onClose={onClose}
      footer={
        <Button variant="secondary" onClick={onClose} className="w-full sm:w-auto">
          Done
        </Button>
      }
    >
      <div className="space-y-4">
        <div className="rounded-input border border-ash bg-paper p-2.5">
          <p className="break-all font-mono text-caption text-charcoal">{share.url}</p>
        </div>

        <div className="grid gap-2">
          <a href={share.whatsAppUrl} target="_blank" rel="noopener noreferrer" className="contents">
            <Button className="w-full justify-start">
              <Icon.whatsapp className="h-4 w-4" />
              WhatsApp
            </Button>
          </a>
          <a href={share.mailtoUrl} className="contents">
            <Button variant="secondary" className="w-full justify-start">
              <Icon.mail className="h-4 w-4" />
              Email
            </Button>
          </a>
          <Button variant="secondary" onClick={copy} className="w-full justify-start">
            <Icon.copy className="h-4 w-4" />
            Copy link
          </Button>
        </div>

        {/* Said plainly: nothing here is delivered by Quotely. */}
        <p className="text-caption text-fog">
          WhatsApp opens with the message ready to send. Email opens a draft in your own mail app.
          Quotely does not send either for you.
          {!share.customerPhone && " This customer has no phone number saved, so WhatsApp will ask you to pick a contact."}
          {!share.customerEmail && " This customer has no email address saved, so you'll need to type one in."}
        </p>

        <details className="rounded-input border border-ash">
          <summary className="cursor-pointer px-3 py-2 text-caption font-medium text-steel">
            Preview message
          </summary>
          <p className="whitespace-pre-line border-t border-ash px-3 py-2.5 text-caption text-fog">
            {share.message}
          </p>
        </details>
      </div>
    </Dialog>
  );
}
