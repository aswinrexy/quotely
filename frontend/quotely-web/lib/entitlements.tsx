"use client";

import { createContext, useCallback, useContext, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { Entitlements } from "@/types";

/**
 * What this business may do, fetched once per session and shared by every page.
 *
 * Used only to decide what to SHOW — a locked button, a usage counter, an upgrade prompt. It is
 * never the thing that permits an action: the server checks again on every endpoint, because a
 * value the browser holds is a value the browser can change.
 *
 * While it is loading, nothing is treated as locked. A flash of "upgrade to continue" on a paid
 * account is worse than a moment of an enabled button that the server would refuse anyway.
 */
interface EntitlementsValue {
  entitlements: Entitlements | null;
  /** Re-reads after something that changes the picture — a coupon, a new invoice. */
  refresh: () => Promise<void>;
}

const EntitlementsContext = createContext<EntitlementsValue | null>(null);

export function EntitlementsProvider({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const [entitlements, setEntitlements] = useState<Entitlements | null>(null);

  const refresh = useCallback(async () => {
    if (!user) {
      setEntitlements(null);
      return;
    }
    try {
      setEntitlements(await api.get<Entitlements>("/api/billing/entitlements"));
    } catch {
      // A failure here must not break the page. Nothing is shown as locked, and the server
      // still refuses anything it should.
      setEntitlements(null);
    }
  }, [user]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return (
    <EntitlementsContext.Provider value={{ entitlements, refresh }}>
      {children}
    </EntitlementsContext.Provider>
  );
}

export function useEntitlements() {
  return useContext(EntitlementsContext) ?? { entitlements: null, refresh: async () => {} };
}

/**
 * Whether a feature should be shown as locked.
 *
 * Defaults to unlocked whenever the answer is not yet known, for the reason above.
 */
export function useLocked(feature: "quotations" | "payments"): boolean {
  const { entitlements } = useEntitlements();
  if (!entitlements || !entitlements.enforcementEnabled) return false;
  return feature === "quotations" ? !entitlements.canCreateQuotation : !entitlements.canConnectPayments;
}
