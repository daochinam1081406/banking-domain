import { useEffect, useState, type FormEvent } from 'react'
import {
  activatePolicy, approveClaim, issuePolicy, listClaims, listPolicies, rejectClaim, submitClaim,
  type Claim, type Policy,
} from '../api/insurance'
import { ApiError } from '../api/client'

const money = (n: number, ccy: string) => `${n.toLocaleString('vi-VN')} ${ccy}`
const today = () => new Date().toISOString().slice(0, 10)

const CLAIM_TAG: Record<Claim['status'], string> = {
  Submitted: 'tag-info', UnderReview: 'tag-warn', Approved: 'tag-warn',
  Rejected: 'tag-debit', Paid: 'tag-credit',
}
const CLAIM_LABEL: Record<Claim['status'], string> = {
  Submitted: 'Đã gửi', UnderReview: 'Đang giám định', Approved: 'Đã duyệt — chờ chi trả',
  Rejected: 'Từ chối', Paid: 'Đã chi trả',
}

export function InsurancePage() {
  const [policies, setPolicies] = useState<Policy[]>([])
  const [claims, setClaims] = useState<Claim[]>([])
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  // Form phát hành hợp đồng
  const [productCode, setProductCode] = useState('HEALTH')
  const [coverage, setCoverage] = useState('100000000')
  const [premium, setPremium] = useState('2000000')
  const [payoutAccount, setPayoutAccount] = useState('ACC-001')

  // Form yêu cầu bồi thường
  const [claimPolicy, setClaimPolicy] = useState('')
  const [claimAmount, setClaimAmount] = useState('5000000')
  const [incidentDate, setIncidentDate] = useState(today())
  const [description, setDescription] = useState('Nằm viện điều trị 3 ngày')

  function fail(err: unknown, fallback: string) {
    setError(err instanceof ApiError && err.detail ? err.detail : fallback)
  }

  async function load() {
    setLoading(true); setError(null)
    try {
      const [p, c] = await Promise.all([listPolicies(), listClaims()])
      setPolicies(p); setClaims(c)
      if (p.length > 0 && !claimPolicy) setClaimPolicy(p[0].policyNumber)
    } catch (err) {
      fail(err, 'Không tải được dữ liệu bảo hiểm.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  async function act(fn: () => Promise<unknown>, okMsg: string, failMsg: string) {
    setError(null); setInfo(null)
    try {
      await fn()
      setInfo(okMsg)
      await load()
    } catch (err) { fail(err, failMsg) }
  }

  async function onIssue(e: FormEvent) {
    e.preventDefault()
    const from = today()
    const to = new Date(Date.now() + 365 * 864e5).toISOString().slice(0, 10)
    await act(
      () => issuePolicy({
        productCode, coverageAmount: Number(coverage), premiumAmount: Number(premium),
        payoutAccount, effectiveFrom: from, effectiveTo: to,
      }),
      'Đã phát hành hợp đồng (Draft) — bấm "Đóng phí" để có hiệu lực.',
      'Phát hành hợp đồng thất bại.')
  }

  async function onSubmitClaim(e: FormEvent) {
    e.preventDefault()
    await act(
      () => submitClaim({
        policyNumber: claimPolicy, requestedAmount: Number(claimAmount),
        incidentDate, description,
      }),
      'Đã gửi yêu cầu bồi thường.',
      'Gửi yêu cầu bồi thường thất bại.')
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div className="section-title" style={{ margin: 0 }}>Hợp đồng bảo hiểm</div>
        <button className="btn btn-ghost" onClick={() => void load()} disabled={loading}>
          {loading ? 'Đang tải…' : 'Làm mới'}
        </button>
      </div>
      {error && <div className="alert alert-error">{error}</div>}
      {info && <div className="alert alert-ok">{info}</div>}

      <div className="grid grid-3" style={{ marginTop: '0.9rem' }}>
        {policies.map((p) => (
          <div key={p.id} className="card acct-card" style={{ cursor: 'default' }}>
            <div className="acct-num">{p.policyNumber} · {p.productCode}</div>
            <div className="acct-balance" style={{ fontSize: '1.35rem' }}>
              {money(p.remainingCoverage, p.currency)}
            </div>
            <div className="acct-ccy">
              còn lại / {money(p.coverageAmount, p.currency)} · phí {money(p.premiumAmount, p.currency)}
            </div>
            <div style={{ marginTop: '0.6rem', display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
              <span className={p.status === 'Active' ? 'tag-credit' : 'tag-info'}>{p.status}</span>
              {p.status === 'Draft' && (
                <button
                  className="btn btn-primary" style={{ padding: '4px 10px', fontSize: '0.78rem' }}
                  onClick={() => void act(() => activatePolicy(p.policyNumber),
                    'Đã đóng phí — hợp đồng có hiệu lực.', 'Kích hoạt thất bại.')}
                >Đóng phí</button>
              )}
            </div>
            <div className="muted" style={{ fontSize: '0.72rem', marginTop: '0.5rem' }}>
              Hiệu lực {p.effectiveFrom} → {p.effectiveTo} · chi trả về {p.payoutAccount}
            </div>
          </div>
        ))}
        {policies.length === 0 && !loading && (
          <div className="muted">Chưa có hợp đồng. Phát hành hợp đồng mới bên dưới.</div>
        )}
      </div>

      <div className="section-title">Phát hành hợp đồng</div>
      <form className="card" onSubmit={onIssue}>
        <div className="form-row">
          <div>
            <label>Sản phẩm</label>
            <select value={productCode} onChange={(e) => setProductCode(e.target.value)}>
              <option value="HEALTH">Sức khoẻ</option>
              <option value="MOTOR">Xe cơ giới</option>
              <option value="LIFE">Nhân thọ</option>
            </select>
          </div>
          <div><label>Số tiền bảo hiểm</label><input type="number" min="1" value={coverage} onChange={(e) => setCoverage(e.target.value)} required /></div>
          <div><label>Phí bảo hiểm</label><input type="number" min="0" value={premium} onChange={(e) => setPremium(e.target.value)} required /></div>
          <div><label>TK nhận bồi thường</label><input value={payoutAccount} onChange={(e) => setPayoutAccount(e.target.value)} required /></div>
        </div>
        <button className="btn btn-primary">Phát hành</button>
      </form>

      <div className="section-title">Yêu cầu bồi thường</div>
      <form className="card" onSubmit={onSubmitClaim}>
        <div className="form-row">
          <div>
            <label>Hợp đồng</label>
            <select value={claimPolicy} onChange={(e) => setClaimPolicy(e.target.value)}>
              {policies.map((p) => <option key={p.id} value={p.policyNumber}>{p.policyNumber} ({p.status})</option>)}
            </select>
          </div>
          <div><label>Số tiền yêu cầu</label><input type="number" min="1" value={claimAmount} onChange={(e) => setClaimAmount(e.target.value)} required /></div>
          <div><label>Ngày xảy ra</label><input type="date" value={incidentDate} onChange={(e) => setIncidentDate(e.target.value)} required /></div>
          <div><label>Mô tả sự cố</label><input value={description} onChange={(e) => setDescription(e.target.value)} required /></div>
        </div>
        <button className="btn btn-primary" disabled={policies.length === 0}>Gửi yêu cầu</button>
      </form>

      <div className="section-title">
        Hồ sơ bồi thường <span className="muted">— duyệt sẽ tự chi trả về tài khoản ngân hàng (saga)</span>
      </div>
      <div className="card">
        {claims.length === 0 ? (
          <div className="muted">Chưa có hồ sơ bồi thường.</div>
        ) : (
          <table className="ledger">
            <thead>
              <tr>
                <th>Hồ sơ</th><th>Hợp đồng</th><th style={{ textAlign: 'right' }}>Yêu cầu</th>
                <th style={{ textAlign: 'right' }}>Duyệt</th><th>Trạng thái</th><th>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {claims.map((c) => (
                <tr key={c.id}>
                  <td>
                    <strong>{c.claimNumber}</strong>
                    <div className="muted" style={{ fontSize: '0.75rem' }}>{c.description}</div>
                  </td>
                  <td className="mono">{c.policyNumber}</td>
                  <td style={{ textAlign: 'right' }}>{money(c.requestedAmount, c.currency)}</td>
                  <td style={{ textAlign: 'right' }}>
                    {c.approvedAmount != null ? money(c.approvedAmount, c.currency) : '—'}
                  </td>
                  <td>
                    <span className={CLAIM_TAG[c.status]}>{CLAIM_LABEL[c.status]}</span>
                    {c.payoutTransferId && (
                      <div className="muted mono" style={{ fontSize: '0.7rem' }}>
                        tx {c.payoutTransferId.slice(0, 8)}…
                      </div>
                    )}
                    {c.decisionReason && (
                      <div className="muted" style={{ fontSize: '0.72rem' }}>{c.decisionReason}</div>
                    )}
                  </td>
                  <td>
                    {(c.status === 'Submitted' || c.status === 'UnderReview') && (
                      <div style={{ display: 'flex', gap: '0.4rem' }}>
                        <button
                          className="btn btn-primary" style={{ padding: '4px 10px', fontSize: '0.76rem' }}
                          onClick={() => void act(() => approveClaim(c.id, c.requestedAmount),
                            'Đã duyệt — đang chi trả về tài khoản ngân hàng…', 'Duyệt thất bại.')}
                        >Duyệt</button>
                        <button
                          className="btn btn-ghost" style={{ padding: '4px 10px', fontSize: '0.76rem' }}
                          onClick={() => void act(() => rejectClaim(c.id, 'Không thuộc phạm vi bảo hiểm'),
                            'Đã từ chối hồ sơ.', 'Từ chối thất bại.')}
                        >Từ chối</button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
