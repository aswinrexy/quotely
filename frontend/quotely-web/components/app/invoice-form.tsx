"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { addDaysIso, formatMoney, todayIso } from "@/lib/format";
import { calculateLine, calculateTotals } from "@/lib/money";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { cn } from "@/lib/cn";
import { Icon } from "@/components/ui/icons";
import { Field, Input, Select, Textarea, inputClass } from "@/components/ui/field";
import { LoadingState } from "@/components/ui/states";
import { UNITS } from "@/components/app/product-form";
import type { Customer, Invoice, PagedResult, Product } from "@/types";

/**
 * Direct invoice creation (V2.4): billing a customer who was never quoted.
 *
 * The totals shown here are a live preview only. Every figure that matters — line totals, tax,
 * the grand total and the invoice number — is recomputed by the server when the invoice is saved,
 * by the same calculator that converts an accepted quotation.
 *
 * Every line starts from the catalogue. That is a product decision, not a technical one: a business
 * that types each line from scratch ends up with no catalogue, and the same service goes out at
 * three different prices because nothing holds the number in one place.
 *
 * Choosing a catalogue item fills the line in. Nothing links the SAVED line back to the product:
 * an invoice line is a snapshot, so a later price change cannot restate a bill already sent. The
 * productId below exists only to drive this form — it is not sent to the server, and there is no
 * column for it. Which is why the server can only enforce "the catalogue is not empty", and the
 * per-line requirement is enforced here, where the person is choosing.
 */

interface ItemRow {
  key: string;
  /** Drives this form only. Never sent: an InvoiceItem has no ProductId by design. */
  productId: string;
  name: string;
  description: string;
  unit: string;
  quantity: string;
  unitPrice: string;
  discount: string;
  taxRate: string;
}

function emptyRow(): ItemRow {
  return {
    key: crypto.randomUUID(),
    productId: "",
    name: "",
    description: "",
    unit: "Service",
    quantity: "1",
    unitPrice: "0",
    discount: "0",
    taxRate: "0",
  };
}

function toNumber(value: string) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

