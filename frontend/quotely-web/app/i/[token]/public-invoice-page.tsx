"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiError, publicApi } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/states";
import { InvoiceStatusBadge } from "@/components/ui/badge";
import { Icon } from "@/components/ui/icons";
import { Mono } from "@/components/ui/table";
import {
  DocumentNotes,
  LineItems,
  MetaItem,
  PartyBlock,
  Totals,
} from "@/components/app/document-view";
import { PayPanel } from "@/components/public/pay-panel";
import type { PublicInvoice } from "@/types";
import { useRouteParam } from "@/lib/route-param";

type Phase = "loading" | "ready" | "notFound" | "error";

/**
 * The customer-facing invoice. Same visual family as the public quotation page and the same
 * document components, so an invoice reads as a document rather than as a payment gateway.
 */
export default function PublicInvoicePage() {
  const token = useRouteParam("/i/[token]", "token");

  const [invoice, setInvoice] = useState<PublicInvoice | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [loadError, setLoadError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setPhase("loading");
    setLoadError(null);
    try {
      setInvoice(
        await publicApi.get<PublicInvoice>(`/api/public/invoices/${encodeURIComponent(token)}`),
      );
      setPhase("ready");
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        setPhase("notFound");
        return;
      }
      setLoadError(err instanceof Error ? err.message : "This invoice could not be loaded.");
      setPhase("error");
    }
  }, [token]);

  /** Silent refresh after a payment, so the balance on screen comes from the server. */
  const refresh = useCallback(async () => {
    try {
      setInvoice(
        await publicApi.get<PublicInvoice>(`/api/public/invoices/${encodeURIComponent(token)}`),
      );
    } catch {
      // The payment panel already shows the server's verdict; keep what we have.
    }
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  if (phase === "loading") {
    return (
      <Shell>
        <div className="flex flex-col items-center gap-3 py-24 text-body text-fog">
          <Spinner />
          Loading invoice…
        </div>
      </Shell>
    );
  }

  if (phase === "notFound") {
    return (
      <Shell>
        <Notice
          title="Invoice not found"
          message="This payment link is invalid or is no longer available. Please ask the business for a new link."
        />
      </Shell>
    );
  }

  if (phase === "error" || !invoice) {
    return (
      <Shell>
        <Notice
          title="Something went wrong"
          message={loadError ?? "This invoice could not be loaded."}
          action={
            <Button variant="secondary" onClick={load}>
              Try again
            </Button>
          }
        />
      </Shell>
    );
  }

  const { business, customer, currency } = invoice;
  const settled = invoice.outstanding <= 0;

  const businessAddress = [
    business.addressLine,
    [business.city, business.state, business.postalCode].filter(Boolean).join(", "),
    business.country,
  ].filter(Boolean) as string[];

  const customerAddress = [
    customer.addressLine,
    [customer.city, customer.state, customer.postalCode].filter(Boolean).join(", "),
    customer.country,
  ].filter(Boolean) as string[];

  return (
    <Shell>
      {invoice.status === "Cancelled" && (
        <Banner tone="neutral" title="This invoice has been cancelled">
          It is no longer payable. Please contact {business.businessName} if you have a question.
        </Banner>
      )}

      {invoice.status !== "Cancelled" && settled && (
        <Banner tone="success" title="Paid in full">
          Thank you. Nothing further is owed on this invoice.
        </Banner>
      )}

      {invoice.status !== "Cancelled" && !settled && invoice.isOverdue && (
        <Banner tone="warning" title="This invoice is past due">
          It was due on {formatDate(invoice.dueDate)}.
        </Banner>
      )}

      <article className="overflow-hidden rounded-lgcard border border-ash bg-canvas">
        <header className="border-b border-ash p-5 sm:p-8">
          <div className="flex flex-col gap-6 sm:flex-row sm:items-start sm:justify-between">
            <div className="min-w-0">
              {business.logoUrl && (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={business.logoUrl} alt="" className="mb-3 h-11 object-contain" />
              )}
              <h1 className="text-body-xl font-semibold text-charcoal">{business.businessName}</h1>
              {businessAddress.map((line) => (
                <p key={line} className="text-body text-fog">
                  {line}
                </p>
              ))}
              {business.phone && <p className="text-body text-fog">{business.phone}</p>}
              {business.email && <p className="break-words text-body text-fog">{business.email}</p>}
              {business.taxNumber && (
                <p className="text-body text-fog">Tax / GST: {business.taxNumber}</p>
              )}
            </div>

            <div className="shrink-0 sm:text-right">
              <p className="text-caption font-medium uppercase tracking-wide text-fog">Invoice</p>
              <p className="mt-1 font-display text-heading-sm text-charcoal">
                <Mono className="text-heading-sm">{invoice.invoiceNumber}</Mono>
              </p>
              <dl className="mt-4 grid gap-2.5 sm:justify-items-end">
                <MetaItem label="Invoice date">{formatDate(invoice.invoiceDate)}</MetaItem>
                <MetaItem label="Due date">{formatDate(invoice.dueDate)}</MetaItem>
              </dl>
              <div className="mt-3 flex sm:justify-end">
                <InvoiceStatusBadge status={invoice.status} />
              </div>
            </div>
          </div>
        </header>

        <section className="border-b border-ash p-5 sm:p-8">
          <PartyBlock
            label="Bill to"
            name={customer.name}
            company={customer.companyName}
            lines={customerAddress}
            contact={[customer.phone, customer.email]}
          />
        </section>

        <section className="p-5 sm:p-8">
          <LineItems items={invoice.items} currency={currency} />

          <div className="mt-6 flex justify-end">
            <Totals
              rows={[
                { label: "Subtotal", value: formatMoney(invoice.subtotal, currency) },
                ...(invoice.discountTotal > 0
                  ? [{ label: "Discount", value: `-${formatMoney(invoice.discountTotal, currency)}` }]
                  : []),
                { label: "Tax", value: formatMoney(invoice.taxTotal, currency) },
                { label: "Total", value: formatMoney(invoice.grandTotal, currency), strong: true },
                { label: "Paid", value: formatMoney(invoice.paid, currency) },
                {
                  label: "Outstanding",
                  value: formatMoney(invoice.outstanding, currency),
                  accent: invoice.outstanding > 0,
                },
              ]}
            />
          </div>
        </section>

        {invoice.payments.length > 0 && (
          <section className="border-t border-ash p-5 sm:p-8">
            <p className="text-caption font-medium uppercase tracking-wide text-fog">Payments received</p>
            <ul className="mt-2.5 divide-y divide-ash">
              {invoice.payments.map((payment, index) => (
                <li
                  key={payment.reference ?? index}
                  className="flex flex-wrap items-baseline justify-between gap-2 py-2.5"
                >
                  <div className="min-w-0">
                    <p className="text-body text-charcoal">
                      {payment.paidAt ? formatDate(payment.paidAt) : "—"}
                    </p>
                    {payment.method && (
                      <p className="text-caption uppercase text-fog">{payment.method}</p>
                    )}
                  </div>
                  <span className="font-medium tabular-nums text-charcoal">
                    {formatMoney(payment.amount, currency)}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        )}

        {(invoice.notes || invoice.terms) && (
          <section className="border-t border-ash p-5 sm:p-8">
            <DocumentNotes notes={invoice.notes} terms={invoice.terms} />
          </section>
        )}

        <footer className="border-t border-ash bg-paper p-5 sm:p-8">
          {invoice.canPay ? (
            <>
              <div className="mb-4 flex flex-wrap items-baseline justify-between gap-2">
                <span className="text-caption font-medium uppercase tracking-wide text-fog">
                  Outstanding
                </span>
                <span className="font-display text-heading-sm text-charcoal">
                  {formatMoney(invoice.outstanding, currency)}
                </span>
              </div>
              <PayPanel token={token} invoice={invoice} onPaid={refresh} />
            </>
          ) : invoice.hasPendingPayment ? (
            <p className="text-body text-steel">
              A payment on this invoice is being confirmed. There is no need to pay again.
            </p>
          ) : (
            <p className="text-body text-steel">
              {settled
                ? "This invoice has been paid in full."
                : "This invoice cannot be paid online at the moment. Please contact the business."}
            </p>
          )}

          <a
            href={publicApi.invoicePdfUrl(token)}
            target="_blank"
            rel="noopener noreferrer"
            className="mt-5 inline-flex items-center gap-1.5 text-body font-medium text-electric underline-offset-4 hover:underline"
          >
            <Icon.download className="h-4 w-4" />
            Download PDF
          </a>
        </footer>
      </article>
    </Shell>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-paper px-3 py-6 sm:px-6 sm:py-12">
      <div className="mx-auto w-full max-w-3xl space-y-4">
        {children}
        <p className="pb-6 pt-2 text-center text-caption text-fog">
          Sent with Quote<span className="text-electric">ly</span>
        </p>
      </div>
    </div>
  );
}

const TONES = {
  success: "bg-mint text-green-ink",
  warning: "bg-amber-wash text-amber-ink",
  neutral: "bg-canvas text-charcoal",
} as const;

function Banner({
  tone,
  title,
  children,
}: {
  tone: keyof typeof TONES;
  title: string;
  children: React.ReactNode;
}) {
  return (
    <div role="status" className={`rounded-card border border-ash px-5 py-4 ${TONES[tone]}`}>
      <p className="font-semibold">{title}</p>
      <p className="mt-0.5 text-body opacity-90">{children}</p>
    </div>
  );
}

function Notice({
  title,
  message,
  action,
}: {
  title: string;
  message: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="rounded-lgcard border border-ash bg-canvas px-6 py-16 text-center">
      <h1 className="font-display text-subheading text-charcoal">{title}</h1>
      <p className="mx-auto mt-2 max-w-sm text-body text-fog">{message}</p>
      {action && <div className="mt-6 flex justify-center">{action}</div>}
    </div>
  );
}
