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

// Auth (Payments /token phát JWT — dùng chung secret nên token dùng được cả 2 service)
export function login(subject: string, role = 'customer') {
  return paymentsApi<{ token: string }>('/token', {
    method: 'POST',
    body: JSON.stringify({ subject, role }),
  })
}

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
