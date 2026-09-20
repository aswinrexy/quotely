"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api, saveBlob } from "@/lib/api";
import { addDaysIso, formatMoney, todayIso } from "@/lib/format";
import { calculateLine, calculateTotals } from "@/lib/money";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Icon } from "@/components/ui/icons";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { cn } from "@/lib/cn";
import { Field, Input, Select, Textarea, inputClass } from "@/components/ui/field";
import { LoadingState } from "@/components/ui/states";
import { UNITS } from "@/components/app/product-form";
import type { Customer, PagedResult, Product, Quotation, QuotationStatus } from "@/types";
import { QUOTATION_STATUSES } from "@/types";

interface ItemRow {
  key: string;
  productId: string | null;
  /**
   * True for a line loaded from an already-saved quotation.
   *
   * Quotations written before the catalogue became mandatory can hold lines with no productId at
   * all. Those documents still have to be editable — refusing to save one because of a rule that
   * did not exist when it was written would strand a customer's quotation, and the rule is about
   * how NEW work is priced, not about rewriting history. New rows get no such exemption.
   */
  existing: boolean;
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
    productId: null,
    existing: false,
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

/** How a field that the catalogue owns looks: visibly not yours to type in. */
const LOCKED_FIELD = "border-ash bg-paper text-fog cursor-not-allowed";

interface Props {
  quotation?: Quotation;
  initialCustomerId?: string;
}

export function QuotationForm({ quotation, initialCustomerId }: Props) {
  const router = useRouter();
  const toast = useToast();

  const [customers, setCustomers] = useState<Customer[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [currency, setCurrency] = useState(quotation?.currency ?? "INR");
  const [loading, setLoading] = useState(true);

  const [customerId, setCustomerId] = useState(quotation?.customer.id ?? initialCustomerId ?? "");
  const [quotationDate, setQuotationDate] = useState(quotation?.quotationDate ?? todayIso());
  const [validUntil, setValidUntil] = useState(quotation?.validUntil ?? addDaysIso(todayIso(), 15));
  const [status, setStatus] = useState<QuotationStatus>(quotation?.status ?? "Draft");
  const [notes, setNotes] = useState(quotation?.notes ?? "");
  const [terms, setTerms] = useState(
    quotation?.terms ?? "Quotation is valid until the specified date. Prices include taxes as shown.",
  );
  const [rows, setRows] = useState<ItemRow[]>(
    quotation
      ? quotation.items.map((item) => ({
          key: item.id,
          productId: item.productId ?? null,
          existing: true,
          name: item.name,
          description: item.description ?? "",
          unit: item.unit,
          quantity: String(item.quantity),
          unitPrice: String(item.unitPrice),
          discount: String(item.discount),
          taxRate: String(item.taxRate),
        }))
      : [emptyRow()],
  );

  const [errors, setErrors] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [generating, setGenerating] = useState(false);

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

  function onProductSelected(key: string, productId: string) {
    if (!productId) {
      updateRow(key, { productId: null });
      return;
    }
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
    if (!quotationDate) found.push("Choose a quotation date.");
    if (!validUntil) found.push("Choose a valid-until date.");
    if (quotationDate && validUntil && validUntil < quotationDate)
      found.push("Valid until must be on or after the quotation date.");
    if (rows.length === 0) found.push("Add at least one item.");

    // Said once, not once per line. With nothing in the catalogue, telling someone to pick from it
    // five times is noise — the one thing they can act on is adding the first product.
    if (products.length === 0 && rows.some((row) => !row.existing))
      found.push("Add a product or service to your catalogue before quoting.");

    rows.forEach((row, index) => {
      const label = row.name.trim() || `Item ${index + 1}`;
      // `existing` rows are exempt: see ItemRow. A new line always has to come from the catalogue.
      if (products.length > 0 && !row.existing && !row.productId)
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

  function buildPayload() {
    return {
      customerId,
      quotationDate,
      validUntil,
      status,
      notes: notes.trim() || null,
      terms: terms.trim() || null,
      items: rows.map((row) => ({
        productId: row.productId,
        name: row.name.trim(),
        description: row.description.trim() || null,
        unit: row.unit,
        quantity: toNumber(row.quantity),
        unitPrice: toNumber(row.unitPrice),
        discount: toNumber(row.discount),
        taxRate: toNumber(row.taxRate),
      })),
    };
  }

  async function save(): Promise<Quotation | null> {
    const found = validate();
    setErrors(found);
    if (found.length > 0) {
      window.scrollTo({ top: 0, behavior: "smooth" });
      return null;
    }

    const payload = buildPayload();
    return quotation
      ? api.put<Quotation>(`/api/quotations/${quotation.id}`, payload)
      : api.post<Quotation>("/api/quotations", payload);
  }

  async function onSave() {
    setSaving(true);
    try {
      const saved = await save();
      if (!saved) return;
      toast(quotation ? "Quotation updated." : "Quotation saved.", "success");
      router.push(`/quotations/${saved.id}`);
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not save the quotation.", "error");
    } finally {
      setSaving(false);
    }
  }

  /** Saves first, then downloads the server-rendered PDF for the saved record. */
  async function onGeneratePdf() {
    setGenerating(true);
    try {
      const saved = await save();
      if (!saved) return;
      const { blob, fileName } = await api.downloadPdf(saved.id);
      saveBlob(blob, fileName);
      toast("PDF downloaded.", "success");
      router.push(`/quotations/${saved.id}`);
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not generate the PDF.", "error");
    } finally {
      setGenerating(false);
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
          before creating a quotation.
        </div>
      )}

      {products.length === 0 && (
        <div className="rounded-card border border-ash bg-amber-wash px-4 py-3 text-body text-amber-ink">
          Your catalogue is empty, and every quoted line is priced from it.{" "}
          <Link href="/products/new" className="font-medium underline">
            Add a product or service
          </Link>{" "}
          before quoting. You only have to do this once — after that it is there for every
          quotation you send.
        </div>
      )}

      <Card>
        <CardHeader title="Quotation details" />
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

            <Field label="Quotation date" htmlFor="quotationDate" required>
              <Input
                id="quotationDate"
                type="date"
                value={quotationDate}
                onChange={(e) => setQuotationDate(e.target.value)}
              />
            </Field>

            <Field label="Valid until" htmlFor="validUntil" required>
              <Input
                id="validUntil"
                type="date"
                min={quotationDate}
                value={validUntil}
                onChange={(e) => setValidUntil(e.target.value)}
              />
            </Field>

            <Field label="Status" htmlFor="status">
              <Select id="status" value={status} onChange={(e) => setStatus(e.target.value as QuotationStatus)}>
                {QUOTATION_STATUSES.map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </Select>
            </Field>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader
          title="Items"
          description="Every line comes from your catalogue. Prices and wording can still be adjusted for this quotation."
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
            // Everything the catalogue owns is fixed once a product is chosen. Quantity, the two
            // text fields and the discount stay editable: quantity and wording are per-job, and a
            // discount is a concession this customer is being given, not a property of the product.
            //
            // A legacy line — saved before the catalogue rule existed — has no productId and stays
            // fully editable, because there is no catalogue entry behind it to be authoritative.
            const fromCatalogue = row.productId !== null;
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
                      value={row.productId ?? ""}
                      onChange={(e) => onProductSelected(row.key, e.target.value)}
                    >
                      <option value="" disabled={!row.existing}>
                        {row.existing && !row.productId ? "Not from the catalogue" : "Select a product or service…"}
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
                      // Deliberately keeps productId. Adjusting the wording for one job is normal;
                      // it does not mean the line stopped coming from the catalogue, and clearing
                      // the link here would make the line unsavable the moment it was edited.
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
                      <Select
                        value={row.unit}
                        disabled={fromCatalogue}
                        title={fromCatalogue ? "Set by the catalogue. Edit the product to change it." : undefined}
                        onChange={(e) => updateRow(row.key, { unit: e.target.value })}
                      >
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
                        disabled={fromCatalogue}
                        title={fromCatalogue ? "Set by the catalogue. Edit the product to change it." : undefined}
                        onChange={(e) => updateRow(row.key, { unit: e.target.value })}
                        className={cn(
                          "mt-1.5 w-full truncate rounded-input border border-ash bg-canvas px-2 py-1 text-caption text-steel",
                          fromCatalogue && LOCKED_FIELD,
                        )}
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
                      <MoneyInput field="unitPrice" row={row} onChange={updateRow} locked={fromCatalogue} />
                    </label>
                    <div className="hidden lg:block">
                      <MoneyInput field="unitPrice" row={row} onChange={updateRow} locked={fromCatalogue} />
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
                      <MoneyInput field="taxRate" row={row} onChange={updateRow} max={100} locked={fromCatalogue} />
                    </label>
                    <div className="hidden lg:block">
                      <MoneyInput field="taxRate" row={row} onChange={updateRow} max={100} locked={fromCatalogue} />
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
                      // Never disabled. The last line resets instead of vanishing, which is the
                      // case that matters: the catalogue picker has no empty option, so choosing
                      // the wrong product on a lone line would otherwise be impossible to undo.
                      onClick={() =>
                        setRows((current) =>
                          current.length === 1 ? [emptyRow()] : current.filter((r) => r.key !== row.key),
                        )
                      }
                      aria-label={rows.length === 1 ? "Clear line 1" : `Remove line ${index + 1}`}
                      title={rows.length === 1 ? "Clear this line" : "Remove this line"}
                      className={cn(
                        "inline-flex h-9 w-9 items-center justify-center rounded-input",
                        "text-fog transition-colors duration-150 ease-out",
                        "hover:bg-rose-wash hover:text-rose-ink",
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

      {/* One primary action; saving is the commitment, the PDF is a convenience. */}
      <div className="flex flex-wrap justify-end gap-2 pb-4">
        <Button
          type="button"
          variant="secondary"
          onClick={() => router.push(quotation ? `/quotations/${quotation.id}` : "/quotations")}
        >
          Cancel
        </Button>
        <Button type="button" variant="secondary" onClick={onGeneratePdf} loading={generating}>
          <Icon.download className="h-4 w-4" />
          Save &amp; download PDF
        </Button>
        <Button type="button" onClick={onSave} loading={saving}>
          {quotation ? "Save changes" : "Save quotation"}
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

/**
 * `locked` is readOnly rather than disabled, deliberately. A disabled field is skipped by keyboard
 * navigation and read out as unavailable, which is the wrong story: the number is not unavailable,
 * it is authoritative. readOnly keeps it focusable and selectable while refusing edits.
 */
function MoneyInput({
  field,
  row,
  onChange,
  max,
  locked,
}: {
  field: "unitPrice" | "discount" | "taxRate";
  row: ItemRow;
  onChange: (key: string, patch: Partial<ItemRow>) => void;
  max?: number;
  locked?: boolean;
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
      readOnly={locked}
      title={locked ? "Set by the catalogue. Edit the product to change it." : undefined}
      value={row[field]}
      onChange={(e) => onChange(row.key, { [field]: e.target.value } as Partial<ItemRow>)}
      className={cn(inputClass, "text-right", locked && LOCKED_FIELD)}
    />
  );
}
