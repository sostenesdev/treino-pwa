let csrf = "";
export class ApiError extends Error {
  constructor(
    public status: number,
    public data: Record<string, unknown>,
    public retryAfterMs = 0,
  ) {
    super(String(data.detail || data.code || data.title || `Erro ${status}`));
  }
}
export async function token() {
  const response = await fetch("/api/auth/csrf", {
    cache: "no-store",
    credentials: "same-origin",
    signal: AbortSignal.timeout(15000),
  });
  if (!response.ok) throw new ApiError(response.status, {});
  csrf = (await response.json()).token;
}
export async function api<T>(
  path: string,
  options: RequestInit = {},
  renewed = false,
): Promise<T> {
  const method = options.method || "GET";
  if (method !== "GET" && !csrf) await token();
  const response = await fetch("/api" + path, {
    ...options,
    signal: options.signal ?? AbortSignal.timeout(20000),
    credentials: "same-origin",
    cache: "no-store",
    headers: {
      ...(options.body ? { "Content-Type": "application/json" } : {}),
      ...(method !== "GET" ? { "X-CSRF-TOKEN": csrf } : {}),
      ...options.headers,
    },
  });
  if (!response.ok) {
    const raw = await response.json().catch(() => ({}));
    const data = typeof raw === "string" ? { detail: raw } : raw;
    if (response.status === 403 && data.code === "CSRF_INVALID" && !renewed) {
      await token();
      return api<T>(path, options, true);
    }
    const retry = response.headers.get("Retry-After");
    const retryAfterMs = retry
      ? Number.isFinite(Number(retry))
        ? Number(retry) * 1000
        : Math.max(0, Date.parse(retry) - Date.now())
      : 0;
    throw new ApiError(response.status, data, retryAfterMs);
  }
  if (response.status === 204) return undefined as T;
  return response.json();
}
