import { useEffect, useState, type FormEvent } from 'react'
import { listAccounts, openAccount, type Account } from '../api/banking'
import { ApiError } from '../api/client'

export function DashboardPage() {
  const [accounts, setAccounts] = useState<Account[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const [number, setNumber] = useState('ACC-100')
  const [initial, setInitial] = useState('1000000')

  async function load() {
    setLoading(true); setError(null)
    try {
      setAccounts(await listAccounts())
    } catch (err) {
      setError(err instanceof ApiError ? `Lỗi tải tài khoản (${err.status})` : 'Không tải được tài khoản.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  async function onOpen(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try {
      await openAccount({ number, initialBalance: Number(initial) })
      await load()
    } catch (err) {
      setError(err instanceof ApiError ? `Mở tài khoản thất bại (${err.status})` : 'Mở tài khoản thất bại.')
    }
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div className="section-title" style={{ margin: 0 }}>Tài khoản</div>
        <button className="btn btn-ghost" onClick={() => void load()} disabled={loading}>
          {loading ? 'Đang tải…' : 'Làm mới'}
        </button>
      </div>
      {error && <div className="alert alert-error">{error}</div>}

      <div className="grid grid-3" style={{ marginTop: '0.9rem' }}>
        {accounts.map((a) => (
          <div key={a.number} className="card acct-card">
            <div className="acct-num">{a.number}</div>
            <div className="acct-balance">{a.balance.toLocaleString('vi-VN')}</div>
            <div className="acct-ccy">{a.currency} · cập nhật {new Date(a.updatedAt).toLocaleTimeString('vi-VN')}</div>
          </div>
        ))}
        {accounts.length === 0 && !loading && (
          <div className="muted">Chưa tải được tài khoản. Kiểm tra API Accounts (8082) đã chạy chưa.</div>
        )}
      </div>

      <div className="section-title">Mở tài khoản mới</div>
      <form className="card" onSubmit={onOpen}>
        <div className="form-row">
          <div><label>Số tài khoản</label><input value={number} onChange={(e) => setNumber(e.target.value)} required /></div>
          <div><label>Số dư ban đầu</label><input type="number" min="0" value={initial} onChange={(e) => setInitial(e.target.value)} required /></div>
        </div>
        <button className="btn btn-primary">Mở tài khoản</button>
      </form>
    </div>
  )
}
