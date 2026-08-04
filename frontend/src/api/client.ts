const PAYMENTS_URL = import.meta.env.VITE_PAYMENTS_URL ?? 'http://localhost:8081'
const ACCOUNTS_URL = import.meta.env.VITE_ACCOUNTS_URL ?? 'http://localhost:8082'

export class ApiError extends Error {
  constructor(public status: number, public detail?: string) {
    super(detail ?? `HTTP ${status}`)
  }
}

export function getToken() { return localStorage.getItem('bank_token') }
export function setToken(t: string) { localStorage.setItem('bank_token', t) }
export function clearToken() { localStorage.removeItem('bank_token') }

async function request<T>(baseUrl: string, path: string, opts: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json', ...(opts.headers as Record<string, string>) }
  const token = getToken()
  if (token) headers['Authorization'] = `Bearer ${token}`

  const res = await fetch(baseUrl + path, { ...opts, headers })
  if (!res.ok) {
    let detail: string | undefined
    try { detail = JSON.stringify(await res.json()) } catch { /* ignore */ }
    throw new ApiError(res.status, detail)
  }
  const text = await res.text()
  return (text ? JSON.parse(text) : null) as T
}

export const paymentsApi = <T>(path: string, opts?: RequestInit) => request<T>(PAYMENTS_URL, path, opts)
export const accountsApi = <T>(path: string, opts?: RequestInit) => request<T>(ACCOUNTS_URL, path, opts)
