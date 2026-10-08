import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Controller, useForm, useWatch, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { Button, IconButton } from '../components/Button'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { FormErrorBanner, Loading, Notice, Tag } from '../components/feedback'
import { RadioGroup, SelectField, TextField } from '../components/fields'
import { PasswordField } from '../components/PasswordField'
import { useToast } from '../components/toast'

type Company = components['schemas']['CompanyResponse']
type Account = components['schemas']['PaymentAccountResponse']

interface Values {
  method: 'bank' | 'mpesa'
  bankCode: string
  branchCode: string
  accountName: string
  accountNumber: string
  mpesaShortcode: string
  password: string
}

/** "Equity Bank •••• 8901" or "M-Pesa · Shortcode 600123": names an account in buttons and questions. */
function shortName(t: TFunction, a: Account) {
  return a.method === 'mpesa' ? t('payingAccounts.mpesaName', { shortcode: a.mpesaShortcode }) : `${a.bankName} ${a.accountNumberMasked}`
}

/**
 * ONB-03: where a company's payroll is paid from, inside the company drawer. Bank and M-Pesa files
 * take the debit account from here. Adding one asks for the password again; the account number
 * only ever comes back masked; removing one keeps it on record for past payrolls.
 */
export function PaymentAccounts({ company }: { company: Company }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const [adding, setAdding] = useState(false)
  const [removing, setRemoving] = useState<Account | null>(null)
  const [busy, setBusy] = useState(false)
  const key = ['payment-accounts', company.id]
  const accounts = useQuery({
    queryKey: key,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/companies/{companyId}/payment-accounts', { params: { path: { companyId: company.id } } })
      if (error || !data) throw new Error('payment-accounts')
      return data
    },
  })

  const changed = () => {
    void queryClient.invalidateQueries({ queryKey: key })
    void queryClient.invalidateQueries({ queryKey: ['companies'] })   // readiness depends on it
  }

  const makeDefault = async (account: Account) => {
    const { data, error } = await api.POST('/api/companies/{companyId}/payment-accounts/{accountId}/default',
      { params: { path: { companyId: company.id, accountId: account.id } } })
    if (data) {
      queryClient.setQueryData(key, data)
      toast.show(t('payingAccounts.defaultChanged'))
    } else {
      toast.show(isApiError(error) ? errorMessage(t, error) : t('apiErrors.network'), 'danger')
    }
  }

  const remove = async () => {
    if (!removing) return
    setBusy(true)
    try {
      const { error, response } = await api.DELETE('/api/companies/{companyId}/payment-accounts/{accountId}',
        { params: { path: { companyId: company.id, accountId: removing.id } } })
      if (response.ok) {
        toast.show(t('payingAccounts.removed'))
        changed()
      } else {
        toast.show(isApiError(error) ? errorMessage(t, error) : t('apiErrors.network'), 'danger')
      }
    } catch {
      toast.show(t('apiErrors.network'), 'danger')
    } finally {
      setBusy(false)
      setRemoving(null)
    }
  }

  const list = accounts.data ?? []
  return (
    <div className="flex flex-col gap-3">
      <p className="m-0 text-hint text-muted">{t('payingAccounts.intro')}</p>
      {!accounts.data ? <Loading rows={1} /> : list.length === 0 && !adding && <Notice>{t('payingAccounts.none')}</Notice>}
      {list.length > 0 && (
        <ul className="m-0 flex list-none flex-col gap-2 p-0">
          {list.map(a => (
            <li key={a.id} className="flex items-center justify-between gap-3 rounded-control border border-line px-3 py-2.5">
              <span className="flex min-w-0 flex-col">
                <span className="font-medium text-ink">
                  {a.method === 'mpesa' ? t('payingAccounts.mpesaName', { shortcode: a.mpesaShortcode }) : `${a.bankName} · ${a.branchName}`}
                </span>
                {a.method === 'bank' && <span className="text-hint text-muted">{a.accountName} · {a.accountNumberMasked}</span>}
              </span>
              <span className="flex flex-none items-center gap-1">
                {a.isDefault
                  ? <Tag tone="brand">{t('payingAccounts.default')}</Tag>
                  : <Button variant="ghost" aria-label={t('payingAccounts.makeDefaultLabel', { name: shortName(t, a) })} onClick={() => void makeDefault(a)}>{t('payingAccounts.makeDefault')}</Button>}
                <IconButton tone="danger" label={t('payingAccounts.remove', { name: shortName(t, a) })} onClick={() => setRemoving(a)}>
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                </IconButton>
              </span>
            </li>
          ))}
        </ul>
      )}
      {adding
        ? <AddAccount company={company} onDone={() => { setAdding(false); changed() }} onCancel={() => setAdding(false)} />
        : <div><Button variant="secondary" onClick={() => setAdding(true)}>{t('payingAccounts.add')}</Button></div>}
      <ConfirmDialog open={!!removing} title={t('payingAccounts.removeTitle', { name: removing ? shortName(t, removing) : '' })}
        confirmLabel={t('payingAccounts.removeConfirm')} busy={busy} onConfirm={remove} onCancel={() => setRemoving(null)}>
        {t('payingAccounts.removeText')}
      </ConfirmDialog>
    </div>
  )
}

