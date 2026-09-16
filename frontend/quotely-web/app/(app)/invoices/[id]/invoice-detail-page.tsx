"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useRouteParam } from "@/lib/route-param";
import { api, saveBlob } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { InvoiceStatusBadge } from "@/components/ui/badge";
import { DetailSkeleton, ErrorState } from "@/components/ui/states";
import { Icon } from "@/components/ui/icons";
import { Mono } from "@/components/ui/table";
import {
  DocumentNotes,
  LineItems,
  MetaItem,
  PartyBlock,
  Totals,
} from "@/components/app/document-view";
import {
  PaymentHistoryCard,
  InvoiceShareCard,
} from "@/components/app/invoice-payments-card";
import { RecordPaymentDialog } from "@/components/app/record-payment-dialog";
import { INVOICE_STATUS_LABELS, type Invoice, type InvoicePayments } from "@/types";

export default function InvoiceDetailPage() {
  const id = useRouteParam("/invoices/[id]", "id");
  const router = useRouter();
  const toast = useToast();
  const [invoice, setInvoice] = useState<Invoice | null>(null);
  const [payments, setPayments] = useState<InvoicePayments | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [recording, setRecording] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [loaded, history] = await Promise.all([
        api.get<Invoice>(`/api/invoices/${id}`),
        api.get<InvoicePayments>(`/api/invoices/${id}/payments`),
      ]);
      setInvoice(loaded);
      setPayments(history);
    } catch (err) {
      setError(err instanceof Error ? err.message : "We couldn't load this invoice.");
    } finally {
      setLoading(false);
    }
  }, [id]);

  /**
   * Refreshes in place, without the loading state. The share panel keeps the generated URL in
   * its own state, and that URL can never be fetched again — a refresh that unmounted the panel
   * would throw the link away before the owner could copy it.
   */
  const refresh = useCallback(async () => {
    try {
      const [loaded, history] = await Promise.all([
        api.get<Invoice>(`/api/invoices/${id}`),
        api.get<InvoicePayments>(`/api/invoices/${id}/payments`),
      ]);
      setInvoice(loaded);
      setPayments(history);
    } catch {
      // A failed background refresh leaves the page on the data it already has.
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  async function download() {
    setDownloading(true);
    try {
      const { blob, fileName } = await api.downloadInvoicePdf(id);
      saveBlob(blob, fileName);
      toast("PDF downloaded", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not generate the PDF.", "error");
    } finally {
      setDownloading(false);
    }
  }

  async function remove() {
    setDeleting(true);
    try {
      await api.delete(`/api/invoices/${id}`);
      toast("Invoice deleted", "success");
      router.push("/invoices");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not delete the invoice.", "error");
      setDeleting(false);
    }
  }

  if (loading) return <DetailSkeleton />;

  if (error || !invoice) {
    return (
      <Card>
        <ErrorState message={error ?? "This invoice could not be found."} onRetry={load} />
      </Card>
    );
  }

  const { business, customer, currency } = invoice;

  const businessAddress = [
    business?.addressLine,
    [business?.city, business?.state, business?.postalCode].filter(Boolean).join(", "),
    business?.country,
  ].filter(Boolean) as string[];

  const customerAddress = [
    customer.addressLine,
    [customer.city, customer.state, customer.postalCode].filter(Boolean).join(", "),
    customer.country,
  ].filter(Boolean) as string[];

  // Both figures are computed by the server from captured payments. The browser never adds up
  // money of its own.
  const paid = invoice.paid;
  const outstanding = invoice.outstanding;

  // A draft has not been issued and a cancelled invoice is void, so neither takes a payment —
  // the same rule the server enforces, mirrored here only to avoid offering a doomed action.
  const canRecordPayment =
    outstanding > 0 && invoice.status !== "Draft" && invoice.status !== "Cancelled";

  return (
    <>
      <div className="mb-5">
        <Link
          href="/invoices"
          className="inline-flex items-center gap-1 text-body text-fog transition-colors duration-150 ease-out hover:text-charcoal"
        >
          <Icon.chevronLeft className="h-3.5 w-3.5" />
          Invoices
        </Link>
        <div className="mt-2 flex flex-wrap items-center gap-3">
          <h1 className="font-display text-heading-sm text-charcoal">
            <Mono className="text-heading-sm">{invoice.invoiceNumber}</Mono>
          </h1>
          <InvoiceStatusBadge status={invoice.status} />
          {invoice.isOverdue && (
            <span className="text-body font-medium text-rose-ink">
              Past due since {formatDate(invoice.dueDate)}
            </span>
          )}
        </div>
        <p className="mt-1 text-body text-fog">For {customer.name}</p>
      </div>

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_300px] xl:items-start">
        <Card className="min-w-0 overflow-hidden">
          <div className="border-b border-ash p-4 sm:p-6">
            <div className="flex flex-wrap items-start justify-between gap-5">
              <div className="min-w-0">
                {business?.logoUrl && (
                  // eslint-disable-next-line @next/next/no-img-element
                  <img src={business.logoUrl} alt="" className="mb-3 h-10 object-contain" />
                )}
                <p className="text-body-lg font-semibold text-charcoal">
                  {business?.businessName || "Your business"}
                </p>
                {businessAddress.map((line) => (
                  <p key={line} className="text-body text-fog">
                    {line}
                  </p>
                ))}
                {business?.phone && <p className="text-body text-fog">{business.phone}</p>}
                {business?.businessEmail && (
                  <p className="break-words text-body text-fog">{business.businessEmail}</p>
                )}
                {business?.taxNumber && (
                  <p className="text-body text-fog">Tax / GST: {business.taxNumber}</p>
                )}
              </div>

              <dl className="grid shrink-0 gap-3 sm:text-right">
                <MetaItem label="Invoice">
                  <Mono>{invoice.invoiceNumber}</Mono>
                </MetaItem>
                <MetaItem label="Invoice date">{formatDate(invoice.invoiceDate)}</MetaItem>
                <MetaItem label="Due date">{formatDate(invoice.dueDate)}</MetaItem>
                {/* Only a converted invoice has a quotation to point back to. */}
                {invoice.quotationId && (
                  <MetaItem label="Quotation">
                    <Link
                      href={`/quotations/${invoice.quotationId}`}
                      className="text-electric hover:underline"
                    >
                      <Mono>{invoice.quotationNumber}</Mono>
                    </Link>
                  </MetaItem>
                )}
              </dl>
            </div>
          </div>

          <div className="border-b border-ash p-4 sm:p-6">
            <PartyBlock
              label="Bill to"
              name={customer.name}
              company={customer.companyName}
              lines={customerAddress}
              contact={[customer.phone, customer.email]}
              footnote="Billing details as they stood when this invoice was raised."
            />
          </div>

          <div className="p-4 sm:p-6">
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
                  { label: "Paid", value: formatMoney(paid, currency) },
                  {
                    label: "Outstanding",
                    value: formatMoney(outstanding, currency),
                    accent: outstanding > 0,
                  },
                ]}
              />
            </div>

            {(invoice.notes || invoice.terms) && (
              <div className="mt-8 border-t border-ash pt-6">
                <DocumentNotes notes={invoice.notes} terms={invoice.terms} />
              </div>
            )}
          </div>
        </Card>

        <div className="space-y-4 xl:sticky xl:top-20">
          <Card>
            <div className="space-y-4 p-4">
              <div>
                <p className="text-caption font-medium uppercase tracking-wide text-fog">
                  {outstanding > 0 ? "Outstanding" : "Total"}
                </p>
                <p className="mt-1 text-heading-sm font-semibold tabular-nums text-charcoal">
                  {formatMoney(outstanding > 0 ? outstanding : invoice.grandTotal, currency)}
                </p>
                {outstanding > 0 && outstanding !== invoice.grandTotal && (
                  <p className="mt-1 text-caption text-fog">
                    of {formatMoney(invoice.grandTotal, currency)}
                  </p>
                )}
              </div>

              <div className="grid gap-2">
                {canRecordPayment && (
                  <Button onClick={() => setRecording(true)}>
                    <Icon.check className="h-4 w-4" />
                    Record payment
                  </Button>
                )}
                {/* One near-black primary per surface: recording money outranks a download. */}
                <Button
                  variant={canRecordPayment ? "secondary" : "primary"}
                  onClick={download}
                  loading={downloading}
                >
                  <Icon.download className="h-4 w-4" />
                  Download PDF
                </Button>
                {invoice.canEdit && (
                  <Link href={`/invoices/${invoice.id}/edit`} className="contents">
                    <Button variant="secondary" className="w-full">
                      <Icon.edit className="h-4 w-4" />
                      Edit
                    </Button>
                  </Link>
                )}
                {invoice.canDelete && (
                  <Button variant="danger" onClick={() => setConfirmDelete(true)}>
                    <Icon.trash className="h-4 w-4" />
                    Delete
                  </Button>
                )}
              </div>
            </div>
          </Card>

          <InvoiceShareCard invoice={invoice} onChanged={refresh} />

          <PaymentHistoryCard payments={payments} invoiceId={invoice.id} onChanged={refresh} />

          {/* What can still be changed, said plainly rather than left for a failed save to reveal. */}
          {(!invoice.canEdit || !invoice.canEditItems) && (
            <Card>
              <div className="p-4">
                <p className="text-caption font-medium uppercase tracking-wide text-fog">Status</p>
                <p className="mt-1.5 text-body text-steel">
                  {!invoice.canEdit
                    ? `${INVOICE_STATUS_LABELS[invoice.status]} invoices can no longer be changed.`
                    : "Issued — the line items are locked, but dates, status and notes can still change."}
                </p>
              </div>
            </Card>
          )}
        </div>
      </div>

      <RecordPaymentDialog
        invoice={invoice}
        open={recording}
        onClose={() => setRecording(false)}
        onRecorded={refresh}
      />

      <ConfirmDialog
        open={confirmDelete}
        title="Delete invoice"
        description={
          invoice.quotationNumber
            ? `${invoice.invoiceNumber} will be permanently deleted. Quotation ${invoice.quotationNumber} can then be invoiced again.`
            : `${invoice.invoiceNumber} will be permanently deleted. This cannot be undone.`
        }
        confirmLabel="Delete invoice"
        loading={deleting}
        onConfirm={remove}
        onCancel={() => setConfirmDelete(false)}
      />
    </>
  );
}
