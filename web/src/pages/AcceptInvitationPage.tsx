import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { passwordStrength } from '../auth/password'
import { Button } from '../components/Button'
import { FormErrorBanner, Loading, Notice } from '../components/feedback'
import { TextField } from '../components/fields'
import { NewPasswordField } from '../components/NewPasswordField'
import { PasswordField } from '../components/PasswordField'
import { AuthLayout } from './AuthLayout'

type Invitation = components['schemas']['InvitationResponse']

const name = (required: string) => z.string().trim().min(1, required).max(60, 'validation.nameTooLong')
// A new account: the same rules as signing up. An existing one: its password.
const newAccount = z.object({
  firstName: name('validation.firstName'),
  lastName: name('validation.lastName'),
  password: z.string().refine(v => passwordStrength(v) !== 'weak', 'validation.passwordWeak'),
})
const existingAccount = z.object({ firstName: z.string(), lastName: z.string(), password: z.string().min(1, 'validation.passwordRequired') })
type Values = z.infer<typeof newAccount>

/**
 * AUTH-08, the page an invitation's link opens: who invited them, to which organization, as what
 * and for which companies. Someone new confirms their name and sets a password; someone with an
 * account enters its password. Either way they land in the app with the invitation's access. A
 * link that no longer works (expired, used, cancelled) says why, with its code.
 */
export default function AcceptInvitationPage() {
  const { t } = useTranslation()
  const { api } = useServices()
  const { token } = useParams({ strict: false }) as { token: string }
  const invitation = useQuery({
    queryKey: ['invitation', token],
    retry: false,
    queryFn: async (): Promise<Invitation | ApiError> => {
      const { data, error } = await api.GET('/api/invitations/{token}', { params: { path: { token } } })
      if (data) return data
      if (isApiError(error)) return error
      throw new Error('invitation')
    },
  })

  if (invitation.isPending) {
    return <AuthLayout title={t('invite.loading')} intro=""><Loading rows={3} /></AuthLayout>
  }
  if (invitation.isError || !invitation.data || isApiError(invitation.data)) {
    const problem = invitation.data && isApiError(invitation.data) ? invitation.data : null
    return (
      <AuthLayout title={t('invite.cantUse')} intro={t('invite.cantUseIntro')}>
        <div className="flex flex-col gap-4">
          {problem ? <FormErrorBanner error={{ ...problem, message: errorMessage(t, problem) }} /> : <Notice tone="danger">{t('apiErrors.network')}</Notice>}
          <p className="m-0 text-label">{t('invite.haveAccount')} <Link to="/signin">{t('signIn.title')}</Link></p>
        </div>
      </AuthLayout>
    )
  }
  return <AcceptForm token={token} invitation={invitation.data} />
}

function AcceptForm({ token, invitation }: { token: string; invitation: Invitation }) {
  const { t } = useTranslation()
  const { api, auth } = useServices()
  const navigate = useNavigate()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const hasAccount = invitation.hasAccount
  const { register, handleSubmit, control, formState: { errors, isSubmitting } } = useForm<Values>({
    resolver: zodResolver(hasAccount ? existingAccount : newAccount),
    defaultValues: { firstName: invitation.firstName, lastName: invitation.lastName, password: '' },
  })
  const password = useWatch({ control, name: 'password' })
  const fieldError = (field: keyof Values) => (errors[field]?.message ? t(errors[field].message) : undefined)
  const contact = invitation.email ?? invitation.phone ?? ''
  const companies = invitation.allCompanies ? t('invite.everyCompany')
    : t('invite.companies', { names: new Intl.ListFormat('en-GB', { type: 'conjunction' }).format(invitation.companies) })

  const onSubmit = async (v: Values) => {
    setBanner(null)
    try {
      const body = hasAccount ? { firstName: null, lastName: null, password: v.password } : { firstName: v.firstName.trim(), lastName: v.lastName.trim(), password: v.password }
      const { data, error } = await api.POST('/api/invitations/{token}/accept', { params: { path: { token } }, body })
      if (data) {
        auth.setAccessToken(data.accessToken, { broadcast: true })
        await navigate({ to: '/' })
        return
      }
      setBanner(isApiError(error) ? error : 'network')
    } catch {
      setBanner('network')
    }
  }

  return (
    <AuthLayout title={t('invite.title', { organization: invitation.organization })}
      intro={t('invite.intro', { inviter: invitation.invitedBy, role: t(`invite.roles.${invitation.group}`), companies })}>
      <form noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        {hasAccount ? (
          <>
            <p className="m-0 text-body text-ink">{t('invite.existing', { contact })}</p>
            <PasswordField label={t('auth.password')} placeholder={t('signIn.passwordPlaceholder')} error={fieldError('password')} {...register('password')} />
            <Button type="submit" size="lg" fullWidth busy={isSubmitting}>{t('invite.signInAndJoin')}</Button>
          </>
        ) : (
          <>
            <div className="flex flex-col gap-0.5">
              <span className="text-label text-muted">{t('invite.yourAccount')}</span>
              <span className="text-body font-medium text-ink">{contact}</span>
            </div>
            <div className="grid gap-4 @sm:grid-cols-2">
              <TextField label={t('signUp.firstName')} placeholder={t('signUp.firstNamePlaceholder')} autoComplete="given-name" error={fieldError('firstName')} {...register('firstName')} />
              <TextField label={t('signUp.lastName')} placeholder={t('signUp.lastNamePlaceholder')} autoComplete="family-name" error={fieldError('lastName')} {...register('lastName')} />
            </div>
            <NewPasswordField label={t('auth.password')} placeholder={t('signUp.passwordPlaceholder')} value={password} error={fieldError('password')} {...register('password')} />
            <Button type="submit" size="lg" fullWidth busy={isSubmitting}>{t('invite.accept')}</Button>
          </>
        )}
      </form>
    </AuthLayout>
  )
}
