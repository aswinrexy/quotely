"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Field, Input, Textarea } from "@/components/ui/field";
import type { PublicResponseRequest } from "@/types";

export type ResponseKind = "accept" | "reject";

interface Props {
  kind: ResponseKind;
  submitting: boolean;
  error: string | null;
  defaultName?: string;
  defaultEmail?: string;
  onSubmit: (payload: PublicResponseRequest) => void;
  onCancel: () => void;
}

const COMMENT_LIMIT = 1000;

export function ResponseDialog({
  kind,
  submitting,
  error,
  defaultName = "",
  defaultEmail = "",
  onSubmit,
  onCancel,
}: Props) {
  const accepting = kind === "accept";
  const [name, setName] = useState(defaultName);
  const [email, setEmail] = useState(defaultEmail);
  const [comment, setComment] = useState("");
  const [nameError, setNameError] = useState<string | null>(null);
  const [emailError, setEmailError] = useState<string | null>(null);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !submitting) onCancel();
    };
    document.addEventListener("keydown", onKey);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = "";
    };
  }, [onCancel, submitting]);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setNameError(null);
    setEmailError(null);

    if (!name.trim()) {
      setNameError("Please enter your name.");
      return;
    }
    if (email.trim() && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
      setEmailError("Enter a valid email address.");
      return;
    }

    onSubmit({
      name: name.trim(),
      email: email.trim() || null,
      comment: comment.trim() || null,
    });
  }

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center p-0 sm:items-center sm:p-4">
      <div className="absolute inset-0 bg-slate-900/50" onClick={() => !submitting && onCancel()} aria-hidden />
      <div
        role="dialog"
        aria-modal="true"
        aria-label={accepting ? "Accept quotation" : "Reject quotation"}
        className="relative max-h-[92vh] w-full max-w-md overflow-y-auto rounded-t-2xl border border-slate-200 bg-white p-5 shadow-xl sm:rounded-2xl"
      >
        <h2 className="text-lg font-semibold text-slate-900">
          {accepting ? "Accept quotation?" : "Reject quotation?"}
        </h2>
        <p className="mt-1 text-sm text-slate-500">
          {accepting
            ? "Let the business know who approved this quotation."
            : "Let the business know who responded, and why if you would like to."}
        </p>

        <form onSubmit={submit} className="mt-5 space-y-4" noValidate>
          <Field label="Your name" htmlFor="response-name" required error={nameError}>
            <Input
              id="response-name"
              value={name}
              autoComplete="name"
              onChange={(e) => setName(e.target.value)}
              placeholder="John Smith"
            />
          </Field>

          <Field label="Email" htmlFor="response-email" error={emailError} hint="Optional.">
            <Input
              id="response-email"
              type="email"
              value={email}
              autoComplete="email"
              onChange={(e) => setEmail(e.target.value)}
              placeholder="john@example.com"
            />
          </Field>

          <Field
            label={accepting ? "Comment" : "Reason"}
            htmlFor="response-comment"
            hint={`Optional. ${COMMENT_LIMIT - comment.length} characters left.`}
          >
            <Textarea
              id="response-comment"
              value={comment}
              maxLength={COMMENT_LIMIT}
              onChange={(e) => setComment(e.target.value)}
              placeholder={accepting ? "Approved. Please proceed." : "The price is outside our budget."}
            />
          </Field>

          {error && (
            <p role="alert" className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
              {error}
            </p>
          )}

          <div className="flex flex-col-reverse gap-2 pt-1 sm:flex-row sm:justify-end">
            <Button type="button" variant="secondary" onClick={onCancel} disabled={submitting}>
              Cancel
            </Button>
            <Button type="submit" variant={accepting ? "primary" : "danger"} loading={submitting}>
              {accepting ? "Confirm acceptance" : "Confirm rejection"}
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
