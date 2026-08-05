import { accountsApi, paymentsApi } from './client'

export type Account = {
  number: string; ownerId: string; balance: number; currency: string; updatedAt: string
}

export type LedgerEntry = {
  id: string; accountNumber: string; transferId: string; direction: 'Debit' | 'Credit'
  amount: number; currency: string; balanceAfter: number; createdAt: string
}
export type TransferResult = { transferId: string; status: string }
export type Transfer = {
  id: string; fromAccount: string; toAccount: string; amount: number
  currency: string; status: string; createdAt: string
}

// Auth — xác thực username/password thật (PBKDF2), trả access + refresh token
export type TokenPair = { accessToken: string; refreshToken: string; expiresInSeconds: number }

export function login(username: string, password: string) {
  return paymentsApi<TokenPair>('/auth/login', {
    method: 'POST',
    body: JSON.stringify({ username, password }),
  })
}

export const me = () => paymentsApi<{ username: string; role: string }>('/auth/me')

// Accounts
export const listAccounts = () => accountsApi<Account[]>('/api/accounts')

export const getStatement = (number: string) =>
  accountsApi<LedgerEntry[]>(`/api/accounts/${encodeURIComponent(number)}/statement`)

export const getAccount = (number: string) =>
  accountsApi<Account>(`/api/accounts/${encodeURIComponent(number)}`)

export const openAccount = (input: { number: string; initialBalance: number; currency?: string }) =>
  accountsApi<{ number: string; balance: number; currency: string }>('/api/accounts', {
    method: 'POST',
    body: JSON.stringify(input),
  })

// Payments / Transfers
export const createTransfer = (input: { fromAccount: string; toAccount: string; amount: number; currency?: string }) =>
  paymentsApi<TransferResult>('/api/transfers', {
    method: 'POST',
    body: JSON.stringify(input),
  })

export const getTransfer = (id: string) =>
  paymentsApi<Transfer>(`/api/transfers/${id}`)
