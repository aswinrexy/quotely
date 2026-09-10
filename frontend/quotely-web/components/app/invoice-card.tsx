"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import type { Invoice, Quotation } from "@/types";

/**
 * The invoicing panel on a quotation. It shows one of three things: the invoice that already
 * exists, the convert action for an accepted quotation, or nothing at all — a quotation the
 * customer has not accepted yet cannot be invoiced, and the server enforces that regardless.
 */
export function InvoiceCard({ quotation, onChanged }: { quotation: Quotation; onChanged: () => void }) {
  const router = useRouter();
  const toast = useToast();
  const [confirming, setConfirming] = useState(false);
  const [converting, setConverting] = useState(false);

  if (quotation.invoiceId) {
    return (
      <Card>
        <CardHeader title="Invoice" description="This quotation has been invoiced." />
        <CardBody className="flex flex-wrap items-center justify-between gap-3">
          <p className="text-sm text-slate-700">
            Invoice <span className="font-semibold text-slate-900">{quotation.invoiceNumber}</span>
          </p>
          <Link href={`/invoices/${quotation.invoiceId}`}>
            <Button variant="secondary">View Invoice</Button>
          </Link>
        </CardBody>
      </Card>
    );
  }

  if (!quotation.canConvertToInvoice) return null;

  async function convert() {
    setConverting(true);
    try {
      const invoice = await api.post<Invoice>(`/api/quotations/${quotation.id}/convert-to-invoice`);
      toast(`Invoice ${invoice.invoiceNumber} created.`, "success");
      setConfirming(false);
      onChanged();
      router.push(`/invoices/${invoice.id}`);
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not create the invoice.", "error");
      setConverting(false);
    }
  }

  return (
    <>
      <Card>
        <CardHeader
          title="Invoice"
          description="The customer accepted this quotation. Convert it to raise the invoice."
        />
        <CardBody className="flex flex-wrap items-center justify-between gap-3">
          <p className="text-sm text-slate-600">
            The invoice keeps a frozen copy of these items and totals, so later price changes cannot
            alter it.
          </p>
          <Button onClick={() => setConfirming(true)}>Convert to Invoice</Button>
        </CardBody>
      </Card>

      {confirming && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div
            className="absolute inset-0 bg-slate-900/40"
            onClick={() => !converting && setConfirming(false)}
            aria-hidden
          />
          <div
            role="dialog"
            aria-modal="true"
            aria-label="Convert to invoice"
            className="relative w-full max-w-md rounded-xl border border-slate-200 bg-white p-5 shadow-xl"
          >
            <h2 className="text-base font-semibold text-slate-900">
              Convert this accepted quotation into an invoice?
            </h2>

            <dl className="mt-4 space-y-2 text-sm">
              <div className="flex justify-between gap-4">
                <dt className="text-slate-500">Quotation</dt>
                <dd className="font-medium text-slate-900">{quotation.quotationNumber}</dd>
              </div>
              <div className="flex justify-between gap-4">
                <dt className="text-slate-500">Customer</dt>
                <dd className="text-right font-medium text-slate-900">{quotation.customer.name}</dd>
              </div>
              <div className="flex justify-between gap-4">
                <dt className="text-slate-500">Total</dt>
                <dd className="font-medium text-slate-900">
                  {formatMoney(quotation.grandTotal, quotation.currency)}
                </dd>
              </div>
            </dl>

            <p className="mt-4 text-sm text-slate-500">
              The invoice number is generated for you, and the due date defaults to 15 days out. You
              can edit both while the invoice is still a draft.
            </p>

            <div className="mt-5 flex justify-end gap-2">
              <Button variant="secondary" onClick={() => setConfirming(false)} disabled={converting}>
                Cancel
              </Button>
              <Button onClick={convert} loading={converting}>
                Convert
              </Button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
