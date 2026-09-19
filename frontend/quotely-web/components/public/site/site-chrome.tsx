import Link from "next/link";

/**
 * The public shell: header and footer for the pages anyone can reach without signing in.
 *
 * These pages exist for two audiences at once. A visitor deciding whether Quotely is real, and
 * a payment-gateway reviewer checking that a business asking to collect money has published who
 * it is, what it sells, what it costs and how to get a refund. Both want the same things, which
 * is why one set of pages serves both.
 *
 * Every contact detail here comes from one place — see COMPANY — so the address on the refund
 * policy can never drift from the address on the contact page.
 */

/**
 * The business behind Quotely.
 *
 * THESE ARE PLACEHOLDERS AND MUST BE REPLACED BEFORE SUBMITTING THE SITE TO RAZORPAY. A payment
 * aggregator verifies that the operator is contactable, and an unreachable phone number or a
 * missing address is a common cause of rejection. They are written here, once, rather than
 * scattered through six pages.
 */
export const COMPANY = {
  legalName: "Quotely",
  email: "support@quotely4you.org",
  phone: "+91 00000 00000",
  addressLines: ["Kerala", "India"],
  supportHours: "Monday to Friday, 10:00–18:00 IST",
  /** How quickly a support email is answered. Stated publicly, so it has to be true. */
  responseTime: "one business day",
};

export function SiteHeader() {
  return (
    <header className="border-b border-ash bg-canvas">
      <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 px-4 py-4">
        <Link href="/" className="font-display text-subheading text-charcoal">
          Quote<span className="text-electric">ly</span>
        </Link>
        <nav className="flex items-center gap-1 text-body">
          <Link href="/pricing" className="rounded-btn px-3 py-2 text-steel hover:bg-paper hover:text-charcoal">
            Pricing
          </Link>
          <Link href="/contact" className="rounded-btn px-3 py-2 text-steel hover:bg-paper hover:text-charcoal">
            Contact
          </Link>
          <Link
            href="/login"
            className="rounded-btn bg-midnight px-3.5 py-2 font-medium text-canvas hover:bg-charcoal"
          >
            Sign in
          </Link>
        </nav>
      </div>
    </header>
  );
}

const POLICIES = [
  { href: "/terms", label: "Terms & Conditions" },
  { href: "/privacy", label: "Privacy Policy" },
  { href: "/refund", label: "Cancellation & Refunds" },
  { href: "/shipping", label: "Service Delivery" },
  { href: "/pricing", label: "Pricing" },
  { href: "/contact", label: "Contact Us" },
];

export function SiteFooter() {
  return (
    <footer className="mt-auto border-t border-ash bg-paper">
      <div className="mx-auto max-w-5xl px-4 py-10">
        <div className="grid gap-8 sm:grid-cols-2">
          <div>
            <p className="font-display text-subheading text-charcoal">
              Quote<span className="text-electric">ly</span>
            </p>
            <p className="mt-2 max-w-xs text-body text-fog">
              Quotations, invoices and payments for small service businesses in India.
            </p>
          </div>

          <div>
            {/*
              Contact details in the footer, on every page. A payment aggregator looks for this
              specifically, and a customer chasing an invoice should not have to hunt for it.
            */}
            <h2 className="text-body font-semibold text-charcoal">Contact</h2>
            <address className="mt-2 space-y-0.5 not-italic text-body text-fog">
              <p>{COMPANY.legalName}</p>
              {COMPANY.addressLines.map((line) => (
                <p key={line}>{line}</p>
              ))}
              <p>
                <a href={`mailto:${COMPANY.email}`} className="text-electric hover:underline">
                  {COMPANY.email}
                </a>
              </p>
              <p>
                <a href={`tel:${COMPANY.phone.replace(/\s/g, "")}`} className="text-electric hover:underline">
                  {COMPANY.phone}
                </a>
              </p>
            </address>
          </div>
        </div>

        <nav className="mt-8 flex flex-wrap gap-x-5 gap-y-2 border-t border-ash pt-6 text-caption">
          {POLICIES.map((policy) => (
            <Link key={policy.href} href={policy.href} className="text-steel hover:text-charcoal hover:underline">
              {policy.label}
            </Link>
          ))}
        </nav>

        <p className="mt-4 text-caption text-fog">
          © {new Date().getFullYear()} {COMPANY.legalName}. All rights reserved.
        </p>
      </div>
    </footer>
  );
}

/** Wraps a policy page: one column, readable measure, consistent heading rhythm. */
export function LegalPage({
  title,
  updated,
  children,
}: {
  title: string;
  /** When the policy last changed. A policy with no date is one nobody can rely on. */
  updated: string;
  children: React.ReactNode;
}) {
  return (
    <div className="mx-auto max-w-3xl px-4 py-12">
      <h1 className="font-display text-heading-sm text-charcoal">{title}</h1>
      <p className="mt-2 text-caption text-fog">Last updated {updated}</p>
      <div className="mt-8 space-y-6 text-body text-steel [&_a]:text-electric [&_a:hover]:underline [&_h2]:text-body-lg [&_h2]:font-semibold [&_h2]:text-charcoal [&_li]:ml-5 [&_li]:list-disc [&_p]:leading-relaxed [&_ul]:space-y-1.5">
        {children}
      </div>
    </div>
  );
}
