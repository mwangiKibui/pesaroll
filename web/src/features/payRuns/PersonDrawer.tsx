import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { Drawer } from '../../components/Drawer'
import { Money } from '../../components/Money'
import { knownNotes, lineDetails } from './lines'

type Person = components['schemas']['PayRunPersonResponse']

const kinds = [['earning', 'earnings'], ['deduction', 'deductions'], ['employer_cost', 'employerCosts']] as const

/**
 * One person's pay in a run (OP-18): each line with how it was worked out, gross and net pay, what to
 * check, and for a correction what was approved before. Amounts are the API's, never added up here.
 */
export function PersonDrawer({ person, onClose }: { person: Person; onClose: () => void }) {
  const { t } = useTranslation()
  const notes = knownNotes(person.notes)
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
                    <Money amount={l.amount} className="flex-none text-body text-ink" />
                  </li>
                ))}
              </ul>
            </section>
          )
        })}
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
    </Drawer>
  )
}
