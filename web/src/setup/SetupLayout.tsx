import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '../components/Button'
import { Navbar } from '../components/Navbar'
import { useMe, useSignOut } from './hooks'
import { setupSteps, type SetupStep } from './steps'

/**
 * The setup steps (ONB): the navbar with who is signed in and Sign out, the list of steps on the
 * left with the current one marked, and the step's form on the right. Phones show the form only.
 */
export function SetupLayout({ current, done, children }: { current: SetupStep; done: SetupStep[]; children: ReactNode }) {
  const { t } = useTranslation()
  const me = useMe()
  const signOut = useSignOut()
  return (
    <div className="flex min-h-dvh flex-col bg-page">
      <Navbar>
        <div className="flex items-center gap-3">
          {me.data && <span className="hidden text-label text-muted sm:inline">{me.data.fullName}</span>}
          <Button variant="secondary" onClick={signOut}>{t('nav.signOut')}</Button>
        </div>
      </Navbar>
      <div className="mx-auto flex w-full max-w-6xl flex-1 gap-8 px-4 py-6 md:px-8 md:py-8">
        <aside className="hidden w-64 flex-none self-start rounded-card border border-line bg-card px-5 py-4 lg:block">
          <ol aria-label={t('setup.stepsLabel')} className="m-0 flex list-none flex-col gap-1 p-0">
            {setupSteps.map((step, i) => {
              const isDone = done.includes(step.key)
              const isCurrent = step.key === current
              return (
                <li key={step.key} aria-current={isCurrent ? 'step' : undefined} className="flex min-h-9 items-center gap-2.5 text-label">
                  <span aria-hidden="true" className={`flex size-6 flex-none items-center justify-center rounded-full text-tag font-semibold ${isDone ? 'bg-brand text-on-brand' : isCurrent ? 'border-2 border-brand text-brand' : 'border border-field-border text-muted'}`}>
                    {isDone ? '✓' : i + 1}
                  </span>
                  <span className={isCurrent ? 'font-semibold text-ink' : 'text-ink'}>{t(`setup.steps.${step.key}`)}</span>
                  {step.optional && <span className="text-hint text-muted">{t('setup.optional')}</span>}
                </li>
              )
            })}
          </ol>
        </aside>
        <main className="flex min-w-0 max-w-xl flex-1 flex-col gap-5">{children}</main>
      </div>
    </div>
  )
}
