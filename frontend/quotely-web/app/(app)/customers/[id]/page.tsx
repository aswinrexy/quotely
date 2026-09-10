"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { StatusBadge } from "@/components/ui/badge";
import { EmptyState, ErrorState, LoadingState } from "@/components/ui/states";
import { Table, TableWrap, Td, Th } from "@/components/ui/table";
import { PageHeader } from "@/components/app/page-header";
import type { Customer, PagedResult, QuotationListItem } from "@/types";

export default function CustomerDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [quotations, setQuotations] = useState<QuotationListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [customerResult, quotationResult] = await Promise.all([
        api.get<Customer>(`/api/customers/${id}`),
        api.get<PagedResult<QuotationListItem>>(`/api/quotations?pageSize=100`),
      ]);
      setCustomer(customerResult);
      setQuotations(quotationResult.items.filter((q) => q.customerId === id));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load the customer.");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) {
    return (
      <Card>
        <LoadingState />
      </Card>
    );
  }

  if (error || !customer) {
    return (
      <Card>
        <ErrorState message={error ?? "Customer not found."} onRetry={load} />
      </Card>
    );
  }

  const details: Array<[string, string]> = [
    ["Company", customer.companyName || "—"],
    ["Email", customer.email || "—"],
    ["Phone", customer.phone || "—"],
    ["Address", [customer.addressLine, customer.city, customer.state, customer.postalCode, customer.country]
      .filter(Boolean)
      .join(", ") || "—"],
    ["Added", formatDate(customer.createdAt)],
  ];

  return (
    <>
      <PageHeader
        title={customer.name}
        description={customer.companyName ?? undefined}
        action={
          <>
            <Link href="/customers">
              <Button variant="secondary">Back</Button>
            </Link>
            <Link href={`/customers/${customer.id}/edit`}>
              <Button variant="secondary">Edit</Button>
            </Link>
            <Link href={`/quotations/new?customerId=${customer.id}`}>
              <Button>Create Quotation</Button>
            </Link>
          </>
        }
      />

      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-1">
          <CardHeader title="Details" />
          <CardBody>
            <dl className="space-y-3">
              {details.map(([label, value]) => (
                <div key={label}>
                  <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">{label}</dt>
                  <dd className="mt-0.5 text-sm text-slate-800">{value}</dd>
                </div>
              ))}
            </dl>
            {customer.notes && (
              <div className="mt-4 rounded-lg bg-slate-50 p-3">
                <p className="text-xs font-medium uppercase tracking-wide text-slate-500">Notes</p>
                <p className="mt-1 whitespace-pre-line text-sm text-slate-700">{customer.notes}</p>
              </div>
            )}
          </CardBody>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader title="Quotations" description="Quotations raised for this customer." />
          {quotations.length === 0 ? (
            <EmptyState
              title="No quotations yet"
              description="Create a quotation for this customer to get started."
              action={
                <Link href={`/quotations/new?customerId=${customer.id}`}>
                  <Button>Create Quotation</Button>
                </Link>
              }
            />
          ) : (
            <TableWrap>
              <Table>
                <thead>
                  <tr>
                    <Th>Number</Th>
                    <Th>Date</Th>
                    <Th>Status</Th>
                    <Th align="right">Total</Th>
                  </tr>
                </thead>
                <tbody>
                  {quotations.map((quotation) => (
                    <tr key={quotation.id} className="hover:bg-slate-50">
                      <Td>
                        <Link
                          href={`/quotations/${quotation.id}`}
                          className="font-medium text-blue-600 hover:underline"
                        >
                          {quotation.quotationNumber}
                        </Link>
                      </Td>
                      <Td>{formatDate(quotation.quotationDate)}</Td>
                      <Td>
                        <StatusBadge status={quotation.status} />
                      </Td>
                      <Td align="right" className="font-medium text-slate-900">
                        {formatMoney(quotation.grandTotal, quotation.currency)}
                      </Td>
                    </tr>
                  ))}
                </tbody>
              </Table>
            </TableWrap>
          )}
        </Card>
      </div>
    </>
  );
}
