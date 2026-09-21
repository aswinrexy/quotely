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
  TRADE_DOCUMENT_TYPES,
  TRADE_DOCUMENT_TYPE_LABELS,
  TRADE_TYPES,
  type PagedResult,
  type TradeInvoiceListItem,
} from "@/types";

const PAGE_SIZE = 10;

export default function ImportExportPage() {
  const toast = useToast();
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [tradeType, setTradeType] = useState("");
  const [documentType, setDocumentType] = useState("");
  const [status, setStatus] = useState("");
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<TradeInvoiceListItem> | null>(null);
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
      if (tradeType) query.set("tradeType", tradeType);
      if (documentType) query.set("documentType", documentType);
      if (status) query.set("status", status);
      setResult(
        await api.get<PagedResult<TradeInvoiceListItem>>(`/api/import-export/invoices?${query}`),
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "We couldn't load your documents.");
    } finally {
      setLoading(false);
    }
  }, [page, debounced, tradeType, documentType, status]);

  useEffect(() => {
    void load();
  }, [load]);

  async function download(item: TradeInvoiceListItem) {
    setDownloading(item.id);
    try {
      const { blob, fileName } = await api.downloadTradeInvoicePdf(item.id);
      saveBlob(blob, fileName);
      toast("PDF downloaded", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not generate the PDF.", "error");
    } finally {
      setDownloading(null);
    }
  }

  const isEmpty = !loading && !error && (result?.items.length ?? 0) === 0;
  const filtered = Boolean(debounced || tradeType || documentType || status);

  return (
    <>
      <PageHeader
        title="Import / Export"
        description="Create professional proforma and commercial invoices for international trade."
        action={
          <Link href="/import-export/new">
            <Button>
              <Icon.plus className="h-4 w-4" />
              Create document
            </Button>
          </Link>
        }
      />

      <Card>
        <div className="flex flex-wrap gap-2 border-b border-ash p-3">
          <SearchInput
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by number, consignee or destination…"
            aria-label="Search documents"
            className="min-w-0 flex-1 sm:max-w-xs"
          />
          <FilterSelect
            value={tradeType}
            onChange={(e) => {
              setTradeType(e.target.value);
              setPage(1);
            }}
            aria-label="Filter by trade type"
          >
            <option value="">Export & import</option>
            {TRADE_TYPES.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </FilterSelect>
          <FilterSelect
            value={documentType}
            onChange={(e) => {
              setDocumentType(e.target.value);
              setPage(1);
            }}
            aria-label="Filter by document type"
          >
            <option value="">All documents</option>
            {TRADE_DOCUMENT_TYPES.map((value) => (
              <option key={value} value={value}>
                {TRADE_DOCUMENT_TYPE_LABELS[value]}
              </option>
            ))}
          </FilterSelect>
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
          <TableSkeleton rows={6} columns={7} />
        ) : error ? (
          <ErrorState message={error} onRetry={load} />
        ) : isEmpty ? (
          <EmptyState
            title={filtered ? "No matching documents" : "No import / export documents yet"}
            description={
              filtered
                ? "Try a different search term or filter."
                : "Build a proforma or commercial invoice with the shipping, weight and HS code detail international trade needs."
            }
            action={
              !filtered && (
                <div className="flex flex-wrap justify-center gap-2">
                  <Link href="/import-export/new">
                    <Button>
                      <Icon.plus className="h-4 w-4" />
                      Create document
                    </Button>
                  </Link>
                  <Link href="/settings/trade">
                    <Button variant="secondary">Set up IEC, GST & APEDA</Button>
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
                    <Th>Document</Th>
                    <Th>Consignee</Th>
                    <Th>Destination</Th>
                    <Th>Date</Th>
                    <Th>Status</Th>
                    <Th align="right">Packages</Th>
                    <Th align="right">Amount</Th>
                    <Th align="right">Actions</Th>
                  </tr>
                </thead>
                <tbody>
                  {result!.items.map((item) => (
                    <Tr key={item.id}>
                      <Td>
                        <Link
                          href={`/import-export/${item.id}`}
                          className="font-medium text-electric hover:underline"
                        >
                          <Mono>{item.documentNumber}</Mono>
                        </Link>
                        <p className="mt-0.5 text-caption text-fog">
                          {item.tradeType} · {TRADE_DOCUMENT_TYPE_LABELS[item.documentType]}
                        </p>
                      </Td>
                      <Td className="text-charcoal">{item.consigneeName}</Td>
                      <Td>{item.finalDestination || "—"}</Td>
                      <Td>{formatDate(item.invoiceDate)}</Td>
                      <Td>
                        <InvoiceStatusBadge status={item.status} />
                      </Td>
                      <Td align="right">{item.totalPackages || "—"}</Td>
                      <Td align="right" className="font-medium text-charcoal">
                        {formatMoney(item.grandTotal, item.currency)}
                      </Td>
                      <Td align="right">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => download(item)}
                          disabled={downloading === item.id}
                        >
                          <Icon.download className="h-4 w-4" />
                          {downloading === item.id ? "…" : "PDF"}
                        </Button>
                      </Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </TableWrap>

            <MobileList>
              {result!.items.map((item) => (
                <MobileRow key={item.id}>
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <Link
                        href={`/import-export/${item.id}`}
                        className="font-medium text-electric hover:underline"
                      >
                        <Mono>{item.documentNumber}</Mono>
                      </Link>
                      <p className="mt-0.5 truncate text-body text-charcoal">{item.consigneeName}</p>
                    </div>
                    <InvoiceStatusBadge status={item.status} />
                  </div>
                  <MobileFacts
                    items={[
                      {
                        label: "Type",
                        value: `${item.tradeType} · ${TRADE_DOCUMENT_TYPE_LABELS[item.documentType]}`,
                      },
                      { label: "Destination", value: item.finalDestination || "—" },
                      { label: "Date", value: formatDate(item.invoiceDate) },
                      { label: "Amount", value: formatMoney(item.grandTotal, item.currency) },
                    ]}
                  />
                  <div className="mt-3 flex flex-wrap gap-2">
                    <Button
                      variant="secondary"
                      size="sm"
                      loading={downloading === item.id}
                      onClick={() => download(item)}
                    >
                      <Icon.download className="h-3.5 w-3.5" />
                      PDF
                    </Button>
                    <Link href={`/import-export/${item.id}`}>
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
