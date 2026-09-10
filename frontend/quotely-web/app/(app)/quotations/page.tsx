"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api, saveBlob } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { Input, Select } from "@/components/ui/field";
import { StatusBadge } from "@/components/ui/badge";
import { EmptyState, ErrorState, LoadingState } from "@/components/ui/states";
import { Pagination, Table, TableWrap, Td, Th } from "@/components/ui/table";
import { PageHeader } from "@/components/app/page-header";
import { QUOTATION_STATUSES, type PagedResult, type QuotationListItem } from "@/types";

const PAGE_SIZE = 10;

export default function QuotationsPage() {
  const toast = useToast();
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [status, setStatus] = useState("");
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
      setError(err instanceof Error ? err.message : "Could not load quotations.");
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
      toast("PDF downloaded.", "success");
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
      toast("Quotation deleted.", "success");
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
        description="Every quotation you have raised."
        action={
          <Link href="/quotations/new">
            <Button>Create Quotation</Button>
          </Link>
        }
      />

      <Card>
        <div className="flex flex-wrap gap-3 border-b border-slate-200 px-4 py-3">
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by number or customer"
            aria-label="Search quotations"
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
            {QUOTATION_STATUSES.map((value) => (
              <option key={value} value={value}>
                {value}
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
            title={filtered ? "No matching quotations" : "No quotations yet"}
            description={
              filtered
                ? "Try a different search or status filter."
                : "Create your first quotation and download it as a professional PDF."
            }
            action={
              !filtered && (
                <Link href="/quotations/new">
                  <Button>Create Quotation</Button>
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
                    <Th>Number</Th>
                    <Th>Customer</Th>
                    <Th>Date</Th>
                    <Th>Valid until</Th>
                    <Th>Status</Th>
                    <Th align="right">Total</Th>
                    <Th align="right">Actions</Th>
                  </tr>
                </thead>
                <tbody>
                  {result!.items.map((quotation) => (
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
                      <Td>{formatDate(quotation.validUntil)}</Td>
                      <Td>
                        <StatusBadge status={quotation.status} />
                        {quotation.respondedAt && (
                          <p className="mt-1 text-xs text-slate-500">{formatDate(quotation.respondedAt)}</p>
                        )}
                      </Td>
                      <Td align="right" className="font-medium text-slate-900">
                        {formatMoney(quotation.grandTotal, quotation.currency)}
                      </Td>
                      <Td align="right">
                        <div className="flex justify-end gap-1">
                          <Button
                            variant="ghost"
                            size="sm"
                            loading={downloading === quotation.id}
                            onClick={() => download(quotation)}
                          >
                            PDF
                          </Button>
                          <Link href={`/quotations/${quotation.id}/edit`}>
                            <Button variant="ghost" size="sm">
                              Edit
                            </Button>
                          </Link>
                          <Button variant="ghost" size="sm" onClick={() => setPendingDelete(quotation)}>
                            Delete
                          </Button>
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

      <ConfirmDialog
        open={pendingDelete !== null}
        title="Delete quotation"
        description={`Delete ${pendingDelete?.quotationNumber}? This cannot be undone.`}
        loading={deleting}
        onConfirm={confirmDelete}
        onCancel={() => setPendingDelete(null)}
      />
    </>
  );
}
