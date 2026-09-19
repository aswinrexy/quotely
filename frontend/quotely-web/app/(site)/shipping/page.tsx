import { COMPANY, LegalPage } from "@/components/public/site/site-chrome";

export const metadata = {
  title: "Service Delivery — Quotely",
  description: "Quotely is a digital service. How and when access is delivered, and what is not shipped.",
};

export default function ShippingPage() {
  return (
    <LegalPage title="Service Delivery" updated="20 September 2026">
      <p>
        Quotely is a software service delivered over the internet. There is nothing physical to
        ship, so no goods are dispatched, no courier is used, and no delivery address is needed.
        This page exists because payment providers ask for it, and to be clear about when you
        actually get what you pay for.
      </p>

      <h2>When access begins</h2>
      <p>
        Immediately. Your account works the moment you create it, before any payment. A paid
        subscription takes effect as soon as the payment is confirmed — normally within seconds,
        and no later than 24 hours if your bank delays confirmation.
      </p>

      <h2>How it is delivered</h2>
      <ul>
        <li>Through any modern web browser, on a phone, tablet or computer.</li>
        <li>No installation, download or hardware.</li>
        <li>Documents you create can be downloaded as PDFs at any time.</li>
      </ul>

      <h2>Availability</h2>
      <p>
        We aim to keep Quotely available at all times. Planned maintenance is announced in advance
        where possible and scheduled outside Indian business hours. We do not currently offer a
        contractual uptime guarantee, and we say so rather than implying one.
      </p>

      <h2>If something does not arrive</h2>
      <p>
        If you have paid and your account has not been upgraded within 24 hours, email{" "}
        <a href={`mailto:${COMPANY.email}`}>{COMPANY.email}</a>. We will either fix it or refund
        you — see our <a href="/refund">Cancellation &amp; Refunds</a> policy.
      </p>

      <h2>Invoices sent through Quotely</h2>
      <p>
        When a business uses Quotely to invoice you for its own work, the delivery of that work is
        between you and that business. Their terms apply, not ours.
      </p>
    </LegalPage>
  );
}
