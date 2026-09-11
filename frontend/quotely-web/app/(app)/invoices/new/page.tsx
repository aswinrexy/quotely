"use client";

import { Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { InvoiceForm } from "@/components/app/invoice-form";
import { PageHeader } from "@/components/app/page-header";
import { DetailSkeleton } from "@/components/ui/states";

function NewInvoice() {
  const params = useSearchParams();
  return <InvoiceForm initialCustomerId={params.get("customerId") ?? undefined} />;
}

export default function NewInvoicePage() {
  return (
    <>
      <PageHeader
        title="New invoice"
        description="Bill a customer directly — no quotation needed."
      />
      <Suspense fallback={<DetailSkeleton />}>
        <NewInvoice />
      </Suspense>
    </>
  );
}
