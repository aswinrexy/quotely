"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiError, publicApi } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/states";
import { StatusBadge } from "@/components/ui/badge";
import { Icon } from "@/components/ui/icons";
import { Mono } from "@/components/ui/table";
import {
  DocumentNotes,
  LineItems,
  MetaItem,
  PartyBlock,
  Totals,
} from "@/components/app/document-view";
import { ResponseDialog, type ResponseKind } from "@/components/public/response-dialog";
import type { PublicQuotation, PublicResponseRequest } from "@/types";
import { useRouteParam } from "@/lib/route-param";

type Phase = "loading" | "ready" | "notFound" | "error";

export default function PublicQuotationPage() {
  const token = useRouteParam("/q/[token]", "token");

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
        <div className="flex flex-col items-center gap-3 py-24 text-body text-fog">
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
              ? "Thank you. Your acceptance has been sent to the business."
              : "Your response has been sent to the business."
          }
          detail={quotation.respondedAt ? `Recorded on ${formatDateTime(quotation.respondedAt)}` : undefined}
        />
      )}

      {!justResponded && responded && (
        <Banner
          tone={quotation.status === "Accepted" ? "success" : "neutral"}
          title={quotation.status === "Accepted" ? "Already accepted" : "Already rejected"}
          message="This quotation has been answered and can no longer be changed here."
          detail={
            quotation.respondedAt
              ? `${quotation.respondedByName ?? "Answered"} · ${formatDateTime(quotation.respondedAt)}`
              : undefined
          }
        />
      )}

      {!responded && quotation.isExpired && (
        <Banner
          tone="warning"
          title="This quotation has expired"
          message={`It was valid until ${formatDate(quotation.validUntil)}. Please contact ${business.businessName} for an updated quotation.`}
        />
      )}

      {/* The document itself: a white sheet on a paper-mist canvas. */}
      <article className="overflow-hidden rounded-lgcard border border-ash bg-canvas">
        <header className="border-b border-ash p-5 sm:p-8">
          <div className="flex flex-col gap-6 sm:flex-row sm:items-start sm:justify-between">
            <div className="min-w-0">
              {business.logoUrl && (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={business.logoUrl} alt="" className="mb-3 h-11 object-contain" />
              )}
              <h1 className="text-body-xl font-semibold text-charcoal">{business.businessName}</h1>
              {businessAddress.map((line) => (
                <p key={line} className="text-body text-fog">
                  {line}
                </p>
              ))}
              {business.phone && <p className="text-body text-fog">{business.phone}</p>}
              {business.email && <p className="break-words text-body text-fog">{business.email}</p>}
              {business.taxNumber && (
                <p className="text-body text-fog">Tax / GST: {business.taxNumber}</p>
              )}
            </div>

            <div className="shrink-0 sm:text-right">
              <p className="text-caption font-medium uppercase tracking-wide text-fog">Quotation</p>
              <p className="mt-1 font-display text-heading-sm text-charcoal">
                <Mono className="text-heading-sm">{quotation.quotationNumber}</Mono>
              </p>
              <dl className="mt-4 grid gap-2.5 sm:justify-items-end">
                <MetaItem label="Date">{formatDate(quotation.quotationDate)}</MetaItem>
                <MetaItem label="Valid until">{formatDate(quotation.validUntil)}</MetaItem>
              </dl>
              <div className="mt-3 flex sm:justify-end">
                <StatusBadge status={quotation.status} />
              </div>
            </div>
          </div>
        </header>

        <section className="border-b border-ash p-5 sm:p-8">
          <PartyBlock
            label="Prepared for"
            name={customer.name}
            company={customer.companyName}
            lines={customerAddress}
            contact={[customer.phone, customer.email]}
          />
        </section>

        <section className="p-5 sm:p-8">
          <LineItems items={quotation.items} currency={currency} />

          <div className="mt-6 flex justify-end">
            <Totals
              rows={[
                { label: "Subtotal", value: formatMoney(quotation.subtotal, currency) },
                ...(quotation.discountTotal > 0
                  ? [{ label: "Discount", value: `-${formatMoney(quotation.discountTotal, currency)}` }]
                  : []),
                { label: "Tax", value: formatMoney(quotation.taxTotal, currency) },
                { label: "Total", value: formatMoney(quotation.grandTotal, currency), strong: true },
              ]}
            />
          </div>
        </section>

        {(quotation.notes || quotation.terms) && (
          <section className="border-t border-ash p-5 sm:p-8">
            <DocumentNotes notes={quotation.notes} terms={quotation.terms} />
          </section>
        )}

        <footer className="border-t border-ash bg-paper p-5 sm:p-8">
          {quotation.canRespond ? (
            <>
              <p className="text-body text-steel">
                Please review the quotation and let {business.businessName} know your decision.
              </p>
              {submitError && (
                <p
                  role="alert"
                  className="mt-3 rounded-btn border border-ash bg-rose-wash px-3 py-2 text-body text-rose-ink"
                >
                  {submitError}
                </p>
              )}
              {/* Full-width, 48px-tall targets on a phone; inline on a desktop. */}
              <div className="mt-4 flex flex-col gap-2 sm:flex-row">
                <Button className="h-12 w-full sm:h-9 sm:w-auto" onClick={() => setDialog("accept")}>
                  <Icon.check className="h-4 w-4" />
                  Accept quotation
                </Button>
                <Button
                  variant="secondary"
                  className="h-12 w-full sm:h-9 sm:w-auto"
                  onClick={() => setDialog("reject")}
                >
                  Reject quotation
                </Button>
              </div>
            </>
          ) : (
            <p className="text-body text-steel">
              {responded
                ? "This quotation has already been answered."
                : "This quotation is no longer open for a response."}
            </p>
          )}

          <a
            href={publicApi.pdfUrl(token)}
            target="_blank"
            rel="noopener noreferrer"
            className="mt-5 inline-flex items-center gap-1.5 text-body font-medium text-electric underline-offset-4 hover:underline"
          >
            <Icon.download className="h-4 w-4" />
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
    <div className="min-h-screen bg-paper px-3 py-6 sm:px-6 sm:py-12">
      <div className="mx-auto w-full max-w-3xl space-y-4">
        {children}
        <p className="pb-6 pt-2 text-center text-caption text-fog">
          Sent with Quote<span className="text-electric">ly</span>
        </p>
      </div>
    </div>
  );
}

const BANNER_TONES = {
  success: "bg-mint text-green",
  warning: "bg-amber-wash text-amber-ink",
  neutral: "bg-canvas text-charcoal",
  error: "bg-rose-wash text-rose-ink",
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
    <div role="status" className={`rounded-card border border-ash px-5 py-4 ${BANNER_TONES[tone]}`}>
      <p className="font-semibold">{title}</p>
      <p className="mt-0.5 text-body opacity-90">{message}</p>
      {detail && <p className="mt-1 text-caption opacity-75">{detail}</p>}
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
    <div className={`rounded-lgcard border border-ash px-6 py-16 text-center ${BANNER_TONES[tone]}`}>
      <h1 className="font-display text-subheading">{title}</h1>
      <p className="mx-auto mt-2 max-w-sm text-body opacity-90">{message}</p>
      {action && <div className="mt-6 flex justify-center">{action}</div>}
    </div>
  );
}

function formatDateTime(iso: string) {
  const date = new Date(iso);
  return date.toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });
}
