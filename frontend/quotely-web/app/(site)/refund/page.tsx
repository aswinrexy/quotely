import { COMPANY, LegalPage } from "@/components/public/site/site-chrome";

export const metadata = {
  title: "Cancellation & Refunds — Quotely",
  description: "How to cancel a Quotely subscription and how refunds are handled, with timelines.",
};

export default function RefundPage() {
  return (
    <LegalPage title="Cancellation & Refunds" updated="20 September 2026">
      <p>
        This policy covers the subscription you pay to Quotely. It does not cover money you pay to
        a business that uses Quotely — see &ldquo;Invoices you received&rdquo; at the end.
      </p>

      <h2>Cancelling your subscription</h2>
      <p>
        You can cancel at any time from <strong>Settings → Billing</strong> in your account. No
        notice period, and no cancellation fee.
      </p>
      <p>
        Cancelling stops the next payment. You keep full access until the end of the period you
        have already paid for — if you cancel on the 3rd and your period runs to the 28th, you keep
        working until the 28th.
      </p>

      <h2>Refunds</h2>
      <ul>
        <li>
          <strong>Charged after cancelling.</strong> Full refund.
        </li>
        <li>
          <strong>Charged twice for the same period.</strong> Full refund of the duplicate.
        </li>
        <li>
          <strong>Quotely was unavailable for a sustained period</strong> during a month you paid
          for. Pro-rata refund for the affected days.
        </li>
        <li>
          <strong>Changed your mind within 7 days</strong> of your first ever payment, having
          created no documents in that time. Full refund.
        </li>
      </ul>
      <p>
        Outside these cases we do not refund part-used months, because you had access to the
        service throughout.
      </p>

      <h2>How long a refund takes</h2>
      <p>
        We approve or decline a refund request within <strong>3 business days</strong>. An approved
        refund is issued to the original payment method, and reaches you within{" "}
        <strong>5 to 7 business days</strong> of approval, depending on your bank or card issuer.
        We will tell you the date we issued it.
      </p>

      <h2>How to request one</h2>
      <p>
        Email{" "}
        <a href={`mailto:${COMPANY.email}`}>{COMPANY.email}</a> from the address on your account,
        with your business name and what happened. We reply within {COMPANY.responseTime}.
      </p>

      <h2>Invoices you received from a business using Quotely</h2>
      <p>
        If you paid an invoice that was sent to you through Quotely, that money went to that
        business, not to Quotely. We cannot refund it. Please contact the business directly — their
        contact details are printed on the invoice. If you cannot reach them, write to us and we
        will help you make contact.
      </p>
    </LegalPage>
  );
}
