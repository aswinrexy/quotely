"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/cn";
import { Field, Input, PasswordInput } from "@/components/ui/field";
import { allValid, checkEmail, checkPassword, checkRequired, passwordRules } from "@/lib/validation";
import { Icon } from "@/components/ui/icons";

export default function RegisterPage() {
  const { register } = useAuth();
  const router = useRouter();
  const [form, setForm] = useState({ fullName: "", businessName: "", email: "", password: "" });
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [touched, setTouched] = useState<Record<string, boolean>>({});

  const checks = {
    fullName: checkRequired(form.fullName, "Your name"),
    email: checkEmail(form.email),
    password: checkPassword(form.password),
  };

  // Every required field well-formed. The button stays enabled regardless — disabling it leaves
  // someone staring at a form with no idea what is wrong — but this decides whether to submit.
  const ready = allValid(checks.fullName, checks.email, checks.password);

  function update(key: keyof typeof form, value: string) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function touch(key: string) {
    setTouched((current) => ({ ...current, [key]: true }));
  }

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);

    if (!ready) {
      // Reveal every message at once rather than one per attempt.
      setTouched({ fullName: true, email: true, password: true });
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
        <Field
          label="Your name"
          htmlFor="fullName"
          required
          check={checks.fullName}
          touched={touched.fullName}
        >
          <Input
            id="fullName"
            autoComplete="name"
            required
            value={form.fullName}
            onChange={(e) => update("fullName", e.target.value)}
            onBlur={() => touch("fullName")}
          />
        </Field>

        <Field label="Business name" htmlFor="businessName" hint="You can change this later in Business Profile.">
          <Input
            id="businessName"
            autoComplete="organization"
            value={form.businessName}
            onChange={(e) => update("businessName", e.target.value)}
          />
        </Field>

        <Field label="Email" htmlFor="email" required check={checks.email} touched={touched.email}>
          <Input
            id="email"
            type="email"
            autoComplete="email"
            required
            value={form.email}
            onChange={(e) => update("email", e.target.value)}
            onBlur={() => touch("email")}
          />
        </Field>

        <Field label="Password" htmlFor="password" required check={checks.password} touched={touched.password}>
          <PasswordInput
            id="password"
            autoComplete="new-password"
            required
            minLength={8}
            value={form.password}
            onChange={(e) => update("password", e.target.value)}
            onBlur={() => touch("password")}
          />
        </Field>

        {/*
          The rules, ticking off as they are met. Shown as a checklist rather than as an error
          after the fact, because these are the server's actual requirements and someone choosing
          a password should be able to see what is wanted before being told they got it wrong.
        */}
        <ul className="-mt-2 space-y-1">
          {passwordRules(form.password).map((rule) => (
            <li
              key={rule.label}
              className={cn("flex items-center gap-1.5 text-caption", rule.met ? "text-mint-ink" : "text-fog")}
            >
              <Icon.check className={cn("h-3.5 w-3.5 shrink-0", rule.met ? "opacity-100" : "opacity-30")} />
              {rule.label}
            </li>
          ))}
        </ul>

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
