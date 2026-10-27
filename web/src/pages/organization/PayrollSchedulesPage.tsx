import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { errorMessage, isApiError } from '../../app/apiErrors'
import { useCurrentCompany } from '../../app/company'
import { OrgShell } from '../../app/OrgShell'
import { permissions, usePermissions } from '../../app/permissions'
import { useServices, type ApiError } from '../../app/services'
import { Button, IconButton } from '../../components/Button'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { EmptyState, FormErrorBanner, Loading, Notice } from '../../components/feedback'
import { PlusIcon } from '../../components/icons'
import { Pagination } from '../../components/Pagination'
import { TableCard } from '../../components/TableCard'
import { usePaged } from '../../components/usePaged'
import { useToast } from '../../components/toast'
import { ScheduleDrawer } from '../../features/schedules/ScheduleDrawer'
import { longDate, useScheduleWording } from '../../features/schedules/wording'
import { useCompanies } from '../../setup/hooks'

type Schedule = components['schemas']['PayScheduleResponse']

/**
 * OP-05, Organization → Payroll schedules: the schedules that pay the company chosen in the top bar,
 * with how many of its people each pays; add, edit and delete for people with Manage on schedules.
 * The drawer is the setup step's (features/schedules); a new schedule starts with the top bar's
 * company only.
 */
export default function PayrollSchedulesPage() {
  return (
    <OrgShell current="schedules">
      <PayrollSchedules />
    </OrgShell>
  )
}

function PayrollSchedules() {
  const { t } = useTranslation()
  const { companyId } = useCurrentCompany()
  const { ready, can } = usePermissions()

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('schedules.tableTitle')}</h1>
        <p className="m-0 text-muted">{t('schedules.orgIntro')}</p>
      </div>
      {!ready || !companyId ? <Loading rows={3} />
        : !can(permissions.schedules.view, companyId) ? <EmptyState title={t('schedules.noAccessTitle')}>{t('schedules.noAccessText')}</EmptyState>
          : <Schedules key={companyId} companyId={companyId} canManage={can(permissions.schedules.manage, companyId)} />}
    </div>
  )
}

function Schedules({ companyId, canManage }: { companyId: string; canManage: boolean }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const companies = useCompanies()
  const { payDay, cutoff } = useScheduleWording()
  const schedules = useQuery({
    queryKey: ['schedules', companyId],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/pay-schedules', { params: { query: { companyId } } })
      if (error || !data) throw new Error('schedules')
      return data
    },
  })
  const [editing, setEditing] = useState<Schedule | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Schedule | null>(null)
  const [deleteError, setDeleteError] = useState<ApiError | 'network' | null>(null)
  const [busy, setBusy] = useState(false)
  const list = schedules.data ?? []
  const paged = usePaged(list)

  const companyNames = (ids: string[]) => (companies.data ?? []).filter(c => ids.includes(c.id)).map(c => c.legalName).join(', ')
  /** "Last working day · cut-off 25th", "Every Friday · cut-off 2 days before"; daily pay has no cut-off. */
  const when = (s: Schedule) => {
    if (s.frequency === 'daily') return payDay(s)
    if (!s.usesCutoff) return t('schedules.withoutCutoff', { payDay: payDay(s) })
    if (s.frequency !== 'monthly' && Number(s.cutoffDaysBefore) === 0) return t('schedules.cutoffOnPayDay', { payDay: payDay(s) })
    return t('schedules.withCutoff', { payDay: payDay(s), cutoff: cutoff(s) })
  }

  const closeDelete = () => { setDeleting(null); setDeleteError(null) }
  // A refusal (people on it, a company's default) stays in the dialog with its code.
  const remove = async () => {
    if (!deleting) return
    setBusy(true)
    setDeleteError(null)
    try {
      const { error, response } = await api.DELETE('/api/pay-schedules/{id}', { params: { path: { id: deleting.id } } })
      if (response.ok) {
        void queryClient.invalidateQueries({ queryKey: ['schedules'] })
        toast.show(t('schedules.deleted'))
        closeDelete()
      } else {
        setDeleteError(isApiError(error) ? error : 'network')
      }
    } catch {
      setDeleteError('network')
    } finally {
      setBusy(false)
    }
  }

  if (!schedules.data || !companies.data) return schedules.isError ? <Notice tone="danger">{t('apiErrors.network')}</Notice> : <Loading rows={3} />

  return <>
    <TableCard footer={<Pagination label={t('pagination.of', { what: t('schedules.tableTitle') })} {...paged.pagination} />} title={t('schedules.tableTitle')} count={list.length}
      actions={canManage && <Button onClick={() => setEditing('new')}><PlusIcon />{t('schedules.add')}</Button>}>
      {list.length === 0 ? <EmptyState title={t('schedules.orgEmptyTitle')}>{t('schedules.orgEmptyText')}</EmptyState> : (
        <table>
          <thead>
            <tr>
              <th scope="col">{t('schedules.schedule')}</th>
              <th scope="col">{t('schedules.payDay')}</th>
              <th scope="col">{t('schedules.companies')}</th>
              <th scope="col" className="text-right">{t('schedules.people')}</th>
              <th scope="col">{t('schedules.nextPayDates')}</th>
              {canManage && <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>}
            </tr>
          </thead>
          <tbody>
            {paged.items.map(s => (
              <tr key={s.id}>
                <td>
                  <span className="block font-medium text-ink">{s.name}</span>
                  <span className="block text-hint text-muted">{t(`schedules.frequencies.${s.frequency}`)}</span>
                </td>
                <td>{when(s)}</td>
                <td>{s.allCompanies ? t('schedules.allCompanies') : companyNames(s.companyIds)}</td>
                <td className="text-right tabular-nums">{Number(s.people)}</td>
                <td className="whitespace-nowrap">{s.nextPayDates.map(d => <span key={d} className="block">{longDate(d)}</span>)}</td>
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

    {editing && (
      <ScheduleDrawer schedule={editing === 'new' ? null : editing} companies={companies.data} schedules={list}
        defaultApplies={{ all: false, ids: [companyId] }} onClose={() => setEditing(null)} />
    )}
    <ConfirmDialog open={!!deleting} title={t('schedules.deleteTitle', { name: deleting?.name })}
      confirmLabel={t('schedules.deleteConfirm')} busy={busy} onConfirm={remove} onCancel={closeDelete}
      problem={deleteError === 'network' ? <Notice tone="danger">{t('apiErrors.network')}</Notice>
        : deleteError && <FormErrorBanner error={{ ...deleteError, message: errorMessage(t, deleteError) }} />}>
      {t('schedules.deleteText')}
    </ConfirmDialog>
  </>
}
