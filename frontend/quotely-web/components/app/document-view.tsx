import { formatMoney } from "@/lib/format";
import { cn } from "@/lib/cn";

/**
 * Shared presentation for the quotation and invoice reading views. Both documents print the
 * same shape, so the party blocks, the line table and the totals live here rather than being
 * written twice with two sets of paddings.
 */

export function PartyBlock({
  label,
  name,
  company,
  lines,
  contact,
  footnote,
}: {
  label: string;
  name: string;
  company?: string | null;
  lines: string[];
  contact?: (string | null | undefined)[];
  footnote?: string;
}) {
  const reachable = (contact ?? []).filter(Boolean) as string[];

  return (
    <div className="min-w-0">
      <p className="text-caption font-medium uppercase tracking-wide text-fog">{label}</p>
      <p className="mt-1.5 text-body-lg font-semibold text-charcoal">{name}</p>
      {company && <p className="text-body text-steel">{company}</p>}
      {lines.map((line) => (
        <p key={line} className="text-body text-fog">
          {line}
        </p>
      ))}
      {reachable.length > 0 && (
        <p className="mt-1 break-words text-body text-fog">{reachable.join("  ·  ")}</p>
      )}
      {footnote && <p className="mt-2 text-caption text-fog">{footnote}</p>}
    </div>
  );
}

export interface DocumentLine {
  id?: string;
  name: string;
  description?: string | null;
  unit: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  taxRate: number;
  lineTotal: number;
}

/** Line items: a table from `sm` upwards, stacked cards below it. */
export function LineItems({ items, currency }: { items: DocumentLine[]; currency: string }) {
  return (
    <>
      <div className="hidden overflow-x-auto sm:block">
        <table className="w-full border-collapse text-body">
          <thead>
            <tr>
              <Head>Description</Head>
              <Head align="right">Qty</Head>
              <Head align="right">Unit price</Head>
              <Head align="right">Discount</Head>
              <Head align="right">Tax</Head>
              <Head align="right">Total</Head>
            </tr>
          </thead>
          <tbody>
            {items.map((item, index) => (
              <tr key={item.id ?? `${item.name}-${index}`} className="border-b border-ash align-top">
                <td className="py-3 pr-4">
                  <p className="font-medium text-charcoal">{item.name}</p>
                  {item.description && <p className="mt-0.5 text-caption text-fog">{item.description}</p>}
                </td>
                <Cell>
                  {item.quantity} {item.unit}
                </Cell>
                <Cell>{formatMoney(item.unitPrice, currency)}</Cell>
                <Cell>{item.discount > 0 ? formatMoney(item.discount, currency) : "—"}</Cell>
                <Cell>{item.taxRate}%</Cell>
                <Cell className="font-medium text-charcoal">{formatMoney(item.lineTotal, currency)}</Cell>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <ul className="space-y-2.5 sm:hidden">
        {items.map((item, index) => (
          <li key={item.id ?? `${item.name}-${index}`} className="rounded-card border border-ash p-3">
            <p className="font-medium text-charcoal">{item.name}</p>
            {item.description && <p className="mt-0.5 text-caption text-fog">{item.description}</p>}
            <dl className="mt-2.5 grid grid-cols-2 gap-x-4 gap-y-1.5">
              <Fact label="Quantity" value={`${item.quantity} ${item.unit}`} />
              <Fact label="Unit price" value={formatMoney(item.unitPrice, currency)} />
              {item.discount > 0 && <Fact label="Discount" value={formatMoney(item.discount, currency)} />}
              <Fact label="Tax" value={`${item.taxRate}%`} />
            </dl>
            <div className="mt-2.5 flex items-center justify-between border-t border-ash pt-2.5">
              <span className="text-caption font-medium uppercase tracking-wide text-fog">Line total</span>
              <span className="font-semibold tabular-nums text-charcoal">
                {formatMoney(item.lineTotal, currency)}
              </span>
            </div>
          </li>
        ))}
      </ul>
    </>
  );
}

function Head({ children, align = "left" }: { children: React.ReactNode; align?: "left" | "right" }) {
  return (
    <th
      scope="col"
      className={cn(
        "border-b border-ash pb-2 text-caption font-medium uppercase tracking-wide text-fog",
        align === "right" ? "pl-4 text-right" : "pr-4 text-left",
      )}
    >
      {children}
    </th>
  );
}

function Cell({ children, className }: { children: React.ReactNode; className?: string }) {
  return <td className={cn("py-3 pl-4 text-right tabular-nums text-steel", className)}>{children}</td>;
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-caption text-fog">{label}</dt>
      <dd className="text-body tabular-nums text-charcoal">{value}</dd>
    </div>
  );
}

export interface TotalRow {
  label: string;
  value: string;
  /** Renders as the emphasised final line. */
  strong?: boolean;
  /** Renders in the accent colour — the outstanding balance, for instance. */
  accent?: boolean;
}

/** The financial summary block. Right-aligned, tabular, with one emphasised row. */
export function Totals({ rows }: { rows: TotalRow[] }) {
  return (
    <dl className="w-full space-y-2 sm:max-w-xs">
      {rows.map((row) =>
        row.strong ? (
          <div
            key={row.label}
            className="mt-1 flex items-center justify-between gap-4 border-t border-smoke pt-3"
          >
            <dt className="text-body font-semibold text-charcoal">{row.label}</dt>
            <dd className="text-body-xl font-semibold tabular-nums text-charcoal">{row.value}</dd>
          </div>
        ) : (
          <div key={row.label} className="flex items-center justify-between gap-4">
            <dt className="text-body text-fog">{row.label}</dt>
            <dd
              className={cn(
                "text-body tabular-nums",
                row.accent ? "font-medium text-electric" : "text-charcoal",
              )}
            >
              {row.value}
            </dd>
          </div>
        ),
      )}
    </dl>
  );
}

/** Notes and terms, printed beneath the totals. */
export function DocumentNotes({ notes, terms }: { notes?: string | null; terms?: string | null }) {
  if (!notes && !terms) return null;

  return (
    <div className="space-y-4">
      {notes && <Block title="Notes" body={notes} />}
      {terms && <Block title="Terms & conditions" body={terms} />}
    </div>
  );
}

function Block({ title, body }: { title: string; body: string }) {
  return (
    <div>
      <p className="text-caption font-medium uppercase tracking-wide text-fog">{title}</p>
      <p className="mt-1 whitespace-pre-line text-body text-steel">{body}</p>
    </div>
  );
}

/** A label/value pair used in the right-hand rail and document meta blocks. */
export function MetaItem({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-caption font-medium uppercase tracking-wide text-fog">{label}</dt>
      <dd className="mt-0.5 text-body text-charcoal">{children}</dd>
    </div>
  );
}
