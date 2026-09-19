import { COMPANY, LegalPage } from "@/components/public/site/site-chrome";

export const metadata = {
  title: "Terms & Conditions — Quotely",
  description: "The terms on which Quotely is provided, including payments, cancellation and liability.",
};

export default function TermsPage() {
  return (
    <LegalPage title="Terms & Conditions" updated="20 September 2026">
      <p>
        These terms govern your use of Quotely, provided by {COMPANY.legalName}. By creating an
        account you agree to them.
      </p>

      <h2>1. What Quotely is</h2>
      <p>
        Quotely is software for producing quotations and invoices, sharing them with your
        customers, and recording payment. We provide the tool. The work you invoice for, and your
        agreement with your customer about it, are entirely yours.
      </p>

      <h2>2. Your account</h2>
      <ul>
        <li>You must give accurate details and keep them current.</li>
        <li>You are responsible for your password and for activity under your account.</li>
        <li>One account represents one business.</li>
        <li>You must be able to enter a contract under Indian law.</li>
      </ul>

      <h2>3. Payments your customers make to you</h2>
      <p>
        <strong>Quotely never holds your money.</strong> When you connect a payment account, your
        customers&rsquo; payments are collected by <em>your</em> account and settle into{" "}
        <em>your</em> bank, under your agreement with that payment provider. Quotely takes no
        commission and is not a party to the transaction.
      </p>
      <p>
        It follows that disputes, chargebacks and refunds on those payments are between you, your
        customer and your payment provider. We will help where we can, but we cannot reverse a
        payment we never held.
      </p>

      <h2>4. Your subscription to Quotely</h2>
      <ul>
        <li>Quotely Pro is ₹150 per month for one business, billed monthly in advance.</li>
        <li>Prices may change with at least 30 days&rsquo; notice by email.</li>
        <li>Cancel any time from Settings → Billing. See <a href="/refund">Cancellation &amp; Refunds</a>.</li>
        <li>
          If a payment fails we will retry. Persistent failure may limit your ability to create new
          documents, but you keep access to what you already have, and you can always export it.
        </li>
      </ul>

      <h2>5. Your data</h2>
      <p>
        Your business data and your customers&rsquo; data remain yours. We process them only to
        provide the service — see our <a href="/privacy">Privacy Policy</a>. You can export your
        data or ask for deletion at any time.
      </p>

      <h2>6. Acceptable use</h2>
      <p>You agree not to use Quotely to:</p>
      <ul>
        <li>Invoice for anything unlawful, or to defraud anyone.</li>
        <li>Send unsolicited bulk messages.</li>
        <li>Attempt to access another business&rsquo;s data, or probe or disrupt the service.</li>
        <li>Resell Quotely as your own product without a written agreement.</li>
      </ul>
      <p>We may suspend an account that does these things, and will say why.</p>

      <h2>7. Availability</h2>
      <p>
        We work to keep Quotely available but do not currently offer a contractual uptime
        guarantee. We may change or withdraw features; where a change materially reduces what you
        rely on, we will give notice.
      </p>

      <h2>8. Liability</h2>
      <p>
        Quotely is provided as it is. To the extent the law allows, our total liability in any
        twelve-month period is limited to what you paid us in that period. We are not liable for
        lost profit or lost business. Nothing here limits liability that cannot lawfully be limited.
      </p>
      <p>
        You are responsible for the accuracy of what you send — figures, tax rates and terms on
        your documents are yours, not ours.
      </p>

      <h2>9. Ending the agreement</h2>
      <p>
        You may close your account at any time. We may end it for a material breach of these terms,
        with notice and an opportunity to put it right unless the breach makes that unreasonable.
        On closure you may export your data for 30 days.
      </p>

      <h2>10. Governing law</h2>
      <p>
        These terms are governed by the laws of India, and the courts of Kerala have exclusive
        jurisdiction.
      </p>

      <h2>11. Contact</h2>
      <p>
        Questions about these terms:{" "}
        <a href={`mailto:${COMPANY.email}`}>{COMPANY.email}</a>.
      </p>
    </LegalPage>
  );
}
