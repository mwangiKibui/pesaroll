import { Link } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { usePermissions } from '../app/permissions'
import { Button } from '../components/Button'
import { buttonClasses } from '../components/buttonStyles'
import { Navbar } from '../components/Navbar'
import { useMe, useSignOut } from './hooks'
import { setupPaths, setupSteps, stepPermission, type SetupStep } from './steps'

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
  const { can } = usePermissions()
  return (
    <div className="flex min-h-dvh flex-col bg-page">
      <Navbar>
        <div className="flex items-center gap-3">
          {/* Where you're signed in (AUTH-05); the app shell's person menu links there too. */}
          {me.data && <Link to="/account/devices" className="hidden text-label text-muted no-underline hover:text-ink md:inline">{me.data.fullName}</Link>}
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
              // An optional step counts as done once you've gone past it (it can be skipped).
              const isDone = done.includes(step.key) || (step.optional && i < setupSteps.findIndex(s => s.key === current))
              const isCurrent = step.key === current
              // Steps with a page are links, done or not, so you can go back and change things; the
              // others need the organization first, and each needs the View permission of its area.
              const path = setupPaths[step.key]
              const needs = stepPermission[step.key]
              const linked = path && (hasOrganization || step.key === 'organization') && (!needs || can(needs))
              const content = <>
                <span aria-hidden="true" className={`flex size-6 flex-none items-center justify-center rounded-full text-tag font-semibold ${isDone ? 'bg-brand text-on-brand' : isCurrent ? 'border-2 border-brand text-brand' : 'border border-field-border text-muted'}`}>
                  {isDone ? '✓' : i + 1}
                </span>
                <span className={isCurrent ? 'font-semibold text-ink' : 'text-ink'}>{t(`setup.steps.${step.key}`)}</span>
                {step.optional && <span className="text-hint text-muted">{t('setup.optional')}</span>}
              </>
              const row = `flex min-h-11 items-center gap-2.5 rounded-control px-2 text-label ${isCurrent ? 'bg-brand-soft' : ''}`
              return (
                <li key={step.key} className={linked ? '' : row}>
                  {linked
                    // The router marks the link to the page you're on (aria-current="page").
                    ? <Link to={path} className={`${row} no-underline hover:bg-raised`}>{content}</Link>
                    : content}
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
