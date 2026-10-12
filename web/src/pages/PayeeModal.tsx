import { useId, useState } from 'react'
import { Controller, useForm, useWatch, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { Button } from '../components/Button'
import { FormErrorBanner, Loading, Notice } from '../components/feedback'
import { RadioGroup, SelectField, Switch, TextField } from '../components/fields'
import { Modal } from '../components/Modal'
import { useBanks, useSaccos } from '../setup/hooks'

type Payee = components['schemas']['PayeeResponse']

interface Values {
  name: string
  method: string
  institution: string
  saccoCode: string
  saccoBranchCode: string
  bankCode: string
  branchCode: string
  accountNumber: string
  paybill: string
  paybillAccount: string
  chequePayableTo: string
  ourReference: string
  /** Each person's account number: entered for each person (off), or picked from their profile (on, then which detail). */
  fromProfile: boolean
  profileDetail: string
}

const methods = ['bank', 'mpesa_paybill', 'cheque', 'cash'] as const
/** The profile details each way offers, likeliest first: a transfer goes to the account they're paid into; a paybill (HELB) usually takes the ID number. */
const profileDetails: Record<string, string[]> = { bank: ['bank_account', 'national_id'], mpesa_paybill: ['national_id', 'mpesa_number'] }

/**
 * Adding a payee from an item's drawer (a small form on top of it): who a deduction's money is
 * paid to and how, with only that way's details, and the organization's reference with them.
 */
export function PayeeModal({ onSaved, onCancel }: { onSaved: (payee: Payee) => void; onCancel: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const formId = useId()
  const banks = useBanks('KE')
  const saccos = useSaccos('KE')
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const { register, handleSubmit, control, setError, setValue, formState: { errors, isSubmitting } } = useForm<Values>({
    defaultValues: { name: '', method: 'bank', institution: 'bank', saccoCode: '', saccoBranchCode: '', bankCode: '', branchCode: '', accountNumber: '', paybill: '', paybillAccount: '', chequePayableTo: '', ourReference: '', fromProfile: false, profileDetail: '' },
  })
  const [method, institution, bankCode, saccoCode, fromProfile] = useWatch({ control, name: ['method', 'institution', 'bankCode', 'saccoCode', 'fromProfile'] })
  const bank = banks.data?.find(b => b.code === bankCode)
  const sacco = saccos.data?.find(s => s.code === saccoCode)
  const toBank = method === 'bank' && institution === 'bank'
  const toSacco = method === 'bank' && institution === 'sacco'

  const onSubmit = async (v: Values) => {
    setBanner(null)
    const profile = v.fromProfile && (v.method === 'bank' || v.method === 'mpesa_paybill')
    if (profile && !v.profileDetail) return setError('profileDetail', { message: t('payees.profileDetailRequired') })
    // Only the chosen way's details are sent.
    const body = {
      name: v.name.trim(), method: v.method,
      // Picked from profiles: none of the payee's own account details.
      institution: v.method === 'bank' && !profile ? v.institution : null,
      bankCode: v.method === 'bank' && !profile && v.institution === 'bank' ? v.bankCode : null,
      branchCode: v.method === 'bank' && !profile && v.institution === 'bank' ? v.branchCode : null,
      saccoCode: v.method === 'bank' && !profile && v.institution === 'sacco' ? v.saccoCode : null,
      saccoBranchCode: v.method === 'bank' && !profile && v.institution === 'sacco' ? v.saccoBranchCode : null,
      accountNumber: v.method === 'bank' && !profile ? v.accountNumber : null,
      paybill: v.method === 'mpesa_paybill' ? v.paybill.trim() : null, paybillAccount: v.method === 'mpesa_paybill' && !profile ? v.paybillAccount.trim() || null : null,
      chequePayableTo: v.method === 'cheque' ? v.chequePayableTo.trim() || null : null,
      ourReference: v.ourReference.trim() || null,
      referenceSource: profile ? v.profileDetail : 'typed',
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

  // Each person's account number: entered (off) or picked from their profile (on, then which detail). Not for cheques or cash.
  const toggle = (
    <Controller control={control} name="fromProfile" render={({ field }) => (
      <Switch label={t('payees.fromProfile')} hint={t(field.value ? 'payees.fromProfileOnHint' : 'payees.fromProfileOffHint')}
        // On: the section's likeliest detail to start.
        checked={field.value} onChange={on => { field.onChange(on); if (on) setValue('profileDetail', profileDetails[method]?.[0] ?? '') }} />
    )} />
  )
  const detail = (
    <Controller control={control} name="profileDetail" render={({ field }) => (
      <SelectField label={t('payees.profileDetail')} placeholder={t('payees.profileDetailPlaceholder')} hint={t('payees.profileDetailHint')}
        error={errors.profileDetail?.message} options={(profileDetails[method] ?? []).map(k => ({ value: k, label: t(`payItems.sources.${k}`) }))} {...field} />
    )} />
  )

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
          <RadioGroup label={t('payees.method')} name={field.name} value={field.value} error={errors.method?.message}
            // Another way offers other details: start again from its likeliest.
            onChange={value => { field.onChange(value); setValue('profileDetail', profileDetails[value]?.[0] ?? '') }}
            options={methods.map(m => ({ value: m, label: t(`payees.methods.${m}`), hint: t(`payees.methodHints.${m}`) }))} />
        )} />
        {/* Bank / SACCO transfer: the toggle first; on, each person's own account from their profile (no account of the payee's). */}
        {method === 'bank' && toggle}
        {method === 'bank' && fromProfile && detail}
        {method === 'bank' && !fromProfile && (
          <Controller control={control} name="institution" render={({ field }) => (
            <RadioGroup label={t('payees.institution')} name={field.name} value={field.value} onChange={field.onChange} error={errors.institution?.message}
              options={[{ value: 'bank', label: t('payees.institutions.bank') }, { value: 'sacco', label: t('payees.institutions.sacco') }]} />
          )} />
        )}
        {toSacco && !fromProfile && (!saccos.data ? <Loading rows={2} /> : <>
          <Controller control={control} name="saccoCode" render={({ field }) => (
            <SelectField label={t('payees.sacco')} placeholder={t('payees.saccoPlaceholder')} error={errors.saccoCode?.message}
              options={saccos.data.map(s => ({ value: s.code, label: s.name }))} value={field.value} name={field.name} onBlur={field.onBlur}
              onChange={value => { field.onChange(value); setValue('saccoBranchCode', '') }} />
          )} />
          <Controller control={control} name="saccoBranchCode" render={({ field }) => (
            <SelectField label={t('payees.branch')} placeholder={t('payees.branchPlaceholder')} error={errors.saccoBranchCode?.message} disabled={!sacco}
              options={(sacco?.branches ?? []).map(b => ({ value: b.code, label: b.name }))} {...field} />
          )} />
          <TextField label={t('payees.accountNumber')} placeholder={t('payees.accountNumberPlaceholder')} maxLength={30}
            error={errors.accountNumber?.message} {...register('accountNumber')} />
        </>)}
        {toBank && !fromProfile && (!banks.data ? <Loading rows={2} /> : <>
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
        {/* M-Pesa paybill: the paybill across, then the toggle; off, the paybill's account; on, which detail of each person's profile. */}
        {method === 'mpesa_paybill' && <>
          <TextField label={t('payees.paybill')} placeholder={t('payees.paybillPlaceholder')} inputMode="numeric" maxLength={7}
            error={errors.paybill?.message} {...register('paybill')} />
          {toggle}
          {fromProfile ? detail : (
            <TextField label={t('payees.paybillAccount')} placeholder={t('payees.paybillAccountPlaceholder')} optional hint={t('payees.paybillAccountHint')}
              maxLength={40} error={errors.paybillAccount?.message} {...register('paybillAccount')} />
          )}
        </>}
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
