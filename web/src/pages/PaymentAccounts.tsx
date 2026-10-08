import { useQuery } from '@tanstack/react-query'
import { useId, useState } from 'react'
import { Controller, useForm, useWatch, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { Button, IconButton } from '../components/Button'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { FormErrorBanner, Loading, Notice, Tag } from '../components/feedback'
import { Modal } from '../components/Modal'
import { TableCard } from '../components/TableCard'
import { RadioGroup, SelectField, TextField } from '../components/fields'
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
}

const none = <span className="text-muted">—</span>

/** "Equity Bank •••• 8901" or "M-Pesa · Shortcode 600123": names an account in buttons and questions. */
function shortName(t: TFunction, a: Account) {
  return a.method === 'mpesa' ? t('payingAccounts.mpesaName', { shortcode: a.mpesaShortcode }) : `${a.bankName} ${a.accountNumberMasked}`
}

/**
 * ONB-03: where a company's payroll is paid from: a table on the company drawer's Paying accounts
 * tab, and adding one in a modal on top. Bank and M-Pesa files
 * take the debit account from here. The account number only ever comes back masked; removing one
 * keeps it on record for past payrolls.
 */
export function PaymentAccounts({ company }: { company: Company }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  // 'new', the account being edited, or nothing.
  const [editing, setEditing] = useState<Account | 'new' | null>(null)
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
  const addButton = <Button onClick={() => setEditing('new')}>{t('payingAccounts.add')}</Button>
  return (
    <div className="flex flex-col gap-3">
      <p className="m-0 text-hint text-muted">{t('payingAccounts.intro')}</p>
      {!accounts.data ? <Loading rows={2} /> : (
        <TableCard title={t('payingAccounts.title')} count={list.length} actions={addButton}>
          {list.length === 0 ? <p className="m-0 px-4 py-6 text-muted">{t('payingAccounts.none')}</p> : (
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('payingAccounts.method')}</th>
                  <th scope="col">{t('payingAccounts.bankAndBranch')}</th>
                  <th scope="col">{t('payingAccounts.accountName')}</th>
                  <th scope="col">{t('payingAccounts.account')}</th>
                  <th scope="col">{t('payingAccounts.default')}</th>
                  <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                </tr>
              </thead>
              <tbody>
                {list.map(a => (
                  <tr key={a.id}>
                    <td>{a.method === 'mpesa' ? t('payingAccounts.mpesa') : t('payingAccounts.bank')}</td>
                    <td>
                      {a.method === 'bank' ? <>
                        <span className="block text-ink">{a.bankName}</span>
                        <span className="block text-hint text-muted">{a.branchName}</span>
                      </> : none}
                    </td>
                    <td>{a.accountName ?? none}</td>
                    <td className="whitespace-nowrap">{a.method === 'mpesa' ? t('payingAccounts.shortcodeValue', { shortcode: a.mpesaShortcode }) : a.accountNumberMasked}</td>
                    <td>
                      {a.isDefault
                        ? <Tag tone="brand">{t('payingAccounts.default')}</Tag>
                        : <Button variant="ghost" aria-label={t('payingAccounts.makeDefaultLabel', { name: shortName(t, a) })} onClick={() => void makeDefault(a)}>{t('payingAccounts.makeDefault')}</Button>}
                    </td>
                    <td className="whitespace-nowrap text-right">
                      <IconButton label={t('payingAccounts.edit', { name: shortName(t, a) })} onClick={() => setEditing(a)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                      </IconButton>
                      <IconButton tone="danger" label={t('payingAccounts.remove', { name: shortName(t, a) })} onClick={() => setRemoving(a)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                      </IconButton>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </TableCard>
      )}
      {editing && <AccountForm company={company} account={editing === 'new' ? null : editing}
        onDone={() => { setEditing(null); changed() }} onCancel={() => setEditing(null)} />}
      <ConfirmDialog open={!!removing} title={t('payingAccounts.removeTitle', { name: removing ? shortName(t, removing) : '' })}
        confirmLabel={t('payingAccounts.removeConfirm')} busy={busy} onConfirm={remove} onCancel={() => setRemoving(null)}>
        {t('payingAccounts.removeText')}
      </ConfirmDialog>
    </div>
  )
}

/**
 * Adding or editing an account, in a modal. Editing replaces the account (the API closes the old
 * one and keeps it on record); a bank account's number can be left empty to keep it.
 */
function AccountForm({ company, account, onDone, onCancel }: { company: Company; account: Account | null; onDone: () => void; onCancel: () => void }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const toast = useToast()
  const formId = useId()
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
    defaultValues: {
      method: account?.method === 'mpesa' ? 'mpesa' : 'bank',
      bankCode: account?.bankCode ?? '',
      branchCode: account?.branchCode ?? '',
      accountName: account?.accountName ?? company.legalName,
      accountNumber: '',
      mpesaShortcode: account?.mpesaShortcode ?? '',
    },
  })
  const method = useWatch({ control, name: 'method' })
  // Editing a bank account: the number can stay as it is.
  const keepsNumber = account?.method === 'bank' && method === 'bank'
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
      const number = v.accountNumber.replace(/[\s-]/g, '')
      if (!(number === '' && keepsNumber) && !/^[0-9]{6,20}$/.test(number)) problems.push(['accountNumber', t('payingAccounts.accountNumberInvalid')])
    }
    for (const [name, message] of problems) setError(name, { message })
    return problems.length === 0
  }

  const onSubmit = async (v: Values) => {
    setBanner(null)
    if (!check(v)) return
    // The other method's fields are sent empty.
    const body = v.method === 'mpesa'
      ? { method: 'mpesa', bankCode: null, branchCode: null, accountName: null, accountNumber: null, mpesaShortcode: v.mpesaShortcode.trim() }
      : { method: 'bank', bankCode: v.bankCode, branchCode: v.branchCode, accountName: v.accountName.trim(), accountNumber: v.accountNumber.trim(), mpesaShortcode: null }
    try {
      const { data, error } = account
        ? await api.PUT('/api/companies/{companyId}/payment-accounts/{accountId}', { params: { path: { companyId: company.id, accountId: account.id } }, body })
        : await api.POST('/api/companies/{companyId}/payment-accounts', { params: { path: { companyId: company.id } }, body })
      if (data) {
        toast.show(account ? t('payingAccounts.saved') : t('payingAccounts.added'))
        onDone()
        return
      }
      if (isApiError(error) && error.code === 'ONB-3510' && error.fields) {
        for (const [name, message] of Object.entries(error.fields)) setError(name as FieldPath<Values>, { message })
      } else setBanner(isApiError(error) ? error : 'network')
    } catch {
      setBanner('network')
    }
  }

  return (
    <Modal open title={account ? t('payingAccounts.editTitle') : t('payingAccounts.add')} onClose={onCancel}
      footer={<>
        <Button variant="secondary" onClick={onCancel}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{account ? t('payingAccounts.save') : t('payingAccounts.submit')}</Button>
      </>}>
    <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
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
            hint={keepsNumber ? t('payingAccounts.keepNumber', { masked: account?.accountNumberMasked }) : undefined}
            autoComplete="off" maxLength={30} error={errors.accountNumber?.message} {...register('accountNumber')} />
        </>
      ) : (
        <TextField label={t('payingAccounts.shortcode')} placeholder={t('payingAccounts.shortcodePlaceholder')} hint={t('payingAccounts.shortcodeHint')}
          inputMode="numeric" maxLength={7} error={errors.mpesaShortcode?.message} {...register('mpesaShortcode')} />
      )}
    </form>
    </Modal>
  )
}
