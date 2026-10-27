import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useCurrentCompany } from '../../app/company'
import { OrgShell } from '../../app/OrgShell'
import { permissions, usePermissions } from '../../app/permissions'
import { useServices } from '../../app/services'
import { Button } from '../../components/Button'
import { EmptyState, Loading, Notice, Tag } from '../../components/feedback'
import { PlusIcon } from '../../components/icons'
import { Money } from '../../components/Money'
import { Pagination } from '../../components/Pagination'
import { TableCard } from '../../components/TableCard'
import { StartPayRunDrawer } from '../../features/payRuns/StartPayRunDrawer'
import { hasTotals, periodLabel, statusTone } from '../../features/payRuns/wording'
import { none, shortDate } from '../../features/schedules/wording'
import { useCompanies } from '../../setup/hooks'

/**
 * OP-18, Pay → Pay runs: the top bar company's runs, newest first and paged on the server, and Start
 * pay run for people who run payroll there. Approvers don't need this list: they open a run from
 * what's sent to them, and never see drafts (the API).
 */
export default function PayRunsPage() {
  return (
    <OrgShell current="payRuns">
      <PayRuns />
    </OrgShell>
  )
}

function PayRuns() {
  const { t } = useTranslation()
  const { companyId } = useCurrentCompany()
  const { ready, can } = usePermissions()

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('payRuns.title')}</h1>
        <p className="m-0 text-muted">{t('payRuns.intro')}</p>
      </div>
      {!ready || !companyId ? <Loading rows={4} />
        : !can(permissions.payRuns.view, companyId) ? <EmptyState title={t('payRuns.noAccessTitle')}>{t('payRuns.noAccessText')}</EmptyState>
          : <Runs key={companyId} companyId={companyId} canStart={can(permissions.payRuns.manage, companyId)} />}
    </div>
  )
}

function Runs({ companyId, canStart }: { companyId: string; canStart: boolean }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const companies = useCompanies()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [starting, setStarting] = useState(false)
  const runs = useQuery({
    queryKey: ['pay-runs', companyId, page, pageSize],
    queryFn: async () => {
      const { data } = await api.GET('/api/pay-runs', { params: { query: { companyId, page, pageSize } } })
      if (!data) throw new Error('pay-runs')
      return data
    },
  })

  if (!runs.data) return runs.isError ? <Notice tone="danger">{t('apiErrors.network')}</Notice> : <Loading rows={4} />
  const total = Number(runs.data.total)

  return <>
    <TableCard title={t('payRuns.title')} count={total}
      footer={<Pagination label={t('pagination.of', { what: t('payRuns.title') })} page={page} pageSize={pageSize} total={total}
        onPage={setPage} onPageSize={size => { setPageSize(size); setPage(1) }} />}
      actions={canStart && <Button onClick={() => setStarting(true)}><PlusIcon />{t('payRuns.start')}</Button>}>
      {runs.data.items.length === 0 ? <EmptyState title={t('payRuns.emptyTitle')}>{t('payRuns.emptyText')}</EmptyState> : (
        <table>
          <thead>
            <tr>
              <th scope="col">{t('payRuns.period')}</th>
              <th scope="col">{t('payRuns.payDate')}</th>
              <th scope="col" className="text-right">{t('payRuns.people')}</th>
              <th scope="col" className="text-right">{t('payRuns.grossPay')}</th>
              <th scope="col" className="text-right">{t('payRuns.netPay')}</th>
              <th scope="col">{t('payRuns.status')}</th>
            </tr>
          </thead>
          <tbody>
            {runs.data.items.map(r => (
              <tr key={r.id}>
                <td>
                  <span className="block font-medium text-ink">{periodLabel(r.periodStart, r.periodEnd)}</span>
                  <span className="block text-hint text-muted">{r.kind === 'correction' ? t('payRuns.correctionOf', { schedule: r.scheduleName }) : r.scheduleName}</span>
                </td>
                <td className="whitespace-nowrap">{shortDate(r.payDate)}</td>
                <td className="text-right tabular-nums">{Number(r.people)}</td>
                <td className="text-right">{hasTotals(r.status) ? <Money amount={r.grossPay} /> : none}</td>
                <td className="text-right">{hasTotals(r.status) ? <Money amount={r.netPay} /> : none}</td>
                <td><Tag tone={statusTone(r.status)}>{t(`payRuns.statuses.${r.status}`)}</Tag></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </TableCard>
    {starting && companies.data && <StartPayRunDrawer companies={companies.data} companyId={companyId} onClose={() => setStarting(false)} />}
  </>
}
