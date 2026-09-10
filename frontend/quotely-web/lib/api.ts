const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5199";
const TOKEN_KEY = "quotely.token";
const USER_KEY = "quotely.user";

export class ApiError extends Error {
  status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

export const tokenStore = {
  get: () => (typeof window === "undefined" ? null : window.localStorage.getItem(TOKEN_KEY)),
  set: (token: string) => window.localStorage.setItem(TOKEN_KEY, token),
  clear: () => {
    window.localStorage.removeItem(TOKEN_KEY);
    window.localStorage.removeItem(USER_KEY);
  },
  userKey: USER_KEY,
};

/** Broadcast so the auth provider can sign the user out when a token expires mid-session. */
function onUnauthorized() {
  if (typeof window === "undefined") return;
  tokenStore.clear();
  window.dispatchEvent(new CustomEvent("quotely:unauthorized"));
}

async function toError(response: Response): Promise<ApiError> {
  let message = "Something went wrong. Please try again.";
  try {
    const body = await response.json();
    if (body?.message) message = body.message;
  } catch {
    // Non-JSON error body; keep the friendly default.
  }
  return new ApiError(message, response.status);
}

/** A 401 from /api/auth/* is a bad credential, not a dropped session. */
function isAuthEndpoint(path: string) {
  return path.startsWith("/api/auth/");
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = tokenStore.get();
  const headers = new Headers(init.headers);
  if (!headers.has("Content-Type") && init.body) headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, { ...init, headers });
  } catch {
    throw new ApiError("Cannot reach the server. Is the API running?", 0);
  }

  if (response.status === 401 && !isAuthEndpoint(path)) {
    onUnauthorized();
    throw new ApiError("Your session has expired. Please sign in again.", 401);
  }
  if (!response.ok) throw await toError(response);
  if (response.status === 204) return undefined as T;

  return (await response.json()) as T;
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) }),
  put: <T>(path: string, body: unknown) =>
    request<T>(path, { method: "PUT", body: JSON.stringify(body) }),
  delete: <T>(path: string) => request<T>(path, { method: "DELETE" }),

  /** Downloads a generated quotation PDF, honouring the filename the API sends back. */
  downloadPdf: (quotationId: string) => downloadPdfFrom(`/api/quotations/${quotationId}/pdf`),

  /** Downloads a generated invoice PDF. */
  downloadInvoicePdf: (invoiceId: string) => downloadPdfFrom(`/api/invoices/${invoiceId}/pdf`),
};

async function downloadPdfFrom(path: string): Promise<{ blob: Blob; fileName: string }> {
  const token = tokenStore.get();
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: "POST",
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });

  if (response.status === 401) {
    onUnauthorized();
    throw new ApiError("Your session has expired. Please sign in again.", 401);
  }
  if (!response.ok) throw await toError(response);

  const disposition = response.headers.get("Content-Disposition") ?? "";
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  return {
    blob: await response.blob(),
    fileName: match ? decodeURIComponent(match[1]) : "document.pdf",
  };
}

/**
 * Customer-facing calls. The share token in the URL is the only credential, so these requests
 * deliberately carry no Authorization header — an owner browsing their own link must not have
 * their session implicitly involved — and a 401/404 here never signs anyone out.
 */
export const publicApi = {
  async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const headers = new Headers(init.headers);
    if (!headers.has("Content-Type") && init.body) headers.set("Content-Type", "application/json");

    let response: Response;
    try {
      response = await fetch(`${API_BASE_URL}${path}`, { ...init, headers });
    } catch {
      throw new ApiError("Cannot reach the server. Please check your connection and try again.", 0);
    }

    if (!response.ok) throw await toError(response);
    return (await response.json()) as T;
  },

  get: <T>(path: string) => publicApi.request<T>(path),
  post: <T>(path: string, body: unknown) =>
    publicApi.request<T>(path, { method: "POST", body: JSON.stringify(body) }),

  pdfUrl: (token: string) => `${API_BASE_URL}/api/public/quotations/${encodeURIComponent(token)}/pdf`,
};

export function saveBlob(blob: Blob, fileName: string) {
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
}
