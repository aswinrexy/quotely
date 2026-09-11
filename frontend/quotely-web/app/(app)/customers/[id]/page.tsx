"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { StatusBadge } from "@/components/ui/badge";
import { DetailSkeleton, EmptyState, ErrorState } from "@/components/ui/states";
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
import { PageHeader } from "@/components/app/page-header";
import { Icon } from "@/components/ui/icons";
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
        <DetailSkeleton />
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
      <Link
        href="/customers"
        className="mb-2 inline-flex items-center gap-1 text-body text-fog transition-colors duration-150 ease-out hover:text-charcoal"
      >
        <Icon.chevronLeft className="h-3.5 w-3.5" />
        Customers
      </Link>

      <PageHeader
        title={customer.name}
        description={customer.companyName ?? undefined}
        action={
          <>
            <Link href={`/customers/${customer.id}/edit`}>
              <Button variant="secondary">Edit</Button>
            </Link>
            <Link href={`/quotations/new?customerId=${customer.id}`}>
              <Button>New quotation</Button>
            </Link>
          </>
        }
      />

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-1">
          <CardHeader title="Details" />
          <CardBody>
            <dl className="space-y-3">
              {details.map(([label, value]) => (
                <div key={label}>
                  <dt className="text-caption font-medium uppercase tracking-wide text-fog">{label}</dt>
                  <dd className="mt-0.5 text-body text-charcoal">{value}</dd>
                </div>
              ))}
            </dl>
            {customer.notes && (
              <div className="mt-4 rounded-card bg-paper p-3">
                <p className="text-caption font-medium uppercase tracking-wide text-fog">Notes</p>
                <p className="mt-1 whitespace-pre-line text-body text-steel">{customer.notes}</p>
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
                  <Button>New quotation</Button>
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
                    <Tr key={quotation.id}>
                      <Td>
                        <Link
                          href={`/quotations/${quotation.id}`}
                          className="font-medium text-electric hover:underline"
                        >
                          <Mono>{quotation.quotationNumber}</Mono>
                        </Link>
                      </Td>
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
          )}

          {quotations.length > 0 && (
            <MobileList>
              {quotations.map((quotation) => (
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
                      { label: "Date", value: formatDate(quotation.quotationDate) },
                      {
                        label: "Total",
                        value: <Amount>{formatMoney(quotation.grandTotal, quotation.currency)}</Amount>,
                      },
                    ]}
                  />
                </MobileRow>
              ))}
            </MobileList>
          )}
        </Card>
      </div>
    </>
  );
}
