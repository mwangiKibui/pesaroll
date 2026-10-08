import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useServices } from '../app/services'
import { Loading } from '../components/feedback'
import { AppShell } from '../components/shells'
import { Card } from '../components/TableCard'
import { useMe, useSignOut } from '../setup/hooks'
import { setupSteps } from '../setup/steps'

/** Home for someone with an organization. Until the dashboard (DASH) exists: the setup progress. */
export default function HomePage() {
  const { t } = useTranslation()
  const { api } = useServices()
  const me = useMe()
  const signOut = useSignOut()
  const organization = useQuery({
    queryKey: ['organization'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/organizations/current')
      if (error || !data) throw new Error('organization')
      return data
    },
  })

  const done = 1 // Organization; the rest come with ONB-03 onwards and ONB-12 tracks them.
  const next = t(`setup.steps.${setupSteps[done].key}`)

  return (
    <AppShell groups={[{ items: [{ key: 'home', label: t('nav.home'), href: '/' }] }]} current="home"
      organization={organization.data?.name ?? ''} person={me.data?.fullName ?? ''} onSignOut={signOut}
      setup={{ done, total: setupSteps.length, next, href: '/setup/organization' }}>
      <div className="mx-auto flex max-w-3xl flex-col gap-5">
        {me.data && organization.data ? (
          <>
            <h1 className="m-0 font-display text-title text-ink">{t('home.title', { name: me.data.firstName })}</h1>
            <Card title={organization.data.name}>
              <p className="m-0 text-muted">{t('home.next', { step: next })}</p>
            </Card>
          </>
        ) : <Loading rows={3} />}
      </div>
    </AppShell>
  )
}
