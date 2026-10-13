import { useQuery } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useId, useState, type ReactNode } from 'react'
import { Controller, useForm, useWatch, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
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
import { useToast } from '../components/toast'
import { usePaged } from '../components/usePaged'
import { useCompanies, useSetupDone } from '../setup/hooks'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

type Company = components['schemas']['CompanyResponse']
type Member = components['schemas']['TeamMember']
type Invitation = components['schemas']['InvitationRow']

const groups = ['administrators', 'hr', 'payroll', 'approvers'] as const

/** A row of the table: someone in, or someone invited. */
type Row =
  | { kind: 'member'; key: string; name: string; contact: string; group: string | null; groupName: string; allCompanies: boolean; companyIds: string[]; member: Member }
  | { kind: 'invitation'; key: string; name: string; contact: string; group: string; groupName: string; allCompanies: boolean; companyIds: string[]; invitation: Invitation }

const trash = <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
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
  const [changing, setChanging] = useState<Member | null>(null)
  const [confirming, setConfirming] = useState<Row | null>(null)
  const [busy, setBusy] = useState(false)

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

  const groupLabel = (group: string | null | undefined, name: string) => (group ? t(`team.groups.${group}`) : name)
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
      kind: 'invitation', key: i.id, name: `${i.firstName} ${i.lastName}`, contact: i.email ?? i.phone ?? '', group: i.group,
      groupName: groupLabel(i.group, i.group), allCompanies: i.allCompanies, companyIds: i.companyIds, invitation: i,
    })),
  ]
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
      {r.kind === 'invitation' && <>
        <IconButton label={t('team.resend', { name: r.name })} onClick={() => void resendTo(r.invitation, r.name)}>{resend}</IconButton>
        <IconButton tone="danger" label={t('team.cancel', { name: r.name })} onClick={() => setConfirming(r)}>{trash}</IconButton>
      </>}
      {r.kind === 'member' && !r.member.isYou && <IconButton tone="danger" label={t('team.remove', { name: r.name })} onClick={() => setConfirming(r)}>{trash}</IconButton>}
    </div>
  )

  const forbidden = members.data && isApiError(members.data)
  const ready = companies.data && members.data && (forbidden || invitations.data)
  return (
    <SetupLayout current="team" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('team.title')}</h1>
        <p className="m-0 text-muted">{t('team.intro')}</p>
      </div>
      {!ready ? <Loading rows={4} /> : forbidden ? <Notice>{t('team.onlyAdministrators')}</Notice> : <>
        {companies.data.length > 1 && (
          <div className="w-full max-w-sm">
            <CompanySelect label={t('team.show')} value={shown} onChange={setShown}
              companies={companies.data.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
          </div>
        )}
        <TableCard title={t('team.tableTitle')} count={visible.length}
          actions={<Button onClick={() => setInviting(true)}><PlusIcon />{t('team.invite')}</Button>}
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
        {/* The last setup step: on to the dashboard. */}
        <Proceed label={t('organization.proceed')} onProceed={() => void navigate({ to: '/' })} />

        {inviting && <InviteDrawer companies={companies.data} onClose={() => setInviting(false)} onSent={() => void refresh()} />}
        {viewing && (
          <Drawer open title={viewing.name} onClose={() => setViewing(null)}
            footer={<>
              <Button variant="secondary" className="mr-auto" onClick={() => setViewing(null)}>{t('common.close')}</Button>
              {viewing.kind === 'invitation' && <>
                <Button variant="secondary" onClick={() => setConfirming(viewing)}>{t('team.cancelInvitation')}</Button>
                <Button busy={busy} onClick={() => void resendTo(viewing.invitation, viewing.name)}>{t('team.resendInvitation')}</Button>
              </>}
              {viewing.kind === 'member' && <>
                {!viewing.member.isYou && <Button variant="secondary" onClick={() => setConfirming(viewing)}>{t('team.removeButton')}</Button>}
                <Button onClick={() => setChanging(viewing.member)}>{t('team.changeAccess')}</Button>
              </>}
            </>}>
            <Details row={viewing} companiesText={companiesText} />
          </Drawer>
        )}
        {changing && <AccessModal member={changing} companies={companies.data} onClose={() => setChanging(null)}
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

/** Group: a searchable dropdown, with what the chosen group may do under it. */
function GroupField({ value, onChange, error }: { value: string; onChange: (value: string) => void; error?: string }) {
  const { t } = useTranslation()
  return (
    <SelectField label={t('team.group')} placeholder={t('team.groupPlaceholder')} value={value} error={error}
      hint={value ? t(`team.groupHints.${value}`) : t('team.groupHint')} onChange={v => v && onChange(v)}
      options={groups.map(g => ({ value: g, label: t(`team.groups.${g}`) }))} />
  )
}

interface InviteValues {
  firstName: string
  lastName: string
  email: string
  phone: string
  group: string
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
    defaultValues: { firstName: '', lastName: '', email: '', phone: '', group: '', companies: { all: true, ids: [] } },
  })

  const onSubmit = async (v: InviteValues) => {
    setBanner(null)
    let invalid = false
    const need = (field: FieldPath<InviteValues>, ok: boolean, message: string) => { if (!ok) { setError(field, { message }); invalid = true } }
    need('firstName', !!v.firstName.trim(), t('validation.firstName'))
    need('lastName', !!v.lastName.trim(), t('validation.lastName'))
    need('email', !!(v.email.trim() || v.phone.trim()), t('team.contactNeeded'))
    need('group', !!v.group, t('team.groupNeeded'))
    if (invalid) return
    const name = `${v.firstName.trim()} ${v.lastName.trim()}`
    try {
      const { data, error } = await api.POST('/api/team/invitations', {
        body: {
          firstName: v.firstName.trim(), lastName: v.lastName.trim(), email: v.email.trim() || null, phone: v.phone.trim() || null, group: v.group,
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
      const fields: Record<string, FieldPath<InviteValues>> = { firstName: 'firstName', lastName: 'lastName', contact: 'email', group: 'group', companyIds: 'companies' }
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
        <Controller control={control} name="group" render={({ field }) => <GroupField value={field.value} onChange={field.onChange} error={errors.group?.message} />} />
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
function AccessModal({ member, companies, onClose, onSaved }: { member: Member; companies: Company[]; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const toast = useToast()
  const formId = useId()
  const current = member.groups[0]
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const { handleSubmit, control, setError, formState: { errors, isSubmitting } } = useForm<{ group: string; companies: CompanySelection }>({
    defaultValues: {
      group: current?.group ?? '',
      companies: !current || current.allCompanies ? { all: true, ids: [] } : { all: false, ids: current.companyIds },
    },
  })
  useWatch({ control, name: 'group' })
  const name = `${member.firstName} ${member.lastName}`

  const onSubmit = async (v: { group: string; companies: CompanySelection }) => {
    setBanner(null)
    if (!v.group) return setError('group', { message: t('team.groupNeeded') })
    try {
      const { error, response } = await api.PUT('/api/team/members/{id}/access', {
        params: { path: { id: member.membershipId } },
        body: { group: v.group, allCompanies: v.companies.all, companyIds: v.companies.all ? [] : v.companies.ids },
      })
      if (response.ok) {
        toast.show(t('team.updated', { name }))
        onSaved()
        return
      }
      setBanner(isApiError(error) ? error : 'network')
    } catch {
      setBanner('network')
    }
  }

  return (
    <Modal open title={t('team.changeTitle', { name })} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{t('common.save')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <Controller control={control} name="group" render={({ field }) => <GroupField value={field.value} onChange={field.onChange} error={errors.group?.message} />} />
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
