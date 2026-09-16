"use client";

import { useParams, usePathname } from "next/navigation";

/**
 * The value of a dynamic route segment, read from the address bar.
 *
 * Quotely ships as static files: every page renders in the browser from the API, and there is no
 * server rendering to do. Next still requires a dynamic segment to be enumerated at build time, so
 * each one is exported once under a placeholder and the host rewrites the real URL onto it.
 *
 * That leaves `useParams()` reporting the placeholder — it is baked into the prerendered page and
 * never revisited — while `usePathname()` reports the URL the visitor actually opened. So the
 * segment is taken from the path, positioned by the route pattern rather than counted blindly.
 *
 * `useParams()` remains the fallback: under `next dev` there is no prerender and it is correct.
 *
 * @param pattern The route as it appears in the URL, e.g. "/q/[token]" or "/customers/[id]/edit".
 *                Route groups such as "(app)" are omitted — they never reach the address bar.
 * @param name    The segment to read, without brackets.
 */
export function useRouteParam(pattern: string, name: string): string {
  const pathname = usePathname();
  const params = useParams<Record<string, string>>();

  const expected = segments(pattern);
  const index = expected.indexOf(`[${name}]`);
  const actual = segments(pathname ?? "");

  // A path of a different shape means the pattern and the route have drifted apart; the baked
  // param is wrong but it is at least a defined value, and the API will answer 404 either way.
  if (index < 0 || actual.length !== expected.length) return params?.[name] ?? "";

  return decodeURIComponent(actual[index]);
}

function segments(path: string): string[] {
  return path.split("/").filter(Boolean);
}
