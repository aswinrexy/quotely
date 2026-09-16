import CustomerDetailPage from "./customer-detail-page";

/**
 * Static-export shell for a route whose parameter cannot be known at build time.
 *
 * Quotely is hosted as static files, and every page renders entirely in the browser from the
 * API — there is no server rendering to do. Next still insists a dynamic segment be enumerated
 * at build time, so one placeholder is emitted and the host rewrites every real URL onto it
 * (see public/_redirects). The browser router then reads the real "id" out of the address
 * bar, which is where it was always going to come from.
 */
export function generateStaticParams() {
  return [{ id: "id" }];
}

export default function Page() {
  return <CustomerDetailPage />;
}
