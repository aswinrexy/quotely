"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api, saveBlob } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Input, Select } from "@/components/ui/field";
import { InvoiceStatusBadge } from "@/components/ui/badge";
import { EmptyState, ErrorState, LoadingState } from "@/components/ui/states";
import { Pagination, Table, TableWrap, Td, Th } from "@/components/ui/table";
import { PageHeader } from "@/components/app/page-header";
import {
  INVOICE_STATUSES,
  INVOICE_STATUS_LABELS,
  type InvoiceListItem,
  type PagedResult,
} from "@/types";

const PAGE_SIZE = 10;

export default function InvoicesPage() {
  const toast = useToast();
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [status, setStatus] = useState("");
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<InvoiceListItem> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState<string | null>(null);

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebounced(search.trim());
      setPage(1);
    }, 300);
    return () => clearTimeout(timer);
  }, [search]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const query = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
      if (debounced) query.set("search", debounced);
      if (status) query.set("status", status);
      setResult(await api.get<PagedResult<InvoiceListItem>>(`/api/invoices?${query}`));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load invoices.");
    } finally {
      setLoading(false);
    }
  }, [page, debounced, status]);

  useEffect(() => {
    void load();
  }, [load]);

  async function download(invoice: InvoiceListItem) {
    setDownloading(invoice.id);
    try {
      const { blob, fileName } = await api.downloadInvoicePdf(invoice.id);
      saveBlob(blob, fileName);
      toast("PDF downloaded.", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not generate the PDF.", "error");
    } finally {
      setDownloading(null);
    }
  }

  const isEmpty = !loading && !error && (result?.items.length ?? 0) === 0;
  const filtered = Boolean(debounced || status);

  return (
    <>
      <PageHeader
        title="Invoices"
        description="Raised from accepted quotations."
      />

      <Card>
        <div className="flex flex-wrap gap-3 border-b border-slate-200 px-4 py-3">
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by number or customer"
            aria-label="Search invoices"
            className="sm:max-w-xs"
          />
          <Select
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
            aria-label="Filter by status"
            className="sm:max-w-40"
          >
            <option value="">All statuses</option>
            {INVOICE_STATUSES.map((value) => (
              <option key={value} value={value}>
                {INVOICE_STATUS_LABELS[value]}
              </option>
            ))}
          </Select>
        </div>

        {loading ? (
          <LoadingState />
        ) : error ? (
          <ErrorState message={error} onRetry={load} />
        ) : isEmpty ? (
          <EmptyState
            title={filtered ? "No matching invoices" : "No invoices yet"}
            description={
              filtered
                ? "Try a different search or status filter."
                : "Accept a quotation, then convert it into an invoice from the quotation page."
            }
            action={
              !filtered && (
                <Link href="/quotations?status=Accepted">
                  <Button variant="secondary">Go to quotations</Button>
                </Link>
              )
            }
          />
        ) : (
          <>
            <TableWrap>
              <Table>
                <thead>
                  <tr>
                    <Th>Invoice</Th>
                    <Th>Customer</Th>
                    <Th>Date</Th>
                    <Th>Due date</Th>
                    <Th>Status</Th>
                    <Th align="right">Amount</Th>
                    <Th align="right">Actions</Th>
                  </tr>
                </thead>
                <tbody>
                  {result!.items.map((invoice) => (
                    <tr key={invoice.id} className="hover:bg-slate-50">
                      <Td>
                        <Link
                          href={`/invoices/${invoice.id}`}
                          className="font-medium text-blue-600 hover:underline"
                        >
                          {invoice.invoiceNumber}
                        </Link>
                        <p className="mt-0.5 text-xs text-slate-500">from {invoice.quotationNumber}</p>
                      </Td>
                      <Td>{invoice.customerName}</Td>
                      <Td>{formatDate(invoice.invoiceDate)}</Td>
                      <Td>
                        {formatDate(invoice.dueDate)}
                        {invoice.isOverdue && (
                          <p className="mt-0.5 text-xs font-medium text-red-600">Past due</p>
                        )}
                      </Td>
                      <Td>
                        <InvoiceStatusBadge status={invoice.status} />
                      </Td>
                      <Td align="right" className="font-medium text-slate-900">
                        {formatMoney(invoice.grandTotal, invoice.currency)}
                      </Td>
                      <Td align="right">
                        <div className="flex justify-end gap-1">
                          <Button
                            variant="ghost"
                            size="sm"
                            loading={downloading === invoice.id}
                            onClick={() => download(invoice)}
                          >
                            PDF
                          </Button>
                          <Link href={`/invoices/${invoice.id}`}>
                            <Button variant="ghost" size="sm">
                              View
                            </Button>
                          </Link>
                        </div>
                      </Td>
                    </tr>
                  ))}
                </tbody>
              </Table>
            </TableWrap>

            <Pagination
              page={result!.page}
              totalPages={result!.totalPages}
              totalCount={result!.totalCount}
              onChange={setPage}
            />
          </>
        )}
      </Card>
    </>
  );
}
