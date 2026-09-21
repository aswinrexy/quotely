import TradeInvoiceEditPage from "./trade-invoice-edit-page";

export function generateStaticParams() {
  return [{ id: "id" }];
}

export default function Page() {
  return <TradeInvoiceEditPage />;
}
