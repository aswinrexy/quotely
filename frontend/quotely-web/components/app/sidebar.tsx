"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/cn";
import { Icon } from "@/components/ui/icons";

type NavItem = { href: string; label: string; icon: (p: { className?: string }) => React.ReactElement };

/** Daily work above the rule, configuration below it. */
export const PRIMARY_NAV: NavItem[] = [
  { href: "/dashboard", label: "Dashboard", icon: Icon.dashboard },
  { href: "/quotations", label: "Quotations", icon: Icon.quotation },
  { href: "/invoices", label: "Invoices", icon: Icon.invoice },
  { href: "/customers", label: "Customers", icon: Icon.customers },
  { href: "/products", label: "Products & Services", icon: Icon.catalogue },
];

export const SECONDARY_NAV: NavItem[] = [
  { href: "/business-profile", label: "Business Profile", icon: Icon.building },
  { href: "/settings/payments", label: "Payments", icon: Icon.card },
];

export function isActive(pathname: string, href: string) {
  return pathname === href || pathname.startsWith(`${href}/`);
}

export function NavLink({
  item,
  active,
  onNavigate,
}: {
  item: NavItem;
  active: boolean;
  onNavigate?: () => void;
}) {
  const Glyph = item.icon;
  return (
    <Link
      href={item.href}
      onClick={onNavigate}
      aria-current={active ? "page" : undefined}
      className={cn(
        "flex items-center gap-2.5 rounded-btn px-2 py-2 text-body font-medium",
        "transition-colors duration-150 ease-out",
        active
          ? // A selected item, not a warning: soft chromatic fill, no thick left border.
            "bg-blue-wash text-charcoal"
          : "text-steel hover:bg-paper hover:text-charcoal",
      )}
    >
      <Glyph className={cn("h-4 w-4 shrink-0", active ? "text-electric" : "text-fog")} />
      <span className="truncate">{item.label}</span>
    </Link>
  );
}

export function SidebarContent({
  email,
  fullName,
  onLogout,
  onNavigate,
}: {
  email: string;
  fullName: string;
  onLogout: () => void;
  onNavigate?: () => void;
}) {
  const pathname = usePathname();

  return (
    <div className="flex h-full flex-col">
      <div className="px-4 py-4">
        <Link
          href="/dashboard"
          onClick={onNavigate}
          className="font-display text-body-xl text-charcoal"
        >
          Quote<span className="text-electric">ly</span>
        </Link>
      </div>

      <nav aria-label="Main" className="flex-1 space-y-0.5 px-2">
        {PRIMARY_NAV.map((item) => (
          <NavLink
            key={item.href}
            item={item}
            active={isActive(pathname, item.href)}
            onNavigate={onNavigate}
          />
        ))}

        <div className="!my-3 border-t border-ash" />

        {SECONDARY_NAV.map((item) => (
          <NavLink
            key={item.href}
            item={item}
            active={isActive(pathname, item.href)}
            onNavigate={onNavigate}
          />
        ))}
      </nav>

      <div className="border-t border-ash p-2">
        <div className="flex items-center gap-2.5 px-2 py-2">
          <span
            aria-hidden
            className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-paper text-caption font-semibold text-steel"
          >
            {(fullName || email).trim().charAt(0).toUpperCase()}
          </span>
          <div className="min-w-0">
            <p className="truncate text-body font-medium text-charcoal">{fullName || "Account"}</p>
            <p className="truncate text-caption text-fog">{email}</p>
          </div>
        </div>
        <button
          onClick={onLogout}
          className={cn(
            "mt-0.5 flex w-full items-center gap-2.5 rounded-btn px-2 py-2 text-left text-body font-medium",
            "text-steel transition-colors duration-150 ease-out hover:bg-paper hover:text-charcoal",
          )}
        >
          <Icon.logout className="h-4 w-4 shrink-0 text-fog" />
          Sign out
        </button>
      </div>
    </div>
  );
}
