import { cn } from "@/lib/cn";
import { INVOICE_STATUS_LABELS, type InvoiceStatus, type QuotationStatus } from "@/types";

/**
 * Status pills. Tints stay soft — a table of twenty rows should read as a document, not a
 * traffic light — and each pill carries a single dot of its accent colour.
 */
const PILL = "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-caption font-medium";

type Tone = "neutral" | "blue" | "green" | "amber" | "rose" | "violet";

const TONES: Record<Tone, { chip: string; dot: string }> = {
  neutral: { chip: "bg-paper text-steel", dot: "bg-silver" },
  blue: { chip: "bg-blue-wash text-sapphire", dot: "bg-electric" },
  green: { chip: "bg-mint text-green-ink", dot: "bg-green" },
  amber: { chip: "bg-amber-wash text-amber-ink", dot: "bg-tangerine" },
  rose: { chip: "bg-rose-wash text-rose-ink", dot: "bg-rose-ink" },
  violet: { chip: "bg-lavender/10 text-lavender", dot: "bg-lavender" },
};

export function Pill({
  tone = "neutral",
  children,
  className,
}: {
  tone?: Tone;
  children: React.ReactNode;
  className?: string;
}) {
  const style = TONES[tone];
  return (
    <span className={cn(PILL, style.chip, className)}>
      <span aria-hidden className={cn("h-1.5 w-1.5 shrink-0 rounded-full", style.dot)} />
      {children}
    </span>
  );
}

const QUOTATION_TONES: Record<QuotationStatus, Tone> = {
  Draft: "neutral",
  Sent: "blue",
  Accepted: "green",
  Rejected: "rose",
  Expired: "amber",
};

export function StatusBadge({ status, className }: { status: QuotationStatus; className?: string }) {
  return (
    <Pill tone={QUOTATION_TONES[status] ?? "neutral"} className={className}>
      {status}
    </Pill>
  );
}

/** Invoice state is its own lifecycle, so it gets its own mapping. */
const INVOICE_TONES: Record<InvoiceStatus, Tone> = {
  Draft: "neutral",
  Sent: "blue",
  PartiallyPaid: "amber",
  Paid: "green",
  Overdue: "rose",
  Cancelled: "neutral",
};

export function InvoiceStatusBadge({
  status,
  className,
}: {
  status: InvoiceStatus;
  className?: string;
}) {
  return (
    <Pill tone={INVOICE_TONES[status] ?? "neutral"} className={className}>
      {INVOICE_STATUS_LABELS[status] ?? status}
    </Pill>
  );
}
