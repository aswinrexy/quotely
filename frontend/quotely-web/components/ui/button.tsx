"use client";

import { cn } from "@/lib/cn";

type Variant = "primary" | "secondary" | "ghost" | "danger";
type Size = "sm" | "md";

const VARIANTS: Record<Variant, string> = {
  // The committed action: near-black fill, one per surface. Blue is a highlight, not a button.
  primary: "bg-midnight text-canvas shadow-subtle hover:bg-charcoal",
  secondary: "border border-ash bg-canvas text-charcoal hover:bg-paper",
  ghost: "text-steel hover:bg-paper hover:text-charcoal",
  // Restrained until the user commits; the confirmation dialog carries the red.
  danger: "border border-ash bg-canvas text-rose-ink hover:border-rose-ink/30 hover:bg-rose-wash/50",
};

const SIZES: Record<Size, string> = {
  sm: "h-8 gap-1.5 px-3 text-caption",
  md: "h-9 gap-2 px-3.5 text-body",
};

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
  loading?: boolean;
}

export function Button({
  variant = "primary",
  size = "md",
  loading = false,
  className,
  children,
  disabled,
  ...props
}: ButtonProps) {
  return (
    <button
      {...props}
      disabled={disabled || loading}
      className={cn(
        "inline-flex shrink-0 items-center justify-center rounded-btn font-medium",
        "transition-colors duration-150 ease-out",
        "disabled:cursor-not-allowed disabled:opacity-50",
        VARIANTS[variant],
        SIZES[size],
        className,
      )}
    >
      {loading && (
        <span
          aria-hidden
          className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-current border-t-transparent"
        />
      )}
      {children}
    </button>
  );
}
