import { useQuery, type QueryClient } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { errorMessage, isApiError } from '../../app/apiErrors'
import { OrgShell } from '../../app/OrgShell'
import { useServices, type ApiError } from '../../app/services'
import { Button } from '../../components/Button'
import { buttonClasses } from '../../components/buttonStyles'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { EmptyState, FormErrorBanner, Loading, Notice, Tag } from '../../components/feedback'
import { TextField } from '../../components/fields'
import { Modal } from '../../components/Modal'
import { Money } from '../../components/Money'
import { Pagination } from '../../components/Pagination'
import { Card, TableCard } from '../../components/TableCard'
import { TabPanel, Tabs } from '../../components/Tabs'
import { useToast } from '../../components/toast'
import i18n from '../../i18n'
import { formatMoney } from '../../lib/money'
import { CorrectDrawer } from '../../features/payRuns/CorrectDrawer'
import { knownNotes } from '../../features/payRuns/lines'
import { PersonDrawer } from '../../features/payRuns/PersonDrawer'
import { RunInputs } from '../../features/payRuns/RunInputs'
import { RunSteps } from '../../features/payRuns/RunSteps'
import { datesLabel, hasTotals, monthName, periodLabel, statusTone } from '../../features/payRuns/wording'
import { shortDate } from '../../features/schedules/wording'
import { useCompanies } from '../../setup/hooks'

type Run = components['schemas']['PayRunDetail']
type Person = components['schemas']['PayRunPersonResponse']
type Problem = ApiError | 'network' | null

/**
 * OP-18, one pay run: where it is (Draft → Awaiting approval → Approved → Paid), its totals, what
 * changed since the last run, who was left out or needs a look, and each person's pay. Payroll works
 * it out, sends it and cancels it (canManage, from the API); the approver it waits for approves it or
 * sends it back (canDecide). Everyone else only looks; the API checks every action again.
 */
export default function PayRunPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  return (
    <OrgShell current="payRuns">
      <PayRun key={id} id={id} />
    </OrgShell>
  )
}

/** Runs the API's answer into the cache, so the page shows what the API now says. */
const keep = (queryClient: QueryClient, run: Run) => {
  queryClient.setQueryData(['pay-run', run.id], run)
  void queryClient.invalidateQueries({ queryKey: ['pay-runs'] })
}

