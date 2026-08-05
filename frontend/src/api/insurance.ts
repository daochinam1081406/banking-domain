import { insuranceApi } from './client'

export type Policy = {
  id: string; policyNumber: string; policyHolderId: string; productCode: string
  coverageAmount: number; claimedAmount: number; remainingCoverage: number
  premiumAmount: number; deductible: number; coPaymentRate: number; waitingPeriodDays: number
  currency: string; payoutAccount: string
  status: 'Draft' | 'Active' | 'Lapsed' | 'Cancelled'
  effectiveFrom: string; effectiveTo: string
}

export type Claim = {
  id: string; claimNumber: string; policyNumber: string; claimantId: string
  requestedAmount: number; assessedCost: number | null; approvedAmount: number | null; currency: string
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

// Bancassurance: trích nợ phí từ tài khoản ngân hàng (hợp đồng Active khi thu đủ — saga)
export const payPremium = (policyNumber: string, debitAccount: string) =>
  insuranceApi<{ policyNumber: string; status: string }>(
    `/api/policies/${encodeURIComponent(policyNumber)}/pay-premium`,
    { method: 'POST', body: JSON.stringify({ debitAccount }) })

// Bồi thường
export const listClaims = () => insuranceApi<Claim[]>('/api/claims')

export const submitClaim = (input: {
  policyNumber: string; requestedAmount: number; incidentDate: string; description: string
}) => insuranceApi<{ claimId: string; claimNumber: string; status: string }>('/api/claims', {
  method: 'POST', body: JSON.stringify(input),
})

// assessedCost = chi phí giám định công nhận; BH thực trả = sau miễn thường + đồng chi trả
export const approveClaim = (claimId: string, assessedCost: number) =>
  insuranceApi<void>(`/api/claims/${claimId}/approve`, {
    method: 'POST', body: JSON.stringify({ assessedCost }),
  })

export const rejectClaim = (claimId: string, reason: string) =>
  insuranceApi<void>(`/api/claims/${claimId}/reject`, {
    method: 'POST', body: JSON.stringify({ reason }),
  })
