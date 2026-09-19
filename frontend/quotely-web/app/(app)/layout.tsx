"use client";

import { useEffect, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import Link from "next/link";
import { useAuth } from "@/lib/auth";
import { EntitlementsProvider } from "@/lib/entitlements";
import { Spinner } from "@/components/ui/states";
import { Icon } from "@/components/ui/icons";
import { PRIMARY_NAV, SECONDARY_NAV, SidebarContent, isActive } from "@/components/app/sidebar";

/** Title shown in the top bar, resolved from the nav rather than duplicated per page. */
function useSectionTitle() {
  const pathname = usePathname();
  const match = [...PRIMARY_NAV, ...SECONDARY_NAV].find((item) => isActive(pathname, item.href));
  return match?.label ?? "Quotely";
}

export default function AppLayout({ children }: { children: React.ReactNode }) {
  const { user, ready, logout } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);
  const section = useSectionTitle();

  useEffect(() => {
    if (ready && !user) router.replace("/login");
  }, [ready, user, router]);

  // The drawer is a page-level overlay: close it when the route underneath changes.
  useEffect(() => {
    setMenuOpen(false);
  }, [pathname]);

  useEffect(() => {
    document.body.style.overflow = menuOpen ? "hidden" : "";
    return () => {
      document.body.style.overflow = "";
    };
  }, [menuOpen]);

  if (!ready || !user) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Spinner />
        <span className="sr-only">Loading…</span>
      </div>
    );
  }

  const sidebar = (
    <SidebarContent
      email={user.email}
      fullName={user.fullName}
      onLogout={logout}
      onNavigate={() => setMenuOpen(false)}
    />
  );

  return (
    // Entitlements are fetched once here and shared by every page beneath, rather than each page
    // asking again. They decide what is shown as locked; the server decides what is allowed.
    <EntitlementsProvider>
    <div className="min-h-screen lg:flex">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-100 focus:rounded-btn focus:bg-midnight focus:px-3 focus:py-2 focus:text-body focus:text-canvas"
      >
        Skip to content
      </a>

      <aside className="hidden w-60 shrink-0 border-r border-ash bg-canvas lg:sticky lg:top-0 lg:block lg:h-screen">
        {sidebar}
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        {/* A quiet top bar: where you are, and nothing else competing for attention. */}
        <header className="sticky top-0 z-30 flex h-14 items-center gap-3 border-b border-ash bg-canvas/95 px-4 backdrop-blur-sm sm:px-6">
          <button
            onClick={() => setMenuOpen(true)}
            aria-label="Open navigation"
            aria-expanded={menuOpen}
            className="-ml-1 rounded-btn p-2 text-steel transition-colors duration-150 ease-out hover:bg-paper hover:text-charcoal lg:hidden"
          >
            <Icon.menu />
          </button>

          <Link href="/dashboard" className="font-display text-body-lg text-charcoal lg:hidden">
            Quote<span className="text-electric">ly</span>
          </Link>

          <p className="hidden min-w-0 truncate text-body font-medium text-charcoal lg:block">
            {section}
          </p>
        </header>

        {menuOpen && (
          <div className="fixed inset-0 z-50 lg:hidden">
            <div
              className="absolute inset-0 bg-midnight/30"
              onClick={() => setMenuOpen(false)}
              aria-hidden
            />
            <aside className="relative h-full w-64 border-r border-ash bg-canvas">
              <button
                onClick={() => setMenuOpen(false)}
                aria-label="Close navigation"
                className="absolute right-2 top-3.5 rounded-btn p-2 text-steel hover:bg-paper hover:text-charcoal"
              >
                <Icon.close />
              </button>
              {sidebar}
            </aside>
          </div>
        )}

        <main id="main" className="min-w-0 flex-1">
          <div className="mx-auto max-w-[1200px] px-4 py-6 sm:px-6 lg:py-8">{children}</div>
        </main>
      </div>
    </div>
    </EntitlementsProvider>
  );
}
