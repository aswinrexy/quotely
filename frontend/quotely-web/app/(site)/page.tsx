import Link from "next/link";

/**
 * The landing page.
 *
 * Replaces a root route that did nothing but redirect to /login. A site whose front door is a
 * login form tells a visitor nothing about what the product does, and tells a payment-gateway
 * reviewer nothing about what the business sells — which is one of the things they check.
 *
 * Static and public: no auth call, no redirect, nothing that needs JavaScript to read.
 */
export const metadata = {
  title: "Quotely — Quotations, invoices and payments for small businesses",
  description:
    "Send professional quotations, turn them into invoices, and get paid online. Built for small service businesses in India.",
};

const FEATURES = [
  {
    title: "Quotations in minutes",
    body: "Build a quotation from your saved products and services, with tax and discounts worked out as you type. Send it as a link or a PDF.",
  },
  {
    title: "One click to an invoice",
    body: "When a customer accepts, the quotation becomes an invoice without retyping a single line. Numbering is sequential and gapless.",
  },
  {
    title: "Paid directly to you",
    body: "Connect your own Razorpay account. Customers pay by UPI, card or netbanking, and the money settles into your bank — never ours.",
  },
  {
    title: "Payments you can reconcile",
    body: "Cash and bank transfers are recorded alongside online payments, so the outstanding figure on an invoice is always the real one.",
  },
];

const STEPS = [
  { step: "1", title: "Create a quotation", body: "Pick a customer, add your lines, set validity." },
  { step: "2", title: "Share the link", body: "By WhatsApp, email or a copied link. No account needed to view it." },
  { step: "3", title: "Get accepted", body: "Your customer approves or declines, and you see it immediately." },
  { step: "4", title: "Invoice and get paid", body: "Convert to an invoice and share a payment link." },
];

export default function LandingPage() {
  return (
    <>
      <section className="mx-auto max-w-5xl px-4 py-16 sm:py-24">
        <div className="max-w-2xl">
          <h1 className="font-display text-heading text-charcoal">
            Professional quotations, invoices and payments — in minutes.
          </h1>
          <p className="mt-4 text-body-lg text-steel">
            Quotely is for small service businesses that would rather be doing the work than
            formatting a document. Quote, invoice, and get paid straight into your own bank account.
          </p>
          <div className="mt-8 flex flex-wrap gap-3">
            <Link
              href="/register"
              className="rounded-btn bg-midnight px-4 py-2.5 text-body font-medium text-canvas hover:bg-charcoal"
            >
              Create an account
            </Link>
            <Link
              href="/pricing"
              className="rounded-btn border border-ash bg-canvas px-4 py-2.5 text-body font-medium text-charcoal hover:bg-paper"
            >
              See pricing
            </Link>
          </div>
          <p className="mt-4 text-caption text-fog">
            Free while we are in early access. No card required to start.
          </p>
        </div>
      </section>

      <section className="border-y border-ash bg-paper">
        <div className="mx-auto max-w-5xl px-4 py-14">
          <h2 className="font-display text-subheading text-charcoal">What Quotely does</h2>
          <div className="mt-8 grid gap-6 sm:grid-cols-2">
            {FEATURES.map((feature) => (
              <div key={feature.title} className="rounded-card border border-ash bg-canvas p-5">
                <h3 className="text-body-lg font-semibold text-charcoal">{feature.title}</h3>
                <p className="mt-2 text-body text-fog">{feature.body}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="mx-auto max-w-5xl px-4 py-14">
        <h2 className="font-display text-subheading text-charcoal">How it works</h2>
        <ol className="mt-8 grid gap-6 sm:grid-cols-2 lg:grid-cols-4">
          {STEPS.map((item) => (
            <li key={item.step}>
              <span className="inline-flex h-7 w-7 items-center justify-center rounded-full bg-blue-wash text-caption font-semibold text-electric">
                {item.step}
              </span>
              <h3 className="mt-3 text-body font-semibold text-charcoal">{item.title}</h3>
              <p className="mt-1 text-body text-fog">{item.body}</p>
            </li>
          ))}
        </ol>
      </section>

      <section className="border-t border-ash bg-paper">
        <div className="mx-auto max-w-5xl px-4 py-14">
          <div className="max-w-2xl">
            <h2 className="font-display text-subheading text-charcoal">
              Your customers pay you, not us
            </h2>
            <p className="mt-3 text-body text-steel">
              Every business on Quotely connects its own Razorpay account. When a customer settles
              an invoice, the payment is collected by that business&rsquo;s account and settles into
              that business&rsquo;s bank. Quotely never holds your money, and never sees your
              customer&rsquo;s card details.
            </p>
            <Link
              href="/register"
              className="mt-6 inline-block rounded-btn bg-midnight px-4 py-2.5 text-body font-medium text-canvas hover:bg-charcoal"
            >
              Get started
            </Link>
          </div>
        </div>
      </section>
    </>
  );
}
