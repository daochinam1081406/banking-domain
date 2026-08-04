import { createContext, useContext, useState, type ReactNode } from 'react'
import { clearToken, getToken, setToken } from '../api/client'
import { login as apiLogin } from '../api/banking'

type AuthState = {
  user: string | null
  isAuthed: boolean
  signIn: (subject: string, role?: string) => Promise<void>
  signOut: () => void
}

const AuthCtx = createContext<AuthState>(null!)
export const useAuth = () => useContext(AuthCtx)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<string | null>(
    getToken() ? localStorage.getItem('bank_user') : null,
  )

  async function signIn(subject: string, role = 'customer') {
    const { token } = await apiLogin(subject, role)
    setToken(token)
    localStorage.setItem('bank_user', subject)
    setUser(subject)
  }

  function signOut() {
    clearToken()
    localStorage.removeItem('bank_user')
    setUser(null)
  }

  return (
    <AuthCtx.Provider value={{ user, isAuthed: !!user, signIn, signOut }}>
      {children}
    </AuthCtx.Provider>
  )
}
