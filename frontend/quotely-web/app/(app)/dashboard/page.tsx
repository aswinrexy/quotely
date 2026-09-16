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
import type { DashboardStats, PagedResult, QuotationListItem, Receivables } from "@/types";

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
  const [receivables, setReceivables] = useState<Receivables | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [statsResult, recentResult, receivablesResult] = await Promise.all([
        api.get<DashboardStats>("/api/quotations/stats"),
        api.get<PagedResult<QuotationListItem>>("/api/quotations?page=1&pageSize=5"),
        // Aggregated by the server. This used to fetch a hundred invoices and add them up here,
        // which was both wrong past a hundred and the browser deciding what money means.
        api.get<Receivables>("/api/invoices/stats?needsAttention=5"),
      ]);
      setStats(statsResult);
      setRecent(recentResult.items);
      setReceivables(receivablesResult);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load your dashboard.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const currency = receivables?.currency ?? stats?.currency ?? "INR";
  const firstName = (user?.fullName || "").trim().split(" ")[0];
  const needsAttention = receivables?.needsAttention ?? [];
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
        {/* Money first: what a small business actually opens the app to find out. */}
        <StatCard
          label="Outstanding"
          value={formatMoney(receivables?.totalOutstanding ?? 0, currency)}
          context={
            receivables
              ? receivables.countOutstanding === 0
                ? "Nothing owed"
                : `${receivables.countOutstanding} unpaid ${receivables.countOutstanding === 1 ? "invoice" : "invoices"}`
              : undefined
          }
          accent
          href="/invoices"
          loading={loading}
        />
        <StatCard
          label="Overdue"
          value={formatMoney(receivables?.totalOverdue ?? 0, currency)}
          context={
            receivables
              ? receivables.countOverdue === 0
                ? "Nothing past due"
                : `${receivables.countOverdue} past due`
              : undefined
          }
          href="/invoices?status=Overdue"
          loading={loading}
        />
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
          href="/quotations?status=Accepted"
          loading={loading}
        />
      </div>

      {/* Compact by design: the few invoices worth a phone call today, not a report. */}
      {!loading && needsAttention.length > 0 && (
        <Card className="mt-5">
          <CardHeader
            title="Needs attention"
            description="Unpaid invoices, longest overdue first."
            action={
              <Link
                href="/invoices?status=Overdue"
                className="inline-flex items-center gap-1 text-body font-medium text-electric hover:underline"
              >
                View overdue
                <Icon.chevronRight className="h-3.5 w-3.5" />
              </Link>
            }
          />

          <TableWrap>
            <Table>
              <thead>
                <tr>
                  <Th>Invoice</Th>
                  <Th>Customer</Th>
                  <Th>Due</Th>
                  <Th align="right">Outstanding</Th>
                </tr>
              </thead>
              <tbody>
                {needsAttention.map((invoice) => (
                  <Tr key={invoice.id}>
                    <Td>
                      <Link
                        href={`/invoices/${invoice.id}`}
                        className="font-medium text-electric hover:underline"
                      >
                        <Mono>{invoice.invoiceNumber}</Mono>
                      </Link>
                    </Td>
                    <Td className="text-charcoal">{invoice.customerName}</Td>
                    <Td>
                      {formatDate(invoice.dueDate)}
                      {invoice.isOverdue && (
                        <span className="ml-1.5 text-caption font-medium text-rose-ink">Past due</span>
                      )}
                    </Td>
                    <Td align="right">
                      <Amount>{formatMoney(invoice.outstanding, invoice.currency)}</Amount>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          </TableWrap>

          <MobileList>
            {needsAttention.map((invoice) => (
              <MobileRow key={invoice.id}>
                <div className="flex items-start justify-between gap-3">
                  <Link
                    href={`/invoices/${invoice.id}`}
                    className="font-medium text-electric hover:underline"
                  >
                    <Mono>{invoice.invoiceNumber}</Mono>
                  </Link>
                  <Amount>{formatMoney(invoice.outstanding, invoice.currency)}</Amount>
                </div>
                <MobileFacts
                  items={[
                    { label: "Customer", value: invoice.customerName },
                    {
                      label: "Due",
                      value: invoice.isOverdue ? (
                        <span className="text-rose-ink">{formatDate(invoice.dueDate)}</span>
                      ) : (
                        formatDate(invoice.dueDate)
                      ),
                    },
                  ]}
                />
              </MobileRow>
            ))}
          </MobileList>
        </Card>
      )}

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
