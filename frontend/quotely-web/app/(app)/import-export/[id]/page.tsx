import TradeInvoiceDetailPage from "./trade-invoice-detail-page";

/**
 * Static-export shell. See the note on the invoice equivalent: the real id is read from the
 * address bar by the client page, because the host rewrites every real URL onto this placeholder.
 */
export function generateStaticParams() {
  return [{ id: "id" }];
}

export default function Page() {
  return <TradeInvoiceDetailPage />;
}
