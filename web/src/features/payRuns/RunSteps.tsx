import { useTranslation } from 'react-i18next'

const steps = ['draft', 'awaiting_approval', 'approved', 'paid'] as const

/** Where a run is: Draft (worked out or not) → Awaiting approval → Approved → Paid (PAY-05). */
const stepOf = (status: string) => (status === 'awaiting_approval' ? 1 : status === 'approved' ? 2 : status === 'paid' ? 3 : 0)

/** A pay run's steps, numbered and joined by a line: done ones filled, the current one ringed. */
export function RunSteps({ status }: { status: string }) {
  const { t } = useTranslation()
  const at = stepOf(status)
  return (
    <ol aria-label={t('payRun.steps')} className="m-0 flex list-none flex-wrap items-center gap-y-2 p-0">
      {steps.map((step, i) => (
        <li key={step} aria-current={i === at ? 'step' : undefined}
          className={`flex items-center gap-2 text-label ${i === at ? 'font-semibold text-ink' : 'text-muted'}`}>
          {i > 0 && <span aria-hidden="true" className={`mx-2.5 h-0.5 w-6 sm:w-10 ${i <= at ? 'bg-brand' : 'bg-line'}`} />}
          <span aria-hidden={false} className={`flex size-6.5 items-center justify-center rounded-full border-[1.5px] text-tag font-semibold ${
            i < at ? 'border-brand bg-brand text-on-brand' : i === at ? 'border-brand bg-card text-brand ring-3 ring-brand-soft' : 'border-field-border bg-card text-muted'}`}>
            {i + 1}
          </span>
          {t(`payRuns.statuses.${step}`)}
        </li>
      ))}
    </ol>
  )
}
