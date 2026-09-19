import { SiteFooter, SiteHeader } from "@/components/public/site/site-chrome";

/**
 * Layout for the pages anyone can read without an account: the landing page and the policies.
 *
 * Deliberately outside the (app) group, so none of it is behind the auth guard. A policy page
 * that redirects to a login screen is a policy page nobody — including a gateway reviewer — can
 * actually read.
 */
export default function SiteLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col bg-canvas">
      <SiteHeader />
      <main className="flex-1">{children}</main>
      <SiteFooter />
    </div>
  );
}
