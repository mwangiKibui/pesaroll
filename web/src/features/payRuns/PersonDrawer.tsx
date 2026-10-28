import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { useServices } from '../../app/services'
import { Button, IconButton } from '../../components/Button'
import { Drawer } from '../../components/Drawer'
import { PlusIcon } from '../../components/icons'
import { Money } from '../../components/Money'
import { knownNotes, lineDetails } from './lines'
import { addable, targetOf } from './review'
import { AddItem, ChangeLine, RunDeductions } from './ReviewForms'

type Person = components['schemas']['PayRunPersonResponse']
type Line = Person['lines'][number]

const kinds = [['earning', 'earnings'], ['deduction', 'deductions'], ['employer_cost', 'employerCosts']] as const

/**
 * One person's pay in a run (OP-18): each line with how it was worked out, gross and net pay, what to
 * check, and for a correction what was approved before. Amounts are the API's, never added up here.
 * While the run can change, Payroll changes lines, adds items and chooses statutory deductions for
 * this run alone, each saying why (PAY-04); the figures show it once the run is worked out again.
 */
export function PersonDrawer({ person, run, onClose }: { person: Person; run: { id: string; status: string; canManage: boolean }; onClose: () => void }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const notes = knownNotes(person.notes)
  const canEdit = run.canManage && ['draft', 'calculated'].includes(run.status)
  const [changing, setChanging] = useState<{ line: Line; payItemId: string | null } | null>(null)
  const [adding, setAdding] = useState(false)
  const inputs = useQuery({
    queryKey: ['pay-run-inputs', run.id],
    enabled: canEdit,
    queryFn: async () => {
      const { data } = await api.GET('/api/pay-runs/{id}/inputs', { params: { path: { id: run.id } } })
      if (!data) throw new Error('pay-run-inputs')
      return data
    },
  })
  const editing = canEdit ? inputs.data : undefined
  return (
    <Drawer open title={person.name} onClose={onClose}>
      <div className="flex flex-col gap-5">
        <p className="m-0 font-mono text-hint text-muted">{person.staffNo}</p>
        {notes.length > 0 && (
          <section className="flex flex-col gap-1">
            <h3 id="person-check" className="m-0 text-label font-medium text-ink">{t('payRun.toCheck')}</h3>
            <ul aria-labelledby="person-check" className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-body text-ink">
              {notes.map(n => <li key={n}>{t(`payRun.notes.${n}.long`)}</li>)}
            </ul>
          </section>
        )}
        {kinds.map(([kind, key]) => {
          const lines = person.lines.filter(l => l.kind === kind)
          if (lines.length === 0) return null
          return (
            <section key={kind} className="flex flex-col gap-1">
              <h3 className="m-0 text-label font-medium text-ink">{t(`payRun.${key}`)}</h3>
              <ul aria-label={t(`payRun.${key}`)} className="m-0 flex list-none flex-col divide-y divide-line-soft p-0">
                {lines.map((l, i) => (
                  <li key={`${l.code}-${i}`} className="flex items-start justify-between gap-4 py-2">
                    <span className="flex min-w-0 flex-col">
                      <span className="text-body text-ink">{l.name}</span>
                      {lineDetails(l).map(d => <span key={d} className="text-hint text-muted">{d}</span>)}
                    </span>
                    <span className="flex flex-none items-center gap-1">
                      <Money amount={l.amount} className="text-body text-ink" />
                      {editing && targetOf(l, editing) !== undefined && (
                        <IconButton label={t('payRun.review.change', { name: l.name })} onClick={() => setChanging({ line: l, payItemId: targetOf(l, editing)! })}>
                          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                        </IconButton>
                      )}
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          )
        })}
        {editing && addable(editing, person.lines).length > 0 && (
          <div><Button variant="secondary" onClick={() => setAdding(true)}><PlusIcon />{t('payRun.review.add')}</Button></div>
        )}
        {editing && <RunDeductions runId={run.id} employeeId={person.employeeId} name={person.name} inputs={editing} canEdit={canEdit} />}
        <dl className="m-0 flex flex-col gap-1 border-t border-line pt-3 text-body">
          <div className="flex justify-between gap-4"><dt className="text-muted">{t('payRuns.grossPay')}</dt><dd className="m-0"><Money amount={person.grossPay} /></dd></div>
          <div className="flex justify-between gap-4"><dt className="text-muted">{t('payRun.totalDeductions')}</dt><dd className="m-0"><Money amount={person.totalDeductions} /></dd></div>
          <div className="flex justify-between gap-4 font-semibold"><dt>{t('payRuns.netPay')}</dt><dd className="m-0"><Money amount={person.netPay} /></dd></div>
          {person.approvedBefore && <>
            <div className="flex justify-between gap-4"><dt className="text-muted">{t('payRun.approvedBefore')}</dt><dd className="m-0"><Money amount={person.approvedBefore.netPay} /></dd></div>
            <div className="flex justify-between gap-4"><dt className="text-muted">{t('payRun.netChange')}</dt><dd className="m-0"><Money amount={person.approvedBefore.netPayChange} /></dd></div>
          </>}
        </dl>
      </div>
      {changing && editing && <ChangeLine runId={run.id} employeeId={person.employeeId} inputs={editing} payItemId={changing.payItemId}
        name={changing.line.name} amount={changing.line.amount} onClose={() => setChanging(null)} />}
      {adding && editing && <AddItem runId={run.id} employeeId={person.employeeId} items={addable(editing, person.lines)} onClose={() => setAdding(false)} />}
    </Drawer>
  )
}
