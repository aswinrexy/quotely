"use client";

import { Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { QuotationForm } from "@/components/app/quotation-form";
import { PageHeader } from "@/components/app/page-header";
import { Card } from "@/components/ui/card";
import { LoadingState } from "@/components/ui/states";

function NewQuotation() {
  const params = useSearchParams();
  return <QuotationForm initialCustomerId={params.get("customerId") ?? undefined} />;
}

export default function NewQuotationPage() {
  return (
    <>
      <PageHeader title="Create Quotation" description="Add your items — totals update as you type." />
      <Suspense
        fallback={
          <Card>
            <LoadingState />
          </Card>
        }
      >
        <NewQuotation />
      </Suspense>
    </>
  );
}
