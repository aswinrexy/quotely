"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { addDaysIso, formatMoney, todayIso } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { cn } from "@/lib/cn";
import { Icon } from "@/components/ui/icons";
import { Field, Input, Select, Textarea, inputClass } from "@/components/ui/field";
import { LoadingState } from "@/components/ui/states";
import {
  TRADE_DELIVERY_TERMS,
  TRADE_DOCUMENT_TYPES,
  TRADE_DOCUMENT_TYPE_LABELS,
  TRADE_PAYMENT_TERMS,
  TRADE_QUANTITY_UNITS,
  TRADE_TYPES,
  type Customer,
  type PagedResult,
  type TradeInvoice,
  type TradeInvoiceDefaults,
  type TradeRateBasis,
  type TradeType,
} from "@/types";

/**
 * Creating and editing an import/export document.
 *
 * The form is sectioned rather than presented as one wall of forty fields, and the sections follow
 * the order a shipment is actually arranged: what the document is, who the parties are, how the
 * goods travel, what they are, and what the business declares. That is also the order the printed
 * document reads in, so filling it in and checking it are the same journey.
 *
 * Every total shown here is a PREVIEW. The server recomputes line totals, weights, package counts
 * and the amount in words when the document is saved — see TradeInvoiceService.
 */

interface GoodsRow {
  key: string;
  marksAndNumbers: string;
  description: string;
  detail: string;
  dimension: string;
  hsCode: string;
  netWeight: string;
  grossWeight: string;
  quantity: string;
  quantityUnit: string;
  rate: string;
  rateBasis: TradeRateBasis;
}

function emptyRow(): GoodsRow {
  return {
    key: crypto.randomUUID(),
    marksAndNumbers: "",
    description: "",
    detail: "",
    dimension: "",
    hsCode: "",
    netWeight: "",
    grossWeight: "",
    quantity: "1",
    quantityUnit: "BOXES",
    rate: "0",
    rateBasis: "PerQuantityUnit",
  };
}

