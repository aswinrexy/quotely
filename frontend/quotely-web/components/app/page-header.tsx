import { cn } from "@/lib/cn";

/**
 * The page's own heading block. The top bar carries navigation context; this carries the
 * page's identity, its one-line explanation and its actions.
 */
export function PageHeader({
  title,
  description,
  action,
  className,
}: {
  title: string;
  description?: string;
  action?: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={cn("mb-5 flex flex-wrap items-start justify-between gap-3", className)}>
      <div className="min-w-0">
        <h1 className="font-display text-heading-sm text-charcoal">{title}</h1>
        {description && <p className="mt-1 text-body text-fog">{description}</p>}
      </div>
      {action && <div className="flex flex-wrap items-center gap-2">{action}</div>}
    </div>
  );
}
