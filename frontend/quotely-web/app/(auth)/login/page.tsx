"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth";
import { Button } from "@/components/ui/button";
import { Field, Input, PasswordInput } from "@/components/ui/field";
import { checkEmail } from "@/lib/validation";

export default function LoginPage() {
  const { login, user, ready } = useAuth();
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [touched, setTouched] = useState({ email: false });

  const emailCheck = checkEmail(email);

  useEffect(() => {
    if (ready && user) router.replace("/dashboard");
  }, [ready, user, router]);

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(email.trim(), password);
      router.replace("/dashboard");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not sign in.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div>
      <h1 className="font-display text-subheading text-charcoal">Sign in</h1>
      <p className="mt-1 text-body text-fog">Welcome back. Enter your details to continue.</p>

      <form onSubmit={onSubmit} className="mt-6 space-y-4" noValidate>
        <Field label="Email" htmlFor="email" required check={emailCheck} touched={touched.email}>
          <Input
            id="email"
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            onBlur={() => setTouched({ email: true })}
          />
        </Field>

        {/* No validation tick on the way in: the password is either the right one or it is not,
            and only the server knows which. A tick here would be meaningless reassurance. */}
        <Field label="Password" htmlFor="password" required>
          <PasswordInput
            id="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </Field>

        {error && (
          <p role="alert" className="rounded-btn border border-ash bg-rose-wash px-3 py-2 text-body text-rose-ink">
            {error}
          </p>
        )}

        <Button type="submit" loading={submitting} className="w-full">
          Sign in
        </Button>
      </form>

      <p className="mt-5 text-center text-body text-fog">
        New to Quotely?{" "}
        <Link href="/register" className="font-medium text-electric hover:underline">
          Create an account
        </Link>
      </p>
    </div>
  );
}
