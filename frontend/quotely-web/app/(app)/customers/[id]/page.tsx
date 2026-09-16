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
import { InvoiceStatusBadge } from "@/components/ui/badge";
import { StatCard } from "@/components/ui/stat-card";
import type { Customer, CustomerSummary, PagedResult, QuotationListItem } from "@/types";

export default function CustomerDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [quotations, setQuotations] = useState<QuotationListItem[]>([]);
  const [summary, setSummary] = useState<CustomerSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [customerResult, quotationResult, summaryResult] = await Promise.all([
        api.get<Customer>(`/api/customers/${id}`),
        // Filtered by the server. This used to request page one of every quotation and filter
        // here, which lost anything belonging to a customer past that first page.
        api.get<PagedResult<QuotationListItem>>(`/api/quotations?customerId=${id}&pageSize=20`),
        api.get<CustomerSummary>(`/api/customers/${id}/summary?pageSize=10`),
      ]);
      setCustomer(customerResult);
      setQuotations(quotationResult.items);
      setSummary(summaryResult);
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
              <Button variant="secondary">New quotation</Button>
            </Link>
            <Link href={`/invoices/new?customerId=${customer.id}`}>
              <Button>New invoice</Button>
            </Link>
          </>
        }
      />

      {/* What this customer owes, before anything else about them. */}
      {summary && summary.invoiceCount > 0 && (
        <div className="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-4">
          <StatCard
            label="Outstanding"
            value={formatMoney(summary.totalOutstanding, summary.currency)}
            context={summary.totalOutstanding > 0 ? "Owed by this customer" : "Nothing owed"}
            accent={summary.totalOutstanding > 0}
          />
          <StatCard
            label="Overdue"
            value={formatMoney(summary.totalOverdue, summary.currency)}
            context={summary.overdueCount > 0 ? `${summary.overdueCount} past due` : "Nothing past due"}
          />
          <StatCard
            label="Invoiced"
            value={formatMoney(summary.totalInvoiced, summary.currency)}
            context={`${summary.invoiceCount} ${summary.invoiceCount === 1 ? "invoice" : "invoices"}`}
          />
          <StatCard
            label="Paid"
            value={formatMoney(summary.totalPaid, summary.currency)}
            context="Received to date"
          />
        </div>
      )}

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

        <div className="space-y-4 lg:col-span-2">
        <Card>
          <CardHeader
            title="Invoices"
            description="Invoices raised for this customer."
            action={
              <Link
                href="/invoices"
                className="inline-flex items-center gap-1 text-body font-medium text-electric hover:underline"
              >
                All invoices
                <Icon.chevronRight className="h-3.5 w-3.5" />
              </Link>
            }
          />
          {!summary || summary.invoices.items.length === 0 ? (
            <EmptyState
              title="No invoices yet"
              description="Bill this customer directly, or convert an accepted quotation."
              action={
                <Link href={`/invoices/new?customerId=${customer.id}`}>
                  <Button>New invoice</Button>
                </Link>
              }
            />
          ) : (
            <>
              <TableWrap>
                <Table>
                  <thead>
                    <tr>
                      <Th>Invoice</Th>
                      <Th>Due</Th>
                      <Th>Status</Th>
                      <Th align="right">Total</Th>
                      <Th align="right">Outstanding</Th>
                    </tr>
                  </thead>
                  <tbody>
                    {summary.invoices.items.map((invoice) => (
                      <Tr key={invoice.id}>
                        <Td>
                          <Link
                            href={`/invoices/${invoice.id}`}
                            className="font-medium text-electric hover:underline"
                          >
                            <Mono>{invoice.invoiceNumber}</Mono>
                          </Link>
                        </Td>
                        <Td>
                          {formatDate(invoice.dueDate)}
                          {invoice.isOverdue && (
                            <span className="ml-1.5 text-caption font-medium text-rose-ink">
                              Past due
                            </span>
                          )}
                        </Td>
                        <Td>
                          <InvoiceStatusBadge status={invoice.status} />
                        </Td>
                        <Td align="right">
                          <Amount>{formatMoney(invoice.grandTotal, invoice.currency)}</Amount>
                        </Td>
                        <Td align="right">
                          {invoice.outstanding > 0 ? (
                            <Amount>{formatMoney(invoice.outstanding, invoice.currency)}</Amount>
                          ) : (
                            <span className="text-caption text-fog">Paid</span>
                          )}
                        </Td>
                      </Tr>
                    ))}
                  </tbody>
                </Table>
              </TableWrap>

              <MobileList>
                {summary.invoices.items.map((invoice) => (
                  <MobileRow key={invoice.id}>
                    <div className="flex items-start justify-between gap-3">
                      <Link
                        href={`/invoices/${invoice.id}`}
                        className="font-medium text-electric hover:underline"
                      >
                        <Mono>{invoice.invoiceNumber}</Mono>
                      </Link>
                      <InvoiceStatusBadge status={invoice.status} />
                    </div>
                    <MobileFacts
                      items={[
                        {
                          label: "Total",
                          value: <Amount>{formatMoney(invoice.grandTotal, invoice.currency)}</Amount>,
                        },
                        {
                          label: "Outstanding",
                          value:
                            invoice.outstanding > 0 ? (
                              <Amount>{formatMoney(invoice.outstanding, invoice.currency)}</Amount>
                            ) : (
                              "Paid"
                            ),
                        },
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
                  </MobileRow>
                ))}
              </MobileList>

              {summary.invoices.totalCount > summary.invoices.items.length && (
                <div className="border-t border-ash px-4 py-2.5 text-caption text-fog">
                  Showing {summary.invoices.items.length} of {summary.invoices.totalCount} invoices.
                </div>
              )}
            </>
          )}
        </Card>

        <Card>
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
      </div>
    </>
  );
}