function PayRun({ id }: { id: string }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const [problem, setProblem] = useState<Problem>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [cancelling, setCancelling] = useState(false)
  const [correcting, setCorrecting] = useState(false)
  const companies = useCompanies()
  const tabsId = useId()
  const [tab, setTab] = useState<'pay' | 'inputs'>('pay')
  // The inputs load the first time they're opened, then stay, so what's typed survives a look at Pay.
  const [visited, setVisited] = useState(false)
  const run = useQuery({
    queryKey: ['pay-run', id],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/pay-runs/{id}', { params: { path: { id } } })
      if (data) return data
      throw isApiError(error) ? error : new Error('pay-run')
    },
    // Working out happens in the background: look again until it's done.
    refetchInterval: q => (q.state.data?.status === 'calculating' ? 1000 : false),
  })

  const back = <Link to="/pay/runs" aria-label={t('payRun.back')} className="text-label font-medium text-brand">← {t('payRuns.title')}</Link>
  if (!run.data) {
    if (!run.isError) return <Loading rows={6} />
    return (
      <div className="flex flex-col gap-4">
        <EmptyState title={isApiError(run.error) ? errorMessage(t, run.error) : t('apiErrors.network')}
          action={<Link to="/pay/runs" className={buttonClasses('secondary')}>{t('payRun.back')}</Link>} />
      </div>
    )
  }
  const r = run.data
  // Runs pay only in the company's currency; anyone paid in another is left out.
  const currency = companies.data?.find(c => c.id === r.companyId)?.currency ?? 'KES'

  /** Posts one of the run's actions; the API answers with the run as it now is. */
  const act = async (name: string, call: () => Promise<{ data?: Run; error?: unknown }>, done?: string) => {
    setBusy(name)
    setProblem(null)
    try {
      const { data, error } = await call()
      if (!data) return setProblem(isApiError(error) ? error : 'network')
      keep(queryClient, data)
      void queryClient.invalidateQueries({ queryKey: ['pay-run-people', id] })
      if (done) toast.show(done)
      return true
    } catch {
      setProblem('network')
    } finally {
      setBusy(null)
    }
  }
  const path = { params: { path: { id } } }
  const calculate = () => act('calculate', () => api.POST('/api/pay-runs/{id}/calculate', path))
  const submit = () => act('submit', () => api.POST('/api/pay-runs/{id}/submit', path), t('payRun.sent'))
  const cancel = async () => { if (await act('cancel', () => api.POST('/api/pay-runs/{id}/cancel', path), t('payRun.cancelled'))) setCancelling(false) }

  const open = ['draft', 'calculated'].includes(r.status)
  const manage = r.canManage && (
    r.status === 'draft' ? <Button variant="secondary" onClick={() => setCancelling(true)}>{t('payRun.cancel')}</Button>
      : r.status === 'calculated' ? <>
      <Button variant="secondary" onClick={() => setCancelling(true)}>{t('payRun.cancel')}</Button>
      <Button variant="secondary" busy={busy === 'calculate'} onClick={calculate}>{t('payRun.recalculate')}</Button>
      <Button busy={busy === 'submit'} onClick={submit}>{t('payRun.send')}</Button>
    </> : r.status === 'approved' ? <Button variant="secondary" onClick={() => setCorrecting(true)}>{t('payRun.correct')}</Button> : null)

  return (
    <div className="flex flex-col gap-5">
      {back}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-col gap-1.5">
          <div className="flex flex-wrap items-center gap-2.5">
            <h1 className="m-0 font-display text-title text-ink">{t(r.kind === 'correction' ? 'payRun.titleCorrection' : 'payRun.title', { schedule: r.scheduleName, period: periodLabel(r.periodStart, r.periodEnd) })}</h1>
            <Tag tone={statusTone(r.status)}>{t(`payRuns.statuses.${r.status}`)}</Tag>
          </div>
          <p className="m-0 text-muted">{t('payRun.pays', { date: shortDate(r.payDate), count: Number(r.totals.people) })}</p>
        </div>
        {manage && <div className="flex flex-wrap gap-2">{manage}</div>}
      </div>
      {r.status !== 'cancelled' && <RunSteps status={r.status} />}

      {problem === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
      {problem && problem !== 'network' && <FormErrorBanner error={{ ...problem, message: errorMessage(t, problem) }}
        hint={problem.code === 'WF-5005' ? t('payRun.workflowHint') : undefined} />}
      {r.status === 'calculating' && <Notice tone="brand">{t('payRun.working')}</Notice>}
      {r.calculationError && r.status === 'draft' && <Notice tone="danger">{t('payRun.calculationFailed', { code: r.calculationError })}</Notice>}
      {r.declineReason && open && <Notice>{t('payRun.declined', { reason: r.declineReason })}</Notice>}
      {r.cutoffOn && open && <Notice tone="brand">{t('payRun.cutoff', { date: shortDate(r.cutoffOn) })}</Notice>}
      {r.status === 'awaiting_approval' && r.approvalRequestId && <Approval requestId={r.approvalRequestId} runId={id} />}

      {r.status !== 'cancelled' && <div className="flex flex-col gap-4">
        <Tabs id={tabsId} label={t('payRun.sections')} current={tab} onChange={key => { setTab(key as 'pay' | 'inputs'); setVisited(v => v || key === 'inputs') }}
          tabs={[{ key: 'pay', label: t('payRun.payTab') }, { key: 'inputs', label: t('payRun.inputsTab') }]} />
        <TabPanel id={tabsId} tab="pay" current={tab}>
          {r.status === 'draft' && <Card title={t('payRun.notWorkedOut')}>
            <dl className="m-0 grid gap-x-6 gap-y-3 sm:grid-cols-3">
              {[[t('payRun.period'), datesLabel(r.periodStart, r.periodEnd)], [t('payRuns.payDate'), shortDate(r.payDate)], [t('payRuns.schedule'), r.scheduleName]].map(([term, value]) => (
                <div key={term} className="flex flex-col gap-0.5">
                  <dt className="text-hint text-muted">{term}</dt>
                  <dd className="m-0 text-body font-medium text-ink">{value}</dd>
                </div>
              ))}
            </dl>
            <p className="m-0 text-muted">{t('payRun.notWorkedOutText')}</p>
            {r.canManage && <div><Button busy={busy === 'calculate'} onClick={calculate}>{t('payRun.calculate')}</Button></div>}
          </Card>}

          {hasTotals(r.status) && <>
            <Totals run={r} />
            {r.changes && <Notice tone="brand">{changesText(r)}</Notice>}
            <Lists run={r} />
            <People runId={id} currency={currency} />
          </>}
        </TabPanel>
        <TabPanel id={tabsId} tab="inputs" current={tab}>
          {visited && <RunInputs run={r} currency={currency} />}
        </TabPanel>
      </div>}

      <ConfirmDialog open={cancelling} title={t('payRun.cancelTitle')} confirmLabel={t('payRun.cancel')} busy={busy === 'cancel'}
        onConfirm={() => void cancel()} onCancel={() => setCancelling(false)}>
        {t('payRun.cancelText')}
      </ConfirmDialog>
      {correcting && <CorrectDrawer runId={id} onClose={() => setCorrecting(false)} />}
    </div>
  )
}

