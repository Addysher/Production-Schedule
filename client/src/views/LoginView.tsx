import { useState, type FormEvent, type KeyboardEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { EyeIcon } from '../components/EyeIcon'

export function LoginView() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  async function doLogin() {
    setLoading(true)
    setError(null)
    try {
      await login(username, password)
      navigate('/dashboard')
    } catch {
      setError('Invalid username or password.')
    } finally {
      setLoading(false)
    }
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    doLogin()
  }

  // Belt-and-braces alongside the form's own onSubmit: a browser's native autofill/credential-suggestion
  // dropdown can swallow the first Enter press meant for the form, so pressing Enter in either field
  // triggers sign-in directly rather than relying solely on implicit form submission.
  function handleKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Enter') {
      e.preventDefault()
      doLogin()
    }
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--page-bg)',
      }}
    >
      <form onSubmit={handleSubmit} className="card" style={{ width: 340 }}>
        <div style={{ fontSize: 20, fontWeight: 700, color: 'var(--brand-primary)', marginBottom: 4 }}>
          Production Scheduler
        </div>
        <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 20 }}>Sign in to continue</div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <label>
            <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Username</div>
            <input
              style={{ width: '100%' }}
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              onKeyDown={handleKeyDown}
              autoFocus
            />
          </label>
          <label>
            <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Password</div>
            <div style={{ position: 'relative' }}>
              <input
                type={showPassword ? 'text' : 'password'}
                style={{ width: '100%', paddingRight: 32 }}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                onKeyDown={handleKeyDown}
              />
              <button
                type="button"
                aria-label="Hold to show password"
                title="Hold to show password"
                onPointerDown={() => setShowPassword(true)}
                onPointerUp={() => setShowPassword(false)}
                onPointerLeave={() => setShowPassword(false)}
                onPointerCancel={() => setShowPassword(false)}
                style={{
                  position: 'absolute',
                  right: 4,
                  top: '50%',
                  transform: 'translateY(-50%)',
                  display: 'flex',
                  background: 'none',
                  border: 'none',
                  padding: 4,
                  lineHeight: 1,
                  cursor: 'pointer',
                  color: 'var(--text-muted)',
                  userSelect: 'none',
                }}
              >
                <EyeIcon crossed={showPassword} />
              </button>
            </div>
          </label>
        </div>

        {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}

        <button className="btn btn-primary" type="submit" disabled={loading} style={{ width: '100%', marginTop: 20 }}>
          {loading ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </div>
  )
}