function AddAccount({ company, onDone, onCancel }: { company: Company; onDone: () => void; onCancel: () => void }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const toast = useToast()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const banks = useQuery({
    queryKey: ['banks', company.countryCode],
    staleTime: Infinity,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/banks', { params: { query: { country: company.countryCode } } })
      if (error || !data) throw new Error('banks')
      return data
    },
  })
  const { register, handleSubmit, control, setError, setValue, formState: { errors, isSubmitting } } = useForm<Values>({
    defaultValues: { method: 'bank', bankCode: '', branchCode: '', accountName: company.legalName, accountNumber: '', mpesaShortcode: '', password: '' },
  })
  const method = useWatch({ control, name: 'method' })
  const bankCode = useWatch({ control, name: 'bankCode' })
  const bank = banks.data?.find(b => b.code === bankCode)

  // The same checks as the API, so most mistakes show before anything is sent.
  const check = (v: Values) => {
    const problems: [FieldPath<Values>, string][] = []
    if (v.method === 'mpesa') {
      if (!/^[0-9]{5,7}$/.test(v.mpesaShortcode.trim())) problems.push(['mpesaShortcode', t('payingAccounts.shortcodeInvalid')])
    } else {
      if (!v.bankCode) problems.push(['bankCode', t('payingAccounts.bankRequired')])
      else if (!v.branchCode) problems.push(['branchCode', t('payingAccounts.branchRequired')])
      if (v.accountName.trim().length < 2 || v.accountName.trim().length > 120) problems.push(['accountName', t('payingAccounts.accountNameInvalid')])
      if (!/^[0-9]{6,20}$/.test(v.accountNumber.replace(/[\s-]/g, ''))) problems.push(['accountNumber', t('payingAccounts.accountNumberInvalid')])
    }
    if (!v.password) problems.push(['password', t('payingAccounts.passwordRequired')])
    for (const [name, message] of problems) setError(name, { message })
    return problems.length === 0
  }

  const onSubmit = async (v: Values) => {
    setBanner(null)
    if (!check(v)) return
    // The other method's fields are sent empty.
    const body = v.method === 'mpesa'
      ? { method: 'mpesa', bankCode: null, branchCode: null, accountName: null, accountNumber: null, mpesaShortcode: v.mpesaShortcode.trim(), password: v.password }
      : { method: 'bank', bankCode: v.bankCode, branchCode: v.branchCode, accountName: v.accountName.trim(), accountNumber: v.accountNumber.trim(), mpesaShortcode: null, password: v.password }
    try {
      const { data, error } = await api.POST('/api/companies/{companyId}/payment-accounts', { params: { path: { companyId: company.id } }, body })
      if (data) {
        toast.show(t('payingAccounts.added'))
        onDone()
        return
      }
      if (isApiError(error) && error.code === 'AUTH-1011') setError('password', { message: errorMessage(t, error) })
      else if (isApiError(error) && error.code === 'ONB-3510' && error.fields) {
        for (const [name, message] of Object.entries(error.fields)) setError(name as FieldPath<Values>, { message })
      } else setBanner(isApiError(error) ? error : 'network')
    } catch {
      setBanner('network')
    }
  }

  return (
    <form noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4 rounded-card border border-line bg-raised p-4">
      {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
      {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
      <Controller control={control} name="method" render={({ field }) => (
        <RadioGroup label={t('payingAccounts.method')} name={field.name} value={field.value} onChange={field.onChange}
          options={[
            { value: 'bank', label: t('payingAccounts.bank'), hint: t('payingAccounts.bankHint') },
            { value: 'mpesa', label: t('payingAccounts.mpesa'), hint: t('payingAccounts.mpesaHint') },
          ]} />
      )} />
      {method === 'bank' ? (
        <>
          {!banks.data ? <Loading rows={2} /> : <>
            <Controller control={control} name="bankCode" render={({ field }) => (
              <SelectField label={t('payingAccounts.bankName')} placeholder={t('payingAccounts.bankPlaceholder')} error={errors.bankCode?.message}
                options={banks.data.map(b => ({ value: b.code, label: b.name }))} {...field}
                onChange={value => { field.onChange(value); setValue('branchCode', '') }} />
            )} />
            <Controller control={control} name="branchCode" render={({ field }) => (
              <SelectField label={t('payingAccounts.branch')} placeholder={t('payingAccounts.branchPlaceholder')} error={errors.branchCode?.message}
                disabled={!bank} options={(bank?.branches ?? []).map(b => ({ value: b.code, label: b.name }))} {...field} />
            )} />
          </>}
          <TextField label={t('payingAccounts.accountName')} placeholder={t('payingAccounts.accountNamePlaceholder')} hint={t('payingAccounts.accountNameHint')}
            maxLength={120} error={errors.accountName?.message} {...register('accountName')} />
          <TextField label={t('payingAccounts.accountNumber')} placeholder={t('payingAccounts.accountNumberPlaceholder')} inputMode="numeric"
            autoComplete="off" maxLength={30} error={errors.accountNumber?.message} {...register('accountNumber')} />
        </>
      ) : (
        <TextField label={t('payingAccounts.shortcode')} placeholder={t('payingAccounts.shortcodePlaceholder')} hint={t('payingAccounts.shortcodeHint')}
          inputMode="numeric" maxLength={7} error={errors.mpesaShortcode?.message} {...register('mpesaShortcode')} />
      )}
      <PasswordField label={t('payingAccounts.password')} placeholder={t('payingAccounts.password')} hint={t('payingAccounts.passwordHint')}
        error={errors.password?.message} {...register('password')} />
      <div className="flex justify-end gap-3">
        <Button variant="secondary" onClick={onCancel}>{t('common.cancel')}</Button>
        <Button type="submit" busy={isSubmitting}>{t('payingAccounts.submit')}</Button>
      </div>
    </form>
  )
}
