import { useEffect, useState, type FormEvent } from 'react'
import { getStatement, listAccounts, openAccount, type Account, type LedgerEntry } from '../api/banking'
import { ApiError } from '../api/client'

export function DashboardPage() {
  const [accounts, setAccounts] = useState<Account[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const [number, setNumber] = useState('ACC-100')
  const [initial, setInitial] = useState('1000000')
  const [currency, setCurrency] = useState('VND')

  // Sao kê của tài khoản đang chọn
  const [selected, setSelected] = useState<string | null>(null)
  const [entries, setEntries] = useState<LedgerEntry[]>([])
  const [loadingStatement, setLoadingStatement] = useState(false)

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

  async function openStatement(accountNumber: string) {
    setSelected(accountNumber)
    setLoadingStatement(true); setError(null)
    try {
      setEntries(await getStatement(accountNumber))
    } catch (err) {
      setError(err instanceof ApiError ? `Không tải được sao kê (${err.status})` : 'Không tải được sao kê.')
      setEntries([])
    } finally {
      setLoadingStatement(false)
    }
  }

  useEffect(() => { void load() }, [])

  async function onOpen(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try {
      await openAccount({ number, initialBalance: Number(initial), currency })
      await load()
    } catch (err) {
      setError(err instanceof ApiError ? `Mở tài khoản thất bại (${err.status})` : 'Mở tài khoản thất bại.')
    }
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div className="section-title" style={{ margin: 0 }}>Tài khoản của tôi</div>
        <button className="btn btn-ghost" onClick={() => void load()} disabled={loading}>
          {loading ? 'Đang tải…' : 'Làm mới'}
        </button>
      </div>
      {error && <div className="alert alert-error">{error}</div>}

      <div className="grid grid-3" style={{ marginTop: '0.9rem' }}>
        {accounts.map((a) => (
          <div
            key={a.number}
            className={`card acct-card${selected === a.number ? ' acct-selected' : ''}`}
            onClick={() => void openStatement(a.number)}
            title="Bấm để xem sao kê"
          >
            <div className="acct-num">{a.number}</div>
            <div className="acct-balance">{a.balance.toLocaleString('vi-VN')}</div>
            <div className="acct-ccy">
              {a.currency} · cập nhật {new Date(a.updatedAt).toLocaleTimeString('vi-VN')}
            </div>
            <div className="acct-hint">Xem sao kê →</div>
          </div>
        ))}
        {accounts.length === 0 && !loading && (
          <div className="muted">Chưa có tài khoản nào của bạn. Mở tài khoản mới bên dưới.</div>
        )}
      </div>

      {selected && (
        <>
          <div className="section-title">
            Sao kê {selected} <span className="muted">— bút toán kép, bất biến</span>
          </div>
          <div className="card">
            {loadingStatement ? (
              <div className="muted">Đang tải sao kê…</div>
            ) : entries.length === 0 ? (
              <div className="muted">Chưa có giao dịch nào.</div>
            ) : (
              <table className="ledger">
                <thead>
                  <tr>
                    <th>Thời gian</th>
                    <th>Loại</th>
                    <th style={{ textAlign: 'right' }}>Số tiền</th>
                    <th style={{ textAlign: 'right' }}>Số dư sau</th>
                    <th>Mã giao dịch</th>
                  </tr>
                </thead>
                <tbody>
                  {entries.map((e) => (
                    <tr key={e.id}>
                      <td className="muted">{new Date(e.createdAt).toLocaleString('vi-VN')}</td>
                      <td>
                        <span className={e.direction === 'Debit' ? 'tag-debit' : 'tag-credit'}>
                          {e.direction === 'Debit' ? 'Ghi nợ' : 'Ghi có'}
                        </span>
                      </td>
                      <td style={{ textAlign: 'right' }} className={e.direction === 'Debit' ? 'amt-out' : 'amt-in'}>
                        {e.direction === 'Debit' ? '−' : '+'}{e.amount.toLocaleString('vi-VN')} {e.currency}
                      </td>
                      <td style={{ textAlign: 'right' }}>{e.balanceAfter.toLocaleString('vi-VN')}</td>
                      <td className="mono muted">{e.transferId.slice(0, 8)}…</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </>
      )}

      <div className="section-title">Mở tài khoản mới</div>
      <form className="card" onSubmit={onOpen}>
        <div className="form-row">
          <div><label>Số tài khoản</label><input value={number} onChange={(e) => setNumber(e.target.value)} required /></div>
          <div><label>Số dư ban đầu</label><input type="number" min="0" value={initial} onChange={(e) => setInitial(e.target.value)} required /></div>
          <div>
            <label>Loại tiền</label>
            <select value={currency} onChange={(e) => setCurrency(e.target.value)}>
              <option value="VND">VND</option>
              <option value="USD">USD</option>
            </select>
          </div>
        </div>
        <button className="btn btn-primary">Mở tài khoản</button>
      </form>
    </div>
  )
}
