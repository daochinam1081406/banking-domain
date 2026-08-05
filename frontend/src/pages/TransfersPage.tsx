import { useState, type FormEvent } from 'react'
import { createTransfer, getAccount } from '../api/banking'
import { ApiError } from '../api/client'

export function TransfersPage() {
  const [from, setFrom] = useState('ACC-001')
  const [to, setTo] = useState('ACC-002')
  const [amount, setAmount] = useState('500000')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)
  const [balances, setBalances] = useState<{ from?: string; to?: string }>({})

  async function refreshBalances() {
    try {
      const [f, t] = await Promise.all([getAccount(from), getAccount(to)])
      setBalances({
        from: f ? `${f.balance.toLocaleString('vi-VN')} ${f.currency}` : '—',
        to: t ? `${t.balance.toLocaleString('vi-VN')} ${t.currency}` : '—',
      })
    } catch { /* ignore */ }
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true); setError(null); setOk(null)
    try {
      const r = await createTransfer({ fromAccount: from, toAccount: to, amount: Number(amount) })
      setOk(`Đã tiếp nhận lệnh chuyển tiền ${r.transferId.slice(0, 8).toUpperCase()}. Số dư sẽ cập nhật trong giây lát.`)
      // Ghi sổ chạy bất đồng bộ ở service khác — chờ rồi mới đọc lại số dư.
      setTimeout(() => void refreshBalances(), 4000)
    } catch (err) {
      if (err instanceof ApiError && err.detail) {
        setError(`Thất bại: ${err.detail}`)
      } else {
        setError('Chuyển tiền thất bại. Vui lòng thử lại.')
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <div className="section-title" style={{ marginTop: 0 }}>Chuyển tiền</div>
      <p className="muted" style={{ marginBottom: '1rem' }}>
        Lệnh được ghi nhận ngay; số dư cập nhật sau khi hệ thống ghi sổ xong.
      </p>

      <form className="card" onSubmit={onSubmit}>
        <div className="form-row">
          <div><label>Từ tài khoản</label><input value={from} onChange={(e) => setFrom(e.target.value)} required /></div>
          <div><label>Đến tài khoản</label><input value={to} onChange={(e) => setTo(e.target.value)} required /></div>
          <div><label>Số tiền</label><input type="number" min="1" value={amount} onChange={(e) => setAmount(e.target.value)} required /></div>
        </div>
        <div style={{ display: 'flex', gap: '0.6rem' }}>
          <button className="btn btn-primary" disabled={busy}>{busy ? 'Đang gửi…' : 'Chuyển tiền'}</button>
          <button type="button" className="btn btn-ghost" onClick={() => void refreshBalances()}>Xem số dư 2 TK</button>
        </div>
        {error && <div className="alert alert-error">{error}</div>}
        {ok && <div className="alert alert-ok">{ok}</div>}
        {(balances.from || balances.to) && (
          <div className="muted" style={{ marginTop: '0.8rem' }}>
            {from}: <b>{balances.from}</b> · {to}: <b>{balances.to}</b>
          </div>
        )}
      </form>
    </div>
  )
}
