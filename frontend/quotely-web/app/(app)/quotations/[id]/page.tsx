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
import { StatusBadge } from "@/components/ui/badge";
import { ErrorState, LoadingState } from "@/components/ui/states";
import { PageHeader } from "@/components/app/page-header";
import { ShareLinkCard } from "@/components/app/share-link-card";
import type { Quotation } from "@/types";

export default function QuotationDetailPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const toast = useToast();
  const [quotation, setQuotation] = useState<Quotation | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setQuotation(await api.get<Quotation>(`/api/quotations/${id}`));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load the quotation.");
    } finally {
      setLoading(false);
    }
  }, [id]);

  /**
   * Refreshes the quotation in place, without the loading state. The share panel keeps the
   * generated URL in its own state, and that URL can never be fetched again — so a refresh that
   * unmounted the panel would throw the link away before the owner could copy it.
   */
  const refresh = useCallback(async () => {
    try {
      setQuotation(await api.get<Quotation>(`/api/quotations/${id}`));
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
      const { blob, fileName } = await api.downloadPdf(id);
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
      await api.delete(`/api/quotations/${id}`);
      toast("Quotation deleted.", "success");
      router.push("/quotations");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not delete the quotation.", "error");
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

  if (error || !quotation) {
    return (
      <Card>
        <ErrorState message={error ?? "Quotation not found."} onRetry={load} />
      </Card>
    );
  }

  const business = quotation.business;
  const customer = quotation.customer;
  const currency = quotation.currency;

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
        title={quotation.quotationNumber}
        description={`For ${customer.name}`}
        action={
          <>
            <Link href="/quotations">
              <Button variant="secondary">Back</Button>
            </Link>
            <Link href={`/quotations/${quotation.id}/edit`}>
              <Button variant="secondary">Edit</Button>
            </Link>
            <Button variant="secondary" onClick={() => setConfirmDelete(true)}>
              Delete
            </Button>
            <Button onClick={download} loading={downloading}>
              Generate &amp; Download PDF
            </Button>
          </>
        }
      />

      <div className="mb-6">
        <ShareLinkCard quotation={quotation} onChanged={refresh} />
      </div>

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
              <p className="text-2xl font-bold tracking-wide text-blue-600">QUOTATION</p>
              <p className="mt-1 font-semibold text-slate-900">{quotation.quotationNumber}</p>
              <p className="mt-3 text-sm text-slate-500">Date: {formatDate(quotation.quotationDate)}</p>
              <p className="text-sm text-slate-500">Valid until: {formatDate(quotation.validUntil)}</p>
              <div className="mt-2 flex justify-end">
                <StatusBadge status={quotation.status} />
              </div>
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
          </div>

          <div className="mt-6 overflow-x-auto">
            <table className="w-full min-w-[560px] border-collapse text-sm">
              <thead>
                <tr className="bg-blue-50/70">
                  <th className="px-3 py-2 text-left text-xs font-semibold uppercase tracking-wide text-blue-700">
                    Description
                  </th>
                  <th className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-blue-700">
                    Qty
                  </th>
                  <th className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-blue-700">
                    Unit price
                  </th>
                  <th className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-blue-700">
                    Discount
                  </th>
                  <th className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-blue-700">
                    Tax
                  </th>
                  <th className="px-3 py-2 text-right text-xs font-semibold uppercase tracking-wide text-blue-700">
                    Total
                  </th>
                </tr>
              </thead>
              <tbody>
                {quotation.items.map((item) => (
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
                <dd className="text-slate-900">{formatMoney(quotation.subtotal, currency)}</dd>
              </div>
              {quotation.discountTotal > 0 && (
                <div className="flex justify-between">
                  <dt className="text-slate-500">Discount</dt>
                  <dd className="text-slate-900">-{formatMoney(quotation.discountTotal, currency)}</dd>
                </div>
              )}
              <div className="flex justify-between">
                <dt className="text-slate-500">Tax</dt>
                <dd className="text-slate-900">{formatMoney(quotation.taxTotal, currency)}</dd>
              </div>
              <div className="mt-2 flex items-center justify-between rounded-lg bg-blue-50 px-3 py-3">
                <dt className="text-sm font-semibold text-blue-700">TOTAL</dt>
                <dd className="text-lg font-bold text-blue-700">
                  {formatMoney(quotation.grandTotal, currency)}
                </dd>
              </div>
            </dl>
          </div>

          {(quotation.notes || quotation.terms) && (
            <div className="mt-8 space-y-5">
              {quotation.notes && (
                <div>
                  <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Notes</p>
                  <p className="mt-1 whitespace-pre-line text-sm text-slate-700">{quotation.notes}</p>
                </div>
              )}
              {quotation.terms && (
                <div>
                  <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                    Terms &amp; conditions
                  </p>
                  <p className="mt-1 whitespace-pre-line text-sm text-slate-700">{quotation.terms}</p>
                </div>
              )}
            </div>
          )}
        </div>
      </Card>

      <ConfirmDialog
        open={confirmDelete}
        title="Delete quotation"
        description={`Delete ${quotation.quotationNumber}? This cannot be undone.`}
        loading={deleting}
        onConfirm={remove}
        onCancel={() => setConfirmDelete(false)}
      />
    </>
  );
}
