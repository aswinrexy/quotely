"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatDate, formatMoney } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Card, CardHeader } from "@/components/ui/card";
import { StatCard } from "@/components/ui/stat-card";
import { StatusBadge } from "@/components/ui/badge";
import { EmptyState, ErrorState, TableSkeleton } from "@/components/ui/states";
import { Icon } from "@/components/ui/icons";
import {
  Amount,
  MobileFacts,
  MobileList,
  MobileRow,
  Mono,
  Table,
  TableWrap,
  Td,
  Th,
  Tr,
} from "@/components/ui/table";
import type { DashboardStats, InvoiceListItem, PagedResult, QuotationListItem } from "@/types";

function greeting(date = new Date()) {
  const hour = date.getHours();
  if (hour < 12) return "Good morning";
  if (hour < 18) return "Good afternoon";
  return "Good evening";
}

export default function DashboardPage() {
  const { user } = useAuth();
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [recent, setRecent] = useState<QuotationListItem[]>([]);
  const [invoices, setInvoices] = useState<InvoiceListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [statsResult, recentResult, invoiceResult] = await Promise.all([
        api.get<DashboardStats>("/api/quotations/stats"),
        api.get<PagedResult<QuotationListItem>>("/api/quotations?page=1&pageSize=5"),
        api.get<PagedResult<InvoiceListItem>>("/api/invoices?page=1&pageSize=100"),
      ]);
      setStats(statsResult);
      setRecent(recentResult.items);
      setInvoices(invoiceResult.items);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load your dashboard.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const currency = stats?.currency ?? "INR";
  const firstName = (user?.fullName || "").trim().split(" ")[0];

  // Invoice figures are derived here rather than added to the API: the dashboard is the only
  // caller that needs them, and the existing list endpoint already returns what they require.
  const invoicedTotal = invoices.reduce((sum, invoice) => sum + invoice.grandTotal, 0);
  const paidTotal = invoices
    .filter((invoice) => invoice.status === "Paid")
    .reduce((sum, invoice) => sum + invoice.grandTotal, 0);
  const winRate =
    stats && stats.totalQuotations > 0
      ? Math.round((stats.acceptedCount / stats.totalQuotations) * 100)
      : 0;

  return (
    <>
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <h1 className="font-display text-heading-sm text-charcoal sm:text-heading">
            {greeting()}
            {firstName ? `, ${firstName}` : ""}
          </h1>
          <p className="mt-1 text-body text-fog">A quick overview of your business.</p>
        </div>
        <Link href="/quotations/new">
          <Button>
            <Icon.plus className="h-4 w-4" />
            New quotation
          </Button>
        </Link>
      </div>

      {error && (
        <Card className="mb-6">
          <ErrorState message={error} onRetry={load} />
        </Card>
      )}

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StatCard
          label="Quotations"
          value={stats ? String(stats.totalQuotations) : "0"}
          context={stats ? `${stats.draftCount} draft · ${stats.sentCount} sent` : undefined}
          href="/quotations"
          loading={loading}
        />
        <StatCard
          label="Accepted"
          value={stats ? String(stats.acceptedCount) : "0"}
          context={stats && stats.totalQuotations > 0 ? `${winRate}% win rate` : "No quotations yet"}
          accent
          href="/quotations?status=Accepted"
          loading={loading}
        />
        <StatCard
          label="Quoted value"
          value={formatMoney(stats?.totalValue ?? 0, currency)}
          context="Across all quotations"
          loading={loading}
        />
        <StatCard
          label="Invoiced"
          value={formatMoney(invoicedTotal, currency)}
          context={`${formatMoney(paidTotal, currency)} paid`}
          href="/invoices"
          loading={loading}
        />
      </div>

      <Card className="mt-5">
        <CardHeader
          title="Recent quotations"
          description="Your five most recent quotations."
          action={
            <Link
              href="/quotations"
              className="inline-flex items-center gap-1 text-body font-medium text-electric hover:underline"
            >
              View all
              <Icon.chevronRight className="h-3.5 w-3.5" />
            </Link>
          }
        />

        {loading ? (
          <TableSkeleton rows={4} columns={5} />
        ) : recent.length === 0 ? (
          <EmptyState
            title="No quotations yet"
            description="Create your first quotation and send it to a customer in a few clicks."
            action={
              <Link href="/quotations/new">
                <Button>Create quotation</Button>
              </Link>
            }
          />
        ) : (
          <>
            <TableWrap>
              <Table>
                <thead>
                  <tr>
                    <Th>Quotation</Th>
                    <Th>Customer</Th>
                    <Th>Date</Th>
                    <Th>Status</Th>
                    <Th align="right">Amount</Th>
                  </tr>
                </thead>
                <tbody>
                  {recent.map((quotation) => (
                    <Tr key={quotation.id}>
                      <Td>
                        <Link
                          href={`/quotations/${quotation.id}`}
                          className="font-medium text-electric hover:underline"
                        >
                          <Mono>{quotation.quotationNumber}</Mono>
                        </Link>
                      </Td>
                      <Td className="text-charcoal">{quotation.customerName}</Td>
                      <Td>{formatDate(quotation.quotationDate)}</Td>
                      <Td>
                        <StatusBadge status={quotation.status} />
                      </Td>
                      <Td align="right">
                        <Amount>{formatMoney(quotation.grandTotal, quotation.currency)}</Amount>
                      </Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </TableWrap>

            <MobileList>
              {recent.map((quotation) => (
                <MobileRow key={quotation.id}>
                  <div className="flex items-start justify-between gap-3">
                    <Link
                      href={`/quotations/${quotation.id}`}
                      className="font-medium text-electric hover:underline"
                    >
                      <Mono>{quotation.quotationNumber}</Mono>
                    </Link>
                    <StatusBadge status={quotation.status} />
                  </div>
                  <MobileFacts
                    items={[
                      { label: "Customer", value: quotation.customerName },
                      {
                        label: "Amount",
                        value: <Amount>{formatMoney(quotation.grandTotal, quotation.currency)}</Amount>,
                      },
                    ]}
                  />
                </MobileRow>
              ))}
            </MobileList>
          </>
        )}
      </Card>
    </>
  );
}
