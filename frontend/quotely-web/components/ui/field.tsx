"use client";

import { Children, cloneElement, isValidElement, useId, useState } from "react";
import { cn } from "@/lib/cn";
import { Icon } from "@/components/ui/icons";
import type { FieldCheck } from "@/lib/validation";

/**
 * Inputs carry a near-black border rather than the usual hairline — the signature of the
 * system: a field reads as something to be filled in, not as decoration.
 */
export const inputClass =
  "block w-full rounded-input border border-midnight bg-canvas px-3 py-2 text-body text-charcoal " +
  "transition-colors duration-150 ease-out placeholder:text-fog " +
  "disabled:border-ash disabled:bg-paper disabled:text-fog";

/**
 * A password field with a reveal toggle.
 *
 * People mistype passwords, and on a phone they mistype them constantly. Letting someone look at
 * what they have typed is the single cheapest way to cut failed sign-ins — and it is their own
 * screen, which they are better placed to judge than we are.
 *
 * It is a <button type="button"> on purpose: inside a form, a bare <button> submits.
 */
export function PasswordInput({
  className,
  id,
  ...props
}: Omit<React.InputHTMLAttributes<HTMLInputElement>, "type">) {
  const [visible, setVisible] = useState(false);
  const generatedId = useId();
  const inputId = id ?? generatedId;

  return (
    <div className="relative">
      <input
        {...props}
        id={inputId}
        type={visible ? "text" : "password"}
        // Room for the toggle, and for the tick when the field sits inside a Field.
        className={cn(inputClass, "pr-20", className)}
      />
      <button
        type="button"
        onClick={() => setVisible((shown) => !shown)}
        // Reachable, but after the field itself — someone tabbing through a form is heading for
        // the submit button, not for this.
        tabIndex={-1}
        aria-controls={inputId}
        aria-pressed={visible}
        aria-label={visible ? "Hide password" : "Show password"}
        title={visible ? "Hide password" : "Show password"}
        className={cn(
          "absolute right-2 top-1/2 -translate-y-1/2 rounded-btn p-1.5",
          "text-fog transition-colors duration-150 ease-out hover:bg-paper hover:text-charcoal",
        )}
      >
        {visible ? <Icon.eyeOff className="h-4 w-4" /> : <Icon.eye className="h-4 w-4" />}
      </button>
    </div>
  );
}

interface FieldProps {
  label: string;
  htmlFor?: string;
  error?: string | null;
  hint?: string;
  required?: boolean;
  className?: string;
  /**
   * The result of a validator, when the field has one. Drives the green tick and — once the
   * field has been touched — the error message, so a form does not have to wire both by hand.
   *
   * <see cref="error"/> still wins when it is set: a message from the server is about something
   * the browser could not have known, and must not be overwritten by a local rule that passes.
   */
  check?: FieldCheck;
  /**
   * Whether the person has finished with this field. The tick and the error both wait for it.
   * Marking a field wrong while someone is still typing the third letter of their email is the
   * most common way validation makes a form feel hostile.
   */
  touched?: boolean;
  children: React.ReactNode;
}

export function Field({ label, htmlFor, error, hint, required, className, check, touched, children }: FieldProps) {
  // A local rule only speaks once the field has been left alone.
  const localError = touched && check?.state === "invalid" ? check.error : null;
  const shownError = error ?? localError;

  // The tick means "this is well-formed", not "this is correct" — only the server can say that.
  const showTick = check?.state === "valid" && (touched ?? true);

  const describedBy = htmlFor ? (shownError ? `${htmlFor}-error` : hint ? `${htmlFor}-hint` : undefined) : undefined;

  // A PasswordInput puts its own reveal toggle against the trailing edge, so the tick has to
  // stand off far enough to clear it — otherwise the two render on top of each other, which is
  // exactly what happened the first time this was built.
  const hasTrailingControl = Children.toArray(children).some(
    (child) => isValidElement(child) && child.type === PasswordInput,
  );

  // The hint or error is wired onto the control itself, so a screen reader announces it with the
  // field rather than leaving it as loose text nearby.
  const control =
    describedBy || shownError
      ? Children.map(children, (child) =>
          isValidElement<{ "aria-describedby"?: string; "aria-invalid"?: boolean }>(child)
            ? cloneElement(child, {
                "aria-describedby": child.props["aria-describedby"] ?? describedBy,
                "aria-invalid": shownError ? true : child.props["aria-invalid"],
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
      {/*
        The tick sits inside the field's trailing edge rather than beside the label, so a column
        of fields reads as a column of ticks as it is filled in. aria-hidden because the state is
        already announced through aria-invalid and the error text — a screen reader does not need
        to be told twice, in a decorative way.
      */}
      <div className="relative">
        {control}
        {showTick && (
          <span
            aria-hidden
            className={cn(
              "pointer-events-none absolute top-1/2 -translate-y-1/2 text-mint-ink",
              hasTrailingControl ? "right-10" : "right-3",
            )}
          >
            <Icon.check className="h-4 w-4" />
          </span>
        )}
      </div>
      {hint && !shownError && (
        <p id={htmlFor ? `${htmlFor}-hint` : undefined} className="text-caption text-fog">
          {hint}
        </p>
      )}
      {shownError && (
        <p
          id={htmlFor ? `${htmlFor}-error` : undefined}
          role="alert"
          className="flex items-center gap-1 text-caption text-rose-ink"
        >
          <Icon.alert className="h-3.5 w-3.5 shrink-0" />
          {shownError}
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
