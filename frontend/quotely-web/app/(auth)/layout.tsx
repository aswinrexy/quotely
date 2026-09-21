import Link from "next/link";
import { Icon } from "@/components/ui/icons";

/**
 * Sign in and create account, side by side with a brand panel.
 *
 * A SPLIT rather than a card floating in the middle of a very wide screen — at desktop width the
 * old layout left most of the page empty, which reads as unfinished rather than minimal.
 *
 * TYPOGRAPHY RATHER THAN A PHOTOGRAPH, deliberately. A stock image would fight a design system
 * built on white canvas, hairline borders and one accent colour; it would cost a few hundred
 * kilobytes on the one page that must load before anyone can do anything, on connections in the
 * markets this product is for; it would need a second treatment for dark mode and a licence to
 * keep; and it would be invisible on a phone, where the panel collapses anyway. The panel earns
 * its space by saying what Quotely does instead of decorating the fact that it exists.
 *
 * Everything claimed here is something the product actually does today.
 */

const POINTS = [
  "Send a quotation, then turn it into an invoice",
  "Collect payments online, into your own account",
  "Raise export and import documents with HS codes and weights",
];

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen lg:grid lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
      {/* Hidden below lg: on a phone it would push the form below the fold, and the form is the
          entire reason anyone opened this page. */}
      <aside className="relative hidden bg-midnight px-10 py-12 text-canvas lg:flex lg:flex-col lg:justify-between xl:px-14">
        <Link href="/" className="font-display text-body-xl">
          Quote<span className="text-electric">ly</span>
        </Link>

        <div className="max-w-md">
          <h2 className="font-display text-heading-sm leading-tight">
            Quotations, invoices and trade documents for small businesses in India.
          </h2>

          <ul className="mt-8 space-y-3.5">
            {POINTS.map((point) => (
              <li key={point} className="flex items-start gap-3 text-body text-silver">
                <Icon.check className="mt-0.5 h-4 w-4 shrink-0 text-electric" />
                <span>{point}</span>
              </li>
            ))}
          </ul>
        </div>

        <p className="text-caption text-steel">
          © {new Date().getFullYear()} Quotely
        </p>
      </aside>

      <main className="flex min-h-screen flex-col justify-center bg-paper px-4 py-12 lg:bg-canvas lg:px-10">
        <div className="mx-auto w-full max-w-sm">
          {/* The wordmark only appears where the panel does not — never both at once. */}
          <div className="mb-6 text-center lg:hidden">
            <Link href="/" className="font-display text-heading-sm text-charcoal">
              Quote<span className="text-electric">ly</span>
            </Link>
          </div>

          <div className="rounded-card border border-ash bg-canvas p-6 lg:border-0 lg:p-0">
            {children}
          </div>
        </div>
      </main>
    </div>
  );
}
