import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { errorMessage, isApiError } from '../../app/apiErrors'
import { useServices, type ApiError } from '../../app/services'
import { Button } from '../../components/Button'
import { FormErrorBanner, Loading, Notice, Tag } from '../../components/feedback'
import { TextField } from '../../components/fields'
import { TableCard } from '../../components/TableCard'
import { useToast } from '../../components/toast'
import { formatMoney } from '../../lib/money'
import { shortDate } from '../schedules/wording'
import { checkCell, columnsOf, entryBody, keyOf, startingValue, type Column } from './inputs'

type Inputs = components['schemas']['RunTimeResponse']
type Run = components['schemas']['PayRunDetail']

/**
 * What's entered for a run (PAY-04, PAY-11, OP-18): days or hours for people paid by the day or
 * hour, items paid by the hour or day, and amounts entered each run, one row per person the run
 * pays (leavers owed final dues included). Payroll edits it until the run is sent; saving sends only
 * what changed, and a worked-out run goes back to draft. Entries after the cut-off wait for the next run.
 */
export function RunInputs({ run, currency }: { run: Run; currency: string }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const [edits, setEdits] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [problem, setProblem] = useState<ApiError | 'network' | null>(null)
  const [busy, setBusy] = useState<'save' | 'copy' | null>(null)
  const canEdit = run.canManage && ['draft', 'calculated'].includes(run.status)
  const inputs = useQuery({
    queryKey: ['pay-run-inputs', run.id],
    queryFn: async () => {
      const { data } = await api.GET('/api/pay-runs/{id}/inputs', { params: { path: { id: run.id } } })
      if (!data) throw new Error('pay-run-inputs')
      return data
    },
  })
  if (!inputs.data) return inputs.isError ? <Notice tone="danger">{t('apiErrors.network')}</Notice> : <Loading rows={4} />
  const data = inputs.data
  const columns = columnsOf(data)
  const value = (employeeId: string, c: Column) => edits[keyOf(employeeId, c)] ?? startingValue(data, employeeId, c)
  const changed = data.people.flatMap(p => columns.filter(c => c.kind !== 'basic' || p.unit)
    .filter(c => edits[keyOf(p.employeeId, c)] !== undefined && edits[keyOf(p.employeeId, c)].trim() !== startingValue(data, p.employeeId, c))
    .map(c => ({ employeeId: p.employeeId, column: c })))
  const late = data.people.some(p => p.entries.some(e => e.afterCutoff))

  /** The API's answer is what's entered now; the run and its figures are looked at again. */
  const keep = (next: Inputs) => {
    queryClient.setQueryData(['pay-run-inputs', run.id], next)
    void queryClient.invalidateQueries({ queryKey: ['pay-run', run.id] })
    void queryClient.invalidateQueries({ queryKey: ['pay-run-people', run.id] })
    void queryClient.invalidateQueries({ queryKey: ['pay-runs'] })
  }

  const save = async () => {
    const found = Object.fromEntries(changed.flatMap(({ employeeId, column }) => {
      const problem = checkCell(column, value(employeeId, column))
      return problem ? [[keyOf(employeeId, column), t(`payRun.inputs.${problem}`)]] : []
    }))
    setErrors(found)
    setProblem(null)
    if (Object.keys(found).length > 0 || changed.length === 0) return
    setBusy('save')
    try {
      const { data: next, error } = await api.PUT('/api/pay-runs/{id}/inputs', {
        params: { path: { id: run.id } }, body: { entries: changed.map(({ employeeId, column }) => entryBody(employeeId, column, value(employeeId, column))) },
      })
      if (next) {
        keep(next)
        setEdits({})
        toast.show(t('payRun.inputs.saved'))
        return
      }
      if (!isApiError(error)) return setProblem('network')
      setProblem(error)
      // "entries[2].quantity": the third entry sent.
      setErrors(Object.fromEntries(Object.entries(error.fields ?? {}).flatMap(([field, message]) => {
        const sent = changed[Number(/^entries\[(\d+)\]/.exec(field)?.[1] ?? -1)]
        return sent ? [[keyOf(sent.employeeId, sent.column), message]] : []
      })))
    } catch {
      setProblem('network')
    } finally {
      setBusy(null)
    }
  }

  const copy = async () => {
    setProblem(null)
    setBusy('copy')
    try {
      const { data: next, error } = await api.POST('/api/pay-runs/{id}/inputs/copy-previous', { params: { path: { id: run.id } } })
      if (!next) return setProblem(isApiError(error) ? error : 'network')
      keep(next)
      toast.show(t('payRun.inputs.copied'))
    } catch {
      setProblem('network')
    } finally {
      setBusy(null)
    }
  }

  const heading = (c: Column) => c.kind === 'basic' ? t('payRun.inputs.worked')
    : c.kind === 'time' ? t('payRun.inputs.timeColumn', { name: c.name, unit: t(`payRun.inputs.units.${c.unit}`) })
      : t('payRuns.inCurrency', { label: c.name, currency })
  const shown = (c: Column, raw: string) => !raw ? '—' : c.kind === 'amount' ? formatMoney(raw, { currency: false, cents: true }) : raw

  return <>
    {problem === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
    {problem && problem !== 'network' && <FormErrorBanner error={{ ...problem, message: errorMessage(t, problem) }} />}
    {canEdit && run.status === 'calculated' && <Notice tone="brand">{t('payRun.inputs.backToDraft')}</Notice>}
    {late && run.cutoffOn && <Notice>{t('payRun.inputs.late', { date: shortDate(run.cutoffOn) })}</Notice>}
    <TableCard title={t('payRun.inputs.title')} count={data.people.length}
      actions={canEdit && <>
        {changed.length > 0 && <span className="text-label text-muted">{t('payRun.inputs.changes', { count: changed.length })}</span>}
        <Button variant="secondary" busy={busy === 'copy'} onClick={() => void copy()}>{t('payRun.inputs.copy')}</Button>
        {changed.length > 0 && <Button busy={busy === 'save'} onClick={() => void save()}>{t('payRun.inputs.save')}</Button>}
      </>}>
      <table>
        <thead>
          <tr>
            <th scope="col" className="sticky left-0 z-10">{t('payRun.employee')}</th>
            {columns.map(c => <th key={c.id} scope="col" className="text-right">{heading(c)}</th>)}
          </tr>
        </thead>
        <tbody>
          {data.people.map(p => (
            <tr key={p.employeeId}>
              <td className="sticky left-0 z-10 bg-card">
                <span className="block font-medium text-ink">{p.name}</span>
                <span className="block font-mono text-hint text-muted">{p.staffNo}</span>
                {p.leftOn && <span className="mt-1 block"><Tag tone="warning">{t('payRun.inputs.left', { date: shortDate(p.leftOn) })}</Tag></span>}
              </td>
              {columns.map(c => {
                if (c.kind === 'basic' && !p.unit) return <td key={c.id} className="text-right text-muted">{t('payRun.inputs.salary')}</td>
                const key = keyOf(p.employeeId, c)
                const raw = value(p.employeeId, c)
                const after = p.entries.some(e => e.afterCutoff && e.payItemId === (c.kind === 'basic' ? null : c.id))
                const label = c.kind === 'basic' ? t(`payRun.inputs.workedFor.${p.unit}`, { name: p.name }) : t('payRun.inputs.cell', { column: heading(c), name: p.name })
                const placeholder = c.kind === 'basic'
                  ? p.usualQuantity != null ? t(`payRun.inputs.usual.${p.unit}`, { count: Number(p.usualQuantity) }) : t(`payRun.inputs.unitName.${p.unit}`)
                  : c.kind === 'time' ? t(`payRun.inputs.unitName.${c.unit}`) : t('payRun.inputs.amount')
                return (
                  <td key={c.id} className="text-right align-top">
                    {canEdit
                      ? <div className="ml-auto w-28">
                          <TextField size="sm" hideLabel label={label} placeholder={placeholder} inputMode="decimal" className="text-right tabular-nums"
                            value={raw} error={errors[key]} onChange={e => setEdits(v => ({ ...v, [key]: e.target.value }))} />
                        </div>
                      : <span className="tabular-nums">{shown(c, raw)}</span>}
                    {after && <span className="mt-1 block"><Tag tone="warning">{t('payRun.inputs.nextRun')}</Tag></span>}
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </TableCard>
  </>
}
