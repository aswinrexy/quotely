"use client";

import { useState } from "react";
import { api, saveBlob } from "@/lib/api";
import { formatDate } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { Icon } from "@/components/ui/icons";
import { ConfirmDialog } from "@/components/ui/dialog";
import { ShareDialog } from "@/components/app/share-dialog";
import type { DocumentShare, PublicQuotationLink, Quotation } from "@/types";

/**
 * Share panel on the quotation details page.
 *
 * Only a hash of the share token is stored, so the URL can be shown exactly once — at the moment
 * it is created. That is why the share material lives in component state here, and why asking
 * again is presented as "replace": the previous link stops working immediately.
 *
 * The WhatsApp and email actions are deep links. Quotely sends nothing itself — WhatsApp opens
 * with the message written, and the mail action opens the owner's own mail client with a draft.
 * The message text, the quotation total and both URLs are composed by the server, which is where
 * the authoritative figures live; the browser only opens what it was handed.
 */
export function ShareLinkCard({
  quotation,
  onChanged,
}: {
  quotation: Quotation;
  onChanged: () => void;
}) {
  const toast = useToast();
  const [share, setShare] = useState<DocumentShare | null>(null);
  const [open, setOpen] = useState(false);
  const [generating, setGenerating] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [confirmReplace, setConfirmReplace] = useState(false);

  const hasExistingLink = quotation.hasPublicLink;

  async function generate() {
    setGenerating(true);
    try {
      const link = await api.post<PublicQuotationLink>(
        `/api/quotations/${quotation.id}/public-link`,
        undefined,
      );
      setShare(link.share);
      setConfirmReplace(false);
      setOpen(true);
      onChanged();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not create the share link.", "error");
    } finally {
      setGenerating(false);
    }
  }

  /** Reopens the panel on material already in memory, rather than silently minting a new link. */
  function openExisting() {
    if (share) setOpen(true);
    else if (hasExistingLink) setConfirmReplace(true);
    else void generate();
  }

  async function downloadPdf() {
    setDownloading(true);
    try {
      const { blob, fileName } = await api.downloadPdf(quotation.id);
      saveBlob(blob, fileName);
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not generate the PDF.", "error");
    } finally {
      setDownloading(false);
    }
  }

  return (
    <>
      <Card>
        <CardHeader
          title="Share with customer"
          description={
            quotation.respondedAt
              ? "The customer has answered through this link."
              : "A link your customer opens to review and respond. No account needed."
          }
        />
        <CardBody className="space-y-3">
          {quotation.respondedAt && (
            <dl className="space-y-2 rounded-card bg-paper p-3">
              <div>
                <dt className="text-caption font-medium uppercase tracking-wide text-fog">
                  {quotation.status === "Accepted" ? "Accepted by" : "Rejected by"}
                </dt>
                <dd className="mt-0.5 text-body text-charcoal">
                  {quotation.respondedByName ?? "—"}
                  {quotation.respondedByEmail ? ` · ${quotation.respondedByEmail}` : ""}
                </dd>
              </div>
              <div>
                <dt className="text-caption font-medium uppercase tracking-wide text-fog">On</dt>
                <dd className="mt-0.5 text-body text-charcoal">{formatDate(quotation.respondedAt)}</dd>
              </div>
              {quotation.responseComment && (
                <div>
                  <dt className="text-caption font-medium uppercase tracking-wide text-fog">Comment</dt>
                  <dd className="mt-0.5 whitespace-pre-line text-body text-steel">
                    {quotation.responseComment}
                  </dd>
                </div>
              )}
            </dl>
          )}

          {share ? (
            <>
              <div className="flex items-center gap-2 text-body text-charcoal">
                <Icon.check className="h-4 w-4 shrink-0 text-electric" />
                <span>Link ready to send</span>
              </div>
              <Button size="sm" onClick={() => setOpen(true)}>
                <Icon.link className="h-3.5 w-3.5" />
                Share quotation
              </Button>
            </>
          ) : hasExistingLink ? (
            <>
              <div className="flex items-center gap-2 text-body text-charcoal">
                <Icon.link className="h-4 w-4 shrink-0 text-electric" />
                <span>
                  Link active
                  {quotation.publicLinkCreatedAt
                    ? ` since ${formatDate(quotation.publicLinkCreatedAt)}`
                    : ""}
                </span>
              </div>
              <p className="text-caption text-fog">
                Only a hash of the link is stored, so the URL cannot be shown again. Sharing again
                issues a new link and immediately stops the old one from working.
              </p>
              <Button variant="secondary" size="sm" onClick={openExisting} loading={generating}>
                <Icon.link className="h-3.5 w-3.5" />
                Share again
              </Button>
            </>
          ) : (
            <>
              <p className="text-body text-fog">
                Anyone with the link can view this quotation and respond, so share it only with
                your customer.
              </p>
              <Button size="sm" onClick={generate} loading={generating}>
                <Icon.link className="h-3.5 w-3.5" />
                Share quotation
              </Button>
            </>
          )}
        </CardBody>
      </Card>

      <ShareDialog
        open={open && share !== null}
        share={share}
        title="Share quotation"
        onClose={() => setOpen(false)}
        extraAction={
          <Button
            variant="secondary"
            onClick={downloadPdf}
            loading={downloading}
            className="w-full justify-start"
          >
            <Icon.download className="h-4 w-4" />
            Download PDF
          </Button>
        }
      />

      <ConfirmDialog
        open={confirmReplace}
        title="Replace share link"
        description="The link you shared previously will stop working immediately, and the new URL is shown only once. Continue?"
        confirmLabel="Replace link"
        tone="primary"
        loading={generating}
        onConfirm={generate}
        onCancel={() => setConfirmReplace(false)}
      />
    </>
  );
}
