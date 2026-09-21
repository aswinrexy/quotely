"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouteParam } from "@/lib/route-param";
import { api } from "@/lib/api";
import { DetailSkeleton, ErrorState } from "@/components/ui/states";
import { PageHeader } from "@/components/app/page-header";
import { TradeInvoiceForm } from "@/components/app/trade-invoice-form";
import { TRADE_DOCUMENT_TYPE_LABELS, type TradeInvoice } from "@/types";

export default function TradeInvoiceEditPage() {
  const id = useRouteParam("/import-export/[id]/edit", "id");
  const [invoice, setInvoice] = useState<TradeInvoice | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    try {
      setInvoice(await api.get<TradeInvoice>(`/api/import-export/invoices/${id}`));
    } catch (err) {
      setError(err instanceof Error ? err.message : "We couldn't load this document.");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) return <DetailSkeleton />;
  if (error) return <ErrorState message={error} onRetry={load} />;
  if (!invoice) return null;

  return (
    <>
      <PageHeader
        title={`Edit ${TRADE_DOCUMENT_TYPE_LABELS[invoice.documentType].toLowerCase()}`}
        description={
          invoice.canEditItems
            ? undefined
            : "This document has been issued, so the goods and totals are fixed. Everything else can still be corrected."
        }
      />
      <TradeInvoiceForm invoice={invoice} />
    </>
  );
}
