"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/field";
import { EmptyState, ErrorState, LoadingState } from "@/components/ui/states";
import { Pagination, Table, TableWrap, Td, Th } from "@/components/ui/table";
import { PageHeader } from "@/components/app/page-header";
import type { Customer, PagedResult } from "@/types";

const PAGE_SIZE = 10;

export default function CustomersPage() {
  const toast = useToast();
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<Customer> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<Customer | null>(null);
  const [deleting, setDeleting] = useState(false);

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
      setResult(await api.get<PagedResult<Customer>>(`/api/customers?${query}`));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load customers.");
    } finally {
      setLoading(false);
    }
  }, [page, debounced]);

  useEffect(() => {
    void load();
  }, [load]);

  async function confirmDelete() {
    if (!pendingDelete) return;
    setDeleting(true);
    try {
      await api.delete(`/api/customers/${pendingDelete.id}`);
      toast("Customer deleted.", "success");
      setPendingDelete(null);
      await load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not delete the customer.", "error");
    } finally {
      setDeleting(false);
    }
  }

  const isEmpty = !loading && !error && (result?.items.length ?? 0) === 0;

  return (
    <>
      <PageHeader
        title="Customers"
        description="People and companies you send quotations to."
        action={
          <Link href="/customers/new">
            <Button>New customer</Button>
          </Link>
        }
      />

      <Card>
        <div className="border-b border-slate-200 px-4 py-3">
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name, company, email or phone"
            aria-label="Search customers"
            className="sm:max-w-sm"
          />
        </div>

        {loading ? (
          <LoadingState />
        ) : error ? (
          <ErrorState message={error} onRetry={load} />
        ) : isEmpty ? (
          <EmptyState
            title={debounced ? "No matching customers" : "No customers yet"}
            description={
              debounced
                ? "Try a different search term."
                : "Add your first customer so you can start quoting."
            }
            action={
              !debounced && (
                <Link href="/customers/new">
                  <Button>New customer</Button>
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
                    <Th>Name</Th>
                    <Th>Company</Th>
                    <Th>Email</Th>
                    <Th>Phone</Th>
                    <Th align="right">Actions</Th>
                  </tr>
                </thead>
                <tbody>
                  {result!.items.map((customer) => (
                    <tr key={customer.id} className="hover:bg-slate-50">
                      <Td>
                        <Link
                          href={`/customers/${customer.id}`}
                          className="font-medium text-blue-600 hover:underline"
                        >
                          {customer.name}
                        </Link>
                      </Td>
                      <Td>{customer.companyName || "—"}</Td>
                      <Td>{customer.email || "—"}</Td>
                      <Td>{customer.phone || "—"}</Td>
                      <Td align="right">
                        <div className="flex justify-end gap-1">
                          <Link href={`/customers/${customer.id}/edit`}>
                            <Button variant="ghost" size="sm">
                              Edit
                            </Button>
                          </Link>
                          <Button variant="ghost" size="sm" onClick={() => setPendingDelete(customer)}>
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
        title="Delete customer"
        description={`Delete ${pendingDelete?.name}? This cannot be undone.`}
        loading={deleting}
        onConfirm={confirmDelete}
        onCancel={() => setPendingDelete(null)}
      />
    </>
  );
}
