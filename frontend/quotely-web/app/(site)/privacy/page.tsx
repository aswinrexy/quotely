import { COMPANY, LegalPage } from "@/components/public/site/site-chrome";

export const metadata = {
  title: "Privacy Policy — Quotely",
  description: "What data Quotely collects, why, how long it is kept, and how to get it deleted.",
};

export default function PrivacyPage() {
  return (
    <LegalPage title="Privacy Policy" updated="20 September 2026">
      <p>
        This explains what {COMPANY.legalName} collects, why, and what you can ask us to do about
        it. It is written to be read rather than to be defensible.
      </p>

      <h2>What we collect</h2>
      <ul>
        <li>
          <strong>Your account.</strong> Name, email address, password (stored only as a hash we
          cannot reverse), and your business&rsquo;s name, address, phone, email and tax number.
        </li>
        <li>
          <strong>What you create.</strong> Your customers&rsquo; names and contact details, your
          products and services, and your quotations, invoices and payment records.
        </li>
        <li>
          <strong>Payment records.</strong> Amounts, currency, status, the method reported by the
          provider (&ldquo;upi&rdquo;, &ldquo;card&rdquo;), and the provider&rsquo;s reference.
        </li>
        <li>
          <strong>Technical logs.</strong> Request paths, timestamps and error details, kept to
          diagnose faults.
        </li>
      </ul>

      <h2>What we never collect</h2>
      <p>
        <strong>We never see or store card numbers, CVVs, UPI PINs or bank passwords.</strong> Card
        details are entered on the payment provider&rsquo;s own checkout and never pass through
        Quotely.
      </p>

      <h2>Your customers&rsquo; data belongs to you</h2>
      <p>
        When you add a customer, that information is yours. We process it only to provide Quotely
        to you. We do not market to your customers, sell their details, or use them to train
        anything.
      </p>

      <h2>Why we collect it</h2>
      <ul>
        <li>To run the service you signed up for — producing your documents and tracking payment.</li>
        <li>To take payment for your subscription.</li>
        <li>To keep the service secure and diagnose faults.</li>
        <li>To meet legal and tax obligations.</li>
      </ul>

      <h2>Who else sees it</h2>
      <ul>
        <li>
          <strong>Razorpay</strong> — payment processing. Handles the payment itself; receives the
          amount, currency and invoice reference.
        </li>
        <li>
          <strong>Our hosting and database providers</strong> — they store the data on our behalf
          under contract and do not use it for anything else.
        </li>
        <li>
          <strong>Anyone you send a link to.</strong> A quotation or invoice link shows that
          document to whoever holds it, which is the point of sharing it.
        </li>
      </ul>
      <p>We do not sell your data to anyone, for any purpose.</p>

      <h2>How it is protected</h2>
      <ul>
        <li>Everything travels over HTTPS.</li>
        <li>Passwords are hashed, never stored in a readable form.</li>
        <li>
          Payment-account credentials you connect are encrypted at rest with AES-256-GCM, under a
          key held separately from the database.
        </li>
        <li>
          Each business&rsquo;s data is isolated. A request can only ever reach the data belonging
          to the signed-in account.
        </li>
        <li>Share links use unguessable tokens, and only a hash of each token is stored.</li>
      </ul>

      <h2>How long we keep it</h2>
      <p>
        For as long as your account exists, and afterwards only where law requires — financial
        records are generally kept for eight years under Indian tax rules. Technical logs are kept
        for up to 90 days.
      </p>

      <h2>Your rights</h2>
      <p>
        You can ask for a copy of your data, ask us to correct it, or ask us to delete your account
        and everything in it. Email{" "}
        <a href={`mailto:${COMPANY.email}`}>{COMPANY.email}</a> and we will respond within{" "}
        {COMPANY.responseTime} and complete the request within 30 days.
      </p>

      <h2>Cookies</h2>
      <p>
        Quotely uses browser storage to keep you signed in and to remember small preferences such
        as a filter you last chose. We do not use advertising or cross-site tracking cookies.
      </p>

      <h2>Changes</h2>
      <p>
        If this policy changes materially, we will tell account holders by email before it takes
        effect. The date at the top always reflects the current version.
      </p>

      <h2>Contact</h2>
      <address className="space-y-0.5 not-italic">
        <p>{COMPANY.legalName}</p>
        {COMPANY.addressLines.map((line) => (
          <p key={line}>{line}</p>
        ))}
        <p>
          <a href={`mailto:${COMPANY.email}`}>{COMPANY.email}</a>
        </p>
      </address>
    </LegalPage>
  );
}
