import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import './i18n'
import App from './App.tsx'
import { createServices } from './app/services'
import { applyStartupTheme } from './theme/theme'

applyStartupTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App services={createServices()} />
  </StrictMode>,
)
