"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { formatDate } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { SearchInput } from "@/components/ui/field";
import { EmptyState, ErrorState, TableSkeleton } from "@/components/ui/states";
import { Icon } from "@/components/ui/icons";
import {
  MobileFacts,
  MobileList,
  MobileRow,
  Pagination,
  Table,
  TableWrap,
  Td,
  Th,
  Tr,
} from "@/components/ui/table";
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
      setError(err instanceof Error ? err.message : "We couldn't load your customers.");
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
      toast("Customer deleted", "success");
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
        description="Manage your customers and their quotation history."
        action={
          <Link href="/customers/new">
            <Button>
              <Icon.plus className="h-4 w-4" />
              Add customer
            </Button>
          </Link>
        }
      />

      <Card>
        <div className="border-b border-ash p-3">
          <SearchInput
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name, company, email or phone…"
            aria-label="Search customers"
            className="sm:max-w-sm"
          />
        </div>

        {loading ? (
          <TableSkeleton rows={6} columns={5} />
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
                  <Button>Add customer</Button>
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
                    <Th>Customer</Th>
                    <Th>Company</Th>
                    <Th>Email</Th>
                    <Th>Phone</Th>
                    <Th>Added</Th>
                    <Th align="right">Actions</Th>
                  </tr>
                </thead>
                <tbody>
                  {result!.items.map((customer) => (
                    <Tr key={customer.id}>
                      <Td>
                        <Link
                          href={`/customers/${customer.id}`}
                          className="font-medium text-electric hover:underline"
                        >
                          {customer.name}
                        </Link>
                      </Td>
                      <Td>{customer.companyName || "—"}</Td>
                      <Td className="break-all">{customer.email || "—"}</Td>
                      <Td>{customer.phone || "—"}</Td>
                      <Td>{formatDate(customer.createdAt)}</Td>
                      <Td align="right">
                        <div className="flex justify-end gap-1">
                          <Link href={`/customers/${customer.id}/edit`}>
                            <Button variant="ghost" size="sm">
                              Edit
                            </Button>
                          </Link>
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setPendingDelete(customer)}
                            aria-label={`Delete ${customer.name}`}
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
              {result!.items.map((customer) => (
                <MobileRow key={customer.id}>
                  <Link
                    href={`/customers/${customer.id}`}
                    className="font-medium text-electric hover:underline"
                  >
                    {customer.name}
                  </Link>
                  <MobileFacts
                    items={[
                      { label: "Company", value: customer.companyName || "—" },
                      { label: "Phone", value: customer.phone || "—" },
                      { label: "Email", value: customer.email || "—" },
                      { label: "Added", value: formatDate(customer.createdAt) },
                    ]}
                  />
                  <div className="mt-3 flex flex-wrap gap-2">
                    <Link href={`/customers/${customer.id}/edit`}>
                      <Button variant="secondary" size="sm">
                        Edit
                      </Button>
                    </Link>
                    <Button variant="ghost" size="sm" onClick={() => setPendingDelete(customer)}>
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
        title="Delete customer"
        description={`${pendingDelete?.name} will be permanently deleted. This cannot be undone.`}
        confirmLabel="Delete customer"
        loading={deleting}
        onConfirm={confirmDelete}
        onCancel={() => setPendingDelete(null)}
      />
    </>
  );
}
