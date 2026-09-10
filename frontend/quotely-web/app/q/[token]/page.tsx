"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { ApiError, publicApi } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/states";
import { ResponseDialog, type ResponseKind } from "@/components/public/response-dialog";
import type { PublicQuotation, PublicResponseRequest } from "@/types";

type Phase = "loading" | "ready" | "notFound" | "error";

export default function PublicQuotationPage() {
  const { token } = useParams<{ token: string }>();

  const [quotation, setQuotation] = useState<PublicQuotation | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [loadError, setLoadError] = useState<string | null>(null);

  const [dialog, setDialog] = useState<ResponseKind | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [justResponded, setJustResponded] = useState(false);

  const load = useCallback(async () => {
    setPhase("loading");
    setLoadError(null);
    try {
      setQuotation(await publicApi.get<PublicQuotation>(`/api/public/quotations/${encodeURIComponent(token)}`));
      setPhase("ready");
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        setPhase("notFound");
        return;
      }
      setLoadError(err instanceof Error ? err.message : "This quotation could not be loaded.");
      setPhase("error");
    }
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  async function respond(kind: ResponseKind, payload: PublicResponseRequest) {
    setSubmitting(true);
    setSubmitError(null);
    try {
      const updated = await publicApi.post<PublicQuotation>(
        `/api/public/quotations/${encodeURIComponent(token)}/${kind}`,
        payload,
      );
      setQuotation(updated);
      setJustResponded(true);
      setDialog(null);
      window.scrollTo({ top: 0, behavior: "smooth" });
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        // Someone already answered, or it expired while the page was open — show the truth.
        setSubmitError(err.message);
        void load();
      } else {
        setSubmitError(err instanceof Error ? err.message : "Your response could not be recorded.");
      }
    } finally {
      setSubmitting(false);
    }
  }

  if (phase === "loading") {
    return (
      <Shell>
        <div className="flex flex-col items-center gap-3 py-24 text-sm text-slate-500">
          <Spinner />
          Loading quotation…
        </div>
      </Shell>
    );
  }

  if (phase === "notFound") {
    return (
      <Shell>
        <Notice
          tone="neutral"
          title="Quotation not found"
          message="This quotation link is invalid or no longer available. Please ask the business for a new link."
        />
      </Shell>
    );
  }

  if (phase === "error" || !quotation) {
    return (
      <Shell>
        <Notice
          tone="error"
          title="Something went wrong"
          message={loadError ?? "This quotation could not be loaded."}
          action={
            <Button variant="secondary" onClick={load}>
              Try again
            </Button>
          }
        />
      </Shell>
    );
  }

  const { business, customer, currency } = quotation;
  const responded = quotation.status === "Accepted" || quotation.status === "Rejected";

  const businessAddress = [
    business.addressLine,
    [business.city, business.state, business.postalCode].filter(Boolean).join(", "),
    business.country,
  ].filter(Boolean) as string[];

  const customerAddress = [
    customer.addressLine,
    [customer.city, customer.state, customer.postalCode].filter(Boolean).join(", "),
    customer.country,
  ].filter(Boolean) as string[];

  return (
    <Shell>
      {justResponded && (
        <Banner
          tone={quotation.status === "Accepted" ? "success" : "neutral"}
          title={quotation.status === "Accepted" ? "Quotation accepted" : "Quotation rejected"}
          message={
            quotation.status === "Accepted"
              ? "Thank you. Your acceptance has been recorded."
              : "Your response has been recorded."
          }
          detail={quotation.respondedAt ? `Recorded on ${formatDateTime(quotation.respondedAt)}` : undefined}
        />
      )}

      {!justResponded && responded && (
        <Banner
          tone={quotation.status === "Accepted" ? "success" : "neutral"}
          title={quotation.status === "Accepted" ? "Quotation accepted" : "Quotation rejected"}
          message={
            quotation.status === "Accepted"
              ? "No further response is required."
              : "This quotation was rejected. No further response is required."
          }
          detail={quotation.respondedAt ? `${quotation.status} on ${formatDateTime(quotation.respondedAt)}` : undefined}
        />
      )}

      {!responded && quotation.isExpired && (
        <Banner
          tone="warning"
          title="This quotation has expired"
          message={`It was valid until ${formatDate(quotation.validUntil)}. Please contact the business for an updated quotation.`}
        />
      )}

      <article className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
        <header className="border-b border-slate-200 px-5 py-6 sm:px-8">
          <div className="flex flex-col gap-5 sm:flex-row sm:items-start sm:justify-between">
            <div className="min-w-0">
              {business.logoUrl && (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={business.logoUrl} alt="" className="mb-3 h-12 object-contain" />
              )}
              <h1 className="text-lg font-semibold text-slate-900 sm:text-xl">{business.businessName}</h1>
              {businessAddress.map((line) => (
                <p key={line} className="text-sm text-slate-500">
                  {line}
                </p>
              ))}
              {business.phone && <p className="text-sm text-slate-500">Phone: {business.phone}</p>}
              {business.email && <p className="break-words text-sm text-slate-500">Email: {business.email}</p>}
              {business.taxNumber && <p className="text-sm text-slate-500">Tax / GST: {business.taxNumber}</p>}
            </div>

            <div className="shrink-0 sm:text-right">
              <p className="text-xl font-bold tracking-wide text-blue-600 sm:text-2xl">QUOTATION</p>
              <p className="mt-0.5 font-semibold text-slate-900">{quotation.quotationNumber}</p>
              <p className="mt-3 text-sm text-slate-500">Date: {formatDate(quotation.quotationDate)}</p>
              <p className="text-sm text-slate-500">Valid until: {formatDate(quotation.validUntil)}</p>
            </div>
          </div>
        </header>

        <section className="border-b border-slate-200 px-5 py-5 sm:px-8">
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Prepared for</p>
          <p className="mt-1 font-semibold text-slate-900">{customer.name}</p>
          {customer.companyName && <p className="text-sm text-slate-700">{customer.companyName}</p>}
          {customerAddress.map((line) => (
            <p key={line} className="text-sm text-slate-500">
              {line}
            </p>
          ))}
          {(customer.phone || customer.email) && (
            <p className="break-words text-sm text-slate-500">
              {[customer.phone, customer.email].filter(Boolean).join("  •  ")}
            </p>
          )}
        </section>

        {/* Wide table on tablet and up; stacked cards on a phone so nothing needs zooming. */}
        <section className="px-5 py-5 sm:px-8">
          <div className="hidden overflow-x-auto sm:block">
            <table className="w-full border-collapse text-sm">
              <thead>
                <tr className="bg-blue-50/70">
                  <Th>Description</Th>
                  <Th align="right">Qty</Th>
                  <Th align="right">Unit price</Th>
                  <Th align="right">Discount</Th>
                  <Th align="right">Tax</Th>
                  <Th align="right">Total</Th>
                </tr>
              </thead>
              <tbody>
                {quotation.items.map((item, index) => (
                  <tr key={`${item.name}-${index}`} className="border-b border-slate-100 align-top">
                    <td className="px-3 py-3">
                      <p className="font-medium text-slate-900">{item.name}</p>
                      {item.description && <p className="mt-0.5 text-xs text-slate-500">{item.description}</p>}
                    </td>
                    <td className="px-3 py-3 text-right text-slate-700 whitespace-nowrap">
                      {item.quantity} {item.unit}
                    </td>
                    <td className="px-3 py-3 text-right text-slate-700 whitespace-nowrap">
                      {formatMoney(item.unitPrice, currency)}
                    </td>
                    <td className="px-3 py-3 text-right text-slate-700 whitespace-nowrap">
                      {item.discount > 0 ? formatMoney(item.discount, currency) : "—"}
                    </td>
                    <td className="px-3 py-3 text-right text-slate-700">{item.taxRate}%</td>
                    <td className="px-3 py-3 text-right font-medium text-slate-900 whitespace-nowrap">
                      {formatMoney(item.lineTotal, currency)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <ul className="space-y-3 sm:hidden">
            {quotation.items.map((item, index) => (
              <li key={`${item.name}-${index}`} className="rounded-xl border border-slate-200 p-3">
                <p className="font-medium text-slate-900">{item.name}</p>
                {item.description && <p className="mt-0.5 text-xs text-slate-500">{item.description}</p>}
                <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-1.5 text-sm">
                  <Row label="Qty" value={`${item.quantity} ${item.unit}`} />
                  <Row label="Unit price" value={formatMoney(item.unitPrice, currency)} />
                  {item.discount > 0 && <Row label="Discount" value={formatMoney(item.discount, currency)} />}
                  <Row label="Tax" value={`${item.taxRate}%`} />
                </dl>
                <div className="mt-3 flex items-center justify-between border-t border-slate-100 pt-2">
                  <span className="text-xs font-medium uppercase tracking-wide text-slate-500">Line total</span>
                  <span className="font-semibold text-slate-900">{formatMoney(item.lineTotal, currency)}</span>
                </div>
              </li>
            ))}
          </ul>

          <div className="mt-6 flex justify-end">
            <dl className="w-full space-y-2 text-sm sm:max-w-xs">
              <Total label="Subtotal" value={formatMoney(quotation.subtotal, currency)} />
              {quotation.discountTotal > 0 && (
                <Total label="Discount" value={`-${formatMoney(quotation.discountTotal, currency)}`} />
              )}
              <Total label="Tax" value={formatMoney(quotation.taxTotal, currency)} />
              <div className="mt-2 flex items-center justify-between rounded-lg bg-blue-50 px-3 py-3">
                <dt className="text-sm font-semibold text-blue-700">TOTAL</dt>
                <dd className="text-lg font-bold text-blue-700">{formatMoney(quotation.grandTotal, currency)}</dd>
              </div>
            </dl>
          </div>
        </section>

        {(quotation.notes || quotation.terms) && (
          <section className="space-y-5 border-t border-slate-200 px-5 py-5 sm:px-8">
            {quotation.notes && (
              <div>
                <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Notes</p>
                <p className="mt-1 whitespace-pre-line text-sm text-slate-700">{quotation.notes}</p>
              </div>
            )}
            {quotation.terms && (
              <div>
                <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Terms &amp; conditions</p>
                <p className="mt-1 whitespace-pre-line text-sm text-slate-700">{quotation.terms}</p>
              </div>
            )}
          </section>
        )}

        <footer className="border-t border-slate-200 bg-slate-50 px-5 py-5 sm:px-8">
          {quotation.canRespond ? (
            <>
              <p className="text-sm text-slate-600">
                Please review the quotation and let {business.businessName} know your decision.
              </p>
              {submitError && (
                <p role="alert" className="mt-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
                  {submitError}
                </p>
              )}
              <div className="mt-4 flex flex-col gap-2 sm:flex-row">
                <Button className="h-12 w-full sm:h-10 sm:w-auto" onClick={() => setDialog("accept")}>
                  Accept quotation
                </Button>
                <Button
                  variant="secondary"
                  className="h-12 w-full sm:h-10 sm:w-auto"
                  onClick={() => setDialog("reject")}
                >
                  Reject quotation
                </Button>
              </div>
            </>
          ) : (
            <p className="text-sm text-slate-600">
              {responded
                ? "This quotation has already been answered."
                : "This quotation is no longer open for a response."}
            </p>
          )}

          <a
            href={publicApi.pdfUrl(token)}
            target="_blank"
            rel="noopener noreferrer"
            className="mt-4 inline-block text-sm font-medium text-blue-600 underline-offset-4 hover:underline"
          >
            Download PDF
          </a>
        </footer>
      </article>

      {dialog && (
        <ResponseDialog
          kind={dialog}
          submitting={submitting}
          error={submitError}
          defaultName={customer.name}
          defaultEmail={customer.email ?? ""}
          onSubmit={(payload) => respond(dialog, payload)}
          onCancel={() => {
            setDialog(null);
            setSubmitError(null);
          }}
        />
      )}
    </Shell>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-slate-50 px-3 py-5 sm:px-6 sm:py-10">
      <div className="mx-auto w-full max-w-3xl space-y-4">
        {children}
        <p className="pb-6 pt-2 text-center text-xs text-slate-400">
          Sent with Quote<span className="text-blue-500">ly</span>
        </p>
      </div>
    </div>
  );
}

const BANNER_TONES = {
  success: "border-emerald-200 bg-emerald-50 text-emerald-900",
  warning: "border-amber-200 bg-amber-50 text-amber-900",
  neutral: "border-slate-200 bg-white text-slate-900",
  error: "border-red-200 bg-red-50 text-red-900",
} as const;

function Banner({
  tone,
  title,
  message,
  detail,
}: {
  tone: keyof typeof BANNER_TONES;
  title: string;
  message: string;
  detail?: string;
}) {
  return (
    <div role="status" className={`rounded-2xl border px-5 py-4 ${BANNER_TONES[tone]}`}>
      <p className="font-semibold">{title}</p>
      <p className="mt-0.5 text-sm opacity-90">{message}</p>
      {detail && <p className="mt-1 text-xs opacity-75">{detail}</p>}
    </div>
  );
}

function Notice({
  tone,
  title,
  message,
  action,
}: {
  tone: keyof typeof BANNER_TONES;
  title: string;
  message: string;
  action?: React.ReactNode;
}) {
  return (
    <div className={`rounded-2xl border px-6 py-12 text-center ${BANNER_TONES[tone]}`}>
      <h1 className="text-lg font-semibold">{title}</h1>
      <p className="mx-auto mt-2 max-w-sm text-sm opacity-90">{message}</p>
      {action && <div className="mt-5 flex justify-center">{action}</div>}
    </div>
  );
}

function Th({ children, align = "left" }: { children: React.ReactNode; align?: "left" | "right" }) {
  return (
    <th
      scope="col"
      className={`px-3 py-2 text-xs font-semibold uppercase tracking-wide text-blue-700 ${
        align === "right" ? "text-right" : "text-left"
      }`}
    >
      {children}
    </th>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <>
      <dt className="text-slate-500">{label}</dt>
      <dd className="text-right text-slate-800">{value}</dd>
    </>
  );
}

function Total({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between">
      <dt className="text-slate-500">{label}</dt>
      <dd className="text-slate-900">{value}</dd>
    </div>
  );
}

function formatDateTime(iso: string) {
  const date = new Date(iso);
  return date.toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });
}
