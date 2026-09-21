"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useRouteParam } from "@/lib/route-param";
import { api, saveBlob } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { InvoiceStatusBadge } from "@/components/ui/badge";
import { DetailSkeleton, ErrorState } from "@/components/ui/states";
import { Icon } from "@/components/ui/icons";
import { Mono, Table, TableWrap, Td, Th, Tr } from "@/components/ui/table";
import { PageHeader } from "@/components/app/page-header";
import { ShareDialog } from "@/components/app/share-dialog";
import {
  TRADE_DOCUMENT_TYPE_LABELS,
  type DocumentShare,
  type TradeInvoice,
} from "@/types";

export default function TradeInvoiceDetailPage() {
  const id = useRouteParam("/import-export/[id]", "id");
  const router = useRouter();
  const toast = useToast();

  const [invoice, setInvoice] = useState<TradeInvoice | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [sharing, setSharing] = useState(false);
  const [share, setShare] = useState<DocumentShare | null>(null);
  const [shareOpen, setShareOpen] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const load = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    try {
      setInvoice(await api.get<TradeInvoice>(`/api/import-export/invoices/${id}`));
    } catch (err) {
      setError(err instanceof Error ? err.message : "We couldn't load this document.");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  async function download() {
    setDownloading(true);
    try {
      const { blob, fileName } = await api.downloadTradeInvoicePdf(id);
      saveBlob(blob, fileName);
      toast("PDF downloaded", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not generate the PDF.", "error");
    } finally {
      setDownloading(false);
    }
  }

  async function createShare() {
    setSharing(true);
    try {
      // The same public-link machinery a domestic invoice uses — one token scheme, one page.
      const link = await api.post<DocumentShare>(
        `/api/import-export/invoices/${id}/public-link`,
        {},
      );
      setShare(link);
      setShareOpen(true);
      await load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not create the link.", "error");
    } finally {
      setSharing(false);
    }
  }

  async function remove() {
    try {
      await api.delete(`/api/import-export/invoices/${id}`);
      toast("Document deleted.", "success");
      router.push("/import-export");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not delete the document.", "error");
    }
  }

  if (loading) return <DetailSkeleton />;
  if (error) return <ErrorState message={error} onRetry={load} />;
  if (!invoice) return null;

  const isExport = invoice.tradeType === "Export";
  // The combined label would contradict a Consignor block two boxes lower.
  const consignorDiffers = !invoice.consignorSameAsParty && Boolean(invoice.consignorName);
  const partyLabel = consignorDiffers
    ? isExport
      ? "Exporter"
      : "Importer"
    : isExport
      ? "Exporter / Consignor"
      : "Importer / Consignee";
  const counterpartyLabel = isExport ? "Consignee" : "Supplier / Exporter";

  return (
    <>
      <PageHeader
        title={TRADE_DOCUMENT_TYPE_LABELS[invoice.documentType]}
        description={`${invoice.documentNumber || invoice.invoiceNumber} · ${invoice.tradeType} · ${formatDate(invoice.invoiceDate)}`}
        action={
          <div className="flex flex-wrap items-center gap-2">
            <InvoiceStatusBadge status={invoice.status} />
            <Button variant="secondary" loading={downloading} onClick={download}>
              <Icon.download className="h-4 w-4" />
              PDF
            </Button>
            <Button variant="secondary" loading={sharing} onClick={createShare}>
              <Icon.link className="h-4 w-4" />
              Share
            </Button>
            {invoice.canEdit && (
              <Link href={`/import-export/${invoice.id}/edit`}>
                <Button variant="secondary">
                  <Icon.edit className="h-4 w-4" />
                  Edit
                </Button>
              </Link>
            )}
            {invoice.canDelete && (
              <Button variant="ghost" onClick={() => setConfirmDelete(true)}>
                <Icon.trash className="h-4 w-4" />
              </Button>
            )}
          </div>
        }
      />

      {/*
        A proforma is an offer describing a shipment that has not happened. Said plainly here so
        nobody wonders why there is no Pay button — the absence is deliberate, not a missing feature.
      */}
      {!invoice.isPayable && (
        <div className="mb-6 rounded-card border border-ash bg-paper px-4 py-3 text-body text-steel">
          A proforma invoice states what a shipment will contain and what it will cost. Nothing is
          owed on it, so it carries no payment link. Raise a{" "}
          <Link href={`/import-export/${invoice.id}/edit`} className="text-electric hover:underline">
            commercial invoice
          </Link>{" "}
          when the goods ship.
        </div>
      )}

      <div className="space-y-6">
        <Card>
          <CardHeader title="Parties" />
          <CardBody className="grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
            <Party label={partyLabel} name={invoice.partyName} address={invoice.partyAddress} />
            {!invoice.consignorSameAsParty && invoice.consignorName && (
              <Party
                label="Consignor"
                name={invoice.consignorName}
                address={invoice.consignorAddress}
              />
            )}
            <Party
              label={counterpartyLabel}
              name={invoice.consigneeName}
              address={invoice.consigneeAddress}
            />
            <Party
              label="Buyer"
              name={invoice.buyerSameAsConsignee ? "Same as consignee" : invoice.buyerName}
              address={invoice.buyerSameAsConsignee ? null : invoice.buyerAddress}
            />
            <Party
              label="Notify party"
              name={invoice.notifyPartyName}
              address={invoice.notifyPartyAddress}
            />
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Shipment" />
          <CardBody className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Meta label="Pre-carriage by" value={invoice.preCarriageBy} />
            <Meta label="Place of receipt" value={invoice.placeOfReceipt} />
            <Meta label="Vessel / flight" value={invoice.vesselOrFlightNumber} />
            <Meta label="Port of loading" value={invoice.portOfLoading} />
            <Meta label="Port of discharge" value={invoice.portOfDischarge} />
            <Meta label="Final destination" value={invoice.finalDestination} />
            <Meta label="Country of origin" value={invoice.countryOfOrigin} />
            <Meta
              label={isExport ? "Country of final destination" : "Country of import"}
              value={invoice.countryOfFinalDestination}
            />
            <Meta label="Terms of delivery" value={invoice.termsOfDelivery} />
            <Meta label="Terms of payment" value={invoice.termsOfPayment} />
            <Meta label="Buyer's order no." value={invoice.buyerOrderNumber} />
            <Meta label="Other reference(s)" value={invoice.otherReferences} />
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Goods" />
          <TableWrap>
            <Table>
              <thead>
                <tr>
                  <Th>Marks</Th>
                  <Th>Description</Th>
                  <Th>HS code</Th>
                  <Th align="right">Net wt</Th>
                  <Th align="right">Gross wt</Th>
                  <Th align="right">Qty</Th>
                  <Th align="right">Rate</Th>
                  <Th align="right">Amount</Th>
                </tr>
              </thead>
              <tbody>
                {invoice.items.map((item) => (
                  <Tr key={item.id}>
                    <Td>{item.marksAndNumbers || "—"}</Td>
                    <Td className="text-charcoal">
                      {item.description}
                      {item.detail && <p className="text-caption text-fog">{item.detail}</p>}
                      {item.dimension && (
                        <p className="text-caption text-fog">Dimension {item.dimension}</p>
                      )}
                    </Td>
                    <Td>
                      <Mono>{item.hsCode || "—"}</Mono>
                    </Td>
                    <Td align="right">{item.netWeight ?? "—"}</Td>
                    <Td align="right">{item.grossWeight ?? "—"}</Td>
                    <Td align="right">
                      {item.quantity} {item.quantityUnit}
                    </Td>
                    <Td align="right">{formatMoney(item.rate, invoice.currency)}</Td>
                    <Td align="right" className="font-medium text-charcoal">
                      {formatMoney(item.lineTotal, invoice.currency)}
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          </TableWrap>

          <CardBody className="border-t border-ash bg-paper">
            <div className="grid gap-4 sm:grid-cols-4">
              <Meta
                label={`Total net weight (${invoice.weightUnit})`}
                value={String(invoice.totalNetWeight)}
              />
              <Meta
                label={`Total gross weight (${invoice.weightUnit})`}
                value={String(invoice.totalGrossWeight)}
              />
              <Meta label="Total packages" value={String(invoice.totalPackages)} />
              <div>
                <p className="text-caption text-fog">Total amount</p>
                <p className="text-body-lg font-semibold text-charcoal">
                  {formatMoney(invoice.grandTotal, invoice.currency)}
                </p>
              </div>
            </div>
            <p className="mt-4 text-body text-steel">
              <span className="text-caption text-fog">Amount chargeable (in words): </span>
              <span className="font-medium text-charcoal">{invoice.amountInWords}</span>
            </p>
          </CardBody>
        </Card>

        {(invoice.iecNumber ||
          invoice.gstNumber ||
          invoice.panNumber ||
          invoice.apedaRegistrationNumber ||
          invoice.headerDeclarations ||
          invoice.footerDeclaration) && (
          <Card>
            <CardHeader title="Registrations & declarations" />
            <CardBody className="space-y-4">
              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
                <Meta label="IEC no." value={invoice.iecNumber} />
                <Meta label="GST no." value={invoice.gstNumber} />
                <Meta label="PAN no." value={invoice.panNumber} />
                <Meta
                  label="APEDA registration"
                  value={
                    invoice.apedaRegistrationNumber
                      ? invoice.apedaValidUntil
                        ? `${invoice.apedaRegistrationNumber} · valid to ${formatDate(invoice.apedaValidUntil)}`
                        : invoice.apedaRegistrationNumber
                      : null
                  }
                />
              </div>
              {invoice.headerDeclarations && (
                <Meta label="Header declarations" value={invoice.headerDeclarations} />
              )}
              {invoice.footerDeclaration && (
                <Meta label="Declaration" value={invoice.footerDeclaration} />
              )}
              <Meta label="Authorised signatory" value={invoice.authorisedSignatory} />
            </CardBody>
          </Card>
        )}
      </div>

      <ShareDialog
        open={shareOpen}
        share={share}
        title="Share document"
        onClose={() => setShareOpen(false)}
      />

      <ConfirmDialog
        open={confirmDelete}
        title="Delete this document?"
        description="This cannot be undone."
        confirmLabel="Delete"
        onConfirm={() => {
          setConfirmDelete(false);
          void remove();
        }}
        onCancel={() => setConfirmDelete(false)}
      />
    </>
  );
}

function Meta({ label, value }: { label: string; value?: string | null }) {
  return (
    <div className="min-w-0">
      <p className="text-caption text-fog">{label}</p>
      <p className="whitespace-pre-line text-body text-charcoal">{value || "—"}</p>
    </div>
  );
}

function Party({
  label,
  name,
  address,
}: {
  label: string;
  name?: string | null;
  address?: string | null;
}) {
  return (
    <div className="min-w-0">
      <p className="text-caption text-fog">{label}</p>
      <p className="text-body font-medium text-charcoal">{name || "—"}</p>
      {address && <p className="mt-0.5 text-body text-steel">{address}</p>}
    </div>
  );
}
