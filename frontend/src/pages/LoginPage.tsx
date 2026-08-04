import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../api/client'

export function LoginPage() {
  const { signIn } = useAuth()
  const nav = useNavigate()
  const [name, setName] = useState('demo')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true); setError(null)
    try {
      await signIn(name.trim() || 'demo')
      nav('/', { replace: true })
    } catch (err) {
      setError(err instanceof ApiError ? `Không lấy được token (${err.status}). API Payments đã chạy chưa?` : 'Đăng nhập thất bại.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-wrap">
      <form className="card login-card" onSubmit={onSubmit}>
        <h1>Banking Console</h1>
        <p>Đăng nhập lấy JWT (Payments <span className="mono">/token</span>). Demo — nhập tên bất kỳ.</p>
        {error && <div className="alert alert-error">{error}</div>}
        <label>Tên người dùng (JWT subject)</label>
        <input value={name} onChange={(e) => setName(e.target.value)} placeholder="demo" />
        <button className="btn btn-primary" style={{ marginTop: '1.1rem', width: '100%', justifyContent: 'center' }} disabled={busy}>
          {busy ? 'Đang đăng nhập…' : 'Đăng nhập'}
        </button>
      </form>
    </div>
  )
}