function toNumber(value: string) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function optionalNumber(value: string): number | null {
  if (value.trim() === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Mirrors the server's rule so the preview and the saved document agree. */
function lineTotal(row: GoodsRow) {
  const priced = row.rateBasis === "PerNetWeight" ? toNumber(row.netWeight) : toNumber(row.quantity);
  return Math.round(priced * toNumber(row.rate) * 100) / 100;
}

export function TradeInvoiceForm({ invoice }: { invoice?: TradeInvoice }) {
  const router = useRouter();
  const toast = useToast();
  const editing = Boolean(invoice);

  const [customers, setCustomers] = useState<Customer[]>([]);
  const [defaults, setDefaults] = useState<TradeInvoiceDefaults | null>(null);
  const [loading, setLoading] = useState(true);

  const [tradeType, setTradeType] = useState<TradeType>(invoice?.tradeType ?? "Export");
  const [documentType, setDocumentType] = useState(invoice?.documentType ?? "ProformaInvoice");
  const [customerId, setCustomerId] = useState(invoice?.customerId ?? "");
  const [invoiceDate, setInvoiceDate] = useState(invoice?.invoiceDate ?? todayIso());
  const [dueDate, setDueDate] = useState(invoice?.dueDate ?? addDaysIso(todayIso(), 15));
  const [currency, setCurrency] = useState(invoice?.currency ?? "INR");

  const [documentNumber, setDocumentNumber] = useState(invoice?.documentNumber ?? "");
  const [buyerOrderNumber, setBuyerOrderNumber] = useState(invoice?.buyerOrderNumber ?? "");
  const [buyerOrderDate, setBuyerOrderDate] = useState(invoice?.buyerOrderDate ?? "");
  const [otherReferences, setOtherReferences] = useState(invoice?.otherReferences ?? "");

  const [partyName, setPartyName] = useState(invoice?.partyName ?? "");
  const [partyAddress, setPartyAddress] = useState(invoice?.partyAddress ?? "");
  const [consignorSameAsParty, setConsignorSameAsParty] = useState(
    invoice?.consignorSameAsParty ?? true,
  );
  const [consignorName, setConsignorName] = useState(invoice?.consignorName ?? "");
  const [consignorAddress, setConsignorAddress] = useState(invoice?.consignorAddress ?? "");
  const [consigneeName, setConsigneeName] = useState(invoice?.consigneeName ?? "");
  const [consigneeAddress, setConsigneeAddress] = useState(invoice?.consigneeAddress ?? "");
  const [buyerSameAsConsignee, setBuyerSameAsConsignee] = useState(
    invoice?.buyerSameAsConsignee ?? true,
  );
  const [buyerName, setBuyerName] = useState(invoice?.buyerName ?? "");
  const [buyerAddress, setBuyerAddress] = useState(invoice?.buyerAddress ?? "");
  const [notifyPartyName, setNotifyPartyName] = useState(invoice?.notifyPartyName ?? "");
  const [notifyPartyAddress, setNotifyPartyAddress] = useState(invoice?.notifyPartyAddress ?? "");

  const [preCarriageBy, setPreCarriageBy] = useState(invoice?.preCarriageBy ?? "");
  const [placeOfReceipt, setPlaceOfReceipt] = useState(invoice?.placeOfReceipt ?? "");
  const [vesselOrFlightNumber, setVesselOrFlightNumber] = useState(invoice?.vesselOrFlightNumber ?? "");
  const [portOfLoading, setPortOfLoading] = useState(invoice?.portOfLoading ?? "");
  const [portOfDischarge, setPortOfDischarge] = useState(invoice?.portOfDischarge ?? "");
  const [finalDestination, setFinalDestination] = useState(invoice?.finalDestination ?? "");
  const [countryOfOrigin, setCountryOfOrigin] = useState(invoice?.countryOfOrigin ?? "");
  const [countryOfFinalDestination, setCountryOfFinalDestination] = useState(
    invoice?.countryOfFinalDestination ?? "",
  );

  const [termsOfDelivery, setTermsOfDelivery] = useState(invoice?.termsOfDelivery ?? "");
  const [termsOfPayment, setTermsOfPayment] = useState(invoice?.termsOfPayment ?? "");
  const [pricingTerm, setPricingTerm] = useState(invoice?.pricingTerm ?? "");

  const [iecNumber, setIecNumber] = useState(invoice?.iecNumber ?? "");
  const [gstNumber, setGstNumber] = useState(invoice?.gstNumber ?? "");
  const [panNumber, setPanNumber] = useState(invoice?.panNumber ?? "");
  const [apedaRegistrationNumber, setApedaRegistrationNumber] = useState(
    invoice?.apedaRegistrationNumber ?? "",
  );
  const [apedaValidUntil, setApedaValidUntil] = useState(invoice?.apedaValidUntil ?? "");

  const [headerDeclarations, setHeaderDeclarations] = useState(invoice?.headerDeclarations ?? "");
  const [footerDeclaration, setFooterDeclaration] = useState(invoice?.footerDeclaration ?? "");
  const [authorisedSignatory, setAuthorisedSignatory] = useState(invoice?.authorisedSignatory ?? "");
  const [notes, setNotes] = useState(invoice?.notes ?? "");

  const [rows, setRows] = useState<GoodsRow[]>(
    invoice
      ? invoice.items.map((item) => ({
          key: item.id,
          marksAndNumbers: item.marksAndNumbers ?? "",
          description: item.description,
          detail: item.detail ?? "",
          dimension: item.dimension ?? "",
          hsCode: item.hsCode ?? "",
          netWeight: item.netWeight === null || item.netWeight === undefined ? "" : String(item.netWeight),
          grossWeight:
            item.grossWeight === null || item.grossWeight === undefined ? "" : String(item.grossWeight),
          quantity: String(item.quantity),
          quantityUnit: item.quantityUnit ?? "BOXES",
          rate: String(item.rate),
          rateBasis: item.rateBasis,
        }))
      : [emptyRow()],
  );

  const [errors, setErrors] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    Promise.all([
      api.get<PagedResult<Customer>>("/api/customers?pageSize=200"),
      api.get<TradeInvoiceDefaults>("/api/import-export/profile/defaults"),
    ])
      .then(([customerResult, loaded]) => {
        setCustomers(customerResult.items);
        setDefaults(loaded);

        // Only ever prefills a BLANK field, and only when creating. An edit must show what the
        // document says, not what the settings say today — the two diverge the moment a setting
        // changes, and silently rewriting a saved document on open would be the worse surprise.
        if (invoice) return;
        setPartyName((v) => v || loaded.partyName || "");
        setPartyAddress((v) => v || loaded.partyAddress || "");
        setCountryOfOrigin((v) => v || loaded.countryOfOrigin || "");
        setTermsOfDelivery((v) => v || loaded.termsOfDelivery || "");
        setTermsOfPayment((v) => v || loaded.termsOfPayment || "");
        setPricingTerm((v) => v || loaded.pricingTerm || "");
        setPortOfLoading((v) => v || loaded.portOfLoading || "");
        setPreCarriageBy((v) => v || loaded.preCarriageBy || "");
        setIecNumber((v) => v || loaded.iecNumber || "");
        setGstNumber((v) => v || loaded.gstNumber || "");
        setPanNumber((v) => v || loaded.panNumber || "");
        setApedaRegistrationNumber((v) => v || loaded.apedaRegistrationNumber || "");
        setApedaValidUntil((v) => v || loaded.apedaValidUntil || "");
        setHeaderDeclarations((v) => v || loaded.headerDeclarations || "");
        setFooterDeclaration((v) => v || loaded.footerDeclaration || "");
        setAuthorisedSignatory((v) => v || loaded.authorisedSignatory || "");
        setCurrency((v) => v || loaded.currency);
      })
      .catch((err) => toast(err instanceof Error ? err.message : "Could not load form data.", "error"))
      .finally(() => setLoading(false));
  }, [toast, invoice]);

  /** Picking a customer fills the consignee block in — that is the point of picking one. */
  function onCustomerSelected(id: string) {
    setCustomerId(id);

    const customer = customers.find((c) => c.id === id);
    if (!customer) return;

    // Never overwrites a consignee the user has already typed over.
    setConsigneeName((current) => current || customer.companyName || customer.name);
    setConsigneeAddress((current) => {
      if (current) return current;
      return [customer.addressLine, customer.city, customer.state, customer.postalCode, customer.country]
        .filter(Boolean)
        .join(", ");
    });
    setCountryOfFinalDestination((current) => current || customer.country || "");
  }

  const totals = useMemo(() => {
    let amount = 0;
    let net = 0;
    let gross = 0;
    let packages = 0;

    for (const row of rows) {
      amount += lineTotal(row);
      net += toNumber(row.netWeight);
      gross += toNumber(row.grossWeight);
      packages += toNumber(row.quantity);
    }

    return { amount, net, gross, packages };
  }, [rows]);

  function updateRow(key: string, patch: Partial<GoodsRow>) {
    setRows((current) => current.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  }

  function validate(): string[] {
    const found: string[] = [];
    if (!customerId) found.push("Select a customer.");
    if (!invoiceDate) found.push("Choose a document date.");
    if (dueDate && invoiceDate && dueDate < invoiceDate)
      found.push("Due date must be on or after the document date.");
    if (rows.length === 0) found.push("Add at least one line of goods.");

    rows.forEach((row, index) => {
      const label = row.description.trim() || `Line ${index + 1}`;
      if (!row.description.trim()) found.push(`${label} needs a description of goods.`);
      if (toNumber(row.quantity) <= 0) found.push(`${label}: quantity must be greater than zero.`);
      if (toNumber(row.rate) < 0) found.push(`${label}: rate cannot be negative.`);

      const net = optionalNumber(row.netWeight);
      const gross = optionalNumber(row.grossWeight);
      if (net !== null && gross !== null && gross < net)
        found.push(`${label}: gross weight cannot be less than net weight.`);
      if (row.rateBasis === "PerNetWeight" && (net === null || net <= 0))
        found.push(`${label} is priced per kilogram, so it needs a net weight.`);
    });

    return found;
  }

  function buildPayload() {
    return {
      customerId,
      tradeType,
      documentType,
      invoiceDate,
      dueDate: dueDate || null,
      currency: currency.trim().toUpperCase() || null,
      documentNumber: documentNumber.trim() || null,
      buyerOrderNumber: buyerOrderNumber.trim() || null,
      buyerOrderDate: buyerOrderDate || null,
      otherReferences: otherReferences.trim() || null,
      partyName: partyName.trim() || null,
      partyAddress: partyAddress.trim() || null,
      consignorSameAsParty,
      consignorName: consignorSameAsParty ? null : consignorName.trim() || null,
      consignorAddress: consignorSameAsParty ? null : consignorAddress.trim() || null,
      consigneeName: consigneeName.trim() || null,
      consigneeAddress: consigneeAddress.trim() || null,
      buyerSameAsConsignee,
      buyerName: buyerSameAsConsignee ? null : buyerName.trim() || null,
      buyerAddress: buyerSameAsConsignee ? null : buyerAddress.trim() || null,
      notifyPartyName: notifyPartyName.trim() || null,
      notifyPartyAddress: notifyPartyAddress.trim() || null,
      preCarriageBy: preCarriageBy.trim() || null,
      placeOfReceipt: placeOfReceipt.trim() || null,
      vesselOrFlightNumber: vesselOrFlightNumber.trim() || null,
      portOfLoading: portOfLoading.trim() || null,
      portOfDischarge: portOfDischarge.trim() || null,
      finalDestination: finalDestination.trim() || null,
      countryOfOrigin: countryOfOrigin.trim() || null,
      countryOfFinalDestination: countryOfFinalDestination.trim() || null,
      termsOfDelivery: termsOfDelivery.trim() || null,
      termsOfPayment: termsOfPayment.trim() || null,
      pricingTerm: pricingTerm.trim() || null,
      iecNumber: iecNumber.trim() || null,
      gstNumber: gstNumber.trim() || null,
      panNumber: panNumber.trim() || null,
      apedaRegistrationNumber: apedaRegistrationNumber.trim() || null,
      apedaValidUntil: apedaValidUntil || null,
      headerDeclarations: headerDeclarations.trim() || null,
      footerDeclaration: footerDeclaration.trim() || null,
      authorisedSignatory: authorisedSignatory.trim() || null,
      notes: notes.trim() || null,
      items: rows.map((row) => ({
        marksAndNumbers: row.marksAndNumbers.trim() || null,
        description: row.description.trim(),
        detail: row.detail.trim() || null,
        dimension: row.dimension.trim() || null,
        hsCode: row.hsCode.trim() || null,
        netWeight: optionalNumber(row.netWeight),
        grossWeight: optionalNumber(row.grossWeight),
        quantity: toNumber(row.quantity),
        quantityUnit: row.quantityUnit.trim() || null,
        rate: toNumber(row.rate),
        rateBasis: row.rateBasis,
        rateLabel: null,
        discount: 0,
        taxRate: 0,
      })),
    };
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
      const payload = buildPayload();
      const saved = invoice
        ? await api.put<TradeInvoice>(`/api/import-export/invoices/${invoice.id}`, payload)
        : await api.post<TradeInvoice>("/api/import-export/invoices", payload);

      toast(editing ? "Document updated." : "Document created.", "success");
      router.push(`/import-export/${saved.id}`);
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not save the document.", "error");
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

  const isExport = tradeType === "Export";
  // Both roles named: on an export the exporter IS the consignor, and on an import the importer
  // IS the consignee. A separate consignor box would duplicate this one on almost every document.
  const partyLabel = isExport ? "Exporter / Consignor" : "Importer / Consignee";
  const counterpartyLabel = isExport ? "Consignee" : "Supplier / Exporter";

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
            Add the consignee as a customer
          </Link>{" "}
          before creating this document.
        </div>
      )}

      {/* ---- A. Document ------------------------------------------------ */}
      <Card>
        <CardHeader
          title="Document"
          description="What you are creating, and the references it should carry."
        />
        <CardBody>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Field label="Trade type" htmlFor="tradeType" required>
              <Select
                id="tradeType"
                value={tradeType}
                onChange={(e) => setTradeType(e.target.value as TradeType)}
              >
                {TRADE_TYPES.map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </Select>
            </Field>

            <Field
              label="Document type"
              htmlFor="documentType"
              required
              hint={
                documentType === "ProformaInvoice"
                  ? "An offer. Nothing is owed on it, so it carries no Pay button."
                  : "A demand for payment. Behaves like any other Quotely invoice."
              }
            >
              <Select
                id="documentType"
                value={documentType}
                onChange={(e) => setDocumentType(e.target.value as typeof documentType)}
              >
                {TRADE_DOCUMENT_TYPES.map((value) => (
                  <option key={value} value={value}>
                    {TRADE_DOCUMENT_TYPE_LABELS[value]}
                  </option>
                ))}
              </Select>
            </Field>

            <Field
              label="Document number"
              htmlFor="documentNumber"
              hint={defaults ? `Blank uses ${defaults.suggestedDocumentNumber}` : undefined}
            >
              <Input
                id="documentNumber"
                value={documentNumber}
                onChange={(e) => setDocumentNumber(e.target.value)}
                placeholder={defaults?.suggestedDocumentNumber}
              />
            </Field>

            <Field label="Currency" htmlFor="currency" required>
              <Input
                id="currency"
                value={currency}
                maxLength={3}
                onChange={(e) => setCurrency(e.target.value.toUpperCase())}
              />
            </Field>

            <Field label="Document date" htmlFor="invoiceDate" required>
              <Input
                id="invoiceDate"
                type="date"
                value={invoiceDate}
                onChange={(e) => setInvoiceDate(e.target.value)}
              />
            </Field>

            <Field label="Due date" htmlFor="dueDate">
              <Input
                id="dueDate"
                type="date"
                min={invoiceDate}
                value={dueDate}
                onChange={(e) => setDueDate(e.target.value)}
              />
            </Field>

            <Field label="Buyer's order no." htmlFor="buyerOrderNumber">
              <Input
                id="buyerOrderNumber"
                value={buyerOrderNumber}
                onChange={(e) => setBuyerOrderNumber(e.target.value)}
              />
            </Field>

            <Field label="Buyer's order date" htmlFor="buyerOrderDate">
              <Input
                id="buyerOrderDate"
                type="date"
                value={buyerOrderDate}
                onChange={(e) => setBuyerOrderDate(e.target.value)}
              />
            </Field>

            <Field label="Other reference(s)" htmlFor="otherReferences" className="sm:col-span-2 lg:col-span-4">
              <Input
                id="otherReferences"
                value={otherReferences}
                onChange={(e) => setOtherReferences(e.target.value)}
              />
            </Field>
          </div>
        </CardBody>
      </Card>

      {/* ---- B. Parties -------------------------------------------------- */}
      <Card>
        <CardHeader
          title="Parties"
          description="Who is shipping, who is receiving, and who should be notified on arrival."
        />
        <CardBody className="space-y-5">
          <Field label="Customer" htmlFor="customer" required hint="Fills the consignee block in below.">
            <Select id="customer" value={customerId} onChange={(e) => onCustomerSelected(e.target.value)}>
              <option value="">Select customer…</option>
              {customers.map((customer) => (
                <option key={customer.id} value={customer.id}>
                  {customer.name}
                  {customer.companyName ? ` — ${customer.companyName}` : ""}
                </option>
              ))}
            </Select>
          </Field>

          <div className="grid gap-5 lg:grid-cols-2">
            <div className="space-y-3">
              <Field label={`${partyLabel} name`} htmlFor="partyName">
                <Input id="partyName" value={partyName} onChange={(e) => setPartyName(e.target.value)} />
              </Field>
              <Field label={`${partyLabel} address`} htmlFor="partyAddress">
                <Textarea
                  id="partyAddress"
                  rows={3}
                  value={partyAddress}
                  onChange={(e) => setPartyAddress(e.target.value)}
                />
              </Field>
            </div>

            <div className="space-y-3">
              <Field label={`${counterpartyLabel} name`} htmlFor="consigneeName">
                <Input
                  id="consigneeName"
                  value={consigneeName}
                  onChange={(e) => setConsigneeName(e.target.value)}
                />
              </Field>
              <Field label={`${counterpartyLabel} address`} htmlFor="consigneeAddress">
                <Textarea
                  id="consigneeAddress"
                  rows={3}
                  value={consigneeAddress}
                  onChange={(e) => setConsigneeAddress(e.target.value)}
                />
              </Field>
            </div>
          </div>

          {/*
            Ticked for the overwhelming majority: a business shipping its own goods is also the
            consignor. Unticked is the agency case — arranging a shipment for a client, where the
            goods leave the client's premises and the two are genuinely different parties.
          */}
          <div className="rounded-card border border-ash p-4">
            <label className="flex items-center gap-2.5 text-body text-charcoal">
              <input
                type="checkbox"
                checked={consignorSameAsParty}
                onChange={(e) => setConsignorSameAsParty(e.target.checked)}
                className="h-4 w-4 rounded border-midnight accent-electric"
              />
              We are also the consignor — the goods ship from us
            </label>

            {!consignorSameAsParty && (
              <div className="mt-4 grid gap-3 sm:grid-cols-2">
                <Field
                  label="Consignor name"
                  htmlFor="consignorName"
                  hint="Whoever the goods actually ship from."
                >
                  <Input
                    id="consignorName"
                    value={consignorName}
                    onChange={(e) => setConsignorName(e.target.value)}
                  />
                </Field>
                <Field label="Consignor address" htmlFor="consignorAddress">
                  <Textarea
                    id="consignorAddress"
                    rows={2}
                    value={consignorAddress}
                    onChange={(e) => setConsignorAddress(e.target.value)}
                  />
                </Field>
              </div>
            )}
          </div>

          <div className="rounded-card border border-ash p-4">
            <label className="flex items-center gap-2.5 text-body text-charcoal">
              <input
                type="checkbox"
                checked={buyerSameAsConsignee}
                onChange={(e) => setBuyerSameAsConsignee(e.target.checked)}
                className="h-4 w-4 rounded border-midnight accent-electric"
              />
              {/* "the consignee", not the counterparty: on an import the consignee is the importer,
                  so naming the counterparty would claim the buyer is the overseas supplier. */}
              Buyer is the same as the consignee
            </label>

            {!buyerSameAsConsignee && (
              <div className="mt-4 grid gap-3 sm:grid-cols-2">
                <Field label="Buyer name" htmlFor="buyerName">
                  <Input id="buyerName" value={buyerName} onChange={(e) => setBuyerName(e.target.value)} />
                </Field>
                <Field label="Buyer address" htmlFor="buyerAddress">
                  <Textarea
                    id="buyerAddress"
                    rows={2}
                    value={buyerAddress}
                    onChange={(e) => setBuyerAddress(e.target.value)}
                  />
                </Field>
              </div>
            )}
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Notify party" htmlFor="notifyPartyName">
              <Input
                id="notifyPartyName"
                value={notifyPartyName}
                onChange={(e) => setNotifyPartyName(e.target.value)}
              />
            </Field>
            <Field label="Notify party address" htmlFor="notifyPartyAddress">
              <Textarea
                id="notifyPartyAddress"
                rows={2}
                value={notifyPartyAddress}
                onChange={(e) => setNotifyPartyAddress(e.target.value)}
              />
            </Field>
          </div>
        </CardBody>
      </Card>

      {/* ---- C. Shipment ------------------------------------------------- */}
      <Card>
        <CardHeader
          title="Shipment"
          description="The route the goods take, in the order they take it."
        />
        <CardBody>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <Field label="Pre-carriage by" htmlFor="preCarriageBy">
              <Input
                id="preCarriageBy"
                value={preCarriageBy}
                onChange={(e) => setPreCarriageBy(e.target.value)}
              />
            </Field>
            <Field label="Place of receipt by pre-carrier" htmlFor="placeOfReceipt">
              <Input
                id="placeOfReceipt"
                value={placeOfReceipt}
                onChange={(e) => setPlaceOfReceipt(e.target.value)}
              />
            </Field>
            <Field label="Vessel / flight no." htmlFor="vesselOrFlightNumber">
              <Input
                id="vesselOrFlightNumber"
                value={vesselOrFlightNumber}
                onChange={(e) => setVesselOrFlightNumber(e.target.value)}
              />
            </Field>
            <Field label="Port of loading" htmlFor="portOfLoading">
              <Input
                id="portOfLoading"
                value={portOfLoading}
                onChange={(e) => setPortOfLoading(e.target.value)}
              />
            </Field>
            <Field label="Airport / port of discharge" htmlFor="portOfDischarge">
              <Input
                id="portOfDischarge"
                value={portOfDischarge}
                onChange={(e) => setPortOfDischarge(e.target.value)}
              />
            </Field>
            <Field label="Final destination" htmlFor="finalDestination">
              <Input
                id="finalDestination"
                value={finalDestination}
                onChange={(e) => setFinalDestination(e.target.value)}
              />
            </Field>
            <Field label="Country of origin" htmlFor="countryOfOrigin">
              <Input
                id="countryOfOrigin"
                value={countryOfOrigin}
                onChange={(e) => setCountryOfOrigin(e.target.value)}
              />
            </Field>
            <Field
              label={isExport ? "Country of final destination" : "Country of import"}
              htmlFor="countryOfFinalDestination"
            >
              <Input
                id="countryOfFinalDestination"
                value={countryOfFinalDestination}
                onChange={(e) => setCountryOfFinalDestination(e.target.value)}
              />
            </Field>
          </div>

          <div className="mt-5 grid gap-4 sm:grid-cols-3">
            <Field label="Terms of delivery" htmlFor="termsOfDelivery">
              <Input
                id="termsOfDelivery"
                list="trade-delivery-terms"
                value={termsOfDelivery}
                onChange={(e) => setTermsOfDelivery(e.target.value)}
              />
              {/* A datalist, not a select: these are the common terms, not the only valid ones. */}
              <datalist id="trade-delivery-terms">
                {TRADE_DELIVERY_TERMS.map((term) => (
                  <option key={term} value={term} />
                ))}
              </datalist>
            </Field>
            <Field label="Terms of payment" htmlFor="termsOfPayment">
              <Input
                id="termsOfPayment"
                list="trade-payment-terms"
                value={termsOfPayment}
                onChange={(e) => setTermsOfPayment(e.target.value)}
              />
              <datalist id="trade-payment-terms">
                {TRADE_PAYMENT_TERMS.map((term) => (
                  <option key={term} value={term} />
                ))}
              </datalist>
            </Field>
            <Field
              label="Pricing term"
              htmlFor="pricingTerm"
              hint="Prints beside the rate and amount columns."
            >
              <Input
                id="pricingTerm"
                list="trade-delivery-terms"
                value={pricingTerm}
                onChange={(e) => setPricingTerm(e.target.value)}
              />
            </Field>
          </div>
        </CardBody>
      </Card>

      {/* ---- D. Goods ---------------------------------------------------- */}
      <Card>
        <CardHeader
          title="Goods"
          description="Totals are recalculated by the server when you save."
          action={
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => setRows((r) => [...r, emptyRow()])}
            >
              + Add line
            </Button>
          }
        />

        <div className="divide-y divide-ash">
          {rows.map((row, index) => (
            <div key={row.key} className="px-4 py-4">
              <div className="flex items-center justify-between gap-3 pb-3">
                <span className="text-caption font-semibold uppercase tracking-wide text-fog">
                  Line {index + 1}
                </span>
                <button
                  type="button"
                  onClick={() =>
                    setRows((current) =>
                      current.length === 1 ? [emptyRow()] : current.filter((r) => r.key !== row.key),
                    )
                  }
                  aria-label={rows.length === 1 ? "Clear line 1" : `Remove line ${index + 1}`}
                  title={rows.length === 1 ? "Clear this line" : "Remove this line"}
                  className={cn(
                    "inline-flex h-8 w-8 items-center justify-center rounded-input",
                    "text-fog transition-colors duration-150 ease-out",
                    "hover:bg-rose-wash hover:text-rose-ink",
                  )}
                >
                  <Icon.trash className="h-4 w-4" />
                </button>
              </div>

              {/* Stacks on a phone, six across on a desktop. A nine-column trade table cannot be
                  made usable at 390px, so it is not attempted — the fields are simply labelled. */}
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-6">
                <Field label="Description of goods" htmlFor={`d-${row.key}`} className="lg:col-span-3">
                  <Input
                    id={`d-${row.key}`}
                    value={row.description}
                    onChange={(e) => updateRow(row.key, { description: e.target.value })}
                  />
                </Field>
                <Field label="Marks & nos." htmlFor={`m-${row.key}`}>
                  <Input
                    id={`m-${row.key}`}
                    value={row.marksAndNumbers}
                    onChange={(e) => updateRow(row.key, { marksAndNumbers: e.target.value })}
                  />
                </Field>
                <Field label="HS code" htmlFor={`h-${row.key}`}>
                  <Input
                    id={`h-${row.key}`}
                    inputMode="numeric"
                    value={row.hsCode}
                    onChange={(e) => updateRow(row.key, { hsCode: e.target.value })}
                  />
                </Field>
                <Field label="Dimension" htmlFor={`dim-${row.key}`}>
                  <Input
                    id={`dim-${row.key}`}
                    value={row.dimension}
                    onChange={(e) => updateRow(row.key, { dimension: e.target.value })}
                  />
                </Field>

                <Field label="Net weight" htmlFor={`nw-${row.key}`}>
                  <Input
                    id={`nw-${row.key}`}
                    type="number"
                    min={0}
                    step="0.001"
                    inputMode="decimal"
                    value={row.netWeight}
                    onChange={(e) => updateRow(row.key, { netWeight: e.target.value })}
                  />
                </Field>
                <Field label="Gross weight" htmlFor={`gw-${row.key}`}>
                  <Input
                    id={`gw-${row.key}`}
                    type="number"
                    min={0}
                    step="0.001"
                    inputMode="decimal"
                    value={row.grossWeight}
                    onChange={(e) => updateRow(row.key, { grossWeight: e.target.value })}
                  />
                </Field>
                <Field label="Quantity" htmlFor={`q-${row.key}`}>
                  <Input
                    id={`q-${row.key}`}
                    type="number"
                    min={0}
                    step="0.001"
                    inputMode="decimal"
                    value={row.quantity}
                    onChange={(e) => updateRow(row.key, { quantity: e.target.value })}
                  />
                </Field>
                <Field label="Unit" htmlFor={`u-${row.key}`}>
                  <Input
                    id={`u-${row.key}`}
                    list="trade-quantity-units"
                    value={row.quantityUnit}
                    onChange={(e) => updateRow(row.key, { quantityUnit: e.target.value.toUpperCase() })}
                  />
                  <datalist id="trade-quantity-units">
                    {TRADE_QUANTITY_UNITS.map((unit) => (
                      <option key={unit} value={unit} />
                    ))}
                  </datalist>
                </Field>
                <Field label="Rate" htmlFor={`r-${row.key}`}>
                  <Input
                    id={`r-${row.key}`}
                    type="number"
                    min={0}
                    step="0.01"
                    inputMode="decimal"
                    value={row.rate}
                    onChange={(e) => updateRow(row.key, { rate: e.target.value })}
                  />
                </Field>
                <Field
                  label="Rate applies to"
                  htmlFor={`rb-${row.key}`}
                  hint={
                    row.rateBasis === "PerNetWeight"
                      ? "Rate × net weight"
                      : `Rate × quantity (${row.quantityUnit || "units"})`
                  }
                >
                  <Select
                    id={`rb-${row.key}`}
                    value={row.rateBasis}
                    onChange={(e) =>
                      updateRow(row.key, { rateBasis: e.target.value as TradeRateBasis })
                    }
                  >
                    <option value="PerQuantityUnit">Each {row.quantityUnit || "unit"}</option>
                    <option value="PerNetWeight">Per unit of net weight</option>
                  </Select>
                </Field>

                <Field label="Detail (optional)" htmlFor={`det-${row.key}`} className="lg:col-span-4">
                  <Input
                    id={`det-${row.key}`}
                    value={row.detail}
                    onChange={(e) => updateRow(row.key, { detail: e.target.value })}
                  />
                </Field>

                <div className="lg:col-span-2 lg:pt-6">
                  <div className="rounded-input bg-paper px-3 py-2 text-right">
                    <p className="text-caption text-fog">Line total</p>
                    <p className="text-body font-semibold text-charcoal">
                      {formatMoney(lineTotal(row), currency)}
                    </p>
                  </div>
                </div>
              </div>
            </div>
          ))}
        </div>

        <CardBody className="border-t border-ash bg-paper">
          <div className="grid gap-3 sm:grid-cols-4">
            <Summary label="Total net weight" value={totals.net.toFixed(2)} />
            <Summary label="Total gross weight" value={totals.gross.toFixed(2)} />
            <Summary label="Total packages" value={String(totals.packages)} />
            <Summary label="Total amount" value={formatMoney(totals.amount, currency)} strong />
          </div>
          <p className="mt-3 text-caption text-fog">
            A preview. The server recalculates every figure — and the amount in words — when you save.
          </p>
        </CardBody>
      </Card>

      {/* ---- E. Identifiers and declarations ------------------------------ */}
      <Card>
        <CardHeader
          title="Registrations & declarations"
          description="Filled in from your Import / Export settings, and editable for this document."
          action={
            <Link href="/settings/trade" className="text-body text-electric hover:underline">
              Edit settings
            </Link>
          }
        />
        <CardBody className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
            <Field label="IEC no." htmlFor="iecNumber">
              <Input id="iecNumber" value={iecNumber} onChange={(e) => setIecNumber(e.target.value)} />
            </Field>
            <Field label="GST no." htmlFor="gstNumber">
              <Input id="gstNumber" value={gstNumber} onChange={(e) => setGstNumber(e.target.value)} />
            </Field>
            <Field label="PAN no." htmlFor="panNumber">
              <Input id="panNumber" value={panNumber} onChange={(e) => setPanNumber(e.target.value)} />
            </Field>
            <Field label="APEDA registration" htmlFor="apeda">
              <Input
                id="apeda"
                value={apedaRegistrationNumber}
                onChange={(e) => setApedaRegistrationNumber(e.target.value)}
              />
            </Field>
            <Field label="APEDA valid until" htmlFor="apedaValidUntil">
              <Input
                id="apedaValidUntil"
                type="date"
                value={apedaValidUntil}
                onChange={(e) => setApedaValidUntil(e.target.value)}
              />
            </Field>
          </div>

          <Field
            label="Header declarations"
            htmlFor="headerDeclarations"
            hint="One per line. Printed under the title. Your words — Quotely does not give tax advice."
          >
            <Textarea
              id="headerDeclarations"
              rows={2}
              value={headerDeclarations}
              onChange={(e) => setHeaderDeclarations(e.target.value)}
            />
          </Field>

          <Field label="Declaration" htmlFor="footerDeclaration" hint="Printed at the foot of the document.">
            <Textarea
              id="footerDeclaration"
              rows={2}
              value={footerDeclaration}
              onChange={(e) => setFooterDeclaration(e.target.value)}
            />
          </Field>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Authorised signatory" htmlFor="authorisedSignatory">
              <Input
                id="authorisedSignatory"
                value={authorisedSignatory}
                onChange={(e) => setAuthorisedSignatory(e.target.value)}
              />
            </Field>
            <Field label="Internal notes" htmlFor="notes">
              <Input id="notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
            </Field>
          </div>
        </CardBody>
      </Card>

      <div className="flex flex-wrap justify-end gap-2">
        <Link href={invoice ? `/import-export/${invoice.id}` : "/import-export"}>
          <Button variant="secondary" type="button">
            Cancel
          </Button>
        </Link>
        <Button type="button" onClick={onSave} disabled={saving}>
          {saving ? "Saving…" : editing ? "Save changes" : "Create document"}
        </Button>
      </div>
    </div>
  );
}

function Summary({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div>
      <p className="text-caption text-fog">{label}</p>
      <p className={cn("text-body text-charcoal", strong && "font-semibold")}>{value}</p>
    </div>
  );
}
