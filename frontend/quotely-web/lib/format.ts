const CURRENCY_SYMBOLS: Record<string, string> = {
  INR: "₹",
  USD: "$",
  EUR: "€",
  GBP: "£",
  AED: "AED ",
  SAR: "SAR ",
  AUD: "A$",
  CAD: "C$",
  SGD: "S$",
};

export const CURRENCIES = ["INR", "USD", "EUR", "GBP", "AED", "SAR", "AUD", "CAD", "SGD"];

export function currencySymbol(currency = "INR") {
  return CURRENCY_SYMBOLS[currency] ?? `${currency} `;
}

export function formatMoney(amount: number, currency = "INR") {
  const value = (Number.isFinite(amount) ? amount : 0).toLocaleString("en-US", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  return `${currencySymbol(currency)}${value}`;
}

/** Formats an ISO date (yyyy-MM-dd) without letting the browser shift it across timezones. */
export function formatDate(value: string) {
  if (!value) return "—";
  const [year, month, day] = value.slice(0, 10).split("-").map(Number);
  if (!year || !month || !day) return value;
  return new Date(Date.UTC(year, month - 1, day)).toLocaleDateString("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    timeZone: "UTC",
  });
}

export function todayIso() {
  return new Date().toISOString().slice(0, 10);
}

export function addDaysIso(iso: string, days: number) {
  const date = new Date(`${iso}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}
