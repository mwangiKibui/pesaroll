import { useId, useState } from 'react'
import { Controller, useForm, useWatch, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { Button } from '../components/Button'
import { FormErrorBanner, Loading, Notice } from '../components/feedback'
import { RadioGroup, SelectField, TextField } from '../components/fields'
import { Modal } from '../components/Modal'
import { useBanks } from '../setup/hooks'

type Payee = components['schemas']['PayeeResponse']

interface Values {
  name: string
  method: string
  bankCode: string
  branchCode: string
  accountNumber: string
  paybill: string
  paybillAccount: string
  chequePayableTo: string
  ourReference: string
}

const methods = ['bank', 'mpesa_paybill', 'cheque', 'cash'] as const

/**
 * Adding a payee from an item's drawer (a small form on top of it): who a deduction's money is
 * paid to and how, with only that way's details, and the organization's reference with them.
 */
export function PayeeModal({ onSaved, onCancel }: { onSaved: (payee: Payee) => void; onCancel: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const formId = useId()
  const banks = useBanks('KE')
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const { register, handleSubmit, control, setError, setValue, formState: { errors, isSubmitting } } = useForm<Values>({
    defaultValues: { name: '', method: 'mpesa_paybill', bankCode: '', branchCode: '', accountNumber: '', paybill: '', paybillAccount: '', chequePayableTo: '', ourReference: '' },
  })
  const [method, bankCode] = useWatch({ control, name: ['method', 'bankCode'] })
  const bank = banks.data?.find(b => b.code === bankCode)

  const onSubmit = async (v: Values) => {
    setBanner(null)
    // Only the chosen way's details are sent.
    const body = {
      name: v.name.trim(), method: v.method,
      bankCode: v.method === 'bank' ? v.bankCode : null, branchCode: v.method === 'bank' ? v.branchCode : null,
      accountNumber: v.method === 'bank' ? v.accountNumber : null,
      paybill: v.method === 'mpesa_paybill' ? v.paybill.trim() : null, paybillAccount: v.method === 'mpesa_paybill' ? v.paybillAccount.trim() || null : null,
      chequePayableTo: v.method === 'cheque' ? v.chequePayableTo.trim() || null : null,
      ourReference: v.ourReference.trim() || null,
    }
    try {
      const { data, error } = await api.POST('/api/payees', { body })
      if (data) {
        await queryClient.invalidateQueries({ queryKey: ['payees'] })
        onSaved(data)
        return
      }
      if (!isApiError(error)) return setBanner('network')
      if (error.code === 'ONB-3551') return setError('name', { message: errorMessage(t, error) })
      const known = Object.entries(error.fields ?? {}).filter(([name]) => name in body)
      for (const [name, message] of known) setError(name as FieldPath<Values>, { message })
      if (known.length === 0) setBanner(error)
    } catch {
      setBanner('network')
    }
  }

  return (
    <Modal open title={t('payees.addTitle')} onClose={onCancel}
      footer={<>
        <Button variant="secondary" onClick={onCancel}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{t('payees.submit')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <TextField label={t('payees.name')} placeholder={t('payees.namePlaceholder')} hint={t('payees.nameHint')} maxLength={120}
          error={errors.name?.message} {...register('name')} />
        <Controller control={control} name="method" render={({ field }) => (
          <RadioGroup label={t('payees.method')} name={field.name} value={field.value} onChange={field.onChange} error={errors.method?.message}
            options={methods.map(m => ({ value: m, label: t(`payees.methods.${m}`), hint: t(`payees.methodHints.${m}`) }))} />
        )} />
        {method === 'bank' && (!banks.data ? <Loading rows={2} /> : <>
          <Controller control={control} name="bankCode" render={({ field }) => (
            <SelectField label={t('payees.bank')} placeholder={t('payees.bankPlaceholder')} error={errors.bankCode?.message}
              options={banks.data.map(b => ({ value: b.code, label: b.name }))} value={field.value} name={field.name} onBlur={field.onBlur}
              onChange={value => { field.onChange(value); setValue('branchCode', '') }} />
          )} />
          <Controller control={control} name="branchCode" render={({ field }) => (
            <SelectField label={t('payees.branch')} placeholder={t('payees.branchPlaceholder')} error={errors.branchCode?.message} disabled={!bank}
              options={(bank?.branches ?? []).map(b => ({ value: b.code, label: `${b.name} (${b.code})` }))} {...field} />
          )} />
          <TextField label={t('payees.accountNumber')} placeholder={t('payees.accountNumberPlaceholder')} inputMode="numeric" maxLength={30}
            error={errors.accountNumber?.message} {...register('accountNumber')} />
        </>)}
        {method === 'mpesa_paybill' && <div className="grid gap-4 sm:grid-cols-2">
          <TextField label={t('payees.paybill')} placeholder={t('payees.paybillPlaceholder')} inputMode="numeric" maxLength={7}
            error={errors.paybill?.message} {...register('paybill')} />
          <TextField label={t('payees.paybillAccount')} placeholder={t('payees.paybillAccountPlaceholder')} optional hint={t('payees.paybillAccountHint')}
            maxLength={40} error={errors.paybillAccount?.message} {...register('paybillAccount')} />
        </div>}
        {method === 'cheque' && (
          <TextField label={t('payees.chequePayableTo')} placeholder={t('payees.chequePayableToPlaceholder')} optional hint={t('payees.chequePayableToHint')}
            maxLength={120} error={errors.chequePayableTo?.message} {...register('chequePayableTo')} />
        )}
        <TextField label={t('payees.ourReference')} placeholder={t('payees.ourReferencePlaceholder')} optional hint={t('payees.ourReferenceHint')}
          maxLength={40} error={errors.ourReference?.message} {...register('ourReference')} />
      </form>
    </Modal>
  )
}
