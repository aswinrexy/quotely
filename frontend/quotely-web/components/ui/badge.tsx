import { cn } from "@/lib/cn";
import { INVOICE_STATUS_LABELS, type InvoiceStatus, type QuotationStatus } from "@/types";

const STATUS_STYLES: Record<QuotationStatus, string> = {
  Draft: "bg-slate-100 text-slate-700 ring-slate-200",
  Sent: "bg-blue-50 text-blue-700 ring-blue-200",
  Accepted: "bg-emerald-50 text-emerald-700 ring-emerald-200",
  Rejected: "bg-red-50 text-red-700 ring-red-200",
  Expired: "bg-amber-50 text-amber-700 ring-amber-200",
};

export function StatusBadge({ status, className }: { status: QuotationStatus; className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset",
        STATUS_STYLES[status] ?? STATUS_STYLES.Draft,
        className,
      )}
    >
      {status}
    </span>
  );
}

const INVOICE_STATUS_STYLES: Record<InvoiceStatus, string> = {
  Draft: "bg-slate-100 text-slate-700 ring-slate-200",
  Sent: "bg-blue-50 text-blue-700 ring-blue-200",
  PartiallyPaid: "bg-amber-50 text-amber-700 ring-amber-200",
  Paid: "bg-emerald-50 text-emerald-700 ring-emerald-200",
  Overdue: "bg-red-50 text-red-700 ring-red-200",
  Cancelled: "bg-slate-100 text-slate-500 ring-slate-200",
};

/** Invoice state is its own lifecycle, so it gets its own palette. */
export function InvoiceStatusBadge({
  status,
  className,
}: {
  status: InvoiceStatus;
  className?: string;
}) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset",
        INVOICE_STATUS_STYLES[status] ?? INVOICE_STATUS_STYLES.Draft,
        className,
      )}
    >
      {INVOICE_STATUS_LABELS[status] ?? status}
    </span>
  );
}
