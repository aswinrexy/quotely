"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { formatDate } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
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
          description="Your customer can open this link to review the quotation and accept or reject it. No account needed."
        />
        <CardBody className="space-y-4">
          {url ? (
            <>
              <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
                <p className="break-all font-mono text-xs text-slate-700">{url}</p>
              </div>
              <div className="flex flex-wrap gap-2">
                <Button onClick={copy}>Copy link</Button>
                <a href={url} target="_blank" rel="noopener noreferrer">
                  <Button variant="secondary">Open customer view</Button>
                </a>
                <Button variant="ghost" onClick={() => setConfirmReplace(true)}>
                  Replace link
                </Button>
              </div>
              <p className="text-xs text-slate-500">
                Copy this link now — for security only a hashed copy is stored, so it cannot be shown again.
                You can always create a replacement.
              </p>
            </>
          ) : hasExistingLink ? (
            <>
              <p className="text-sm text-slate-600">
                A share link is active
                {quotation.publicLinkCreatedAt ? ` since ${formatDate(quotation.publicLinkCreatedAt)}` : ""}. The URL
                itself is not stored, so it cannot be displayed again.
              </p>
              <Button variant="secondary" onClick={() => setConfirmReplace(true)}>
                Create a new link
              </Button>
              <p className="text-xs text-slate-500">Creating a new link stops the previous one from working.</p>
            </>
          ) : (
            <>
              <Button onClick={generate} loading={generating}>
                Generate share link
              </Button>
              <p className="text-xs text-slate-500">
                Anyone with the link can view this quotation and respond, so share it only with your customer.
              </p>
            </>
          )}

          {quotation.respondedAt && (
            <div
              className={`rounded-lg border px-3 py-3 text-sm ${
                quotation.status === "Accepted"
                  ? "border-emerald-200 bg-emerald-50 text-emerald-900"
                  : "border-red-200 bg-red-50 text-red-900"
              }`}
            >
              <p className="font-medium">
                {quotation.status === "Accepted" ? "Accepted" : "Rejected"} by{" "}
                {quotation.respondedByName ?? "the customer"} on {formatDate(quotation.respondedAt)}
              </p>
              {quotation.respondedByEmail && (
                <p className="mt-0.5 break-words text-xs opacity-80">{quotation.respondedByEmail}</p>
              )}
              {quotation.responseComment && (
                <p className="mt-2 whitespace-pre-line text-sm opacity-90">“{quotation.responseComment}”</p>
              )}
            </div>
          )}
        </CardBody>
      </Card>

      <ConfirmDialog
        open={confirmReplace}
        title="Create a new share link?"
        description="The link you shared before will stop working immediately. Anyone using it will see a 'not found' page."
        confirmLabel="Create new link"
        loading={generating}
        onConfirm={generate}
        onCancel={() => setConfirmReplace(false)}
      />
    </>
  );
}
