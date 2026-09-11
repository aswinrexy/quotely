"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { QuotationForm } from "@/components/app/quotation-form";
import { PageHeader } from "@/components/app/page-header";
import { Card } from "@/components/ui/card";
import { DetailSkeleton, ErrorState } from "@/components/ui/states";
import type { Quotation } from "@/types";

export default function EditQuotationPage() {
  const { id } = useParams<{ id: string }>();
  const [quotation, setQuotation] = useState<Quotation | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<Quotation>(`/api/quotations/${id}`)
      .then(setQuotation)
      .catch((err) => setError(err instanceof Error ? err.message : "Could not load the quotation."));
  }, [id]);

  return (
    <>
      <PageHeader
        title={quotation ? `Edit ${quotation.quotationNumber}` : "Edit quotation"}
        description="Changes are recalculated and verified by the server."
      />
      {error ? (
        <Card>
          <ErrorState message={error} />
        </Card>
      ) : !quotation ? (
        <DetailSkeleton />
      ) : (
        <QuotationForm quotation={quotation} />
      )}
    </>
  );
}
