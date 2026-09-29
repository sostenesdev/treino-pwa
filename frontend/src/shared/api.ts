let csrf = '';
export async function token() { const response = await fetch('/api/auth/csrf', { cache: 'no-store', credentials: 'same-origin' }); if (!response.ok) throw Error('Não foi possível iniciar a sessão.'); csrf = (await response.json()).token; }
export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const method = options.method || 'GET';
  if (method !== 'GET' && !csrf) await token();
  const response = await fetch('/api' + path, { ...options, credentials: 'same-origin', cache: 'no-store', headers: { ...(options.body ? { 'Content-Type': 'application/json' } : {}), ...(method !== 'GET' ? { 'X-CSRF-TOKEN': csrf } : {}), ...options.headers } });
  if (!response.ok) { const data = await response.json().catch(() => ({})); throw Object.assign(new Error(data.detail || data.code || `Erro ${response.status}`), { status: response.status, data }); }
  if (response.status === 204) return undefined as T;
  return response.json();
}
