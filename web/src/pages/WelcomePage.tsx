import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useServices } from '../app/services'
import { Button } from '../components/Button'
import { Loading } from '../components/feedback'
import { LanguageSwitch, ThemeSwitch } from '../components/switches'
import { Card } from '../components/TableCard'

/** After sign-up or sign-in, until "Create your organization" (ONB-02) exists. */
export default function WelcomePage() {
  const { t } = useTranslation()
  const { api, auth, queryClient } = useServices()
  const me = useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/auth/me')
      if (error || !data) throw new Error('me')
      return data
    },
  })

  const signOut = async () => {
    await auth.signOut()
    queryClient.clear()
  }

  return (
    <main className="mx-auto flex min-h-dvh max-w-lg flex-col justify-center gap-6 px-5 py-12">
      <span className="font-display text-drawer-title text-brand">Pesaroll</span>
      {me.data ? (
        <Card>
          <h1 className="m-0 font-display text-title text-ink">{t('welcome.title', { name: me.data.firstName })}</h1>
          <p className="m-0 text-muted">{t('welcome.next')}</p>
          <div><Button variant="secondary" onClick={signOut}>{t('welcome.signOut')}</Button></div>
        </Card>
      ) : <Loading rows={3} />}
      <div className="flex flex-col gap-3">
        <LanguageSwitch />
        <ThemeSwitch />
      </div>
    </main>
  )
}
