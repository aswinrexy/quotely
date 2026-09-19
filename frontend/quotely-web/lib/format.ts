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

/**
 * Today, as the person in front of the screen understands it.
 *
 * NOT toISOString() — that converts to UTC first, so anywhere east of Greenwich returns
 * yesterday for the early hours of the morning. In India (UTC+5:30) every invoice created before
 * 05:30 was dated the previous day, which is exactly the bug this replaced.
 */
export function todayIso() {
  const now = new Date();
  const month = `${now.getMonth() + 1}`.padStart(2, "0");
  const day = `${now.getDate()}`.padStart(2, "0");
  return `${now.getFullYear()}-${month}-${day}`;
}

export function addDaysIso(iso: string, days: number) {
  const date = new Date(`${iso}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}
