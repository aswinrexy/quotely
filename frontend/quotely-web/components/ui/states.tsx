"use client";

import { cn } from "@/lib/cn";
import { Button } from "@/components/ui/button";
import { Icon } from "@/components/ui/icons";

export function Spinner({ className }: { className?: string }) {
  return (
    <span
      aria-hidden
      className={cn(
        "inline-block h-4 w-4 animate-spin rounded-full border-2 border-ash border-t-electric",
        className,
      )}
    />
  );
}

/** A single shimmering block. Compose these into the shape of whatever is loading. */
export function Skeleton({ className }: { className?: string }) {
  return <div className={cn("animate-pulse rounded-input bg-paper", className)} />;
}

/**
 * Table placeholder that keeps the page height stable while rows arrive, instead of
 * collapsing to a spinner and then jumping.
 */
export function TableSkeleton({ rows = 5, columns = 5 }: { rows?: number; columns?: number }) {
  return (
    <div aria-busy="true" aria-live="polite" className="px-4 py-2">
      <span className="sr-only">Loading…</span>
      {Array.from({ length: rows }).map((_, row) => (
        <div key={row} className="flex items-center gap-4 border-b border-ash py-3.5 last:border-0">
          {Array.from({ length: columns }).map((_, column) => (
            <Skeleton
              key={column}
              className={cn("h-3.5", column === 0 ? "w-28" : "flex-1", column === columns - 1 && "w-16 flex-none")}
            />
          ))}
        </div>
      ))}
    </div>
  );
}

/** Placeholder for a detail page: a heading block plus a body panel. */
export function DetailSkeleton() {
  return (
    <div aria-busy="true" aria-live="polite" className="space-y-6">
      <span className="sr-only">Loading…</span>
      <div className="space-y-2">
        <Skeleton className="h-7 w-48" />
        <Skeleton className="h-3.5 w-32" />
      </div>
      <div className="rounded-card border border-ash p-4">
        <div className="space-y-3">
          <Skeleton className="h-3.5 w-1/3" />
          <Skeleton className="h-3.5 w-1/2" />
          <Skeleton className="h-3.5 w-1/4" />
        </div>
        <div className="mt-6 space-y-3">
          {Array.from({ length: 3 }).map((_, row) => (
            <Skeleton key={row} className="h-10 w-full" />
          ))}
        </div>
      </div>
    </div>
  );
}

/** Kept for the few places that genuinely need a centred indicator, such as the auth gate. */
export function LoadingState({ label = "Loading…" }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2.5 px-4 py-14 text-body text-fog">
      <Spinner />
      {label}
    </div>
  );
}

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-16 text-center">
      <h3 className="text-body-lg font-semibold text-charcoal">{title}</h3>
      <p className="mt-1.5 max-w-sm text-body text-fog">{description}</p>
      {action && <div className="mt-5">{action}</div>}
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-14 text-center">
      <Icon.alert className="h-5 w-5 text-rose-ink" />
      <h3 className="mt-3 text-body-lg font-semibold text-charcoal">Something went wrong</h3>
      <p className="mt-1.5 max-w-sm text-body text-fog">{message}</p>
      {onRetry && (
        <div className="mt-5">
          <Button variant="secondary" onClick={onRetry}>
            Try again
          </Button>
        </div>
      )}
    </div>
  );
}
