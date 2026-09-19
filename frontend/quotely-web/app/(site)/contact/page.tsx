import { COMPANY } from "@/components/public/site/site-chrome";

export const metadata = {
  title: "Contact us — Quotely",
  description: "How to reach Quotely: email, phone, support hours and registered address.",
};

export default function ContactPage() {
  return (
    <div className="mx-auto max-w-3xl px-4 py-12">
      <h1 className="font-display text-heading-sm text-charcoal">Contact us</h1>
      <p className="mt-2 text-body-lg text-steel">
        A real person reads these. We reply within {COMPANY.responseTime}.
      </p>

      <div className="mt-8 grid gap-4 sm:grid-cols-2">
        <div className="rounded-card border border-ash bg-canvas p-5">
          <h2 className="text-body font-semibold text-charcoal">Email</h2>
          <p className="mt-1.5 text-body">
            <a href={`mailto:${COMPANY.email}`} className="text-electric hover:underline">
              {COMPANY.email}
            </a>
          </p>
          <p className="mt-1 text-caption text-fog">The fastest way to reach us.</p>
        </div>

        <div className="rounded-card border border-ash bg-canvas p-5">
          <h2 className="text-body font-semibold text-charcoal">Phone</h2>
          <p className="mt-1.5 text-body">
            <a href={`tel:${COMPANY.phone.replace(/\s/g, "")}`} className="text-electric hover:underline">
              {COMPANY.phone}
            </a>
          </p>
          <p className="mt-1 text-caption text-fog">{COMPANY.supportHours}</p>
        </div>
      </div>

      <div className="mt-6 rounded-card border border-ash bg-canvas p-5">
        <h2 className="text-body font-semibold text-charcoal">Registered address</h2>
        <address className="mt-1.5 space-y-0.5 not-italic text-body text-steel">
          <p>{COMPANY.legalName}</p>
          {COMPANY.addressLines.map((line) => (
            <p key={line}>{line}</p>
          ))}
        </address>
      </div>

      <div className="mt-8 space-y-5 text-body text-steel">
        <div>
          <h2 className="text-body-lg font-semibold text-charcoal">About a payment you made</h2>
          <p className="mt-1.5">
            If you have paid an invoice you received through Quotely, that payment was collected by
            the business that sent it to you, not by Quotely. Contact them directly — their name,
            email and phone number are printed on the invoice. We can help you reach them if you
            cannot.
          </p>
        </div>

        <div>
          <h2 className="text-body-lg font-semibold text-charcoal">About your Quotely subscription</h2>
          <p className="mt-1.5">
            Email us at{" "}
            <a href={`mailto:${COMPANY.email}`} className="text-electric hover:underline">
              {COMPANY.email}
            </a>{" "}
            from the address on your account, and include your business name.
          </p>
        </div>
      </div>
    </div>
  );
}
