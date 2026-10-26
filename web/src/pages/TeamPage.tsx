import { useQuery } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useId, useState, type ReactNode } from 'react'
import { Controller, useForm, useWatch, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { permissions, usePermissions } from '../app/permissions'
import { useServices, type ApiError } from '../app/services'
import { Button, IconButton } from '../components/Button'
import { CompanySelect, type CompanySelection } from '../components/CompanySelect'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { Drawer } from '../components/Drawer'
import { EmptyState, FormErrorBanner, Loading, Notice, Tag } from '../components/feedback'
import { SelectField, TextField } from '../components/fields'
import { PlusIcon } from '../components/icons'
import { Modal } from '../components/Modal'
import { Pagination } from '../components/Pagination'
import { TableCard } from '../components/TableCard'
import { TabPanel, Tabs } from '../components/Tabs'
import { useToast } from '../components/toast'
import { usePaged } from '../components/usePaged'
import { useCompanies, useSetupDone } from '../setup/hooks'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

type Company = components['schemas']['CompanyResponse']
type TeamGroups = components['schemas']['TeamGroups']
type TeamGroup = components['schemas']['TeamGroup']
type Member = components['schemas']['TeamMember']
type Invitation = components['schemas']['InvitationRow']

const groups = ['administrators', 'hr', 'payroll', 'approvers'] as const

/** A row of the table: someone in, or someone invited. */
type Row =
  | { kind: 'member'; key: string; name: string; contact: string; group: string | null; groupName: string; allCompanies: boolean; companyIds: string[]; member: Member }
  | { kind: 'invitation'; key: string; name: string; contact: string; group: string | null; groupName: string; allCompanies: boolean; companyIds: string[]; invitation: Invitation }

const trash = <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
const pencil = <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
const eye = <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></svg>
const resend = <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M21 12a9 9 0 1 1-3-6.7L21 8M21 3v5h-5" /></svg>

/** "1 Oct 2026". */
const shortDate = (iso: string) => new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'Africa/Nairobi' }).format(new Date(iso))

/**
 * ONB-11, the optional Team access step: who else uses Pesaroll, with their group (what they may
 * do) and companies. Administrators invite people by email or SMS (a drawer), resend or cancel
 * invitations, change someone's access (a small form on top of their drawer) or remove them. The
 * organization always keeps an administrator (the server refuses otherwise).
 */
/** The organization's groups and what each can do, per area (ACC-04). */
function useTeamGroups(enabled = true) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['team', 'groups'],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/team/groups')
      if (error || !data) throw new Error('groups')
      return data
    },
  })
}

/** "Manage: employees, locations · View: payroll schedules", from a group's levels per area. */
function useDescribe() {
  const { t, i18n } = useTranslation()
  return (groups: TeamGroups, group: TeamGroup) => {
    const name = (area: string) => {
      const found = groups.areas.find(a => a.key === area)?.name
      return (found ? (i18n.language === 'sw' ? found.sw : found.en) : area).toLowerCase()
    }
    const at = (level: string) => group.areas.filter(a => a.level === level).map(a => name(a.area))
    const manage = at('manage'), view = at('view')
    // Everything: the most of every area (Manage, or View where that is all an area has, like the audit log).
    const most = (area: string) => groups.areas.find(a => a.key === area)?.levels.at(-1) ?? 'manage'
    if (group.areas.every(a => a.level === most(a.area))) return t('team.access.everything')
    const parts = [manage.length ? t('team.access.manage', { areas: manage.join(', ') }) : null, view.length ? t('team.access.view', { areas: view.join(', ') }) : null]
    return parts.filter(Boolean).join(' · ') || t('team.access.nothing')
  }
}

