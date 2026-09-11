import { cn } from "@/lib/cn";

/** Border-defined container. No shadow: the 1px ash edge is what makes it a card. */
export function Card({ className, children }: { className?: string; children: React.ReactNode }) {
  return <div className={cn("rounded-card border border-ash bg-canvas", className)}>{children}</div>;
}

export function CardHeader({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3 border-b border-ash px-4 py-3">
      <div className="min-w-0">
        <h2 className="text-body font-semibold text-charcoal">{title}</h2>
        {description && <p className="mt-0.5 text-caption text-fog">{description}</p>}
      </div>
      {action}
    </div>
  );
}

export function CardBody({ className, children }: { className?: string; children: React.ReactNode }) {
  return <div className={cn("px-4 py-4", className)}>{children}</div>;
}

/**
 * A settings-style section: title and explanation on the left at wide sizes, fields on the
 * right. Keeps long forms readable instead of stacking one tall column down the page.
 */
export function SectionCard({
  title,
  description,
  footer,
  children,
}: {
  title: string;
  description?: string;
  footer?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <section className="rounded-card border border-ash bg-canvas">
      <div className="grid gap-4 p-4 lg:grid-cols-[minmax(0,260px)_minmax(0,1fr)] lg:gap-8 lg:p-6">
        <div className="lg:pr-4">
          <h2 className="text-body-lg font-semibold text-charcoal">{title}</h2>
          {description && <p className="mt-1 text-body text-fog">{description}</p>}
        </div>
        <div className="min-w-0">{children}</div>
      </div>
      {footer && (
        <div className="flex flex-wrap items-center justify-end gap-2 border-t border-ash px-4 py-3 lg:px-6">
          {footer}
        </div>
      )}
    </section>
  );
}

/** Tonal panel for content nested inside a card — totals blocks, callouts, summaries. */
export function Panel({ className, children }: { className?: string; children: React.ReactNode }) {
  return <div className={cn("rounded-card bg-paper p-4", className)}>{children}</div>;
}
