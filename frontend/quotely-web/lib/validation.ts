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

  // Deliberately requires a dot and a two-letter-or-longer ending after it, so "asw@iim" is
  // rejected while "asw@iim.com" and "asw@iim.co.in" are accepted. The domain part cannot start
  // or end with a dot or hyphen, which is what catches the remaining typos people actually make.
  if (!/^[^\s@]+@[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,}$/.test(trimmed))
    return fail("Enter a valid email address, like name@business.com.");

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
/** Keeps everything but digits. What the mobile fields store and validate. */
export function digitsOnly(value: string): string {
  return value.replace(/\D/g, "");
}

/** Letters, spaces, hyphens and apostrophes — the characters place names actually use. */
export function lettersOnly(value: string): string {
  return value.replace(/[^A-Za-z\s'-]/g, "");
}

/** Letters and digits, for a postal code. Upper-cased, since that is how they are written. */
export function alphanumericOnly(value: string): string {
  return value.replace(/[^A-Za-z0-9\s]/g, "").toUpperCase();
}

/** An Indian mobile number: exactly ten digits, beginning 6, 7, 8 or 9. */
export const MOBILE_DIGITS = 10;

/**
 * A mobile number, for fields that are capped at ten digits and hold nothing but digits.
 *
 * Stricter than <see cref="checkPhone"/> on purpose: those fields strip non-digits as they are
 * typed, so anything reaching here is already clean and the only question is the length and
 * the leading digit.
 */
export function checkMobile(value: string, required = false): FieldCheck {
  const digits = digitsOnly(value);
  if (!digits) return required ? EMPTY : OK;

  if (digits.length < MOBILE_DIGITS) return fail(`A mobile number is ${MOBILE_DIGITS} digits.`);
  if (digits.length > MOBILE_DIGITS) return fail(`A mobile number is ${MOBILE_DIGITS} digits.`);
  if (!/^[6-9]/.test(digits)) return fail("An Indian mobile number starts with 6, 7, 8 or 9.");

  return OK;
}

/** A postal code: up to eight letters and digits. */
export const POSTAL_CODE_MAX = 8;

export function checkPostalCode(value: string): FieldCheck {
  const trimmed = value.trim();
  if (!trimmed) return OK;
  if (!/^[A-Za-z0-9\s]+$/.test(trimmed)) return fail("A postal code uses letters and numbers only.");
  if (trimmed.replace(/\s/g, "").length > POSTAL_CODE_MAX)
    return fail(`A postal code is at most ${POSTAL_CODE_MAX} characters.`);
  return OK;
}

/** A country name: letters only. */
export function checkCountry(value: string): FieldCheck {
  const trimmed = value.trim();
  if (!trimmed) return OK;
  if (!/^[A-Za-z\s'-]+$/.test(trimmed)) return fail("A country name uses letters only.");
  if (trimmed.length < 2) return fail("That country name is too short.");
  return OK;
}

/**
 * A phone number in a field that accepts international formats — spaces, dashes, brackets and a
 * leading + are all fine, because that is how people write numbers down. What is checked is the
 * count of digits.
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

/**
 * What the API actually enforces, mirrored here so the browser and the server agree.
 *
 * ASP.NET Identity is configured with RequiredLength = 8 and RequireNonAlphanumeric = false,
 * leaving its defaults for the rest: a digit, a lower-case letter and an upper-case letter.
 * The browser used to check only the length, so a password like "password" passed here and was
 * then rejected by the server with a message nobody had been warned about.
 *
 * A special character is allowed but NOT required — matching RequireNonAlphanumeric = false.
 */
export const PASSWORD_MIN_LENGTH = 8;

export interface PasswordRule {
  label: string;
  met: boolean;
}

/** The individual rules, so the form can show which are satisfied as someone types. */
export function passwordRules(value: string): PasswordRule[] {
  return [
    { label: `At least ${PASSWORD_MIN_LENGTH} characters`, met: value.length >= PASSWORD_MIN_LENGTH },
    { label: "A lower-case letter", met: /[a-z]/.test(value) },
    { label: "An upper-case letter", met: /[A-Z]/.test(value) },
    { label: "A number", met: /[0-9]/.test(value) },
  ];
}

export function checkPassword(value: string): FieldCheck {
  if (!value) return EMPTY;

  const unmet = passwordRules(value).filter((rule) => !rule.met);
  if (unmet.length === 0) return OK;

  // One message naming what is still missing, rather than a rule at a time.
  return fail(`Still needed: ${unmet.map((rule) => rule.label.toLowerCase()).join(", ")}.`);
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
