import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import './styles/theme.css'
import App from './App.tsx'
import { AuthProvider } from './auth/AuthContext'
import { applyStoredOverride } from './theme/applyTheme'

// Applied synchronously, before the first paint: a signed-out view (the login page) has no Settings
// to fetch yet, but a returning user's own light/dark choice (header toggle, localStorage) should
// still apply immediately rather than flashing light and waiting for Layout to mount post-login.
applyStoredOverride()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
)