/** "Since September 2026: Otieno Ouma joined. Gross pay up KES 7,500, net pay down KES 1,200." */
function changesText(r: Run) {
  const c = r.changes!
  const t = (key: string, values?: Record<string, unknown>) => i18n.t(key, values)
  const monthly = periodLabel(r.periodStart, r.periodEnd) === monthName(r.periodStart)
  const since = monthly ? monthName(c.previousPeriodStart) : t('payRun.runOf', { date: shortDate(c.previousPeriodStart) })
  const names = (people: { name: string }[]) => people.length > 3 ? t('payRun.peopleCount', { count: people.length }) : people.map(p => p.name).join(', ')
  const move = (amount: number | string) => {
    const n = Number(amount)
    return n === 0 ? t('payRun.same') : t(n > 0 ? 'payRun.up' : 'payRun.down', { amount: formatMoney(Math.abs(n)) })
  }
  return [
    t('payRun.since', { period: since }),
    c.added.length > 0 && t('payRun.joined', { names: names(c.added) }),
    c.gone.length > 0 && t('payRun.gone', { names: names(c.gone) }),
    t('payRun.moved', { gross: move(c.grossPayChange), net: move(c.netPayChange) }),
  ].filter(Boolean).join(' ')
}

function Totals({ run }: { run: Run }) {
  const { t } = useTranslation()
  const item = (label: string, amount: number | string, strong = false) => (
    <div className="flex flex-col gap-0.5">
      <dt className="text-hint text-muted">{label}</dt>
      <dd className={`m-0 ${strong ? 'font-display text-drawer-title text-ink' : 'text-body font-medium text-ink'}`}><Money amount={amount} /></dd>
    </div>
  )
  return (
    <div role="group" aria-label={t('payRun.totals')} className="rounded-card border border-line bg-card p-5">
      <dl className="m-0 grid grid-cols-2 gap-4 sm:grid-cols-4">
        {item(t('payRuns.grossPay'), run.totals.grossPay)}
        {item(t('payRun.paye'), run.totals.paye)}
        {item(t('payRuns.netPay'), run.totals.netPay, true)}
        {item(t('payRun.employerCosts'), run.totals.employerCost)}
      </dl>
    </div>
  )
}

