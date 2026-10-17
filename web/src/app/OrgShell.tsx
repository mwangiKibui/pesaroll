import { useRouter } from '@tanstack/react-router'
import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { SearchBox } from '../components/SearchBox'
import { SelectField } from '../components/fields'
import { AppShell } from '../components/shells'
import { useCompanies, useMe, useOrganization, useSignOut } from '../setup/hooks'
import { chooseCompany, CurrentCompanyContext, rememberCompany, rememberedCompany } from './company'
import { featureOn } from './features'
import { navigation, searchPages, visibleNavigation } from './navigation'
import { permissions, usePermissions } from './permissions'

/**
 * The organization portal's frame (OP-01) around a page: the shell with the person's sections (by
 * permission and flag), the company switcher (DASH-03), search and the person menu. Pages inside
 * read the chosen company with `useCurrentCompany()`.
 */
export function OrgShell({ current, children }: { current: string; children: ReactNode }) {
  const { t } = useTranslation()
  const router = useRouter()
  const me = useMe()
  const organization = useOrganization()
  const companies = useCompanies()
  const { can } = usePermissions()
  const signOut = useSignOut()
  const [chosen, setChosen] = useState<string | null>(null)

  const companyId = chooseCompany(companies.data ?? [], chosen ?? (me.data ? rememberedCompany(me.data.id) : null))
  const setCompanyId = (id: string) => {
    setChosen(id)
    if (me.data) rememberCompany(me.data.id, id)
  }
  const go = (href: string) => { void router.history.push(href) }

  const sections = visibleNavigation(can, featureOn, companyId ?? undefined).map(s => ({
    key: s.key,
    label: s.key === 'main' ? undefined : t(`nav.sections.${s.key}`),
    links: s.links.map(l => ({ key: l.key, label: t(`nav.links.${l.key}`), href: l.href })),
  }))
  const sectionOf = (key: string) => navigation.find(s => s.links.some(l => l.key === key))?.key ?? 'main'
  const search = (query: string) => searchPages(query, can, featureOn, companyId ?? undefined).map(l => ({
    key: l.key, label: t(`nav.links.${l.key}`), href: l.href, group: t(`nav.sections.${sectionOf(l.key)}`),
  }))

  // Back to company setup (the build steps), always one click away for people who set companies up.
  const setupHref = navigation.flatMap(s => s.links).find(l => l.key === 'setup')?.href ?? '/setup/organization'
  const topBar = <>
    {(companies.data?.length ?? 0) > 0 && (
      <div className="w-40 flex-none sm:w-56">
        <SelectField label={t('nav.company')} hideLabel size="sm" placeholder={t('nav.companyPlaceholder')} value={companyId ?? ''}
          options={(companies.data ?? []).map(c => ({ value: c.id, label: c.legalName }))} onChange={setCompanyId} />
      </div>
    )}
    <div className="hidden min-w-0 flex-1 sm:block">
      <SearchBox label={t('nav.search')} placeholder={t('nav.searchPlaceholder')} empty={t('nav.searchNone')} search={search} onPick={go} />
    </div>
    {can(permissions.companies.manage) && (
      <a href={setupHref} onClick={e => { e.preventDefault(); go(setupHref) }} aria-label={t('nav.links.setup')}
        className="flex min-h-10 flex-none items-center gap-2 rounded-control border border-line px-3 text-label font-medium text-ink no-underline hover:bg-raised">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" /></svg>
        <span className="hidden sm:inline">{t('nav.links.setup')}</span>
      </a>
    )}
  </>

  const person = { name: me.data?.fullName ?? '', detail: organization.data?.name }
  const personMenu = (close: () => void) => (
    <>
      <div className="flex items-center gap-2.5 p-2">
        <span className="flex min-w-0 flex-col">
          <span className="truncate text-label font-semibold">{me.data?.fullName}</span>
          <span className="truncate text-tag text-muted">{me.data?.email ?? me.data?.phone}</span>
        </span>
      </div>
      <div className="flex flex-col gap-0.5 border-t border-line-soft pt-1.5">
        <span className="px-2 py-1 text-tag uppercase tracking-wider text-muted">{t('nav.useAs')}</span>
        <div aria-current="true" className="flex min-h-11 items-center gap-2.5 rounded-control bg-brand-soft px-2 text-label">
          <span className="flex min-w-0 flex-1 flex-col">
            <span className="font-medium">{t('nav.organizationPortal')}</span>
            <span className="truncate text-tag text-muted">{organization.data?.name}</span>
          </span>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" className="text-brand"><path d="M5 12l5 5 9-10" /></svg>
        </div>
        {featureOn('employeePortal') && (
          <a href="/me" onClick={e => { e.preventDefault(); close(); go('/me') }} className="flex min-h-11 flex-col justify-center rounded-control px-2 text-label text-ink no-underline hover:bg-raised">
            <span className="font-medium">{t('nav.employeePortal')}</span>
            <span className="text-tag text-muted">{t('nav.employeePortalHint')}</span>
          </a>
        )}
      </div>
      <div className="flex flex-col gap-0.5 border-t border-line-soft pt-1.5">
        <a href="/account/devices" onClick={e => { e.preventDefault(); close(); go('/account/devices') }}
          className="flex min-h-11 items-center rounded-control px-2 text-label font-medium text-ink no-underline hover:bg-raised">{t('nav.devices')}</a>
        <button type="button" onClick={() => { close(); void signOut() }}
          className="flex min-h-11 items-center rounded-control px-2 text-left text-label font-medium text-ink hover:bg-raised">{t('nav.signOut')}</button>
      </div>
    </>
  )

  return (
    <CurrentCompanyContext.Provider value={{ companyId, setCompanyId }}>
      <AppShell sections={sections} current={current} organization={organization.data?.name ?? ''} topBar={topBar}
        person={person} personMenu={personMenu} onNavigate={go}>
        {children}
      </AppShell>
    </CurrentCompanyContext.Provider>
  )
}