export function InvoiceForm({ initialCustomerId }: { initialCustomerId?: string }) {
  const router = useRouter();
  const toast = useToast();

  const [customers, setCustomers] = useState<Customer[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [currency, setCurrency] = useState("INR");
  const [loading, setLoading] = useState(true);

  const [customerId, setCustomerId] = useState(initialCustomerId ?? "");
  const [invoiceDate, setInvoiceDate] = useState(todayIso());
  const [dueDate, setDueDate] = useState(addDaysIso(todayIso(), 15));
  const [notes, setNotes] = useState("");
  const [terms, setTerms] = useState("Payment due by the date shown above.");
  const [rows, setRows] = useState<ItemRow[]>([emptyRow()]);

  const [errors, setErrors] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    Promise.all([
      api.get<PagedResult<Customer>>("/api/customers?pageSize=100"),
      api.get<PagedResult<Product>>("/api/products?pageSize=200"),
      api.get<{ currency: string }>("/api/business-profile"),
    ])
      .then(([customerResult, productResult, profile]) => {
        setCustomers(customerResult.items);
        setProducts(productResult.items);
        setCurrency(profile.currency || "INR");
      })
      .catch((err) => toast(err instanceof Error ? err.message : "Could not load form data.", "error"))
      .finally(() => setLoading(false));
  }, [toast]);

  const totals = useMemo(
    () =>
      calculateTotals(
        rows.map((row) => ({
          quantity: toNumber(row.quantity),
          unitPrice: toNumber(row.unitPrice),
          discount: toNumber(row.discount),
          taxRate: toNumber(row.taxRate),
        })),
      ),
    [rows],
  );

  function updateRow(key: string, patch: Partial<ItemRow>) {
    setRows((current) => current.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  }

  /** Prefill only — the product reference is intentionally not carried onto the invoice line. */
  function onProductSelected(key: string, productId: string) {
    const product = products.find((p) => p.id === productId);
    if (!product) return;

    updateRow(key, {
      productId: product.id,
      name: product.name,
      description: product.description ?? "",
      unit: product.unit,
      unitPrice: String(product.price),
      taxRate: String(product.taxRate),
    });
  }

  function validate(): string[] {
    const found: string[] = [];
    if (!customerId) found.push("Select a customer.");
    if (!invoiceDate) found.push("Choose an invoice date.");
    if (!dueDate) found.push("Choose a due date.");
    if (invoiceDate && dueDate && dueDate < invoiceDate)
      found.push("Due date must be on or after the invoice date.");
    if (rows.length === 0) found.push("Add at least one item.");

    // Said once, not once per line. With nothing in the catalogue, telling someone to pick from it
    // five times is noise — the one thing they can act on is adding the first product.
    if (products.length === 0)
      found.push("Add a product or service to your catalogue before creating an invoice.");

    rows.forEach((row, index) => {
      const label = row.name.trim() || `Item ${index + 1}`;
      // Before the description check, because "pick a product" is the actionable instruction and
      // a row with no product selected has no description to complain about either.
      if (products.length > 0 && !row.productId)
        found.push(`${label}: choose a product or service from your catalogue.`);
      if (!row.name.trim()) found.push(`${label} needs a description.`);
      if (toNumber(row.quantity) <= 0) found.push(`${label}: quantity must be greater than zero.`);
      if (toNumber(row.unitPrice) < 0) found.push(`${label}: unit price cannot be negative.`);
      if (toNumber(row.discount) < 0) found.push(`${label}: discount cannot be negative.`);
      const tax = toNumber(row.taxRate);
      if (tax < 0 || tax > 100) found.push(`${label}: tax rate must be between 0 and 100.`);
    });

    return found;
  }

  async function onSave() {
    const found = validate();
    setErrors(found);
    if (found.length > 0) {
      window.scrollTo({ top: 0, behavior: "smooth" });
      return;
    }

    setSaving(true);
    try {
      const saved = await api.post<Invoice>("/api/invoices", {
        customerId,
        invoiceDate,
        dueDate,
        notes: notes.trim() || null,
        terms: terms.trim() || null,
        items: rows.map((row) => ({
          name: row.name.trim(),
          description: row.description.trim() || null,
          unit: row.unit,
          quantity: toNumber(row.quantity),
          unitPrice: toNumber(row.unitPrice),
          discount: toNumber(row.discount),
          taxRate: toNumber(row.taxRate),
        })),
      });
      toast("Invoice created.", "success");
      router.push(`/invoices/${saved.id}`);
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not create the invoice.", "error");
    } finally {
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

  return (
    <div className="space-y-6">
      {errors.length > 0 && (
        <div role="alert" className="rounded-card border border-ash bg-rose-wash px-4 py-3">
          <p className="text-body font-medium text-rose-ink">Please fix the following:</p>
          <ul className="mt-1 list-disc space-y-0.5 pl-5 text-body text-rose-ink">
            {errors.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
        </div>
      )}

      {customers.length === 0 && (
        <div className="rounded-card border border-ash bg-amber-wash px-4 py-3 text-body text-amber-ink">
          You have no customers yet.{" "}
          <Link href="/customers/new" className="font-medium underline">
            Add a customer
          </Link>{" "}
          before creating an invoice.
        </div>
      )}

      {products.length === 0 && (
        <div className="rounded-card border border-ash bg-amber-wash px-4 py-3 text-body text-amber-ink">
          Your catalogue is empty, and every invoice line is priced from it.{" "}
          <Link href="/products/new" className="font-medium underline">
            Add a product or service
          </Link>{" "}
          before creating an invoice. You only have to do this once — after that it is there for
          every invoice you send.
        </div>
      )}

      <Card>
        <CardHeader
          title="Invoice details"
          description="The billing address is copied from the customer when you save, and stays fixed afterwards."
        />
        <CardBody>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Field label="Customer" htmlFor="customer" required className="sm:col-span-2">
              <Select id="customer" value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
                <option value="">Select customer…</option>
                {customers.map((customer) => (
                  <option key={customer.id} value={customer.id}>
                    {customer.name}
                    {customer.companyName ? ` — ${customer.companyName}` : ""}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Invoice date" htmlFor="invoiceDate" required>
              <Input
                id="invoiceDate"
                type="date"
                value={invoiceDate}
                onChange={(e) => setInvoiceDate(e.target.value)}
              />
            </Field>

            <Field label="Due date" htmlFor="dueDate" required>
              <Input
                id="dueDate"
                type="date"
                min={invoiceDate}
                value={dueDate}
                onChange={(e) => setDueDate(e.target.value)}
              />
            </Field>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader
          title="Items"
          description="Every line comes from your catalogue. Prices and wording can still be adjusted for this invoice."
          action={
            <Button type="button" variant="secondary" size="sm" onClick={() => setRows((r) => [...r, emptyRow()])}>
              + Add item
            </Button>
          }
        />

        <div className="hidden lg:block">
          <div className="grid grid-cols-[minmax(0,3fr)_112px_120px_120px_88px_120px_44px] gap-2 border-b border-ash bg-paper px-4 py-2 text-caption font-semibold uppercase tracking-wide text-fog">
            <span>Product / Service</span>
            <span className="text-right">Qty</span>
            <span className="text-right">Unit price</span>
            <span className="text-right">Discount price</span>
            <span className="text-right">Tax %</span>
            <span className="text-right">Total</span>
            <span />
          </div>
        </div>

        <div className="divide-y divide-ash">
          {rows.map((row, index) => {
            const line = calculateLine({
              quantity: toNumber(row.quantity),
              unitPrice: toNumber(row.unitPrice),
              discount: toNumber(row.discount),
              taxRate: toNumber(row.taxRate),
            });

            return (
              <div key={row.key} className="px-4 py-4 lg:py-3">
                <div className="grid gap-3 lg:grid-cols-[minmax(0,3fr)_112px_120px_120px_88px_120px_44px] lg:items-start lg:gap-2">
                  <div className="space-y-2">
                    <Select
                      aria-label={`Catalogue item for line ${index + 1}`}
                      value={row.productId}
                      onChange={(e) => onProductSelected(row.key, e.target.value)}
                    >
                      <option value="" disabled>
                        Select a product or service…
                      </option>
                      {products.map((product) => (
                        <option key={product.id} value={product.id}>
                          {product.name} — {formatMoney(product.price, currency)}
                        </option>
                      ))}
                    </Select>
                    <Input
                      aria-label={`Description for line ${index + 1}`}
                      value={row.name}
                      onChange={(e) => updateRow(row.key, { name: e.target.value })}
                    />
                    <Input
                      aria-label={`Detail for line ${index + 1}`}
                      value={row.description}
                      onChange={(e) => updateRow(row.key, { description: e.target.value })}
                      className="text-caption"
                    />
                    <div className="lg:hidden">
                      <label className="text-caption font-medium text-fog">Unit</label>
                      <Select value={row.unit} onChange={(e) => updateRow(row.key, { unit: e.target.value })}>
                        {UNITS.map((unit) => (
                          <option key={unit} value={unit}>
                            {unit}
                          </option>
                        ))}
                      </Select>
                    </div>
                  </div>

                  <div className="grid grid-cols-2 gap-3 lg:contents">
                    <label className="lg:hidden">
                      <span className="text-caption font-medium text-fog">Qty</span>
                      <QuantityInput row={row} onChange={updateRow} />
                    </label>
                    <div className="hidden lg:block">
                      <QuantityInput row={row} onChange={updateRow} />
                      <select
                        aria-label={`Unit for line ${index + 1}`}
                        value={row.unit}
                        onChange={(e) => updateRow(row.key, { unit: e.target.value })}
                        className="mt-1.5 w-full truncate rounded-input border border-ash bg-canvas px-2 py-1 text-caption text-steel"
                      >
                        {UNITS.map((unit) => (
                          <option key={unit} value={unit}>
                            {unit}
                          </option>
                        ))}
                      </select>
                    </div>

                    <label className="lg:hidden">
                      <span className="text-caption font-medium text-fog">Unit price</span>
                      <MoneyInput field="unitPrice" row={row} onChange={updateRow} />
                    </label>
                    <div className="hidden lg:block">
                      <MoneyInput field="unitPrice" row={row} onChange={updateRow} />
                    </div>

                    <label className="lg:hidden">
                      <span className="text-caption font-medium text-fog">Discount price</span>
                      <MoneyInput field="discount" row={row} onChange={updateRow} />
                    </label>
                    <div className="hidden lg:block">
                      <MoneyInput field="discount" row={row} onChange={updateRow} />
                    </div>

                    <label className="lg:hidden">
                      <span className="text-caption font-medium text-fog">Tax %</span>
                      <MoneyInput field="taxRate" row={row} onChange={updateRow} max={100} />
                    </label>
                    <div className="hidden lg:block">
                      <MoneyInput field="taxRate" row={row} onChange={updateRow} max={100} />
                    </div>
                  </div>

                  <div className="flex items-center justify-between lg:block lg:pt-2 lg:text-right">
                    <span className="text-caption font-medium text-fog lg:hidden">Line total</span>
                    <span className="text-body font-semibold text-charcoal">
                      {formatMoney(line.lineTotal, currency)}
                    </span>
                  </div>

                  <div className="flex justify-end lg:pt-1">
                    <button
                      type="button"
                      onClick={() => setRows((current) => current.filter((r) => r.key !== row.key))}
                      disabled={rows.length === 1}
                      aria-label={`Remove line ${index + 1}`}
                      title="Remove this line"
                      className={cn(
                        "inline-flex h-9 w-9 items-center justify-center rounded-input",
                        "text-fog transition-colors duration-150 ease-out",
                        "hover:bg-rose-wash hover:text-rose-ink",
                        "disabled:pointer-events-none disabled:opacity-30",
                      )}
                    >
                      <Icon.trash className="h-4 w-4" />
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        <div className="border-t border-ash px-4 py-3">
          <Button type="button" variant="secondary" size="sm" onClick={() => setRows((r) => [...r, emptyRow()])}>
            + Add item
          </Button>
        </div>
      </Card>

      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader title="Notes & terms" />
          <CardBody className="space-y-4">
            <Field label="Notes" htmlFor="notes">
              <Textarea
                id="notes"
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
              />
            </Field>
            <Field label="Terms & conditions" htmlFor="terms">
              <Textarea id="terms" value={terms} onChange={(e) => setTerms(e.target.value)} />
            </Field>
          </CardBody>
        </Card>

        <Card className="h-fit">
          <CardHeader title="Summary" />
          <CardBody className="space-y-2">
            <SummaryRow label="Subtotal" value={formatMoney(totals.subtotal, currency)} />
            <SummaryRow label="Discount" value={`-${formatMoney(totals.discountTotal, currency)}`} />
            <SummaryRow label="Tax" value={formatMoney(totals.taxTotal, currency)} />
            <div className="mt-3 flex items-center justify-between border-t border-smoke pt-3">
              <span className="text-body font-semibold text-charcoal">Total</span>
              <span className="text-body-xl font-semibold tabular-nums text-charcoal">
                {formatMoney(totals.grandTotal, currency)}
              </span>
            </div>
            <p className="pt-1 text-caption text-fog">
              Totals are confirmed by the server when you save.
            </p>
          </CardBody>
        </Card>
      </div>

      <div className="flex flex-wrap justify-end gap-2 pb-4">
        <Button type="button" variant="secondary" onClick={() => router.push("/invoices")}>
          Cancel
        </Button>
        <Button type="button" onClick={onSave} loading={saving}>
          Create invoice
        </Button>
      </div>
    </div>
  );
}

function SummaryRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-body text-fog">{label}</span>
      <span className="text-body tabular-nums text-charcoal">{value}</span>
    </div>
  );
}

function QuantityInput({
  row,
  onChange,
}: {
  row: ItemRow;
  onChange: (key: string, patch: Partial<ItemRow>) => void;
}) {
  return (
    <input
      type="number"
      min={0}
      step="0.001"
      inputMode="decimal"
      aria-label="Quantity"
      value={row.quantity}
      onChange={(e) => onChange(row.key, { quantity: e.target.value })}
      className={`${inputClass} text-right`}
    />
  );
}

function MoneyInput({
  field,
  row,
  onChange,
  max,
}: {
  field: "unitPrice" | "discount" | "taxRate";
  row: ItemRow;
  onChange: (key: string, patch: Partial<ItemRow>) => void;
  max?: number;
}) {
  const labels = { unitPrice: "Unit price", discount: "Discount", taxRate: "Tax rate" } as const;
  return (
    <input
      type="number"
      min={0}
      max={max}
      step="0.01"
      inputMode="decimal"
      aria-label={labels[field]}
      value={row[field]}
      onChange={(e) => onChange(row.key, { [field]: e.target.value } as Partial<ItemRow>)}
      className={`${inputClass} text-right`}
    />
  );
}
