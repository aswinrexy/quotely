"use client";

import { useEffect, useRef } from "react";
import { cn } from "@/lib/cn";
import { Button } from "@/components/ui/button";

/**
 * Centred modal. Escape closes it, focus moves inside on open and Tab is trapped, and the
 * page behind it stops scrolling. On a phone it becomes a bottom sheet so the actions sit
 * within thumb reach.
 */
export function Dialog({
  open,
  title,
  description,
  onClose,
  children,
  footer,
  className,
}: {
  open: boolean;
  title: string;
  description?: React.ReactNode;
  onClose: () => void;
  children?: React.ReactNode;
  footer?: React.ReactNode;
  className?: string;
}) {
  const panel = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;

    const previouslyFocused = document.activeElement as HTMLElement | null;
    const focusables = () =>
      Array.from(
        panel.current?.querySelectorAll<HTMLElement>(
          'a[href], button:not([disabled]), input, select, textarea, [tabindex]:not([tabindex="-1"])',
        ) ?? [],
      );

    focusables()[0]?.focus();

    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
        return;
      }
      if (event.key !== "Tab") return;

      const items = focusables();
      if (items.length === 0) return;
      const first = items[0];
      const last = items[items.length - 1];

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener("keydown", onKey);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = "";
      previouslyFocused?.focus();
    };
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center sm:items-center sm:p-4">
      <div className="absolute inset-0 bg-midnight/30" onClick={onClose} aria-hidden />
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={cn(
          "relative w-full max-h-[92vh] overflow-y-auto border border-ash bg-canvas shadow-overlay",
          "rounded-t-lgcard sm:max-w-md sm:rounded-card",
          className,
        )}
      >
        <div className="p-5">
          <h2 className="text-body-lg font-semibold text-charcoal">{title}</h2>
          {description && <div className="mt-1.5 text-body text-fog">{description}</div>}
          {children && <div className="mt-4">{children}</div>}
        </div>
        {footer && (
          <div className="flex flex-col-reverse gap-2 border-t border-ash px-5 py-4 sm:flex-row sm:justify-end">
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  description: string;
  confirmLabel?: string;
  /** Destructive confirmations carry the red here, at the moment of commitment. */
  tone?: "danger" | "primary";
  loading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel = "Delete",
  tone = "danger",
  loading = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  return (
    <Dialog
      open={open}
      title={title}
      description={description}
      onClose={loading ? () => {} : onCancel}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={loading} className="w-full sm:w-auto">
            Cancel
          </Button>
          <Button
            onClick={onConfirm}
            loading={loading}
            className={cn(
              "w-full sm:w-auto",
              tone === "danger" && "bg-rose-ink text-canvas hover:bg-rose-ink/90",
            )}
          >
            {confirmLabel}
          </Button>
        </>
      }
    />
  );
}
