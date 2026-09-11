"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api, saveBlob } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { FilterSelect, SearchInput } from "@/components/ui/field";
import { InvoiceStatusBadge } from "@/components/ui/badge";
import { EmptyState, ErrorState, TableSkeleton } from "@/components/ui/states";
import { Icon } from "@/components/ui/icons";
import {
  Amount,
  MobileFacts,
  MobileList,
  MobileRow,
  Mono,
  Pagination,
  Table,
  TableWrap,
  Td,
  Th,
  Tr,
} from "@/components/ui/table";
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
      setError(err instanceof Error ? err.message : "We couldn't load your invoices.");
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
      toast("PDF downloaded", "success");
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
        description="Bill a customer directly, or raise one from an accepted quotation."
        action={
          <Link href="/invoices/new">
            <Button>
              <Icon.plus className="h-4 w-4" />
              Create invoice
            </Button>
          </Link>
        }
      />

      <Card>
        <div className="flex flex-wrap gap-2 border-b border-ash p-3">
          <SearchInput
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search invoices…"
            aria-label="Search invoices"
            className="min-w-0 flex-1 sm:max-w-xs"
          />
          <FilterSelect
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
            aria-label="Filter by status"
          >
            <option value="">All statuses</option>
            {INVOICE_STATUSES.map((value) => (
              <option key={value} value={value}>
                {INVOICE_STATUS_LABELS[value]}
              </option>
            ))}
          </FilterSelect>
        </div>

        {loading ? (
          <TableSkeleton rows={6} columns={6} />
        ) : error ? (
          <ErrorState message={error} onRetry={load} />
        ) : isEmpty ? (
          <EmptyState
            title={filtered ? "No matching invoices" : "No invoices yet"}
            description={
              filtered
                ? "Try a different search term or status filter."
                : "Create an invoice for a customer, or convert an accepted quotation into one."
            }
            action={
              !filtered && (
                <div className="flex flex-wrap justify-center gap-2">
                  <Link href="/invoices/new">
                    <Button>
                      <Icon.plus className="h-4 w-4" />
                      Create invoice
                    </Button>
                  </Link>
                  <Link href="/quotations?status=Accepted">
                    <Button variant="secondary">View accepted quotations</Button>
                  </Link>
                </div>
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
                    <Th>Issued</Th>
                    <Th>Due</Th>
                    <Th>Status</Th>
                    <Th align="right">Amount</Th>
                    <Th align="right">Actions</Th>
                  </tr>
                </thead>
                <tbody>
                  {result!.items.map((invoice) => (
                    <Tr key={invoice.id}>
                      <Td>
                        <Link
                          href={`/invoices/${invoice.id}`}
                          className="font-medium text-electric hover:underline"
                        >
                          <Mono>{invoice.invoiceNumber}</Mono>
                        </Link>
                        <p className="mt-0.5 text-caption text-fog">
                          {invoice.quotationNumber ? `from ${invoice.quotationNumber}` : "Direct invoice"}
                        </p>
                      </Td>
                      <Td className="text-charcoal">{invoice.customerName}</Td>
                      <Td>{formatDate(invoice.invoiceDate)}</Td>
                      <Td>
                        {formatDate(invoice.dueDate)}
                        {invoice.isOverdue && (
                          <p className="mt-0.5 text-caption font-medium text-rose-ink">Past due</p>
                        )}
                      </Td>
                      <Td>
                        <InvoiceStatusBadge status={invoice.status} />
                      </Td>
                      <Td align="right">
                        <Amount>{formatMoney(invoice.grandTotal, invoice.currency)}</Amount>
                      </Td>
                      <Td align="right">
                        <div className="flex justify-end gap-1">
                          <Button
                            variant="ghost"
                            size="sm"
                            loading={downloading === invoice.id}
                            onClick={() => download(invoice)}
                            aria-label={`Download ${invoice.invoiceNumber} as PDF`}
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
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </TableWrap>

            <MobileList>
              {result!.items.map((invoice) => (
                <MobileRow key={invoice.id}>
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <Link
                        href={`/invoices/${invoice.id}`}
                        className="font-medium text-electric hover:underline"
                      >
                        <Mono>{invoice.invoiceNumber}</Mono>
                      </Link>
                      <p className="mt-0.5 text-caption text-fog">
                        {invoice.quotationNumber ? `from ${invoice.quotationNumber}` : "Direct invoice"}
                      </p>
                    </div>
                    <InvoiceStatusBadge status={invoice.status} />
                  </div>
                  <MobileFacts
                    items={[
                      { label: "Customer", value: invoice.customerName },
                      {
                        label: "Amount",
                        value: <Amount>{formatMoney(invoice.grandTotal, invoice.currency)}</Amount>,
                      },
                      { label: "Issued", value: formatDate(invoice.invoiceDate) },
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
                  <div className="mt-3 flex flex-wrap gap-2">
                    <Button
                      variant="secondary"
                      size="sm"
                      loading={downloading === invoice.id}
                      onClick={() => download(invoice)}
                    >
                      <Icon.download className="h-3.5 w-3.5" />
                      PDF
                    </Button>
                    <Link href={`/invoices/${invoice.id}`}>
                      <Button variant="secondary" size="sm">
                        View
                      </Button>
                    </Link>
                  </div>
                </MobileRow>
              ))}
            </MobileList>

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
