"use client";

import { cn } from "@/lib/cn";
import { Icon } from "@/components/ui/icons";

/**
 * Desktop data table. Rows are separated by a single bottom hairline; there are no vertical
 * grid lines, and the whole table is hidden below `md` in favour of <MobileList>, so a phone
 * never has to scroll the application sideways.
 */
export function TableWrap({ children }: { children: React.ReactNode }) {
  return <div className="hidden overflow-x-auto md:block">{children}</div>;
}

export function Table({ children }: { children: React.ReactNode }) {
  return <table className="w-full border-collapse text-body">{children}</table>;
}

export function Th({
  children,
  align = "left",
  className,
}: {
  children?: React.ReactNode;
  align?: "left" | "right";
  className?: string;
}) {
  return (
    <th
      scope="col"
      className={cn(
        "border-b border-ash px-4 py-2.5 text-caption font-medium uppercase tracking-wide text-fog",
        align === "right" ? "text-right" : "text-left",
        className,
      )}
    >
      {children}
    </th>
  );
}

export function Td({
  children,
  align = "left",
  className,
}: {
  children?: React.ReactNode;
  align?: "left" | "right";
  className?: string;
}) {
  return (
    <td
      className={cn(
        "border-b border-ash px-4 py-3 align-middle text-steel",
        align === "right" ? "text-right" : "text-left",
        className,
      )}
    >
      {children}
    </td>
  );
}

export function Tr({ children }: { children: React.ReactNode }) {
  return <tr className="transition-colors duration-150 ease-out hover:bg-paper">{children}</tr>;
}

/** Money and other figures: tabular numerals so columns line up on the decimal. */
export function Amount({ children, className }: { children: React.ReactNode; className?: string }) {
  return (
    <span className={cn("font-medium tabular-nums text-charcoal", className)}>{children}</span>
  );
}

/** Technical identifiers — quotation and invoice numbers. */
export function Mono({ children, className }: { children: React.ReactNode; className?: string }) {
  return <span className={cn("font-mono text-body", className)}>{children}</span>;
}

// ---- mobile ----------------------------------------------------------------

/** The same rows as cards, shown only below `md`. */
export function MobileList({ children }: { children: React.ReactNode }) {
  return <ul className="divide-y divide-ash md:hidden">{children}</ul>;
}

export function MobileRow({ children }: { children: React.ReactNode }) {
  return <li className="px-4 py-3.5">{children}</li>;
}

/** Label/value pairs inside a mobile row. */
export function MobileFacts({ items }: { items: { label: string; value: React.ReactNode }[] }) {
  return (
    <dl className="mt-2.5 grid grid-cols-2 gap-x-4 gap-y-1.5">
      {items.map((item) => (
        <div key={item.label} className="min-w-0">
          <dt className="text-caption text-fog">{item.label}</dt>
          <dd className="truncate text-body text-charcoal">{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}

export function Pagination({
  page,
  totalPages,
  totalCount,
  onChange,
}: {
  page: number;
  totalPages: number;
  totalCount: number;
  onChange: (page: number) => void;
}) {
  if (totalPages <= 1) return null;

  const button =
    "inline-flex h-8 items-center gap-1 rounded-btn border border-ash bg-canvas px-2.5 " +
    "text-caption font-medium text-charcoal transition-colors duration-150 ease-out " +
    "hover:bg-paper disabled:cursor-not-allowed disabled:opacity-50";

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-ash px-4 py-3">
      <p className="text-caption text-fog">
        Page {page} of {totalPages} · {totalCount} total
      </p>
      <div className="flex gap-2">
        <button onClick={() => onChange(page - 1)} disabled={page <= 1} className={button}>
          <Icon.chevronLeft className="h-3.5 w-3.5" />
          Previous
        </button>
        <button onClick={() => onChange(page + 1)} disabled={page >= totalPages} className={button}>
          Next
          <Icon.chevronRight className="h-3.5 w-3.5" />
        </button>
      </div>
    </div>
  );
}
