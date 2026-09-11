import Link from "next/link";
import { cn } from "@/lib/cn";
import { Skeleton } from "@/components/ui/states";

/**
 * Compact KPI tile: a quiet label, the figure, and one line of context beneath it.
 * `accent` lifts a single headline number to Electric Blue — used sparingly, never on all four.
 */
export function StatCard({
  label,
  value,
  context,
  accent = false,
  href,
  loading = false,
}: {
  label: string;
  value: string;
  context?: string;
  accent?: boolean;
  href?: string;
  loading?: boolean;
}) {
  const body = (
    <>
      <p className="text-caption font-medium uppercase tracking-wide text-fog">{label}</p>
      {loading ? (
        <Skeleton className="mt-2.5 h-7 w-20" />
      ) : (
        <p
          className={cn(
            "mt-2 text-heading-sm font-semibold tabular-nums",
            accent ? "text-electric" : "text-charcoal",
          )}
        >
          {value}
        </p>
      )}
      {context && !loading && <p className="mt-1 text-caption text-fog">{context}</p>}
      {context && loading && <Skeleton className="mt-1.5 h-3 w-24" />}
    </>
  );

  const className = cn(
    "block rounded-card border border-ash bg-canvas p-4",
    href && "transition-colors duration-150 ease-out hover:border-smoke hover:bg-paper",
  );

  return href ? (
    <Link href={href} className={className}>
      {body}
    </Link>
  ) : (
    <div className={className}>{body}</div>
  );
}
