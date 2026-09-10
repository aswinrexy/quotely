"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { api, tokenStore } from "@/lib/api";
import type { AuthResponse, User } from "@/types";

interface AuthContextValue {
  user: User | null;
  ready: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (input: { email: string; password: string; fullName: string; businessName?: string }) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [ready, setReady] = useState(false);
  const router = useRouter();

  useEffect(() => {
    const token = tokenStore.get();
    const stored = window.localStorage.getItem(tokenStore.userKey);
    if (token && stored) {
      try {
        setUser(JSON.parse(stored) as User);
      } catch {
        tokenStore.clear();
      }
    }
    setReady(true);
  }, []);

  // The API client raises this when a request comes back 401.
  useEffect(() => {
    const handle = () => {
      setUser(null);
      router.replace("/login");
    };
    window.addEventListener("quotely:unauthorized", handle);
    return () => window.removeEventListener("quotely:unauthorized", handle);
  }, [router]);

  const persist = useCallback((auth: AuthResponse) => {
    tokenStore.set(auth.accessToken);
    window.localStorage.setItem(tokenStore.userKey, JSON.stringify(auth.user));
    setUser(auth.user);
  }, []);

  const login = useCallback(
    async (email: string, password: string) => {
      persist(await api.post<AuthResponse>("/api/auth/login", { email, password }));
    },
    [persist],
  );

  const register = useCallback(
    async (input: { email: string; password: string; fullName: string; businessName?: string }) => {
      persist(await api.post<AuthResponse>("/api/auth/register", input));
    },
    [persist],
  );

  const logout = useCallback(() => {
    tokenStore.clear();
    setUser(null);
    router.replace("/login");
  }, [router]);

  const value = useMemo(
    () => ({ user, ready, login, register, logout }),
    [user, ready, login, register, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth must be used inside AuthProvider");
  return context;
}
