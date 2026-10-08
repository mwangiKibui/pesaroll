import { StrictMode, lazy, Suspense } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import './i18n'
import App from './App.tsx'
import { ToastProvider } from './components/feedback'
import { applyTheme, readPreference } from './theme/theme'

applyTheme(readPreference())

// The components page exists only in development; production builds leave it out.
const ComponentsPage = import.meta.env.DEV ? lazy(() => import('./dev/ComponentsPage')) : null
const showComponents = ComponentsPage && window.location.pathname.startsWith('/dev/components')

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ToastProvider>
      {showComponents ? <Suspense><ComponentsPage /></Suspense> : <App />}
    </ToastProvider>
  </StrictMode>,
)
