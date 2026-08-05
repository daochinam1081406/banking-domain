import { insuranceApi } from './client'

export type Policy = {
  id: string; policyNumber: string; policyHolderId: string; productCode: string
  coverageAmount: number; claimedAmount: number; remainingCoverage: number
  premiumAmount: number; currency: string; payoutAccount: string
  status: 'Draft' | 'Active' | 'Lapsed' | 'Cancelled'
  effectiveFrom: string; effectiveTo: string
}

export type Claim = {
  id: string; claimNumber: string; policyNumber: string; claimantId: string
  requestedAmount: number; approvedAmount: number | null; currency: string
  incidentDate: string; description: string
  status: 'Submitted' | 'UnderReview' | 'Approved' | 'Rejected' | 'Paid'
  reviewerId: string | null; decisionReason: string | null
  payoutTransferId: string | null; createdAt: string
}

// Hợp đồng
export const listPolicies = () => insuranceApi<Policy[]>('/api/policies')

export const issuePolicy = (input: {
  productCode: string; coverageAmount: number; premiumAmount: number
  payoutAccount: string; effectiveFrom: string; effectiveTo: string; currency?: string
}) => insuranceApi<{ policyId: string; policyNumber: string; status: string }>('/api/policies', {
  method: 'POST', body: JSON.stringify(input),
})

export const activatePolicy = (policyNumber: string) =>
  insuranceApi<void>(`/api/policies/${encodeURIComponent(policyNumber)}/activate`, { method: 'POST' })

// Bồi thường
export const listClaims = () => insuranceApi<Claim[]>('/api/claims')

export const submitClaim = (input: {
  policyNumber: string; requestedAmount: number; incidentDate: string; description: string
}) => insuranceApi<{ claimId: string; claimNumber: string; status: string }>('/api/claims', {
  method: 'POST', body: JSON.stringify(input),
})

export const approveClaim = (claimId: string, approvedAmount: number) =>
  insuranceApi<void>(`/api/claims/${claimId}/approve`, {
    method: 'POST', body: JSON.stringify({ approvedAmount }),
  })

export const rejectClaim = (claimId: string, reason: string) =>
  insuranceApi<void>(`/api/claims/${claimId}/reject`, {
    method: 'POST', body: JSON.stringify({ reason }),
  })
