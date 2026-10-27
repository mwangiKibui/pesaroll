import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { permissions, usePermissions } from '../app/permissions'
import { useServices } from '../app/services'
import { Button, IconButton } from '../components/Button'
import { CompanySelect, type CompanySelection } from '../components/CompanySelect'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { EmptyState, Loading } from '../components/feedback'
import { PlusIcon } from '../components/icons'
import { Pagination } from '../components/Pagination'
import { TableCard } from '../components/TableCard'
import { usePaged } from '../components/usePaged'
import { useToast } from '../components/toast'
import { ScheduleDrawer } from '../features/schedules/ScheduleDrawer'
import { longDate, none, useScheduleWording } from '../features/schedules/wording'
import { useCompanies, useSchedules, useSetupDone } from '../setup/hooks'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

type Schedule = components['schemas']['PayScheduleResponse']

/**
 * ONB-06: how often (monthly, weekly, every two weeks or daily) and when people are paid. Each company already has a default schedule (the
 * suggested Monthly, last working day, made with the company); more schedules serve one, several or
 * all companies. Pay runs (PAY-03) take their periods and pay dates from these. The drawer is shared
 * with Organization → Payroll schedules (features/schedules).
 */
export default function SchedulesPage() {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const navigate = useNavigate()
  const companies = useCompanies()
  const schedules = useSchedules()
  const done = useSetupDone(true)
  const canManage = usePermissions().can(permissions.schedules.manage)
  const [shown, setShown] = useState<CompanySelection>({ all: true, ids: [] })
  const [editing, setEditing] = useState<Schedule | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Schedule | null>(null)
  const [busy, setBusy] = useState(false)

  const ready = companies.data && schedules.data
  const list = (schedules.data ?? []).filter(s => shown.all || s.allCompanies || s.companyIds.some(id => shown.ids.includes(id)))
  const paged = usePaged(list)
  const companyNames = (ids: string[]) => (companies.data ?? []).filter(c => ids.includes(c.id)).map(c => c.legalName).join(', ')
  const { payDay, cutoff } = useScheduleWording()

  const remove = async () => {
    if (!deleting) return
    setBusy(true)
    try {
      const { error, response } = await api.DELETE('/api/pay-schedules/{id}', { params: { path: { id: deleting.id } } })
      if (response.ok) {
        queryClient.setQueryData<Schedule[]>(['schedules'], old => old?.filter(s => s.id !== deleting.id))
        toast.show(t('schedules.deleted'))
      } else {
        toast.show(isApiError(error) ? errorMessage(t, error) : t('apiErrors.network'), 'danger')
      }
    } catch {
      toast.show(t('apiErrors.network'), 'danger')
    } finally {
      setBusy(false)
      setDeleting(null)
    }
  }

  return (
    <SetupLayout current="schedules" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('schedules.title')}</h1>
        <p className="m-0 text-muted">{t('schedules.intro')}</p>
      </div>
      {!ready ? <Loading rows={3} /> : <>
        {companies.data.length > 1 && (
          <div className="max-w-sm">
            <CompanySelect label={t('schedules.show')} value={shown} onChange={setShown}
              companies={companies.data.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
          </div>
        )}
        <TableCard footer={<Pagination label={t('pagination.of', { what: t('schedules.tableTitle') })} {...paged.pagination} />} title={t('schedules.tableTitle')} count={list.length}
          actions={canManage && <Button onClick={() => setEditing('new')}><PlusIcon />{t('schedules.add')}</Button>}>
          {list.length === 0 ? <EmptyState title={t('schedules.emptyTitle')}>{t('schedules.emptyText')}</EmptyState> : (
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('schedules.schedule')}</th>
                  <th scope="col">{t('schedules.frequency')}</th>
                  <th scope="col">{t('schedules.payDay')}</th>
                  <th scope="col">{t('schedules.cutoff')}</th>
                  <th scope="col">{t('schedules.nextPayDates')}</th>
                  <th scope="col">{t('schedules.appliesTo')}</th>
                  <th scope="col">{t('schedules.defaultFor')}</th>
                  <th scope="col">{t('schedules.reminders')}</th>
                  {canManage && <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>}
                </tr>
              </thead>
              <tbody>
                {paged.items.map(s => (
                  <tr key={s.id}>
                    <td className="font-medium text-ink">{s.name}</td>
                    <td>{t(`schedules.frequencies.${s.frequency}`)}</td>
                    <td>{payDay(s)}</td>
                    <td>{cutoff(s)}</td>
                    <td className="whitespace-nowrap">{s.nextPayDates.map(d => <span key={d} className="block">{longDate(d)}</span>)}</td>
                    <td>{s.allCompanies ? t('schedules.allCompanies') : companyNames(s.companyIds) || none}</td>
                    <td>{companyNames(s.defaultFor) || none}</td>
                    <td>{s.reminders.length || none}</td>
                    {canManage && <td className="whitespace-nowrap text-right">
                      <IconButton label={t('schedules.edit', { name: s.name })} onClick={() => setEditing(s)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                      </IconButton>
                      <IconButton tone="danger" label={t('schedules.delete', { name: s.name })} onClick={() => setDeleting(s)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                      </IconButton>
                    </td>}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </TableCard>
        <Proceed label={t('setup.proceed', { step: t('setup.steps.leavePlans') })} onProceed={() => void navigate({ to: '/setup/leave-plans' })} />

        {editing && (
          <ScheduleDrawer schedule={editing === 'new' ? null : editing} companies={companies.data} schedules={schedules.data}
            // A new schedule serves the companies being shown, as the selector suggests.
            defaultApplies={companies.data.length === 1 ? { all: false, ids: [companies.data[0].id] } : shown}
            onClose={() => setEditing(null)} />
        )}
      </>}
      <ConfirmDialog open={!!deleting} title={t('schedules.deleteTitle', { name: deleting?.name })}
        confirmLabel={t('schedules.deleteConfirm')} busy={busy} onConfirm={remove} onCancel={() => setDeleting(null)}>
        {t('schedules.deleteText')}
      </ConfirmDialog>
    </SetupLayout>
  )
}