/** Who was left out and why, and who needs a look before it's sent. */
function Lists({ run }: { run: Run }) {
  const { t } = useTranslation()
  const section = (key: string, items: ReactNode[]) => items.length > 0 && (
    <section className="flex flex-col gap-2 rounded-card border border-line bg-card p-5">
      <h2 id={`run-${key}`} className="m-0 text-body font-semibold text-ink">{t(`payRun.${key}`)}</h2>
      <ul aria-labelledby={`run-${key}`} className="m-0 flex list-none flex-col divide-y divide-line-soft p-0">{items}</ul>
    </section>
  )
  const who = (name: string, staffNo: string) => <span className="flex flex-col"><span className="text-body text-ink">{name}</span><span className="font-mono text-hint text-muted">{staffNo}</span></span>
  return <>
    {section('leftOut', run.leftOut.map(p => (
      <li key={p.employeeId} className="flex flex-wrap items-center justify-between gap-2 py-2">
        {who(p.name, p.staffNo)}
        <span className="text-label text-muted">{t(`payRun.leftOutReasons.${p.reason}`, { defaultValue: '' })}</span>
      </li>
    )))}
    {section('flagged', run.flagged.map(p => (
      <li key={p.employeeId} className="flex flex-wrap items-center justify-between gap-2 py-2">
        {who(p.name, p.staffNo)}
        <span className="flex flex-wrap gap-1.5">{knownNotes(p.notes).map(n => <Tag key={n} tone="warning">{t(`payRun.notes.${n}.short`)}</Tag>)}</span>
      </li>
    )))}
  </>
}

