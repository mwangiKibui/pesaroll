import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices } from '../app/services'
import { Button } from '../components/Button'
import { buttonClasses } from '../components/buttonStyles'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { Loading, Tag } from '../components/feedback'
import { Navbar } from '../components/Navbar'
import { TableCard } from '../components/TableCard'
import { useToast } from '../components/toast'
import { useMe, useSignOut } from '../setup/hooks'

type Device = components['schemas']['SessionResponse']

const when = (iso: string) => new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', timeZone: 'Africa/Nairobi' })
  .format(new Date(iso))

/**
 * AUTH-05: where you're signed in. Every device with an active session, the one you're on marked;
 * sign out any one of them, or every other one at once. Signing out this one is signing out here.
 * The person menu of the app shell (OP-01) links here, and so does your name in the setup navbar.
 */
export default function DevicesPage() {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const me = useMe()
  const signOut = useSignOut()
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)

  const devices = useQuery({
    queryKey: ['sessions'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/auth/sessions')
      if (error || !data) throw new Error('sessions')
      return data
    },
  })

  const act = async (work: () => Promise<{ error?: unknown; response: Response }>, done: string) => {
    setBusy(true)
    try {
      const { error, response } = await work()
      if (response.ok) toast.show(done)
      else toast.show(isApiError(error) ? errorMessage(t, error) : t('apiErrors.network'), 'danger')
    } catch {
      toast.show(t('apiErrors.network'), 'danger')
    } finally {
      setBusy(false)
      void queryClient.invalidateQueries({ queryKey: ['sessions'] })
    }
  }
  const end = (d: Device) => (d.current
    ? signOut()
    : act(() => api.POST('/api/auth/sessions/{id}/end', { params: { path: { id: d.id } } }), t('devices.signedOut', { device: d.device })))
  const endOthers = async () => {
    await act(() => api.POST('/api/auth/sessions/end-others'), t('devices.othersSignedOut'))
    setConfirming(false)
  }

  const list = devices.data ?? []
  const others = list.some(d => !d.current)
  return (
    <div className="flex min-h-dvh flex-col bg-page">
      <Navbar>
        <div className="flex items-center gap-3">
          {me.data && <span className="hidden text-label text-muted sm:inline">{me.data.fullName}</span>}
          <Link to="/" className={buttonClasses('secondary')}>{t('devices.back')}</Link>
        </div>
      </Navbar>
      <main className="mx-auto flex w-full max-w-4xl flex-col gap-5 px-4 py-6 md:px-10 md:py-8">
        <div className="flex flex-col gap-1.5">
          <h1 className="m-0 font-display text-title text-ink">{t('devices.title')}</h1>
          <p className="m-0 text-muted">{t('devices.intro')}</p>
        </div>
        {!devices.data ? <Loading rows={3} /> : (
          <TableCard title={t('devices.tableTitle')} count={list.length}
            actions={others && <Button variant="secondary" onClick={() => setConfirming(true)}>{t('devices.signOutOthers')}</Button>}>
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('devices.device')}</th>
                  <th scope="col">{t('devices.where')}</th>
                  <th scope="col">{t('devices.signedIn')}</th>
                  <th scope="col">{t('devices.lastActive')}</th>
                  <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                </tr>
              </thead>
              <tbody>
                {list.map(d => (
                  <tr key={d.id}>
                    <td>
                      <span className="block font-medium text-ink">{d.device}</span>
                      {d.current && <Tag tone="brand">{t('devices.thisDevice')}</Tag>}
                    </td>
                    <td className="font-mono text-hint">{d.ipAddress ?? t('devices.unknownPlace')}</td>
                    <td className="whitespace-nowrap">{when(d.signedInAt)}</td>
                    <td className="whitespace-nowrap">{when(d.lastActiveAt)}</td>
                    <td className="whitespace-nowrap text-right">
                      <Button variant="ghost" busy={busy} aria-label={t('devices.signOutDevice', { device: d.device })} onClick={() => void end(d)}>
                        {t('devices.signOut')}
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </TableCard>
        )}
      </main>
      <ConfirmDialog open={confirming} busy={busy} onConfirm={() => void endOthers()} onCancel={() => setConfirming(false)}
        title={t('devices.othersTitle')} confirmLabel={t('devices.othersButton')}>
        {t('devices.othersText')}
      </ConfirmDialog>
    </div>
  )
}
