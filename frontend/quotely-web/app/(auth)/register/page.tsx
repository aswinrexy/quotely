"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth";
import { Button } from "@/components/ui/button";
import { Field, Input } from "@/components/ui/field";

export default function RegisterPage() {
  const { register } = useAuth();
  const router = useRouter();
  const [form, setForm] = useState({ fullName: "", businessName: "", email: "", password: "" });
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  function update(key: keyof typeof form, value: string) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);

    if (form.password.length < 8) {
      setError("Password must be at least 8 characters.");
      return;
    }

    setSubmitting(true);
    try {
      await register({
        email: form.email.trim(),
        password: form.password,
        fullName: form.fullName.trim(),
        businessName: form.businessName.trim() || undefined,
      });
      router.replace("/dashboard");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create your account.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div>
      <h1 className="font-display text-subheading text-charcoal">Create your account</h1>
      <p className="mt-1 text-body text-fog">Start sending professional quotations today.</p>

      <form onSubmit={onSubmit} className="mt-6 space-y-4" noValidate>
        <Field label="Your name" htmlFor="fullName" required>
          <Input
            id="fullName"
            required
            value={form.fullName}
            onChange={(e) => update("fullName", e.target.value)}
            placeholder="Ravi Kumar"
          />
        </Field>

        <Field label="Business name" htmlFor="businessName" hint="You can change this later in Business Profile.">
          <Input
            id="businessName"
            value={form.businessName}
            onChange={(e) => update("businessName", e.target.value)}
            placeholder="ABC Electricals"
          />
        </Field>

        <Field label="Email" htmlFor="email" required>
          <Input
            id="email"
            type="email"
            autoComplete="email"
            required
            value={form.email}
            onChange={(e) => update("email", e.target.value)}
            placeholder="you@business.com"
          />
        </Field>

        <Field label="Password" htmlFor="password" required hint="At least 8 characters.">
          <Input
            id="password"
            type="password"
            autoComplete="new-password"
            required
            minLength={8}
            value={form.password}
            onChange={(e) => update("password", e.target.value)}
            placeholder="••••••••"
          />
        </Field>

        {error && (
          <p role="alert" className="rounded-btn border border-ash bg-rose-wash px-3 py-2 text-body text-rose-ink">
            {error}
          </p>
        )}

        <Button type="submit" loading={submitting} className="w-full">
          Create account
        </Button>
      </form>

      <p className="mt-5 text-center text-body text-fog">
        Already have an account?{" "}
        <Link href="/login" className="font-medium text-electric hover:underline">
          Sign in
        </Link>
      </p>
    </div>
  );
}
