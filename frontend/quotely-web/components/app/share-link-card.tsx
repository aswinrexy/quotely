"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { formatDate } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { Icon } from "@/components/ui/icons";
import { ConfirmDialog } from "@/components/ui/dialog";
import type { PublicQuotationLink, Quotation } from "@/types";

/**
 * Share link panel on the quotation details page.
 *
 * The API stores only a hash of the share token, so a link can be shown exactly once — at the
 * moment it is created. That is why this panel keeps the URL in component state after generating
 * it, and why asking for a link again is presented as "replace", with a warning: the previous
 * URL stops working immediately.
 */
export function ShareLinkCard({
  quotation,
  onChanged,
}: {
  quotation: Quotation;
  onChanged: () => void;
}) {
  const toast = useToast();
  const [url, setUrl] = useState<string | null>(null);
  const [generating, setGenerating] = useState(false);
  const [confirmReplace, setConfirmReplace] = useState(false);

  const hasExistingLink = quotation.hasPublicLink;

  async function generate() {
    setGenerating(true);
    try {
      const link = await api.post<PublicQuotationLink>(`/api/quotations/${quotation.id}/public-link`, undefined);
      setUrl(link.url);
      setConfirmReplace(false);
      toast(hasExistingLink ? "New share link created." : "Share link created.", "success");
      onChanged();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not create the share link.", "error");
    } finally {
      setGenerating(false);
    }
  }

  async function copy() {
    if (!url) return;
    try {
      await navigator.clipboard.writeText(url);
      toast("Link copied", "success");
    } catch {
      // Clipboard access can be blocked (insecure origin, permissions); the URL stays selectable.
      toast("Copy failed — select the link and copy it manually.", "error");
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

          {url ? (
            <>
              <div className="rounded-input border border-ash bg-paper p-2.5">
                <p className="break-all font-mono text-caption text-charcoal">{url}</p>
              </div>
              <p className="text-caption text-fog">
                Copy it now — this link is shown once and cannot be displayed again.
              </p>
              <div className="flex flex-wrap gap-2">
                <Button size="sm" onClick={copy}>
                  <Icon.copy className="h-3.5 w-3.5" />
                  Copy link
                </Button>
                <a href={url} target="_blank" rel="noopener noreferrer">
                  <Button variant="secondary" size="sm">
                    <Icon.external className="h-3.5 w-3.5" />
                    Open
                  </Button>
                </a>
              </div>
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
                Only a hash of the link is stored, so the URL cannot be shown again. Creating a new
                one immediately stops the old link from working.
              </p>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => setConfirmReplace(true)}
                loading={generating}
              >
                Replace link
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
                Generate share link
              </Button>
            </>
          )}
        </CardBody>
      </Card>

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
