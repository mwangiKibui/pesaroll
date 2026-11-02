import { useQueries, useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { errorMessage, isApiError } from '../../app/apiErrors'
import { useServices, type ApiError } from '../../app/services'
import { Button } from '../../components/Button'
import { FormErrorBanner, Loading, Notice, Tag } from '../../components/feedback'
import { SelectField, TextField } from '../../components/fields'
import { Modal } from '../../components/Modal'
import { formatMoney } from '../../lib/money'
import { periodLabel } from '../payRuns/wording'
import { shortDate } from '../schedules/wording'

type Done = components['schemas']['PayChangeResponse']
type Field = 'amount' | 'basis' | 'usualQuantity' | 'effectiveFrom' | 'reason'
const bases = ['monthly', 'weekly', 'fortnightly', 'daily', 'hourly'] as const

/**
 * Basic pay over time on the profile (PAY-04): each change from its date, with why. Amounts load only
 * after Show amounts, as the API records who saw them (ACC-05). With Manage on pay, pay can be changed
 * from an earlier date; the API then says which runs went back to draft and which approved runs pay
 * the old amount, to correct.
 */
export function PayChanges({ companyId, employeeId, currency, showPay, canManage }: {
  companyId: string; employeeId: string; currency: string; showPay: boolean; canManage: boolean
}) {
  const { t } = useTranslation()
  const { api } = useServices()
  const [changing, setChanging] = useState(false)
  const [done, setDone] = useState<Done | null>(null)
  const history = useQuery({
    queryKey: ['employee', employeeId, 'pay-changes'],
    enabled: showPay,
    queryFn: async () => {
      const { data } = await api.GET('/api/companies/{companyId}/employees/{employeeId}/pay-changes', { params: { path: { companyId, employeeId } } })
      if (!data) throw new Error('pay-changes')
      return data
    },
  })
  const per = (amount: number | string, basis: string) => t(`employees.per.${basis}`, { amount: formatMoney(amount, { currency }) })

  return (
    <section aria-label={t('employees.payChanges.title')} className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="m-0 text-label font-semibold text-ink">{t('employees.payChanges.title')}</h3>
        {canManage && <Button variant="secondary" onClick={() => setChanging(true)}>{t('employees.payChanges.change')}</Button>}
      </div>
      {done && <Result done={done} />}
      {!showPay ? <p className="m-0 text-label text-muted">{t('employees.payChanges.showFirst')}</p>
        : !history.data ? (history.isError ? <Notice tone="danger">{t('apiErrors.network')}</Notice> : <Loading rows={2} />) : (
          <ul className="m-0 flex list-none flex-col divide-y divide-line-soft p-0">
            {history.data.map(r => (
              <li key={r.id} className="flex flex-col py-2">
                <span className="flex flex-wrap items-center gap-2 text-label text-muted">
                  {r.validTo ? t('employees.payChanges.fromTo', { from: shortDate(r.validFrom), to: shortDate(r.validTo) }) : t('employees.payChanges.from', { from: shortDate(r.validFrom) })}
                  {r.replacedAt && <Tag>{t('employees.payChanges.replaced')}</Tag>}
                </span>
                <span className="text-body text-ink">{per(r.amount, r.basis)}</span>
                {r.reason && <span className="text-hint text-muted">{r.reason}</span>}
              </li>
            ))}
          </ul>
        )}
      {changing && <ChangeForm companyId={companyId} employeeId={employeeId} basis={history.data?.find(r => !r.replacedAt)?.basis}
        onClose={() => setChanging(false)} onDone={d => { setDone(d); setChanging(false) }} />}
    </section>
  )
}

/** What the change set off: runs to work out again and approved runs to correct, each linked. */
function Result({ done }: { done: Done }) {
  const { t } = useTranslation()
  const { api } = useServices()
  // The API names the runs sent back by id; their months come from each run.
  const sentBack = useQueries({
    queries: done.sentBackToDraft.map(id => ({
      queryKey: ['pay-run', id],
      queryFn: async () => {
        const { data } = await api.GET('/api/pay-runs/{id}', { params: { path: { id } } })
        if (!data) throw new Error('pay-run')
        return data
      },
    })),
  })
  const runLink = (id: string, label: string) => <Link to="/pay/runs/$id" params={{ id }} className="font-medium text-brand">{label}</Link>
  return (
    <div role="status">
      <Notice tone="brand">
        <p className="m-0">{t('employees.payChanges.saved', { from: shortDate(done.change.validFrom) })}</p>
        {done.sentBackToDraft.length > 0 && <>
          <p className="m-0 mt-2">{t('employees.payChanges.sentBack')}</p>
          <ul className="m-0 pl-5">
            {sentBack.map((q, i) => <li key={done.sentBackToDraft[i]}>{q.data ? runLink(q.data.id, periodLabel(q.data.periodStart, q.data.periodEnd)) : '…'}</li>)}
          </ul>
        </>}
        {done.needCorrection.length > 0 && <>
          <p className="m-0 mt-2">{t('employees.payChanges.toCorrect')}</p>
          <ul className="m-0 pl-5">
            {done.needCorrection.map(r => <li key={r.payRunId}>{runLink(r.payRunId, periodLabel(r.periodStart, r.periodEnd))}</li>)}
          </ul>
        </>}
      </Notice>
    </div>
  )
}

function ChangeForm({ companyId, employeeId, basis: current, onClose, onDone }: {
  companyId: string; employeeId: string; basis?: string; onClose: () => void; onDone: (done: Done) => void
}) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const [amount, setAmount] = useState('')
  const [basis, setBasis] = useState(current ?? 'monthly')
  const [usual, setUsual] = useState('')
  const [from, setFrom] = useState('')
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<Partial<Record<Field, string>>>({})
  const [problem, setProblem] = useState<ApiError | 'network' | null>(null)
  const [busy, setBusy] = useState(false)
  const byUnit = basis === 'daily' || basis === 'hourly'

  const save = async (e: FormEvent) => {
    e.preventDefault()
    const n = Number(amount.replace(/kes|ksh|[\s,]/gi, ''))
    const days = usual.trim() ? Number(usual) : null
    const found: Partial<Record<Field, string>> = {
      ...(!amount.trim() || !(n > 0) ? { amount: t('employees.payChanges.amountRequired') } : {}),
      ...(byUnit && days !== null && !(days > 0 && days <= 744) ? { usualQuantity: t('employees.payChanges.usualInvalid') } : {}),
      ...(!from ? { effectiveFrom: t('employees.payChanges.fromRequired') } : {}),
      ...(!reason.trim() ? { reason: t('employees.payChanges.reasonRequired') } : {}),
    }
    setErrors(found)
    if (Object.keys(found).length > 0) return
    setBusy(true)
    setProblem(null)
    try {
      const { data, error } = await api.POST('/api/companies/{companyId}/employees/{employeeId}/pay-changes', {
        params: { path: { companyId, employeeId } },
        body: { amount: n, basis, usualQuantity: byUnit ? days : null, effectiveFrom: from, reason: reason.trim() },
      })
      if (data) {
        void queryClient.invalidateQueries({ queryKey: ['employee', employeeId] })
        void queryClient.invalidateQueries({ queryKey: ['employees'] })
        void queryClient.invalidateQueries({ queryKey: ['pay-runs'] })
        for (const id of data.sentBackToDraft) void queryClient.invalidateQueries({ queryKey: ['pay-run', id] })
        onDone(data)
        return
      }
      if (!isApiError(error)) return setProblem('network')
      const fields = Object.fromEntries(Object.entries(error.fields ?? {}).filter(([k]) => ['amount', 'basis', 'usualQuantity', 'effectiveFrom', 'reason'].includes(k)))
      if (Object.keys(fields).length > 0) setErrors(fields)
      else setProblem(error)
    } catch {
      setProblem('network')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open title={t('employees.payChanges.change')} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form="pay-change" busy={busy}>{t('common.save')}</Button>
      </>}>
      <form id="pay-change" noValidate onSubmit={e => void save(e)} className="flex flex-col gap-4">
        {problem === 'network' ? <Notice tone="danger">{t('apiErrors.network')}</Notice>
          : problem && <FormErrorBanner error={{ ...problem, message: errorMessage(t, problem) }} />}
        <p className="m-0 text-muted">{t('employees.payChanges.intro')}</p>
        <TextField label={t('employees.basicPay')} placeholder={t('employees.payChanges.amountPlaceholder')} inputMode="decimal" error={errors.amount}
          value={amount} onChange={e => setAmount(e.target.value)} />
        <SelectField label={t('employees.payChanges.basis')} placeholder={t('employees.payChanges.basisPlaceholder')} error={errors.basis} value={basis}
          onChange={v => setBasis(v)} options={bases.map(b => ({ value: b, label: t(`employees.payChanges.bases.${b}`) }))} />
        {byUnit && <TextField label={t(`employees.payChanges.usual.${basis}`)} placeholder={t('employees.payChanges.usualPlaceholder')} inputMode="decimal" optional
          error={errors.usualQuantity} value={usual} onChange={e => setUsual(e.target.value)} />}
        <TextField type="date" label={t('employees.payChanges.effectiveFrom')} placeholder={t('employees.payChanges.effectiveFromPlaceholder')} error={errors.effectiveFrom}
          value={from} onChange={e => setFrom(e.target.value)} />
        <TextField label={t('payRun.reason')} placeholder={t('employees.payChanges.reasonPlaceholder')} error={errors.reason} value={reason} onChange={e => setReason(e.target.value)} />
      </form>
    </Modal>
  )
}
