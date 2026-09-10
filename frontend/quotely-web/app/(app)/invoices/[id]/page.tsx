"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { api, saveBlob } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { InvoiceStatusBadge } from "@/components/ui/badge";
import { ErrorState, LoadingState } from "@/components/ui/states";
import { PageHeader } from "@/components/app/page-header";
import { INVOICE_STATUS_LABELS, type Invoice } from "@/types";

export default function InvoiceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const toast = useToast();
  const [invoice, setInvoice] = useState<Invoice | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setInvoice(await api.get<Invoice>(`/api/invoices/${id}`));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load the invoice.");
    } finally {
      setLoading(false);
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
      toast("PDF downloaded.", "success");
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
      toast("Invoice deleted.", "success");
      router.push("/invoices");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not delete the invoice.", "error");
      setDeleting(false);
    }
  }

  if (loading) {
    return (
      <Card>
        <LoadingState />
      </Card>
    );
  }

  if (error || !invoice) {
    return (
      <Card>
        <ErrorState message={error ?? "Invoice not found."} onRetry={load} />
      </Card>
    );
  }

  const business = invoice.business;
  const customer = invoice.customer;
  const currency = invoice.currency;

  const businessAddress = [
    business?.addressLine,
    [business?.city, business?.state, business?.postalCode].filter(Boolean).join(", "),
    business?.country,
  ].filter(Boolean);

  const customerAddress = [
    customer.addressLine,
    [customer.city, customer.state, customer.postalCode].filter(Boolean).join(", "),
    customer.country,
  ].filter(Boolean);

  return (
    <>
      <PageHeader
        title={invoice.invoiceNumber}
        description={`For ${customer.name}`}
        action={
          <>
            <Link href="/invoices">
              <Button variant="secondary">Back</Button>
            </Link>
            {invoice.canEdit && (
              <Link href={`/invoices/${invoice.id}/edit`}>
                <Button variant="secondary">Edit</Button>
              </Link>
            )}
            {invoice.canDelete && (
              <Button variant="secondary" onClick={() => setConfirmDelete(true)}>
                Delete
              </Button>
            )}
            <Button onClick={download} loading={downloading}>
              Download PDF
            </Button>
          </>
        }
      />

      <Card className="mb-6">
        <div className="flex flex-wrap items-center justify-between gap-3 px-5 py-4">
          <div className="flex flex-wrap items-center gap-3">
            <InvoiceStatusBadge status={invoice.status} />
            {invoice.isOverdue && (
              <span className="text-sm font-medium text-red-600">
                Past due since {formatDate(invoice.dueDate)}
              </span>
            )}
            {!invoice.canEditItems && invoice.canEdit && (
              <span className="text-sm text-slate-500">
                Issued — the line items are locked, but dates, status and notes can still change.
              </span>
            )}
            {!invoice.canEdit && (
              <span className="text-sm text-slate-500">
                {INVOICE_STATUS_LABELS[invoice.status]} invoices can no longer be changed.
              </span>
            )}
          </div>
          <Link
            href={`/quotations/${invoice.quotationId}`}
            className="text-sm font-medium text-blue-600 hover:underline"
          >
            From quotation {invoice.quotationNumber}
          </Link>
        </div>
      </Card>

      {/* Browser preview that mirrors the generated PDF. */}
      <Card className="overflow-hidden">
        <div className="px-5 py-6 sm:px-8 sm:py-8">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0">
              {business?.logoUrl && (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={business.logoUrl} alt="" className="mb-3 h-12 object-contain" />
              )}
              <p className="text-lg font-semibold text-slate-900">{business?.businessName || "Your Business"}</p>
              {businessAddress.map((line) => (
                <p key={line} className="text-sm text-slate-500">
                  {line}
                </p>
              ))}
              {business?.phone && <p className="text-sm text-slate-500">Phone: {business.phone}</p>}
              {business?.businessEmail && <p className="text-sm text-slate-500">Email: {business.businessEmail}</p>}
              {business?.taxNumber && <p className="text-sm text-slate-500">Tax / GST: {business.taxNumber}</p>}
            </div>

            <div className="text-right">
              <p className="text-2xl font-bold tracking-wide text-teal-700">INVOICE</p>
              <p className="mt-1 font-semibold text-slate-900">{invoice.invoiceNumber}</p>
              <p className="mt-3 text-sm text-slate-500">Invoice date: {formatDate(invoice.invoiceDate)}</p>
              <p className="text-sm text-slate-500">Due date: {formatDate(invoice.dueDate)}</p>
              <p className="text-sm text-slate-500">Quotation: {invoice.quotationNumber}</p>
            </div>
          </div>

          <hr className="my-6 border-slate-200" />

          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Bill to</p>
            <p className="mt-1 font-semibold text-slate-900">{customer.name}</p>
            {customer.companyName && <p className="text-sm text-slate-700">{customer.companyName}</p>}
            {customerAddress.map((line) => (
              <p key={line} className="text-sm text-slate-500">
                {line}
              </p>
            ))}
            {(customer.phone || customer.email) && (
              <p className="text-sm text-slate-500">
                {[customer.phone, customer.email].filter(Boolean).join("  •  ")}
              </p>
            )}
            <p className="mt-2 text-xs text-slate-400">
              Billing details as they stood when this invoice was raised.
            </p>
          </div>

          <div className="mt-6 overflow-x-auto">
            <table className="w-full min-w-[560px] border-collapse text-sm">
              <thead>
                <tr className="bg-teal-50">
                  <th className="px-3 py-2 text-left text-xs font-semibold uppercase tracking-wide text-teal-700">
                    Description
                  </th>
                  <th className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-teal-700">
                    Qty
                  </th>
                  <th className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-teal-700">
                    Unit price
                  </th>
                  <th className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-teal-700">
                    Discount
                  </th>
                  <th className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-teal-700">
                    Tax
                  </th>
                  <th className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-teal-700">
                    Total
                  </th>
                </tr>
              </thead>
              <tbody>
                {invoice.items.map((item) => (
                  <tr key={item.id} className="border-b border-slate-100 align-top">
                    <td className="px-3 py-3">
                      <p className="font-medium text-slate-900">{item.name}</p>
                      {item.description && <p className="mt-0.5 text-xs text-slate-500">{item.description}</p>}
                    </td>
                    <td className="px-3 py-3 text-right text-slate-700">
                      {item.quantity} {item.unit}
                    </td>
                    <td className="px-3 py-3 text-right text-slate-700">
                      {formatMoney(item.unitPrice, currency)}
                    </td>
                    <td className="px-3 py-3 text-right text-slate-700">
                      {item.discount > 0 ? formatMoney(item.discount, currency) : "—"}
                    </td>
                    <td className="px-3 py-3 text-right text-slate-700">{item.taxRate}%</td>
                    <td className="px-3 py-3 text-right font-medium text-slate-900">
                      {formatMoney(item.lineTotal, currency)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="mt-6 flex justify-end">
            <dl className="w-full max-w-xs space-y-2 text-sm">
              <div className="flex justify-between">
                <dt className="text-slate-500">Subtotal</dt>
                <dd className="text-slate-900">{formatMoney(invoice.subtotal, currency)}</dd>
              </div>
              {invoice.discountTotal > 0 && (
                <div className="flex justify-between">
                  <dt className="text-slate-500">Discount</dt>
                  <dd className="text-slate-900">-{formatMoney(invoice.discountTotal, currency)}</dd>
                </div>
              )}
              <div className="flex justify-between">
                <dt className="text-slate-500">Tax</dt>
                <dd className="text-slate-900">{formatMoney(invoice.taxTotal, currency)}</dd>
              </div>
              <div className="mt-2 flex items-center justify-between rounded-lg bg-teal-50 px-3 py-3">
                <dt className="text-sm font-semibold text-teal-700">TOTAL DUE</dt>
                <dd className="text-lg font-bold text-teal-700">
                  {formatMoney(invoice.grandTotal, currency)}
                </dd>
              </div>
            </dl>
          </div>

          {(invoice.notes || invoice.terms) && (
            <div className="mt-8 space-y-5">
              {invoice.notes && (
                <div>
                  <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Notes</p>
                  <p className="mt-1 whitespace-pre-line text-sm text-slate-700">{invoice.notes}</p>
                </div>
              )}
              {invoice.terms && (
                <div>
                  <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                    Terms &amp; conditions
                  </p>
                  <p className="mt-1 whitespace-pre-line text-sm text-slate-700">{invoice.terms}</p>
                </div>
              )}
            </div>
          )}
        </div>
      </Card>

      <ConfirmDialog
        open={confirmDelete}
        title="Delete invoice"
        description={`Delete ${invoice.invoiceNumber}? This cannot be undone. The quotation ${invoice.quotationNumber} can then be invoiced again.`}
        loading={deleting}
        onConfirm={remove}
        onCancel={() => setConfirmDelete(false)}
      />
    </>
  );
}
