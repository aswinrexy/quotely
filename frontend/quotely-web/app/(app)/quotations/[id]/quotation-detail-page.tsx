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
import { StatusBadge } from "@/components/ui/badge";
import { DetailSkeleton, ErrorState } from "@/components/ui/states";
import { Icon } from "@/components/ui/icons";
import { Mono } from "@/components/ui/table";
import { ShareLinkCard } from "@/components/app/share-link-card";
import { InvoiceCard } from "@/components/app/invoice-card";
import {
  DocumentNotes,
  LineItems,
  MetaItem,
  PartyBlock,
  Totals,
} from "@/components/app/document-view";

export default function QuotationDetailPage() {
  const id = useRouteParam("/quotations/[id]", "id");
  const router = useRouter();
  const toast = useToast();
  const [quotation, setQuotation] = useState<import("@/types").Quotation | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setQuotation(await api.get<import("@/types").Quotation>(`/api/quotations/${id}`));
    } catch (err) {
      setError(err instanceof Error ? err.message : "We couldn't load this quotation.");
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
      setQuotation(await api.get<import("@/types").Quotation>(`/api/quotations/${id}`));
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
      await api.delete(`/api/quotations/${id}`);
      toast("Quotation deleted", "success");
      router.push("/quotations");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not delete the quotation.", "error");
      setDeleting(false);
    }
  }

  if (loading) return <DetailSkeleton />;

  if (error || !quotation) {
    return (
      <Card>
        <ErrorState message={error ?? "This quotation could not be found."} onRetry={load} />
      </Card>
    );
  }

  const { business, customer, currency } = quotation;

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

  return (
    <>
      <div className="mb-5">
        <Link
          href="/quotations"
          className="inline-flex items-center gap-1 text-body text-fog transition-colors duration-150 ease-out hover:text-charcoal"
        >
          <Icon.chevronLeft className="h-3.5 w-3.5" />
          Quotations
        </Link>
        <div className="mt-2 flex flex-wrap items-center gap-3">
          <h1 className="font-display text-heading-sm text-charcoal">
            <Mono className="text-heading-sm">{quotation.quotationNumber}</Mono>
          </h1>
          <StatusBadge status={quotation.status} />
        </div>
        <p className="mt-1 text-body text-fog">For {customer.name}</p>
      </div>

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_300px] xl:items-start">
        {/* ---- the document itself ---- */}
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
                <MetaItem label="Quotation">
                  <Mono>{quotation.quotationNumber}</Mono>
                </MetaItem>
                <MetaItem label="Date">{formatDate(quotation.quotationDate)}</MetaItem>
                <MetaItem label="Valid until">{formatDate(quotation.validUntil)}</MetaItem>
              </dl>
            </div>
          </div>

          <div className="border-b border-ash p-4 sm:p-6">
            <PartyBlock
              label="Prepared for"
              name={customer.name}
              company={customer.companyName}
              lines={customerAddress}
              contact={[customer.phone, customer.email]}
            />
          </div>

          <div className="p-4 sm:p-6">
            <LineItems items={quotation.items} currency={currency} />

            <div className="mt-6 flex justify-end">
              <Totals
                rows={[
                  { label: "Subtotal", value: formatMoney(quotation.subtotal, currency) },
                  ...(quotation.discountTotal > 0
                    ? [{ label: "Discount", value: `-${formatMoney(quotation.discountTotal, currency)}` }]
                    : []),
                  { label: "Tax", value: formatMoney(quotation.taxTotal, currency) },
                  { label: "Total", value: formatMoney(quotation.grandTotal, currency), strong: true },
                ]}
              />
            </div>

            {(quotation.notes || quotation.terms) && (
              <div className="mt-8 border-t border-ash pt-6">
                <DocumentNotes notes={quotation.notes} terms={quotation.terms} />
              </div>
            )}
          </div>
        </Card>

        {/* ---- the action rail ---- */}
        <div className="space-y-4 xl:sticky xl:top-20">
          <Card>
            <div className="space-y-4 p-4">
              <div>
                <p className="text-caption font-medium uppercase tracking-wide text-fog">Total</p>
                <p className="mt-1 text-heading-sm font-semibold tabular-nums text-charcoal">
                  {formatMoney(quotation.grandTotal, currency)}
                </p>
              </div>

              <div className="grid gap-2">
                <Button onClick={download} loading={downloading}>
                  <Icon.download className="h-4 w-4" />
                  Download PDF
                </Button>
                <Link href={`/quotations/${quotation.id}/edit`} className="contents">
                  <Button variant="secondary" className="w-full">
                    <Icon.edit className="h-4 w-4" />
                    Edit
                  </Button>
                </Link>
                <Button variant="danger" onClick={() => setConfirmDelete(true)}>
                  <Icon.trash className="h-4 w-4" />
                  Delete
                </Button>
              </div>
            </div>
          </Card>

          <InvoiceCard quotation={quotation} onChanged={refresh} />
          <ShareLinkCard quotation={quotation} onChanged={refresh} />
        </div>
      </div>

      <ConfirmDialog
        open={confirmDelete}
        title="Delete quotation"
        description={`${quotation.quotationNumber} will be permanently deleted. This cannot be undone.`}
        confirmLabel="Delete quotation"
        loading={deleting}
        onConfirm={remove}
        onCancel={() => setConfirmDelete(false)}
      />
    </>
  );
}
