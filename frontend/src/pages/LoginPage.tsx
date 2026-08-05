import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../api/client'

export function LoginPage() {
  const { signIn } = useAuth()
  const nav = useNavigate()
  const [username, setUsername] = useState('demo')
  const [password, setPassword] = useState('Demo@123')
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
        : 'Không kết nối được máy chủ. Kiểm tra API Payments (8081).')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-wrap">
      <form className="card login-card" onSubmit={onSubmit}>
        <h1>Banking Console</h1>
        <p>Đăng nhập bằng tài khoản hệ thống. Mật khẩu được hash PBKDF2-SHA256 (600k vòng).</p>
        {error && <div className="alert alert-error">{error}</div>}
        <label>Tên đăng nhập</label>
        <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" required />
        <div style={{ marginTop: '0.9rem' }}>
          <label>Mật khẩu</label>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password" required />
        </div>
        <div className="muted" style={{ marginTop: '0.9rem', fontSize: '0.76rem', lineHeight: 1.7 }}>
          Tài khoản thử nghiệm:<br />
          <span className="mono">demo / Demo@123</span> — khách hàng<br />
          <span className="mono">adjuster / Adjuster@123</span> — giám định viên (duyệt bồi thường)
        </div>
        <button className="btn btn-primary" style={{ marginTop: '1.1rem', width: '100%', justifyContent: 'center' }} disabled={busy}>
          {busy ? 'Đang đăng nhập…' : 'Đăng nhập'}
        </button>
      </form>
    </div>
  )
}
