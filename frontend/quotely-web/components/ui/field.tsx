"use client";

import { Children, cloneElement, isValidElement } from "react";
import { cn } from "@/lib/cn";
import { Icon } from "@/components/ui/icons";

/**
 * Inputs carry a near-black border rather than the usual hairline — the signature of the
 * system: a field reads as something to be filled in, not as decoration.
 */
export const inputClass =
  "block w-full rounded-input border border-midnight bg-canvas px-3 py-2 text-body text-charcoal " +
  "transition-colors duration-150 ease-out placeholder:text-fog " +
  "disabled:border-ash disabled:bg-paper disabled:text-fog";

interface FieldProps {
  label: string;
  htmlFor?: string;
  error?: string | null;
  hint?: string;
  required?: boolean;
  className?: string;
  children: React.ReactNode;
}

export function Field({ label, htmlFor, error, hint, required, className, children }: FieldProps) {
  const describedBy = htmlFor ? (error ? `${htmlFor}-error` : hint ? `${htmlFor}-hint` : undefined) : undefined;

  // The hint or error is wired onto the control itself, so a screen reader announces it with the
  // field rather than leaving it as loose text nearby.
  const control =
    describedBy || error
      ? Children.map(children, (child) =>
          isValidElement<{ "aria-describedby"?: string; "aria-invalid"?: boolean }>(child)
            ? cloneElement(child, {
                "aria-describedby": child.props["aria-describedby"] ?? describedBy,
                "aria-invalid": error ? true : child.props["aria-invalid"],
              })
            : child,
        )
      : children;

  return (
    <div className={cn("space-y-1.5", className)}>
      <label htmlFor={htmlFor} className="block text-body font-medium text-charcoal">
        {label}
        {required && (
          <span className="ml-0.5 text-rose-ink" aria-hidden>
            *
          </span>
        )}
        {required && <span className="sr-only"> (required)</span>}
      </label>
      {control}
      {hint && !error && (
        <p id={htmlFor ? `${htmlFor}-hint` : undefined} className="text-caption text-fog">
          {hint}
        </p>
      )}
      {error && (
        <p
          id={htmlFor ? `${htmlFor}-error` : undefined}
          role="alert"
          className="flex items-center gap-1 text-caption text-rose-ink"
        >
          <Icon.alert className="h-3.5 w-3.5 shrink-0" />
          {error}
        </p>
      )}
    </div>
  );
}

export function Input({ className, ...props }: React.InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={cn(inputClass, className)} />;
}

export function Textarea({ className, ...props }: React.TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea {...props} className={cn(inputClass, "min-h-24 resize-y", className)} />;
}

export function Select({ className, children, ...props }: React.SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select {...props} className={cn(inputClass, "appearance-none bg-canvas pr-8", className)}>
      {children}
    </select>
  );
}

/**
 * Search is a filter control rather than a form field, so it uses the quieter hairline border
 * and carries its icon inline.
 */
export function SearchInput({
  className,
  ...props
}: React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <div className={cn("relative", className)}>
      <Icon.search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fog" />
      <input
        type="search"
        {...props}
        className={cn(
          "block h-9 w-full rounded-btn border border-ash bg-canvas pl-9 pr-3 text-body text-charcoal",
          "transition-colors duration-150 ease-out placeholder:text-fog hover:border-smoke",
        )}
      />
    </div>
  );
}

/** A compact filter dropdown that sits beside the search box. */
export function FilterSelect({
  className,
  children,
  ...props
}: React.SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      {...props}
      className={cn(
        "h-9 rounded-btn border border-ash bg-canvas px-3 text-body font-medium text-charcoal",
        "transition-colors duration-150 ease-out hover:border-smoke",
        className,
      )}
    >
      {children}
    </select>
  );
}
