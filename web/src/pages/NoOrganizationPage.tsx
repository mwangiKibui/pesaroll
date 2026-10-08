import { useTranslation } from 'react-i18next'
import { Button } from '../components/Button'
import { Navbar } from '../components/Navbar'
import { useMe, useSignOut } from '../setup/hooks'

/** Signed in, no organization, and not someone who onboards one (invited, removed, staff, partner). */
export default function NoOrganizationPage() {
  const { t } = useTranslation()
  const me = useMe()
  const signOut = useSignOut()
  return (
    <div className="flex min-h-dvh flex-col bg-page">
      <Navbar>
        <div className="flex items-center gap-3">
          {me.data && <span className="hidden text-label text-muted sm:inline">{me.data.fullName}</span>}
          <Button variant="secondary" onClick={signOut}>{t('nav.signOut')}</Button>
        </div>
      </Navbar>
      <main className="mx-auto flex w-full max-w-lg flex-1 flex-col justify-center gap-3 px-5 py-12">
        <h1 className="m-0 font-display text-title text-ink">{t('noOrganization.title')}</h1>
        <p className="m-0 text-muted">{t('noOrganization.text')}</p>
      </main>
    </div>
  )
}
