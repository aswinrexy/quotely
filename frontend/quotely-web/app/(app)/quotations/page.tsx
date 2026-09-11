"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { api, saveBlob } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { FilterSelect, SearchInput } from "@/components/ui/field";
import { StatusBadge } from "@/components/ui/badge";
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
import { QUOTATION_STATUSES, type PagedResult, type QuotationListItem } from "@/types";

const PAGE_SIZE = 10;

export default function QuotationsPage() {
  const toast = useToast();
  const params = useSearchParams();
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [status, setStatus] = useState(params.get("status") ?? "");
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<QuotationListItem> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<QuotationListItem | null>(null);
  const [deleting, setDeleting] = useState(false);
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
      setResult(await api.get<PagedResult<QuotationListItem>>(`/api/quotations?${query}`));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load your quotations.");
    } finally {
      setLoading(false);
    }
  }, [page, debounced, status]);

  useEffect(() => {
    void load();
  }, [load]);

  async function download(quotation: QuotationListItem) {
    setDownloading(quotation.id);
    try {
      const { blob, fileName } = await api.downloadPdf(quotation.id);
      saveBlob(blob, fileName);
      toast("PDF downloaded", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not generate the PDF.", "error");
    } finally {
      setDownloading(null);
    }
  }

  async function confirmDelete() {
    if (!pendingDelete) return;
    setDeleting(true);
    try {
      await api.delete(`/api/quotations/${pendingDelete.id}`);
      toast("Quotation deleted", "success");
      setPendingDelete(null);
      await load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not delete the quotation.", "error");
    } finally {
      setDeleting(false);
    }
  }

  const isEmpty = !loading && !error && (result?.items.length ?? 0) === 0;
  const filtered = Boolean(debounced || status);

  return (
    <>
      <PageHeader
        title="Quotations"
        description="Create and manage customer quotations."
        action={
          <Link href="/quotations/new">
            <Button>
              <Icon.plus className="h-4 w-4" />
              New quotation
            </Button>
          </Link>
        }
      />

      <Card>
        <div className="flex flex-wrap gap-2 border-b border-ash p-3">
          <SearchInput
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search quotations…"
            aria-label="Search quotations"
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
            {QUOTATION_STATUSES.map((value) => (
              <option key={value} value={value}>
                {value}
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
            title={filtered ? "No matching quotations" : "No quotations yet"}
            description={
              filtered
                ? "Try a different search term or status filter."
                : "Create your first quotation and send it to a customer in a few clicks."
            }
            action={
              !filtered && (
                <Link href="/quotations/new">
                  <Button>Create quotation</Button>
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
                    <Th>Quotation</Th>
                    <Th>Customer</Th>
                    <Th>Date</Th>
                    <Th>Valid until</Th>
                    <Th>Status</Th>
                    <Th align="right">Amount</Th>
                    <Th align="right">Actions</Th>
                  </tr>
                </thead>
                <tbody>
                  {result!.items.map((quotation) => (
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
                      <Td>{formatDate(quotation.validUntil)}</Td>
                      <Td>
                        <StatusBadge status={quotation.status} />
                      </Td>
                      <Td align="right">
                        <Amount>{formatMoney(quotation.grandTotal, quotation.currency)}</Amount>
                      </Td>
                      <Td align="right">
                        <div className="flex justify-end gap-1">
                          <Button
                            variant="ghost"
                            size="sm"
                            loading={downloading === quotation.id}
                            onClick={() => download(quotation)}
                            aria-label={`Download ${quotation.quotationNumber} as PDF`}
                          >
                            PDF
                          </Button>
                          <Link href={`/quotations/${quotation.id}/edit`}>
                            <Button variant="ghost" size="sm">
                              Edit
                            </Button>
                          </Link>
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setPendingDelete(quotation)}
                            aria-label={`Delete ${quotation.quotationNumber}`}
                          >
                            Delete
                          </Button>
                        </div>
                      </Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </TableWrap>

            <MobileList>
              {result!.items.map((quotation) => (
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
                      { label: "Date", value: formatDate(quotation.quotationDate) },
                      { label: "Valid until", value: formatDate(quotation.validUntil) },
                    ]}
                  />
                  <div className="mt-3 flex flex-wrap gap-2">
                    <Button
                      variant="secondary"
                      size="sm"
                      loading={downloading === quotation.id}
                      onClick={() => download(quotation)}
                    >
                      <Icon.download className="h-3.5 w-3.5" />
                      PDF
                    </Button>
                    <Link href={`/quotations/${quotation.id}/edit`}>
                      <Button variant="secondary" size="sm">
                        Edit
                      </Button>
                    </Link>
                    <Button variant="ghost" size="sm" onClick={() => setPendingDelete(quotation)}>
                      Delete
                    </Button>
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

      <ConfirmDialog
        open={pendingDelete !== null}
        title="Delete quotation"
        description={`${pendingDelete?.quotationNumber} will be permanently deleted. This cannot be undone.`}
        confirmLabel="Delete quotation"
        loading={deleting}
        onConfirm={confirmDelete}
        onCancel={() => setPendingDelete(null)}
      />
    </>
  );
}
