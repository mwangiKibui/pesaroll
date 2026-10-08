import { Link } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '../components/Button'
import { buttonClasses } from '../components/buttonStyles'
import { Navbar } from '../components/Navbar'
import { useMe, useSignOut } from './hooks'
import { setupSteps, type SetupStep } from './steps'

/**
 * The setup steps (ONB), full width: the navbar (who is signed in, Sign out, and once the
 * organization exists "Proceed to Org Dashboard"), the steps as a left sidebar with the current one
 * marked, and the step's content filling the rest. Phones show the content only.
 */
export function SetupLayout({ current, done, hasOrganization, children }: {
  current: SetupStep
  done: SetupStep[]
  hasOrganization: boolean
  children: ReactNode
}) {
  const { t } = useTranslation()
  const me = useMe()
  const signOut = useSignOut()
  return (
    <div className="flex min-h-dvh flex-col bg-page">
      <Navbar>
        <div className="flex items-center gap-3">
          {me.data && <span className="hidden text-label text-muted md:inline">{me.data.fullName}</span>}
          <Button variant="secondary" onClick={signOut}>{t('nav.signOut')}</Button>
          {hasOrganization && (
            <Link to="/" className={buttonClasses('primary')}>
              {t('organization.proceed')}
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M5 12h14M13 6l6 6-6 6" /></svg>
            </Link>
          )}
        </div>
      </Navbar>
      <div className="flex flex-1">
        <aside aria-label={t('setup.stepsLabel')} className="hidden w-72 flex-none border-r border-line bg-card px-5 py-6 lg:block">
          <ol aria-label={t('setup.stepsLabel')} className="sticky top-22 m-0 flex list-none flex-col gap-1 p-0">
            {setupSteps.map((step, i) => {
              const isDone = done.includes(step.key)
              const isCurrent = step.key === current
              return (
                <li key={step.key} aria-current={isCurrent ? 'step' : undefined}
                  className={`flex min-h-11 items-center gap-2.5 rounded-control px-2 text-label ${isCurrent ? 'bg-brand-soft' : ''}`}>
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
        <main className="flex min-w-0 flex-1 flex-col gap-5 px-4 py-6 md:px-10 md:py-8">{children}</main>
      </div>
    </div>
  )
}
