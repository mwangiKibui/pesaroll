import { useTranslation } from 'react-i18next'
import { Card } from './components/TableCard'
import { LanguageSwitch, ThemeSwitch } from './components/switches'

/** Placeholder home until the sign-up and sign-in screens (#141). */
export default function App() {
  const { t } = useTranslation()
  return (
    <main className="mx-auto flex min-h-dvh max-w-md flex-col justify-center gap-6 px-4 py-12">
      <div className="flex flex-col gap-1">
        <h1 className="m-0 font-display text-title text-brand">Pesaroll</h1>
        <p className="m-0 text-muted">{t('app.tagline')}</p>
      </div>
      <Card>
        <p className="m-0">{t('app.comingSoon')}</p>
        <ThemeSwitch />
        <LanguageSwitch />
      </Card>
    </main>
  )
}
