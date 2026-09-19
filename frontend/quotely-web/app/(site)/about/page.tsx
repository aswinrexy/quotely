import { COMPANY } from "@/components/public/site/site-chrome";

export const metadata = {
  title: "About — Quotely",
  description: "Who builds Quotely and why.",
};

export default function AboutPage() {
  return (
    <div className="mx-auto max-w-3xl px-4 py-12">
      <h1 className="font-display text-heading-sm text-charcoal">About Quotely</h1>

      <div className="mt-8 space-y-6 text-body text-steel">
        <p>
          Quotely is built for small service businesses in India — electricians, plumbers,
          interior fitters, consultants — who spend evenings formatting documents instead of
          resting.
        </p>
        <p>
          The idea is narrow on purpose. Send a quotation that looks professional. When it is
          accepted, turn it into an invoice without retyping anything. Share a link the customer
          can pay from. See what is still outstanding. That is the whole product.
        </p>
        <p>
          The part we care most about: <strong>your money goes to you</strong>. Every business on
          Quotely connects its own payment account, so a customer&rsquo;s payment settles into that
          business&rsquo;s bank. We are not a middleman holding your cash, and we take no cut of it.
        </p>

        <h2 className="text-body-lg font-semibold text-charcoal">Business details</h2>
        <address className="space-y-0.5 not-italic">
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
  );
}
