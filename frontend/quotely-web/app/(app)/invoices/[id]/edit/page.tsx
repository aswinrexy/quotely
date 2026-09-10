"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { formatMoney } from "@/lib/format";
import { calculateLine, calculateTotals } from "@/lib/money";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { Field, Input, Select, Textarea } from "@/components/ui/field";
import { ErrorState, LoadingState } from "@/components/ui/states";
import { PageHeader } from "@/components/app/page-header";
import {
  INVOICE_STATUSES,
  INVOICE_STATUS_LABELS,
  type Invoice,
  type InvoiceStatus,
  type SaveInvoiceItemRequest,
} from "@/types";

interface ItemRow extends SaveInvoiceItemRequest {
  key: string;
}

function emptyRow(): ItemRow {
  return {
    key: `${Date.now()}-${Math.random()}`,
    name: "",
    description: "",
    unit: "Service",
    quantity: 1,
    unitPrice: 0,
    discount: 0,
    taxRate: 0,
  };
}

export default function EditInvoicePage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const toast = useToast();

  const [invoice, setInvoice] = useState<Invoice | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [invoiceDate, setInvoiceDate] = useState("");
  const [dueDate, setDueDate] = useState("");
  const [status, setStatus] = useState<InvoiceStatus>("Draft");
  const [notes, setNotes] = useState("");
  const [terms, setTerms] = useState("");
  const [items, setItems] = useState<ItemRow[]>([]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const loaded = await api.get<Invoice>(`/api/invoices/${id}`);
      setInvoice(loaded);
      setInvoiceDate(loaded.invoiceDate.slice(0, 10));
      setDueDate(loaded.dueDate.slice(0, 10));
      setStatus(loaded.status);
      setNotes(loaded.notes ?? "");
      setTerms(loaded.terms ?? "");
      setItems(
        loaded.items.map((item) => ({
          key: item.id,
          name: item.name,
          description: item.description ?? "",
          unit: item.unit,
          quantity: item.quantity,
          unitPrice: item.unitPrice,
          discount: item.discount,
          taxRate: item.taxRate,
        })),
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load the invoice.");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  function updateItem(key: string, patch: Partial<ItemRow>) {
    setItems((current) => current.map((item) => (item.key === key ? { ...item, ...patch } : item)));
  }

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!invoice) return;

    if (dueDate < invoiceDate) {
      toast("Due date must be on or after the invoice date.", "error");
      return;
    }

    setSaving(true);
    try {
      // Line items are only sent while the invoice is still a draft — the server rejects them
      // afterwards, and an issued invoice must keep the figures the customer received.
      const payload = {
        invoiceDate,
        dueDate,
        status,
        notes: notes.trim() || null,
        terms: terms.trim() || null,
        items: invoice.canEditItems
          ? items.map(({ key: _key, ...item }) => ({
              ...item,
              description: item.description?.trim() || null,
              quantity: Number(item.quantity) || 0,
              unitPrice: Number(item.unitPrice) || 0,
              discount: Number(item.discount) || 0,
              taxRate: Number(item.taxRate) || 0,
            }))
          : undefined,
      };

      await api.put<Invoice>(`/api/invoices/${id}`, payload);
      toast("Invoice saved.", "success");
      router.push(`/invoices/${id}`);
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not save the invoice.", "error");
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <Card>
        <LoadingState />
      </Card>
    );
  }

  if (error || !invoice) {
    return (
      <Card>
        <ErrorState message={error ?? "Invoice not found."} onRetry={load} />
      </Card>
    );
  }

  if (!invoice.canEdit) {
    return (
      <Card>
        <ErrorState
          message={`${invoice.invoiceNumber} is ${INVOICE_STATUS_LABELS[invoice.status].toLowerCase()} and can no longer be changed.`}
        />
      </Card>
    );
  }

  const totals = calculateTotals(items);

  return (
    <form onSubmit={save}>
      <PageHeader
        title={`Edit ${invoice.invoiceNumber}`}
        description={`For ${invoice.customer.name}`}
        action={
          <>
            <Link href={`/invoices/${invoice.id}`}>
              <Button variant="secondary" type="button">
                Cancel
              </Button>
            </Link>
            <Button type="submit" loading={saving}>
              Save invoice
            </Button>
          </>
        }
      />

      <Card className="mb-6">
        <CardHeader title="Invoice details" />
        <CardBody className="grid gap-4 sm:grid-cols-3">
          <Field label="Invoice date" htmlFor="invoiceDate" required>
            <Input
              id="invoiceDate"
              type="date"
              value={invoiceDate}
              onChange={(e) => setInvoiceDate(e.target.value)}
              required
            />
          </Field>
          <Field label="Due date" htmlFor="dueDate" required>
            <Input
              id="dueDate"
              type="date"
              value={dueDate}
              min={invoiceDate}
              onChange={(e) => setDueDate(e.target.value)}
              required
            />
          </Field>
          <Field label="Status" htmlFor="status">
            <Select id="status" value={status} onChange={(e) => setStatus(e.target.value as InvoiceStatus)}>
              {INVOICE_STATUSES.map((value) => (
                <option key={value} value={value}>
                  {INVOICE_STATUS_LABELS[value]}
                </option>
              ))}
            </Select>
          </Field>
        </CardBody>
      </Card>

      <Card className="mb-6">
        <CardHeader
          title="Items"
          description={
            invoice.canEditItems
              ? "The server recalculates every total when you save."
              : "Locked: this invoice has already been issued."
          }
          action={
            invoice.canEditItems && (
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={() => setItems((current) => [...current, emptyRow()])}
              >
                Add item
              </Button>
            )
          }
        />
        <CardBody className="space-y-4">
          {items.map((item, index) => {
            const line = calculateLine(item);
            return (
              <div key={item.key} className="rounded-lg border border-slate-200 p-4">
                <div className="mb-3 flex items-center justify-between">
                  <p className="text-sm font-medium text-slate-700">Item {index + 1}</p>
                  {invoice.canEditItems && items.length > 1 && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => setItems((current) => current.filter((row) => row.key !== item.key))}
                    >
                      Remove
                    </Button>
                  )}
                </div>

                <div className="grid gap-3 sm:grid-cols-2">
                  <Field label="Description" htmlFor={`name-${item.key}`} required className="sm:col-span-2">
                    <Input
                      id={`name-${item.key}`}
                      value={item.name}
                      disabled={!invoice.canEditItems}
                      onChange={(e) => updateItem(item.key, { name: e.target.value })}
                      required
                    />
                  </Field>
                  <Field label="Details" htmlFor={`desc-${item.key}`} className="sm:col-span-2">
                    <Input
                      id={`desc-${item.key}`}
                      value={item.description ?? ""}
                      disabled={!invoice.canEditItems}
                      onChange={(e) => updateItem(item.key, { description: e.target.value })}
                    />
                  </Field>
                  <Field label="Unit" htmlFor={`unit-${item.key}`}>
                    <Input
                      id={`unit-${item.key}`}
                      value={item.unit}
                      disabled={!invoice.canEditItems}
                      onChange={(e) => updateItem(item.key, { unit: e.target.value })}
                    />
                  </Field>
                  <Field label="Quantity" htmlFor={`qty-${item.key}`}>
                    <Input
                      id={`qty-${item.key}`}
                      type="number"
                      min="0"
                      step="0.001"
                      value={item.quantity}
                      disabled={!invoice.canEditItems}
                      onChange={(e) => updateItem(item.key, { quantity: Number(e.target.value) })}
                    />
                  </Field>
                  <Field label="Unit price" htmlFor={`price-${item.key}`}>
                    <Input
                      id={`price-${item.key}`}
                      type="number"
                      min="0"
                      step="0.01"
                      value={item.unitPrice}
                      disabled={!invoice.canEditItems}
                      onChange={(e) => updateItem(item.key, { unitPrice: Number(e.target.value) })}
                    />
                  </Field>
                  <Field label="Discount" htmlFor={`disc-${item.key}`}>
                    <Input
                      id={`disc-${item.key}`}
                      type="number"
                      min="0"
                      step="0.01"
                      value={item.discount}
                      disabled={!invoice.canEditItems}
                      onChange={(e) => updateItem(item.key, { discount: Number(e.target.value) })}
                    />
                  </Field>
                  <Field label="Tax %" htmlFor={`tax-${item.key}`}>
                    <Input
                      id={`tax-${item.key}`}
                      type="number"
                      min="0"
                      max="100"
                      step="0.01"
                      value={item.taxRate}
                      disabled={!invoice.canEditItems}
                      onChange={(e) => updateItem(item.key, { taxRate: Number(e.target.value) })}
                    />
                  </Field>
                </div>

                <p className="mt-3 text-right text-sm text-slate-600">
                  Line total:{" "}
                  <span className="font-medium text-slate-900">
                    {formatMoney(line.lineTotal, invoice.currency)}
                  </span>
                </p>
              </div>
            );
          })}
        </CardBody>
      </Card>

      <Card className="mb-6">
        <CardHeader title="Notes & terms" />
        <CardBody className="grid gap-4 sm:grid-cols-2">
          <Field label="Notes" htmlFor="notes">
            <Textarea id="notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
          </Field>
          <Field label="Terms & conditions" htmlFor="terms">
            <Textarea id="terms" value={terms} onChange={(e) => setTerms(e.target.value)} />
          </Field>
        </CardBody>
      </Card>

      <Card>
        <CardBody className="flex justify-end">
          <dl className="w-full max-w-xs space-y-2 text-sm">
            <div className="flex justify-between">
              <dt className="text-slate-500">Subtotal</dt>
              <dd className="text-slate-900">{formatMoney(totals.subtotal, invoice.currency)}</dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-slate-500">Discount</dt>
              <dd className="text-slate-900">-{formatMoney(totals.discountTotal, invoice.currency)}</dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-slate-500">Tax</dt>
              <dd className="text-slate-900">{formatMoney(totals.taxTotal, invoice.currency)}</dd>
            </div>
            <div className="mt-2 flex items-center justify-between rounded-lg bg-teal-50 px-3 py-3">
              <dt className="text-sm font-semibold text-teal-700">TOTAL DUE</dt>
              <dd className="text-lg font-bold text-teal-700">
                {formatMoney(totals.grandTotal, invoice.currency)}
              </dd>
            </div>
            <p className="text-xs text-slate-400">Preview only — the server recalculates on save.</p>
          </dl>
        </CardBody>
      </Card>
    </form>
  );
}
