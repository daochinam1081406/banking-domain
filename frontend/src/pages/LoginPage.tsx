import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../api/client'

export function LoginPage() {
  const { signIn } = useAuth()
  const nav = useNavigate()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true); setError(null)
    try {
      await signIn(username.trim(), password)
      nav('/', { replace: true })
    } catch (err) {
      setError(err instanceof ApiError && err.status === 400
        ? 'Tên đăng nhập hoặc mật khẩu không đúng.'
        : 'Không kết nối được máy chủ. Vui lòng thử lại.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-wrap">
      <form className="card login-card" onSubmit={onSubmit}>
        <h1>Banking Console</h1>
        <p>Đăng nhập để tiếp tục.</p>
        {error && <div className="alert alert-error">{error}</div>}
        <label>Tên đăng nhập</label>
        <input value={username} onChange={(e) => setUsername(e.target.value)}
          autoComplete="username" autoFocus required />
        <div style={{ marginTop: '0.9rem' }}>
          <label>Mật khẩu</label>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password" required />
        </div>
        <button className="btn btn-primary" style={{ marginTop: '1.1rem', width: '100%', justifyContent: 'center' }} disabled={busy}>
          {busy ? 'Đang đăng nhập…' : 'Đăng nhập'}
        </button>
      </form>
    </div>
  )
}
