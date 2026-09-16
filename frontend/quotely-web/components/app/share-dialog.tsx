"use client";

import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Icon } from "@/components/ui/icons";
import type { DocumentShare } from "@/types";

/**
 * The ways a document link can be handed to a customer. Shared by invoices (V2.4) and quotations
 * (V2.6) — the material is the same shape in both cases and the actions are identical, so there is
 * one sharing surface rather than two that drift apart.
 *
 * Everything shown here was composed by the server while the public URL still existed: the
 * message, the amounts inside it, and both deep links. The browser only opens what it was handed.
 *
 * On a phone the underlying Dialog is already a bottom sheet, so the actions sit within thumb
 * reach without a separate mobile treatment.
 */
export function ShareDialog({
  open,
  share,
  title,
  onClose,
  extraAction,
}: {
  open: boolean;
  share: DocumentShare | null;
  /** "Share invoice" / "Share quotation" — the only wording that differs. */
  title: string;
  onClose: () => void;
  /** Optional trailing action, e.g. the PDF the customer will also be able to open. */
  extraAction?: React.ReactNode;
}) {
  const toast = useToast();

  async function copy() {
    if (!share) return;
    try {
      await navigator.clipboard.writeText(share.url);
      toast("Link copied", "success");
    } catch {
      // Clipboard access can be blocked; the URL stays selectable on screen.
      toast("Copy failed — select the link and copy it manually.", "error");
    }
  }

  if (!share) return null;

  return (
    <Dialog
      open={open}
      title={title}
      description="Copy it now — this link is shown once and cannot be displayed again."
      onClose={onClose}
      footer={
        <Button variant="secondary" onClick={onClose} className="w-full sm:w-auto">
          Done
        </Button>
      }
    >
      <div className="space-y-4">
        <div className="rounded-input border border-ash bg-paper p-2.5">
          <p className="break-all font-mono text-caption text-charcoal">{share.url}</p>
        </div>

        <div className="grid gap-2">
          <a href={share.whatsAppUrl} target="_blank" rel="noopener noreferrer" className="contents">
            <Button className="w-full justify-start">
              <Icon.whatsapp className="h-4 w-4" />
              WhatsApp
            </Button>
          </a>
          <a href={share.mailtoUrl} className="contents">
            <Button variant="secondary" className="w-full justify-start">
              <Icon.mail className="h-4 w-4" />
              Email
            </Button>
          </a>
          <Button variant="secondary" onClick={copy} className="w-full justify-start">
            <Icon.copy className="h-4 w-4" />
            Copy link
          </Button>
          {extraAction}
        </div>

        {/* Said plainly: nothing here is delivered by Quotely. */}
        <p className="text-caption text-fog">
          WhatsApp opens with the message ready to send. Email opens a draft in your own mail app.
          Quotely does not send either for you.
          {!share.customerPhone && " This customer has no phone number saved, so WhatsApp will ask you to pick a contact."}
          {!share.customerEmail && " This customer has no email address saved, so you'll need to type one in."}
        </p>

        <details className="rounded-input border border-ash">
          <summary className="cursor-pointer px-3 py-2 text-caption font-medium text-steel">
            Preview message
          </summary>
          <p className="whitespace-pre-line border-t border-ash px-3 py-2.5 text-caption text-fog">
            {share.message}
          </p>
        </details>
      </div>
    </Dialog>
  );
}
