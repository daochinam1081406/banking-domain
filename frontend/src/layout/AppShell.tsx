import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export function AppShell() {
  const { user, signOut } = useAuth()
  return (
    <div className="app-shell">
      <nav className="sidebar">
        <div className="brand">
          <span className="brand-badge">₿</span>
          <span>Banking</span>
        </div>
        <div className="nav">
          <NavLink to="/" end>Dashboard</NavLink>
          <NavLink to="/transfers">Chuyển tiền</NavLink>
          <NavLink to="/insurance">Bảo hiểm</NavLink>
        </div>
        <div className="sidebar-foot">
          .NET microservices · Azure Service Bus<br />Event-driven demo
        </div>
      </nav>
      <main className="main">
        <div className="topbar">
          <div>
            <h1>Banking Console</h1>
            <div className="sub">Insurance · Accounts (SQL Server) · Payments (PostgreSQL) · Outbox → Service Bus</div>
          </div>
          <div className="user-chip">
            <span>👤 {user}</span>
            <button className="btn btn-ghost" onClick={signOut}>Đăng xuất</button>
          </div>
        </div>
        <Outlet />
      </main>
    </div>
  )
}
