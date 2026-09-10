"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Card, CardHeader } from "@/components/ui/card";
import { StatusBadge } from "@/components/ui/badge";
import { EmptyState, ErrorState, LoadingState } from "@/components/ui/states";
import { Table, TableWrap, Td, Th } from "@/components/ui/table";
import { PageHeader } from "@/components/app/page-header";
import type { DashboardStats, PagedResult, QuotationListItem } from "@/types";

export default function DashboardPage() {
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [recent, setRecent] = useState<QuotationListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [statsResult, recentResult] = await Promise.all([
        api.get<DashboardStats>("/api/quotations/stats"),
        api.get<PagedResult<QuotationListItem>>("/api/quotations?page=1&pageSize=5"),
      ]);
      setStats(statsResult);
      setRecent(recentResult.items);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load your dashboard.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const cards = [
    { label: "Total quotations", value: stats ? String(stats.totalQuotations) : "—" },
    { label: "Drafts", value: stats ? String(stats.draftCount) : "—" },
    { label: "Sent", value: stats ? String(stats.sentCount) : "—" },
    { label: "Accepted", value: stats ? String(stats.acceptedCount) : "—" },
    {
      label: "Total quotation value",
      value: stats ? formatMoney(stats.totalValue, stats.currency) : "—",
      wide: true,
    },
  ];

  return (
    <>
      <PageHeader
        title="Dashboard"
        description="An overview of your quotation activity."
        action={
          <Link href="/quotations/new">
            <Button>Create Quotation</Button>
          </Link>
        }
      />

      {error && (
        <Card className="mb-6">
          <ErrorState message={error} onRetry={load} />
        </Card>
      )}

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {cards.map((card) => (
          <Card key={card.label} className={card.wide ? "col-span-2 lg:col-span-4" : undefined}>
            <div className="px-5 py-4">
              <p className="text-xs font-medium uppercase tracking-wide text-slate-500">{card.label}</p>
              <p className="mt-2 text-2xl font-semibold text-slate-900">
                {loading ? <span className="text-slate-300">—</span> : card.value}
              </p>
            </div>
          </Card>
        ))}
      </div>

      <Card className="mt-6">
        <CardHeader
          title="Recent quotations"
          description="Your five most recent quotations."
          action={
            <Link href="/quotations" className="text-sm font-medium text-blue-600 hover:underline">
              View all
            </Link>
          }
        />

        {loading ? (
          <LoadingState />
        ) : recent.length === 0 ? (
          <EmptyState
            title="No quotations yet"
            description="Create your first quotation and download it as a professional PDF."
            action={
              <Link href="/quotations/new">
                <Button>Create Quotation</Button>
              </Link>
            }
          />
        ) : (
          <TableWrap>
            <Table>
              <thead>
                <tr>
                  <Th>Number</Th>
                  <Th>Customer</Th>
                  <Th>Date</Th>
                  <Th>Status</Th>
                  <Th align="right">Total</Th>
                </tr>
              </thead>
              <tbody>
                {recent.map((quotation) => (
                  <tr key={quotation.id} className="hover:bg-slate-50">
                    <Td>
                      <Link
                        href={`/quotations/${quotation.id}`}
                        className="font-medium text-blue-600 hover:underline"
                      >
                        {quotation.quotationNumber}
                      </Link>
                    </Td>
                    <Td>{quotation.customerName}</Td>
                    <Td>{formatDate(quotation.quotationDate)}</Td>
                    <Td>
                      <StatusBadge status={quotation.status} />
                    </Td>
                    <Td align="right" className="font-medium text-slate-900">
                      {formatMoney(quotation.grandTotal, quotation.currency)}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          </TableWrap>
        )}
      </Card>
    </>
  );
}
