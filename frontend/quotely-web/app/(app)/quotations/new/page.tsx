"use client";

import { Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { QuotationForm } from "@/components/app/quotation-form";
import { PageHeader } from "@/components/app/page-header";
import { DetailSkeleton } from "@/components/ui/states";

function NewQuotation() {
  const params = useSearchParams();
  return <QuotationForm initialCustomerId={params.get("customerId") ?? undefined} />;
}

export default function NewQuotationPage() {
  return (
    <>
      <PageHeader title="New quotation" description="Add your items — totals update as you type." />
      <Suspense
        fallback={<DetailSkeleton />}
      >
        <NewQuotation />
      </Suspense>
    </>
  );
}
