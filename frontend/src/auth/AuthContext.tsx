import { createContext, useContext, useState, type ReactNode } from 'react'
import { clearToken, getToken, setToken } from '../api/client'
import { login as apiLogin, me } from '../api/banking'

type AuthState = {
  user: string | null
  role: string | null
  isAdjuster: boolean
  isAuthed: boolean
  signIn: (username: string, password: string) => Promise<void>
  signOut: () => void
}

const AuthCtx = createContext<AuthState>(null!)
export const useAuth = () => useContext(AuthCtx)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<string | null>(
    getToken() ? localStorage.getItem('bank_user') : null,
  )
  const [role, setRole] = useState<string | null>(
    getToken() ? localStorage.getItem('bank_role') : null,
  )

  async function signIn(username: string, password: string) {
    const pair = await apiLogin(username, password)
    setToken(pair.accessToken)
    localStorage.setItem('bank_refresh', pair.refreshToken)

    const profile = await me()
    localStorage.setItem('bank_user', profile.username)
    localStorage.setItem('bank_role', profile.role)
    setUser(profile.username)
    setRole(profile.role)
  }

  function signOut() {
    clearToken()
    localStorage.removeItem('bank_user')
    localStorage.removeItem('bank_role')
    localStorage.removeItem('bank_refresh')
    setUser(null); setRole(null)
  }

  return (
    <AuthCtx.Provider value={{
      user, role, isAdjuster: role === 'adjuster', isAuthed: !!user, signIn, signOut,
    }}>
      {children}
    </AuthCtx.Provider>
  )
}
