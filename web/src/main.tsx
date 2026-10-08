import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import './i18n'
import App from './App.tsx'
import { createServices } from './app/services'
import { applyTheme, readPreference } from './theme/theme'

applyTheme(readPreference())

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App services={createServices()} />
  </StrictMode>,
)
