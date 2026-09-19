/**
 * Field validation, in one place.
 *
 * Every rule here returns the same shape, so a form can ask "is this field good yet?" without
 * each form inventing its own answer. That matters for the tick: a green check that appears on
 * a merely non-empty field is worse than no check at all, because it tells someone their input
 * is accepted when the server may still reject it.
 *
 * These are conveniences for the person typing, never a security boundary. The API validates
 * everything again, and is the only thing that decides what is stored.
 */

export type FieldState = "empty" | "valid" | "invalid";

export interface FieldCheck {
  state: FieldState;
  /** Shown only once the field has been touched, so nobody is scolded mid-word. */
  error: string | null;
}

const OK: FieldCheck = { state: "valid", error: null };
const EMPTY: FieldCheck = { state: "empty", error: null };

const fail = (error: string): FieldCheck => ({ state: "invalid", error });

/** A required free-text value: a name, a business name, a label. */
export function checkRequired(value: string, what = "This field"): FieldCheck {
  const trimmed = value.trim();
  if (!trimmed) return EMPTY;
  if (trimmed.length < 2) return fail(`${what} is too short.`);
  return OK;
}

/**
 * Email, checked loosely on purpose.
 *
 * The only way to know an address is real is to send to it, and every "strict" regex in
 * circulation rejects addresses that are perfectly valid. This catches the mistakes people
 * actually make — a missing @, a missing dot, a trailing space — and leaves the rest to the server.
 */
export function checkEmail(value: string, required = true): FieldCheck {
  const trimmed = value.trim();
  if (!trimmed) return required ? EMPTY : OK;
  if (/\s/.test(trimmed)) return fail("An email address cannot contain spaces.");
  if (!/^[^@]+@[^@]+\.[^@]{2,}$/.test(trimmed)) return fail("Enter a valid email address, like name@business.com.");
  return OK;
}

/**
 * A phone number.
 *
 * Deliberately permissive about formatting — spaces, dashes, brackets and a leading + are all
 * fine, because that is how people write numbers down. What is checked is the count of digits.
 *
 * An Indian mobile is ten digits starting 6–9, and is the common case; with a country code it
 * becomes eleven or twelve. International numbers run to fifteen digits under E.164. Rejecting
 * anything outside that range catches typos without turning away a legitimate foreign customer.
 */
export function checkPhone(value: string, required = false): FieldCheck {
  const trimmed = value.trim();
  if (!trimmed) return required ? EMPTY : OK;

  if (/[^\d\s+()\-.]/.test(trimmed)) return fail("A phone number can only contain digits, spaces, + ( ) and -.");

  const digits = trimmed.replace(/\D/g, "");
  if (digits.length < 8) return fail("That number looks too short.");
  if (digits.length > 15) return fail("That number looks too long.");

  // A bare ten-digit number is read as Indian, where mobiles never begin 0–5.
  const bare = digits.length === 10;
  if (bare && !/^[6-9]/.test(digits)) return fail("An Indian mobile number starts with 6, 7, 8 or 9.");

  return OK;
}

/** What the API enforces: at least eight characters. Nothing more is invented here. */
export const PASSWORD_MIN_LENGTH = 8;

export function checkPassword(value: string): FieldCheck {
  if (!value) return EMPTY;
  if (value.length < PASSWORD_MIN_LENGTH)
    return fail(`Use at least ${PASSWORD_MIN_LENGTH} characters.`);
  return OK;
}

/** Confirming a password: only meaningful once the first one is valid. */
export function checkPasswordConfirmation(password: string, confirmation: string): FieldCheck {
  if (!confirmation) return EMPTY;
  if (password !== confirmation) return fail("The two passwords do not match.");
  return OK;
}

/** A GSTIN is fifteen characters in a fixed shape. Optional, but checked when given. */
export function checkTaxNumber(value: string): FieldCheck {
  const trimmed = value.trim().toUpperCase();
  if (!trimmed) return OK;
  if (!/^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]{3}$/.test(trimmed))
    return fail("A GSTIN is 15 characters, like 33ABCDE1234F1Z5.");
  return OK;
}

/** A Razorpay publishable key. The secret is never shape-checked — only the provider knows it. */
export function checkRazorpayKeyId(value: string): FieldCheck {
  const trimmed = value.trim();
  if (!trimmed) return EMPTY;
  if (!trimmed.startsWith("rzp_")) return fail("A Razorpay Key ID begins with rzp_.");
  if (trimmed.length < 12) return fail("That Key ID looks incomplete.");
  return OK;
}

/** A positive money amount. Blank is "not filled in yet", not "invalid". */
export function checkAmount(value: string, { required = true, max }: { required?: boolean; max?: number } = {}): FieldCheck {
  const trimmed = value.trim();
  if (!trimmed) return required ? EMPTY : OK;

  const amount = Number(trimmed);
  if (!Number.isFinite(amount)) return fail("Enter a number.");
  if (amount <= 0) return fail("Enter an amount greater than zero.");
  if (max !== undefined && amount > max) return fail(`That is more than the ${max.toFixed(2)} outstanding.`);
  return OK;
}

/** True when every check passed and nothing required is still blank. */
export function allValid(...checks: FieldCheck[]): boolean {
  return checks.every((check) => check.state === "valid");
}
