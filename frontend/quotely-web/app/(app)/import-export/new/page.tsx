"use client";

import { TradeInvoiceForm } from "@/components/app/trade-invoice-form";
import { PageHeader } from "@/components/app/page-header";

export default function NewTradeInvoicePage() {
  return (
    <>
      <PageHeader
        title="New import / export document"
        description="A proforma or commercial invoice with the shipping, weight and HS code detail international trade needs."
      />
      <TradeInvoiceForm />
    </>
  );
}
