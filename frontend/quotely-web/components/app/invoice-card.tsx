"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { Dialog } from "@/components/ui/dialog";
import { Icon } from "@/components/ui/icons";
import { Mono } from "@/components/ui/table";
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

  async function convert() {
    setConverting(true);
    try {
      const invoice = await api.post<Invoice>(`/api/quotations/${quotation.id}/convert-to-invoice`);
      toast(`Invoice ${invoice.invoiceNumber} created`, "success");
      setConfirming(false);
      onChanged();
      router.push(`/invoices/${invoice.id}`);
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not create the invoice.", "error");
      setConverting(false);
    }
  }

  if (quotation.invoiceId) {
    return (
      <Card>
        <CardHeader title="Invoice" description="This quotation has been invoiced." />
        <CardBody className="space-y-3">
          <p className="text-body text-charcoal">
            <Mono>{quotation.invoiceNumber}</Mono>
          </p>
          <Link href={`/invoices/${quotation.invoiceId}`} className="contents">
            <Button variant="secondary" size="sm" className="w-full">
              <Icon.invoice className="h-3.5 w-3.5" />
              View invoice
            </Button>
          </Link>
        </CardBody>
      </Card>
    );
  }

  if (!quotation.canConvertToInvoice) return null;

  return (
    <>
      <Card>
        <CardHeader title="Invoice" description="Accepted — ready to be invoiced." />
        <CardBody className="space-y-3">
          <p className="text-body text-fog">
            The invoice keeps a frozen copy of these items and totals, so later price changes
            cannot alter it.
          </p>
          <Button size="sm" className="w-full" onClick={() => setConfirming(true)}>
            Convert to invoice
          </Button>
        </CardBody>
      </Card>

      <Dialog
        open={confirming}
        title="Convert this accepted quotation into an invoice?"
        onClose={() => !converting && setConfirming(false)}
        footer={
          <>
            <Button
              variant="secondary"
              onClick={() => setConfirming(false)}
              disabled={converting}
              className="w-full sm:w-auto"
            >
              Cancel
            </Button>
            <Button onClick={convert} loading={converting} className="w-full sm:w-auto">
              Convert
            </Button>
          </>
        }
      >
        <dl className="space-y-2.5 rounded-card bg-paper p-3">
          <Row label="Quotation">
            <Mono>{quotation.quotationNumber}</Mono>
          </Row>
          <Row label="Customer">{quotation.customer.name}</Row>
          <Row label="Total">
            <span className="tabular-nums">
              {formatMoney(quotation.grandTotal, quotation.currency)}
            </span>
          </Row>
        </dl>
        <p className="mt-3 text-body text-fog">
          The invoice number is generated for you, and the due date defaults to 15 days out. You can
          edit both while the invoice is still a draft.
        </p>
      </Dialog>
    </>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <dt className="text-body text-fog">{label}</dt>
      <dd className="text-right text-body font-medium text-charcoal">{children}</dd>
    </div>
  );
}