/** Everyone the run pays, one row each; the headings carry the currency so the figures stay plain. */
function People({ runId, currency }: { runId: string; currency: string }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [viewing, setViewing] = useState<Person | null>(null)
  const people = useQuery({
    queryKey: ['pay-run-people', runId, page, pageSize],
    queryFn: async () => {
      const { data } = await api.GET('/api/pay-runs/{id}/people', { params: { path: { id: runId }, query: { page, pageSize } } })
      if (!data) throw new Error('pay-run-people')
      return data
    },
  })
  if (!people.data) return people.isError ? <Notice tone="danger">{t('apiErrors.network')}</Notice> : <Loading rows={4} />
  const total = Number(people.data.total)

  return <>
    <TableCard title={t('payRun.people')} count={total}
      footer={<Pagination label={t('pagination.of', { what: t('payRuns.people') })} page={page} pageSize={pageSize} total={total}
        onPage={setPage} onPageSize={size => { setPageSize(size); setPage(1) }} />}>
      <table>
        <thead>
          <tr>
            <th scope="col">{t('payRun.employee')}</th>
            <th scope="col" className="text-right">{t('payRuns.inCurrency', { label: t('payRuns.grossPay'), currency })}</th>
            <th scope="col" className="text-right">{t('payRuns.inCurrency', { label: t('payRun.paye'), currency })}</th>
            <th scope="col" className="text-right">{t('payRuns.inCurrency', { label: t('payRun.shif'), currency })}</th>
            <th scope="col" className="text-right">{t('payRuns.inCurrency', { label: t('payRun.nssf'), currency })}</th>
            <th scope="col" className="text-right">{t('payRuns.inCurrency', { label: t('payRun.housingLevy'), currency })}</th>
            <th scope="col" className="text-right">{t('payRuns.inCurrency', { label: t('payRuns.netPay'), currency })}</th>
            <th scope="col" className="text-right"><span className="sr-only">{t('payRun.actions')}</span></th>
          </tr>
        </thead>
        <tbody>
          {people.data.items.map(p => (
            <tr key={p.employeeId}>
              <td>
                <span className="block font-medium text-ink">{p.name}</span>
                <span className="block font-mono text-hint text-muted">{p.staffNo}</span>
                {knownNotes(p.notes).length > 0 && <span className="mt-1 flex flex-wrap gap-1">
                  {knownNotes(p.notes).map(n => <Tag key={n} tone="warning">{t(`payRun.notes.${n}.short`)}</Tag>)}
                </span>}
              </td>
              <td className="text-right"><Money amount={p.grossPay} currency={false} cents /></td>
              <td className="text-right"><Money amount={p.paye} currency={false} cents /></td>
              <td className="text-right"><Money amount={p.shif} currency={false} cents /></td>
              <td className="text-right"><Money amount={p.nssfEmployee} currency={false} cents /></td>
              <td className="text-right"><Money amount={p.housingLevyEmployee} currency={false} cents /></td>
              <td className="text-right font-medium"><Money amount={p.netPay} currency={false} cents /></td>
              <td className="text-right">
                <Button variant="ghost" aria-label={t('payRun.view', { name: p.name })} onClick={() => setViewing(p)}>{t('payRun.viewShort')}</Button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </TableCard>
    {viewing && <PersonDrawer person={viewing} onClose={() => setViewing(null)} />}
  </>
}

/** For a run awaiting approval: Approve and Send back for the approver it waits for, otherwise who it waits for. */
function Approval({ requestId, runId }: { requestId: string; runId: string }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const [problem, setProblem] = useState<Problem>(null)
  const [busy, setBusy] = useState(false)
  const [sending, setSending] = useState(false)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string>()
  const request = useQuery({
    queryKey: ['approval', requestId],
    queryFn: async () => {
      const { data } = await api.GET('/api/approvals/{id}', { params: { path: { id: requestId } } })
      if (!data) throw new Error('approval')
      return data
    },
  })
  if (!request.data) return null

  const decide = async (call: () => Promise<{ data?: unknown; error?: unknown }>, done: string) => {
    setBusy(true)
    setProblem(null)
    try {
      const { data, error } = await call()
      if (!data) return setProblem(isApiError(error) ? error : 'network')
      void queryClient.invalidateQueries({ queryKey: ['pay-run', runId] })
      void queryClient.invalidateQueries({ queryKey: ['approval', requestId] })
      void queryClient.invalidateQueries({ queryKey: ['pay-runs'] })
      toast.show(done)
      setSending(false)
    } catch {
      setProblem('network')
    } finally {
      setBusy(false)
    }
  }
  const path = { params: { path: { id: requestId } } }
  const approve = () => decide(() => api.POST('/api/approvals/{id}/approve', path), t('payRun.approved'))
  const sendBack = (e: FormEvent) => {
    e.preventDefault()
    if (!reason.trim()) return setReasonError(t('payRun.reasonRequired'))
    setReasonError(undefined)
    void decide(() => api.POST('/api/approvals/{id}/decline', { ...path, body: { reason: reason.trim() } }), t('payRun.sentBack'))
  }
  const banner = problem === 'network' ? <Notice tone="danger">{t('apiErrors.network')}</Notice>
    : problem ? <FormErrorBanner error={{ ...problem, message: errorMessage(t, problem) }} /> : null

  if (!request.data.canDecide)
    return <Notice tone="brand">{t('payRun.waitingFor', { names: request.data.waitingFor.join(', ') })}</Notice>
  return <>
    {!sending && banner}
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-card border border-line bg-card p-4">
      <p className="m-0 text-body text-ink">{t('payRun.yourApproval', { name: request.data.sentBy })}</p>
      <div className="flex flex-wrap gap-2">
        <Button variant="secondary" onClick={() => { setProblem(null); setSending(true) }}>{t('payRun.sendBack')}</Button>
        <Button busy={busy && !sending} onClick={() => void approve()}>{t('payRun.approve')}</Button>
      </div>
    </div>
    <Modal open={sending} title={t('payRun.sendBackTitle')} onClose={() => setSending(false)}
      footer={<>
        <Button variant="secondary" onClick={() => setSending(false)}>{t('common.cancel')}</Button>
        <Button type="submit" form="send-back" busy={busy}>{t('payRun.sendBack')}</Button>
      </>}>
      <form id="send-back" noValidate onSubmit={sendBack} className="flex flex-col gap-4">
        {banner}
        <TextField label={t('payRun.reason')} placeholder={t('payRun.reasonPlaceholder')} error={reasonError} value={reason} onChange={e => setReason(e.target.value)} />
      </form>
    </Modal>
  </>
}
