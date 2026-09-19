"use client";

import Link from "next/link";
import { Panel } from "@/components/ui/card";
import { Icon } from "@/components/ui/icons";
import { cn } from "@/lib/cn";
import type { UsageQuota } from "@/types";

/**
 * Shown where a paid feature would be.
 *
 * The feature stays visible and its purpose stays described — hiding it would leave someone
 * wondering whether Quotely does the thing at all. What changes is that the action is replaced by
 * a straight explanation of why it is unavailable and what to do about it.
 */
export function UpgradeNotice({
  title,
  body,
  className,
}: {
  title: string;
  body: string;
  className?: string;
}) {
  return (
    <Panel className={cn("flex flex-wrap items-start gap-3", className)}>
      <span className="mt-0.5 shrink-0 rounded-full bg-blue-wash p-1.5 text-electric">
        <Icon.alert className="h-4 w-4" />
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-body font-semibold text-charcoal">{title}</p>
        <p className="mt-1 text-body text-fog">{body}</p>
        <Link
          href="/settings/billing"
          className="mt-3 inline-block rounded-btn bg-midnight px-3.5 py-2 text-body font-medium text-canvas hover:bg-charcoal"
        >
          Upgrade to Quotely Pro
        </Link>
      </div>
    </Panel>
  );
}

/**
 * "7 of 10 used" beside a create button.
 *
 * Silent on a paid account, and silent until someone is over halfway through an allowance —
 * counting down from the very first one would make a generous limit feel like a leash.
 */
export function UsageHint({ quota, what }: { quota: UsageQuota | undefined; what: string }) {
  if (!quota?.hasLimit || quota.limit === null) return null;
  if (quota.used < quota.limit / 2) return null;

  return (
    <span
      className={cn(
        "text-caption",
        quota.exhausted ? "font-medium text-rose-ink" : "text-fog",
      )}
    >
      {quota.exhausted ? (
        <>
          {what} limit reached ({quota.used} of {quota.limit}) ·{" "}
          <Link href="/settings/billing" className="underline hover:text-charcoal">
            Upgrade
          </Link>
        </>
      ) : (
        <>
          {quota.used} of {quota.limit} {what} used
        </>
      )}
    </span>
  );
}
