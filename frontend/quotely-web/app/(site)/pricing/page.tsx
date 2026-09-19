import Link from "next/link";
import { COMPANY } from "@/components/public/site/site-chrome";

export const metadata = {
  title: "Pricing — Quotely",
  description: "Quotely Pro is ₹150 per month for one business. Cancel any time.",
};

const INCLUDED = [
  "Unlimited quotations and invoices",
  "Unlimited customers, products and services",
  "Shareable quotation and invoice links",
  "PDF generation for every document",
  "Online payments into your own Razorpay account",
  "Manual payment recording for cash, cheque and bank transfers",
  "Receivables and outstanding balance tracking",
];

export default function PricingPage() {
  return (
    <div className="mx-auto max-w-3xl px-4 py-12">
      <h1 className="font-display text-heading-sm text-charcoal">Pricing</h1>
      <p className="mt-2 text-body-lg text-steel">One plan. No per-document charges.</p>

      <div className="mt-8 rounded-card border border-ash bg-canvas p-6">
        <h2 className="text-body-lg font-semibold text-charcoal">Quotely Pro</h2>
        <p className="mt-1 text-body text-fog">Everything in Quotely, for one business.</p>

        <p className="mt-5 flex items-baseline gap-1.5">
          <span className="font-display text-heading text-charcoal">₹150</span>
          <span className="text-body text-fog">per month, including GST where applicable</span>
        </p>

        <ul className="mt-6 space-y-2 text-body text-steel">
          {INCLUDED.map((item) => (
            <li key={item} className="ml-5 list-disc">
              {item}
            </li>
          ))}
        </ul>

        <Link
          href="/register"
          className="mt-7 inline-block rounded-btn bg-midnight px-4 py-2.5 text-body font-medium text-canvas hover:bg-charcoal"
        >
          Create an account
        </Link>
      </div>

      <div className="mt-8 space-y-5 text-body text-steel">
        <div>
          <h2 className="text-body-lg font-semibold text-charcoal">Free while we are in early access</h2>
          <p className="mt-1.5">
            Quotely is currently free to use. You will be told well in advance before that changes,
            and nothing is charged until you set up a payment yourself.
          </p>
        </div>

        <div>
          <h2 className="text-body-lg font-semibold text-charcoal">Billing period</h2>
          <p className="mt-1.5">
            Quotely Pro is billed monthly in advance. Your subscription renews on the same date each
            month until you cancel it.
          </p>
        </div>

        <div>
          <h2 className="text-body-lg font-semibold text-charcoal">What Quotely does not charge for</h2>
          <p className="mt-1.5">
            Quotely takes no commission on the payments your customers make to you. Those are
            collected by your own payment account, and any transaction fee is charged by your
            payment provider under your agreement with them — not by us.
          </p>
        </div>

        <div>
          <h2 className="text-body-lg font-semibold text-charcoal">Questions</h2>
          <p className="mt-1.5">
            Email{" "}
            <a href={`mailto:${COMPANY.email}`} className="text-electric hover:underline">
              {COMPANY.email}
            </a>{" "}
            and we will reply within {COMPANY.responseTime}.
          </p>
        </div>
      </div>
    </div>
  );
}