export default function TeamPage() {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const navigate = useNavigate()
  const companies = useCompanies()
  const done = useSetupDone(true)
  const [shown, setShown] = useState<CompanySelection>({ all: true, ids: [] })
  const [inviting, setInviting] = useState(false)
  const [viewing, setViewing] = useState<Row | null>(null)
  const [changing, setChanging] = useState<Changing | null>(null)
  const [confirming, setConfirming] = useState<Row | null>(null)
  const [busy, setBusy] = useState(false)
  const [tab, setTab] = useState<'people' | 'groups'>('people')
  const [editingGroup, setEditingGroup] = useState<TeamGroup | 'new' | null>(null)
  const [deletingGroup, setDeletingGroup] = useState<TeamGroup | null>(null)
  const tabsId = useId()
  // View on team access shows everything here; only Manage changes it. The server checks every change.
  const canManage = usePermissions().can(permissions.team.manage)
  const describe = useDescribe()

  const members = useQuery({
    queryKey: ['team', 'members'],
    retry: false,
    queryFn: async (): Promise<Member[] | ApiError> => {
      const { data, error } = await api.GET('/api/team/members')
      if (data) return data
      if (isApiError(error)) return error
      throw new Error('members')
    },
  })
  const invitations = useQuery({
    queryKey: ['team', 'invitations'],
    enabled: !!members.data && !isApiError(members.data),
    queryFn: async () => {
      const { data, error } = await api.GET('/api/team/invitations')
      if (error || !data) throw new Error('invitations')
      return data
    },
  })

  const teamGroups = useTeamGroups(!!members.data && !isApiError(members.data))

  // Standard groups by their translated name; the organization's own (ACC-08) by the name it gave them.
  const groupLabel = (group: string | null | undefined, name: string) =>
    !group ? name : (groups as readonly string[]).includes(group) ? t(`team.groups.${group}`) : teamGroups.data?.items.find(g => g.key === group)?.name ?? name
  const companyName = (id: string) => companies.data?.find(c => c.id === id)?.legalName ?? ''
  const companiesText = (all: boolean, ids: string[]) => (all ? t('team.allCompanies') : ids.map(companyName).join(', '))

  const rows: Row[] = [
    ...(Array.isArray(members.data) ? members.data : []).map((m): Row => {
      const access = m.groups[0]
      return {
        kind: 'member', key: m.membershipId, name: `${m.firstName} ${m.lastName}`, contact: m.email ?? m.phone ?? '',
        group: access?.group ?? null, groupName: m.groups.map(g => groupLabel(g.group, g.name)).join(', ') || t('team.noGroup'),
        allCompanies: m.groups.some(g => g.allCompanies), companyIds: [...new Set(m.groups.flatMap(g => g.companyIds))], member: m,
      }
    }),
    ...(invitations.data ?? []).map((i): Row => ({
      kind: 'invitation', key: i.id, name: `${i.firstName} ${i.lastName}`, contact: i.email ?? i.phone ?? '', group: i.groups[0] ?? null,
      groupName: i.groups.map(g => groupLabel(g, g)).join(', '), allCompanies: i.allCompanies, companyIds: i.companyIds, invitation: i,
    })),
  ]
  // The company selector on Groups (ACC-08): the groups for the companies shown, and their people there.
  const covers = (all: boolean, ids: string[]) => shown.all || all || ids.some(id => shown.ids.includes(id))
  const shownGroups = (teamGroups.data?.items ?? []).filter(g => covers(g.allCompanies, g.companyIds))
  const peopleIn = (key: string) => (Array.isArray(members.data) ? members.data : [])
    .filter(m => m.groups.some(a => a.group === key && covers(a.allCompanies, a.companyIds))).length
  // The company selector: people and invitations covering any company shown.
  const visible = rows.filter(r => shown.all || r.allCompanies || r.companyIds.some(id => shown.ids.includes(id)))
  const paged = usePaged(visible, 20)

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['team'] })
  const act = async (work: () => Promise<{ error?: unknown; response: Response }>, success: string) => {
    setBusy(true)
    try {
      const { error, response } = await work()
      if (response.ok) {
        void refresh()
        toast.show(success)
        return true
      }
      toast.show(isApiError(error) ? errorMessage(t, error) : t('apiErrors.network'), 'danger')
    } catch {
      toast.show(t('apiErrors.network'), 'danger')
    } finally {
      setBusy(false)
    }
    return false
  }
  const resendTo = (i: Invitation, name: string) =>
    act(() => api.POST('/api/team/invitations/{id}/resend', { params: { path: { id: i.id } } }), t('team.resent', { name }))
  const confirm = async () => {
    if (!confirming) return
    const ok = confirming.kind === 'invitation'
      ? await act(() => api.DELETE('/api/team/invitations/{id}', { params: { path: { id: confirming.invitation.id } } }), t('team.cancelled', { name: confirming.name }))
      : await act(() => api.DELETE('/api/team/members/{id}', { params: { path: { id: confirming.member.membershipId } } }), t('team.removed', { name: confirming.name }))
    if (ok && viewing?.key === confirming.key) setViewing(null)
    setConfirming(null)
  }

  const status = (r: Row) => (r.kind === 'member' ? <Tag tone="brand">{t('team.statuses.active')}</Tag>
    : r.invitation.status === 'expired' ? <Tag tone="warning">{t('team.statuses.expired')}</Tag> : <Tag>{t('team.statuses.invited')}</Tag>)
  const actions = (r: Row) => (
    <div className="flex justify-end">
      <IconButton label={t('team.view', { name: r.name })} onClick={() => setViewing(r)}>{eye}</IconButton>
      {canManage && <IconButton label={t(r.kind === 'member' ? 'team.changeTitle' : 'team.changeInvitationTitle', { name: r.name })}
        onClick={() => setChanging(r.kind === 'member' ? { kind: 'member', member: r.member } : { kind: 'invitation', invitation: r.invitation })}>{pencil}</IconButton>}
      {canManage && r.kind === 'invitation' && <>
        <IconButton label={t('team.resend', { name: r.name })} onClick={() => void resendTo(r.invitation, r.name)}>{resend}</IconButton>
        <IconButton tone="danger" label={t('team.cancel', { name: r.name })} onClick={() => setConfirming(r)}>{trash}</IconButton>
      </>}
      {canManage && r.kind === 'member' && !r.member.isYou && <IconButton tone="danger" label={t('team.remove', { name: r.name })} onClick={() => setConfirming(r)}>{trash}</IconButton>}
    </div>
  )

  const forbidden = members.data && isApiError(members.data)
  const ready = companies.data && members.data && (forbidden || (invitations.data && teamGroups.data))
  return (
    <SetupLayout current="team" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('team.title')}</h1>
        <p className="m-0 text-muted">{t('team.intro')}</p>
      </div>
      {!ready ? <Loading rows={4} /> : forbidden || !teamGroups.data ? <Notice>{t('team.onlyAdministrators')}</Notice> : <>
        {companies.data.length > 1 && (
          <div className="w-full max-w-sm">
            <CompanySelect label={t('team.show')} value={shown} onChange={setShown}
              companies={companies.data.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
          </div>
        )}
        <Tabs id={tabsId} label={t('team.tabsLabel')} current={tab} onChange={key => setTab(key as 'people' | 'groups')}
          tabs={[{ key: 'people', label: t('team.tabs.people') }, { key: 'groups', label: t('team.tabs.groups') }]} />
        <TabPanel id={tabsId} tab="people" current={tab}>
        <TableCard title={t('team.tableTitle')} count={visible.length}
          actions={canManage && <Button onClick={() => setInviting(true)}><PlusIcon />{t('team.invite')}</Button>}
          footer={<Pagination label={t('pagination.of', { what: t('team.tableTitle') })} {...paged.pagination} />}>
          {visible.length === 0 ? <EmptyState title={t('team.emptyTitle')}>{t('team.emptyText')}</EmptyState> : (
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('team.name')}</th>
                  <th scope="col">{t('team.group')}</th>
                  <th scope="col">{t('team.companies')}</th>
                  <th scope="col">{t('team.status')}</th>
                  <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                </tr>
              </thead>
              <tbody>
                {paged.items.map(r => (
                  <tr key={r.key}>
                    <td>
                      <span className="block font-medium text-ink">{r.name}{r.kind === 'member' && r.member.isYou && <span className="font-normal text-muted"> {t('team.you')}</span>}</span>
                      <span className="block text-hint text-muted">{r.contact}</span>
                    </td>
                    <td>{r.groupName}</td>
                    <td>{companiesText(r.allCompanies, r.companyIds)}</td>
                    <td>{status(r)}</td>
                    <td className="whitespace-nowrap text-right">{actions(r)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </TableCard>
        </TabPanel>
        <TabPanel id={tabsId} tab="groups" current={tab}>
          <TableCard title={t('team.groupsTitle')} count={shownGroups.length}
            actions={canManage && <Button onClick={() => setEditingGroup('new')}><PlusIcon />{t('team.addGroup')}</Button>}>
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('team.group')}</th>
                  <th scope="col">{t('team.canDo')}</th>
                  <th scope="col">{t('team.companies')}</th>
                  <th scope="col">{t('team.people')}</th>
                  {canManage && <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>}
                </tr>
              </thead>
              <tbody>
                {shownGroups.map(g => (
                  <tr key={g.key}>
                    <td>
                      <span className="block font-medium text-ink">{g.name}</span>
                      {g.custom
                        ? g.description && <span className="block text-hint text-muted">{g.description}</span>
                        : !g.standard && <span className="block text-hint text-muted">{t('team.changedHere')}</span>}
                    </td>
                    <td>{describe(teamGroups.data, g)}</td>
                    <td>{companiesText(g.allCompanies, g.companyIds)}</td>
                    <td className="tabular-nums">{peopleIn(g.key)}</td>
                    {canManage && <td className="whitespace-nowrap text-right">
                      <IconButton label={t('team.changeGroup', { name: g.name })} onClick={() => setEditingGroup(g)}>{pencil}</IconButton>
                      {g.custom && <IconButton tone="danger" label={t('team.deleteGroup', { name: g.name })} onClick={() => setDeletingGroup(g)}>{trash}</IconButton>}
                    </td>}
                  </tr>
                ))}
              </tbody>
            </table>
          </TableCard>
        </TabPanel>
        {/* The last setup step: on to the dashboard. */}
        <Proceed label={t('organization.proceed')} onProceed={() => void navigate({ to: '/' })} />

        {inviting && <InviteDrawer companies={companies.data} onClose={() => setInviting(false)} onSent={() => void refresh()} />}
        {viewing && (
          <Drawer open title={viewing.name} onClose={() => setViewing(null)}
            footer={<>
              <Button variant="secondary" className="mr-auto" onClick={() => setViewing(null)}>{t('common.close')}</Button>
              {canManage && viewing.kind === 'invitation' && <>
                <Button variant="secondary" onClick={() => setConfirming(viewing)}>{t('team.cancelInvitation')}</Button>
                <Button variant="secondary" onClick={() => setChanging({ kind: 'invitation', invitation: viewing.invitation })}>{t('team.changeInvitation')}</Button>
                <Button busy={busy} onClick={() => void resendTo(viewing.invitation, viewing.name)}>{t('team.resendInvitation')}</Button>
              </>}
              {canManage && viewing.kind === 'member' && <>
                {!viewing.member.isYou && <Button variant="secondary" onClick={() => setConfirming(viewing)}>{t('team.removeButton')}</Button>}
                <Button onClick={() => setChanging({ kind: 'member', member: viewing.member })}>{t('team.changeAccess')}</Button>
              </>}
            </>}>
            <Details row={viewing} companiesText={companiesText} />
          </Drawer>
        )}
        {editingGroup && <GroupAccessDrawer groups={teamGroups.data} group={editingGroup === 'new' ? null : editingGroup} companies={companies.data} onClose={() => setEditingGroup(null)} />}
        <ConfirmDialog open={!!deletingGroup} busy={busy} title={t('team.deleteGroupTitle', { name: deletingGroup?.name })} confirmLabel={t('team.deleteGroupButton')}
          onCancel={() => setDeletingGroup(null)}
          onConfirm={() => void (async () => {
            if (!deletingGroup) return
            await act(() => api.DELETE('/api/team/groups/{key}', { params: { path: { key: deletingGroup.key } } }), t('team.groupDeleted', { name: deletingGroup.name }))
            setDeletingGroup(null)
          })()}>
          {t('team.deleteGroupText')}
        </ConfirmDialog>
        {changing && <AccessModal target={changing} companies={companies.data} onClose={() => setChanging(null)}
          onSaved={() => { setChanging(null); setViewing(null); void refresh() }} />}
      </>}
      <ConfirmDialog open={!!confirming} busy={busy} onConfirm={() => void confirm()} onCancel={() => setConfirming(null)}
        title={confirming?.kind === 'invitation' ? t('team.cancelTitle', { name: confirming?.name }) : t('team.removeTitle', { name: confirming?.name })}
        confirmLabel={confirming?.kind === 'invitation' ? t('team.cancelInvitation') : t('team.removeButton')}>
        {confirming?.kind === 'invitation' ? t('team.cancelText') : t('team.removeText')}
      </ConfirmDialog>
    </SetupLayout>
  )
}

/** A person or invitation to read: how to reach them, their access, and dates. */
function Details({ row, companiesText }: { row: Row; companiesText: (all: boolean, ids: string[]) => string }) {
  const { t } = useTranslation()
  const items: [string, ReactNode][] = row.kind === 'member'
    ? [
        [t('team.email'), row.member.email], [t('team.phone'), row.member.phone],
        [t('team.group'), row.member.groups.map(g => g.name).join(', ') || t('team.noGroup')],
        [t('team.companies'), companiesText(row.allCompanies, row.companyIds)], [t('team.joined'), shortDate(row.member.joinedAt)],
      ]
    : [
        [t('team.email'), row.invitation.email], [t('team.phone'), row.invitation.phone], [t('team.group'), row.groupName],
        [t('team.companies'), companiesText(row.allCompanies, row.companyIds)],
        [t('team.sentOn'), t('team.sentTimes', { date: shortDate(row.invitation.sentAt), count: Number(row.invitation.sendCount) })],
        [t(row.invitation.status === 'expired' ? 'team.expired' : 'team.expires'), shortDate(row.invitation.expiresAt)],
      ]
  return (
    <dl className="m-0 grid gap-x-6 gap-y-4 sm:grid-cols-2">
      {items.map(([label, value]) => (
        <div key={label} className="flex flex-col gap-0.5">
          <dt className="text-label text-muted">{label}</dt>
          <dd className="m-0 text-body text-ink">{value || <span className="text-muted">—</span>}</dd>
        </div>
      ))}
    </dl>
  )
}

/**
 * Groups: one or more, each with what it allows (HR and Payroll for one person is common in small
 * businesses). Someone in Payroll and Approvers could approve pay runs they prepared: say so.
 */
function GroupsField({ value, onChange, error }: { value: string[]; onChange: (value: string[]) => void; error?: string }) {
  const { t } = useTranslation()
  const id = useId()
  // What each group can do in this organization (ACC-04); the standard wording until it has loaded.
  const teamGroups = useTeamGroups()
  const describe = useDescribe()
  const hint = (g: string) => {
    const found = teamGroups.data?.items.find(x => x.key === g)
    return found && teamGroups.data ? describe(teamGroups.data, found) : t(`team.groupHints.${g}`)
  }
  // The standard groups, then the organization's own (ACC-08).
  const keys = teamGroups.data?.items.map(x => x.key) ?? [...groups]
  const label = (g: string) => ((groups as readonly string[]).includes(g) ? t(`team.groups.${g}`) : teamGroups.data?.items.find(x => x.key === g)?.name ?? g)
  const toggle = (g: string, on: boolean) => onChange(keys.filter(x => (x === g ? on : value.includes(x))))
  return (
    <fieldset aria-describedby={`${id}-hint`} className="m-0 flex flex-col gap-1 border-0 p-0">
      <legend className="mb-0.5 p-0 text-label font-medium text-ink">{t('team.groupsLabel')}</legend>
      <p id={`${id}-hint`} className="m-0 mb-1 text-hint text-muted">{t('team.groupsHint')}</p>
      {keys.map(g => (
        <label key={g} className="flex cursor-pointer items-start gap-2.5 py-1.5">
          <input type="checkbox" className="mt-0.5 size-5 flex-none accent-brand" checked={value.includes(g)} onChange={e => toggle(g, e.target.checked)} />
          <span className="flex flex-col">
            <span className="text-body text-ink">{label(g)}</span>
            <span className="text-hint text-muted">{hint(g)}</span>
          </span>
        </label>
      ))}
      {error && <p className="m-0 text-hint font-medium text-danger-ink">{error}</p>}
      {value.includes('payroll') && value.includes('approvers') && <Notice>{t('team.selfApproval')}</Notice>}
    </fieldset>
  )
}

interface InviteValues {
  firstName: string
  lastName: string
  email: string
  phone: string
  groups: string[]
  companies: CompanySelection
}

/** Inviting someone: their name, an email and/or phone (the message goes to each), the group and companies. */
function InviteDrawer({ companies, onClose, onSent }: { companies: Company[]; onClose: () => void; onSent: () => void }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const toast = useToast()
  const formId = useId()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const { register, handleSubmit, control, setError, formState: { errors, isSubmitting } } = useForm<InviteValues>({
    defaultValues: { firstName: '', lastName: '', email: '', phone: '', groups: [], companies: { all: true, ids: [] } },
  })

  const onSubmit = async (v: InviteValues) => {
    setBanner(null)
    let invalid = false
    const need = (field: FieldPath<InviteValues>, ok: boolean, message: string) => { if (!ok) { setError(field, { message }); invalid = true } }
    need('firstName', !!v.firstName.trim(), t('validation.firstName'))
    need('lastName', !!v.lastName.trim(), t('validation.lastName'))
    need('email', !!(v.email.trim() || v.phone.trim()), t('team.contactNeeded'))
    need('groups', v.groups.length > 0, t('team.groupNeeded'))
    if (invalid) return
    const name = `${v.firstName.trim()} ${v.lastName.trim()}`
    try {
      const { data, error } = await api.POST('/api/team/invitations', {
        body: {
          firstName: v.firstName.trim(), lastName: v.lastName.trim(), email: v.email.trim() || null, phone: v.phone.trim() || null, groups: v.groups,
          allCompanies: v.companies.all, companyIds: v.companies.all ? [] : v.companies.ids,
        },
      })
      if (data) {
        toast.show(t('team.sent', { name }))
        onSent()
        onClose()
        return
      }
      if (!isApiError(error)) return setBanner('network')
      const fields: Record<string, FieldPath<InviteValues>> = { firstName: 'firstName', lastName: 'lastName', contact: 'email', groups: 'groups', companyIds: 'companies' }
      const known = Object.entries(error.fields ?? {}).filter(([k]) => k in fields)
      for (const [k, message] of known) setError(fields[k], { message })
      if (known.length === 0) setBanner(error)
    } catch {
      setBanner('network')
    }
  }

  return (
    <Drawer open title={t('team.inviteTitle')} onClose={onClose}
      footer={<>
        <Button variant="secondary" className="mr-auto" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{t('team.send')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label={t('signUp.firstName')} placeholder={t('signUp.firstNamePlaceholder')} maxLength={60} error={errors.firstName?.message} {...register('firstName')} />
          <TextField label={t('signUp.lastName')} placeholder={t('signUp.lastNamePlaceholder')} maxLength={60} error={errors.lastName?.message} {...register('lastName')} />
        </div>
        <TextField type="email" label={t('team.email')} placeholder={t('team.emailPlaceholder')} hint={t('team.contactHint')} error={errors.email?.message} {...register('email')} />
        <TextField type="tel" label={t('team.phone')} placeholder={t('team.phonePlaceholder')} optional inputMode="tel" error={errors.phone?.message} {...register('phone')} />
        <Controller control={control} name="groups" render={({ field }) => <GroupsField value={field.value} onChange={field.onChange} error={errors.groups?.message} />} />
        {companies.length > 1 && (
          <Controller control={control} name="companies" render={({ field }) => (
            <div className="flex flex-col gap-1.5">
              <CompanySelect label={t('team.companies')} value={field.value} onChange={field.onChange}
                companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
              {errors.companies?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.companies.message}</p>}
            </div>
          )} />
        )}
      </form>
    </Drawer>
  )
}

/** Changing someone's group and companies: a small form on top of their drawer. */
/** What Change access works on: a member's groups and companies, or an open invitation's (ACC-04), which they get when they accept. */
type Changing = { kind: 'member'; member: Member } | { kind: 'invitation'; invitation: Invitation }

function AccessModal({ target, companies, onClose, onSaved }: { target: Changing; companies: Company[]; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const toast = useToast()
  const formId = useId()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const current = target.kind === 'member' ? target.member.groups[0] : target.invitation
  const { handleSubmit, control, setError, formState: { errors, isSubmitting } } = useForm<{ groups: string[]; companies: CompanySelection }>({
    defaultValues: {
      groups: target.kind === 'member' ? target.member.groups.map(g => g.group).filter((g): g is string => !!g) : [...target.invitation.groups],
      companies: !current || current.allCompanies ? { all: true, ids: [] } : { all: false, ids: current.companyIds },
    },
  })
  useWatch({ control, name: 'groups' })
  const person = target.kind === 'member' ? target.member : target.invitation
  const name = `${person.firstName} ${person.lastName}`

  const onSubmit = async (v: { groups: string[]; companies: CompanySelection }) => {
    setBanner(null)
    if (!v.groups.length) return setError('groups', { message: t('team.groupNeeded') })
    try {
      const body = { groups: v.groups, allCompanies: v.companies.all, companyIds: v.companies.all ? [] : v.companies.ids }
      const { error, response } = target.kind === 'member'
        ? await api.PUT('/api/team/members/{id}/access', { params: { path: { id: target.member.membershipId } }, body })
        : await api.PUT('/api/team/invitations/{id}/access', { params: { path: { id: target.invitation.id } }, body })
      if (response.ok) {
        toast.show(t(target.kind === 'member' ? 'team.updated' : 'team.invitationUpdated', { name }))
        onSaved()
        return
      }
      setBanner(isApiError(error) ? error : 'network')
    } catch {
      setBanner('network')
    }
  }

  return (
    <Modal open title={t(target.kind === 'member' ? 'team.changeTitle' : 'team.changeInvitationTitle', { name })} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{t('common.save')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        {target.kind === 'invitation' && <Notice>{t('team.linkStays')}</Notice>}
        <Controller control={control} name="groups" render={({ field }) => <GroupsField value={field.value} onChange={field.onChange} error={errors.groups?.message} />} />
        {companies.length > 1 && (
          <Controller control={control} name="companies" render={({ field }) => (
            <CompanySelect label={t('team.companies')} value={field.value} onChange={field.onChange}
              companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
          )} />
        )}
      </form>
    </Modal>
  )
}

/**
 * What a group can do (ACC-04): a level per area (No access, View, Manage; Manage includes View),
 * for everyone in the group in this organization. Back to standard when it was changed.
 */
function GroupAccessDrawer({ groups, group, companies, onClose }: { groups: TeamGroups; group: TeamGroup | null; companies: Company[]; onClose: () => void }) {
  const { t, i18n } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const [levels, setLevels] = useState<Record<string, string>>(() =>
    Object.fromEntries(groups.areas.map(a => [a.key, group?.areas.find(x => x.area === a.key)?.level ?? 'none'])))
  // A new group, or one of the organization's own, has a name and description too (ACC-08).
  const named = !group || group.custom
  const [name, setName] = useState(group?.name ?? '')
  const [description, setDescription] = useState(group?.description ?? '')
  const [scope, setScope] = useState<CompanySelection>(group && !group.allCompanies ? { all: false, ids: group.companyIds } : { all: true, ids: [] })
  const where = { allCompanies: scope.all, companyIds: scope.all ? [] : scope.ids }
  const scopeChanged = !!group && (scope.all !== group.allCompanies || (!scope.all && scope.ids.join() !== group.companyIds.join()))
  const [busy, setBusy] = useState(false)
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  // The levels each area has: No access, then View and Manage, or only View (the audit log).
  const options = (levels: string[]) => ['none', ...levels].map(value => ({ value, label: t(`team.levels.${value}`) }))

  const send = async (work: () => Promise<{ error?: unknown; response: Response }>, done: string) => {
    setBusy(true)
    setBanner(null)
    try {
      const { error, response } = await work()
      if (response.ok) {
        void queryClient.invalidateQueries({ queryKey: ['team'] })
        void queryClient.invalidateQueries({ queryKey: ['me'] })
        toast.show(done)
        onClose()
        return
      }
      setBanner(isApiError(error) ? error : 'network')
    } catch {
      setBanner('network')
    } finally {
      setBusy(false)
    }
  }
  const save = () => {
    if (!group) {
      return send(() => api.POST('/api/team/groups', { body: { name, description: description || null, areas: levels, ...where } }), t('team.groupAdded', { name }))
    }
    return send(async () => {
      if (group.custom && (name !== group.name || description !== (group.description ?? '') || scopeChanged)) {
        const renamed = await api.PATCH('/api/team/groups/{key}', { params: { path: { key: group.key } }, body: { name, description: description || null, ...where } })
        if (!renamed.response.ok) return renamed
      }
      return api.PUT('/api/team/groups/{key}/access', { params: { path: { key: group.key } }, body: { areas: levels } })
    }, t('team.groupUpdated', { name: group.custom ? name : group.name }))
  }
  const reset = () => group && send(() => api.DELETE('/api/team/groups/{key}/access', { params: { path: { key: group.key } } }),
    t('team.groupReset', { name: group.name }))

  return (
    <Drawer open title={group ? t('team.groupTitle', { name: group.name }) : t('team.addGroup')} onClose={onClose}
      footer={<>
        {group && !group.custom && !group.standard && <Button variant="ghost" className="mr-auto" busy={busy} onClick={() => void reset()}>{t('team.backToStandard')}</Button>}
        <Button variant="secondary" className={!group || group.custom || group.standard ? 'mr-auto' : undefined} onClick={onClose}>{t('common.cancel')}</Button>
        <Button busy={busy} onClick={() => void save()}>{group ? t('common.save') : t('team.addGroup')}</Button>
      </>}>
      <div className="flex flex-col gap-4">
        <p className="m-0 text-muted">{t('team.groupIntro')}</p>
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        {named && <>
          <TextField label={t('team.groupName')} placeholder={t('team.groupNamePlaceholder')} maxLength={60} value={name} onChange={e => setName(e.target.value)} />
          <TextField label={t('team.groupDescription')} optional placeholder={t('team.groupDescriptionPlaceholder')} maxLength={200}
            value={description} onChange={e => setDescription(e.target.value)} />
          {companies.length > 1 && (
            <CompanySelect label={t('team.companies')} value={scope} onChange={setScope}
              companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
          )}
        </>}
        <div className="grid gap-4 sm:grid-cols-2">
          {groups.areas.map(a => (
            <SelectField key={a.key} label={i18n.language === 'sw' ? a.name.sw : a.name.en} placeholder={t('team.levelPlaceholder')} options={options(a.levels)}
              value={levels[a.key] ?? 'none'} onChange={v => setLevels(l => ({ ...l, [a.key]: v }))} />
          ))}
        </div>
      </div>
    </Drawer>
  )
}
