"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { SearchInput } from "@/components/ui/field";
import { EmptyState, ErrorState, TableSkeleton } from "@/components/ui/states";
import { Icon } from "@/components/ui/icons";
import {
  Amount,
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
import type { BusinessProfile, PagedResult, Product } from "@/types";

const PAGE_SIZE = 20;

export default function ProductsPage() {
  const toast = useToast();
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<Product> | null>(null);
  const [currency, setCurrency] = useState("INR");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<Product | null>(null);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebounced(search.trim());
      setPage(1);
    }, 300);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    api
      .get<BusinessProfile>("/api/business-profile")
      .then((profile) => setCurrency(profile.currency || "INR"))
      .catch(() => setCurrency("INR"));
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const query = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
      if (debounced) query.set("search", debounced);
      setResult(await api.get<PagedResult<Product>>(`/api/products?${query}`));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load products.");
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
      await api.delete(`/api/products/${pendingDelete.id}`);
      toast("Item deleted", "success");
      setPendingDelete(null);
      await load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not delete the product.", "error");
    } finally {
      setDeleting(false);
    }
  }

  const isEmpty = !loading && !error && (result?.items.length ?? 0) === 0;

  return (
    <>
      <PageHeader
        title="Products & Services"
        description="Build your reusable pricing catalogue."
        action={
          <Link href="/products/new">
            <Button>
              <Icon.plus className="h-4 w-4" />
              Add item
            </Button>
          </Link>
        }
      />

      <Card>
        <div className="border-b border-ash p-3">
          <SearchInput
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search products and services…"
            aria-label="Search products and services"
            className="sm:max-w-sm"
          />
        </div>

        {loading ? (
          <TableSkeleton rows={6} columns={5} />
        ) : error ? (
          <ErrorState message={error} onRetry={load} />
        ) : isEmpty ? (
          <EmptyState
            title={debounced ? "No matching items" : "No products or services yet"}
            description={
              debounced
                ? "Try a different search term."
                : "Add the work you do most often so quoting takes seconds."
            }
            action={
              !debounced && (
                <Link href="/products/new">
                  <Button>Add item</Button>
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
                    <Th>Unit</Th>
                    <Th align="right">Price</Th>
                    <Th align="right">Tax</Th>
                    <Th align="right">Actions</Th>
                  </tr>
                </thead>
                <tbody>
                  {result!.items.map((product) => (
                    <Tr key={product.id}>
                      <Td>
                        <p className="font-medium text-charcoal">{product.name}</p>
                        {product.description && (
                          <p className="mt-0.5 line-clamp-1 text-caption text-fog">{product.description}</p>
                        )}
                      </Td>
                      <Td>{product.unit}</Td>
                      <Td align="right">
                        <Amount>{formatMoney(product.price, currency)}</Amount>
                      </Td>
                      <Td align="right" className="tabular-nums">
                        {product.taxRate}%
                      </Td>
                      <Td align="right">
                        <div className="flex justify-end gap-1">
                          <Link href={`/products/${product.id}/edit`}>
                            <Button variant="ghost" size="sm">
                              Edit
                            </Button>
                          </Link>
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setPendingDelete(product)}
                            aria-label={`Delete ${product.name}`}
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
              {result!.items.map((product) => (
                <MobileRow key={product.id}>
                  <p className="font-medium text-charcoal">{product.name}</p>
                  {product.description && (
                    <p className="mt-0.5 text-caption text-fog">{product.description}</p>
                  )}
                  <MobileFacts
                    items={[
                      {
                        label: "Price",
                        value: <Amount>{formatMoney(product.price, currency)}</Amount>,
                      },
                      { label: "Unit", value: product.unit },
                      { label: "Tax", value: `${product.taxRate}%` },
                    ]}
                  />
                  <div className="mt-3 flex flex-wrap gap-2">
                    <Link href={`/products/${product.id}/edit`}>
                      <Button variant="secondary" size="sm">
                        Edit
                      </Button>
                    </Link>
                    <Button variant="ghost" size="sm" onClick={() => setPendingDelete(product)}>
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
        title="Delete item"
        description={`${pendingDelete?.name} will be removed from your catalogue. Existing quotations and invoices keep their saved line items.`}
        confirmLabel="Delete item"
        loading={deleting}
        onConfirm={confirmDelete}
        onCancel={() => setPendingDelete(null)}
      />
    </>
  );
}
