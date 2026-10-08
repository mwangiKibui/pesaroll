import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { errorKey, errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { passwordStrength } from '../auth/password'
import { Button } from '../components/Button'
import { FormErrorBanner, Loading, Notice } from '../components/feedback'
import { TextField } from '../components/fields'
import { PasswordField } from '../components/PasswordField'
import { splitContact } from '../lib/identifiers'
import { AuthLayout } from './AuthLayout'

// Same rules as the API (AUTH-1004 to AUTH-1009), plus a password strength the screen asks for.
// Messages are translation keys.
function schemaFor(otherRoleId: string | undefined) {
  return z.object({
    firstName: z.string().trim().min(1, 'validation.firstName').max(60, 'validation.nameTooLong'),
    middleName: z.string().trim().max(60, 'validation.nameTooLong'),
    lastName: z.string().trim().min(1, 'validation.lastName').max(60, 'validation.nameTooLong'),
    contact: z.string().refine(v => splitContact(v) !== null, 'validation.contact'),
    password: z.string().refine(v => passwordStrength(v) !== 'weak', 'validation.passwordWeak'),
    roleId: z.string({ error: 'validation.role' }).min(1, 'validation.role'),
    roleOther: z.string().trim().max(80, 'validation.roleOtherLong'),
  }).refine(v => v.roleId !== otherRoleId || v.roleOther.length > 0, { message: 'validation.roleOther', path: ['roleOther'] })
}

type Values = z.infer<ReturnType<typeof schemaFor>>

// Server errors that belong under a field; the rest show as a banner.
const fieldFor: Record<string, keyof Values> = { 'AUTH-1003': 'contact', 'AUTH-1005': 'contact', 'AUTH-1004': 'password', 'AUTH-1009': 'roleOther' }

export default function SignUpPage() {
  const { t, i18n } = useTranslation()
  const { api, auth } = useServices()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const lang = i18n.language === 'sw' ? 'sw' : 'en'

  const roles = useQuery({
    queryKey: ['signup-roles'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/auth/signup-roles')
      if (error || !data) throw new Error('signup roles')
      return data
    },
  })
  const otherRoleId = useMemo(() => roles.data?.flatMap(g => g.roles).find(r => r.isOther)?.id, [roles.data])

  const { register, handleSubmit, control, setError, formState: { errors, isSubmitting } } = useForm<Values>({
    resolver: zodResolver(schemaFor(otherRoleId)),
    defaultValues: { firstName: '', middleName: '', lastName: '', contact: '', password: '', roleOther: '' },
  })
  const password = useWatch({ control, name: 'password' })
  const roleId = useWatch({ control, name: 'roleId' })
  const isOther = roleId !== undefined && roleId === otherRoleId
  const fieldError = (name: keyof Values) => (errors[name]?.message ? t(errors[name].message) : undefined)

  const onSubmit = async (v: Values) => {
    setBanner(null)
    const contact = splitContact(v.contact)
    try {
      const { data, error } = await api.POST('/api/auth/signup', {
        body: {
          firstName: v.firstName, middleName: v.middleName || null, lastName: v.lastName,
          email: contact && 'email' in contact ? contact.email : null,
          phone: contact && 'phone' in contact ? contact.phone : null,
          password: v.password, roleId: v.roleId, roleOther: isOther ? v.roleOther : null,
        },
      })
      if (data) {
        auth.setAccessToken(data.accessToken, { broadcast: true })
        return
      }
      if (isApiError(error) && fieldFor[error.code]) setError(fieldFor[error.code], { message: errorKey(error.code) })
      else if (isApiError(error)) setBanner(error)
      else setBanner('network')
    } catch {
      setBanner('network')
    }
  }

  const level = passwordStrength(password)

  return (
    <AuthLayout title={t('signUp.title')} intro={t('signUp.intro')}>
      <form noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label={t('signUp.firstName')} autoComplete="given-name" error={fieldError('firstName')} {...register('firstName')} />
          <TextField label={t('signUp.lastName')} autoComplete="family-name" error={fieldError('lastName')} {...register('lastName')} />
        </div>
        <TextField label={t('signUp.middleName')} optional autoComplete="additional-name" error={fieldError('middleName')} {...register('middleName')} />
        <TextField label={t('auth.contact')} hint={t('signUp.contactHint')} autoComplete="username" inputMode="email" error={fieldError('contact')} {...register('contact')} />
        <div className="flex flex-col gap-1.5">
          <PasswordField label={t('auth.password')} isNew
            hint={password ? t('signUp.strength', { level: t(`signUp.${level}`) }) : t('signUp.passwordHint')}
            error={fieldError('password')} {...register('password')} />
          {password && (
            <div aria-hidden="true" className="flex gap-1">
              {(['weak', 'fair', 'strong'] as const).map((step, i) => {
                const filled = i <= ['weak', 'fair', 'strong'].indexOf(level)
                const colour = level === 'weak' ? 'bg-danger' : level === 'fair' ? 'bg-warning-ink' : 'bg-brand'
                return <span key={step} className={`h-1.5 flex-1 rounded-full ${filled ? colour : 'bg-line'}`} />
              })}
            </div>
          )}
        </div>

        <fieldset className="m-0 flex flex-col gap-3 border-0 p-0" aria-describedby={errors.roleId ? 'role-error' : undefined}>
          <legend className="mb-1 p-0 text-label font-medium text-ink">{t('signUp.role')}</legend>
          {roles.isPending && <Loading rows={3} />}
          {roles.data?.map(group => (
            <fieldset key={group.code} className="m-0 flex flex-col gap-2 border-0 p-0">
              <legend className="mb-1.5 p-0 text-tag uppercase tracking-wider text-muted">{group.name[lang]}</legend>
              <div className="grid gap-2 sm:grid-cols-2">
                {group.roles.map(role => (
                  <label key={role.id} className="flex cursor-pointer items-start gap-2.5 rounded-card border border-line bg-card p-3 has-checked:border-brand has-checked:bg-brand-soft">
                    <input type="radio" value={role.id} className="mt-0.5 size-[18px] flex-none accent-brand" {...register('roleId')} />
                    <span className="flex flex-col">
                      <span className="text-label font-semibold text-ink">{role.name[lang]}</span>
                      <span className="text-hint text-muted">{role.description[lang]}</span>
                    </span>
                  </label>
                ))}
              </div>
            </fieldset>
          ))}
          {errors.roleId && <p id="role-error" className="m-0 text-hint font-medium text-danger-ink">{fieldError('roleId')}</p>}
        </fieldset>
        {isOther && <TextField label={t('signUp.roleOther')} error={fieldError('roleOther')} maxLength={80} {...register('roleOther')} />}

        <Button type="submit" size="lg" fullWidth busy={isSubmitting}>{t('signUp.submit')}</Button>
        <p className="m-0 text-hint text-muted">{t('signUp.terms')}</p>
        <p className="m-0 text-label">{t('signUp.haveAccount')} <Link to="/signin">{t('signUp.signIn')}</Link></p>
      </form>
    </AuthLayout>
  )
}
