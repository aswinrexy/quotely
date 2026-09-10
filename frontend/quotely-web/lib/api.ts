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

  if (response.status === 401) {
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

  /** Downloads a generated PDF, honouring the filename the API sends back. */
  async downloadPdf(quotationId: string): Promise<{ blob: Blob; fileName: string }> {
    const token = tokenStore.get();
    const response = await fetch(`${API_BASE_URL}/api/quotations/${quotationId}/pdf`, {
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
      fileName: match ? decodeURIComponent(match[1]) : "quotation.pdf",
    };
  },
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
