import { useQuery } from '@tanstack/react-query'
import { useEffect, useId, useState } from 'react'
import { Controller, useForm, useWatch, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { errorMessage, isApiError } from '../../app/apiErrors'
import { permissions, usePermissions } from '../../app/permissions'
import { useServices, type ApiError } from '../../app/services'
import { Button } from '../../components/Button'
import { Drawer } from '../../components/Drawer'
import { FormErrorBanner, Loading, Notice } from '../../components/feedback'
import { Checkbox, RadioGroup, SelectField, TextField } from '../../components/fields'
import { TabPanel, Tabs } from '../../components/Tabs'
import { useToast } from '../../components/toast'
import { accountProblem, rules } from '../../lib/employeeRules'
import { useBanks, useLeavePlans, useLocations, useSchedules, useStructure } from '../../setup/hooks'
import { tabs, types, usePaysBy, type Company, type Detail, type TabKey } from './shared'

interface Values {
  firstName: string; middleName: string; lastName: string; dateOfBirth: string; phone: string; email: string; address: string; staffNo: string; hireDate: string; exitDate: string
  nationalId: string; kraPin: string; nssfNo: string; shaNo: string
  companyId: string; departmentId: string; positionId: string; unitId: string; locationId: string; lineManagerId: string; payScheduleId: string; leavePlanId: string
  employmentType: string; contractEnd: string
  basicAmount: string
  method: string; bankCode: string; branchCode: string; accountNumber: string; mpesaPhone: string
}

/** Which tab each field is on, to mark the tab when the server finds a problem there. */
const tabOf: Record<string, TabKey> = {
  firstName: 'personal', middleName: 'personal', lastName: 'personal', dateOfBirth: 'personal', phone: 'personal', email: 'personal', address: 'personal',
  staffNo: 'personal', hireDate: 'personal', exitDate: 'personal', nationalId: 'ids', kraPin: 'ids', nssfNo: 'ids', shaNo: 'ids',
  companyId: 'job', departmentId: 'job', positionId: 'job', unitId: 'job', locationId: 'job', lineManagerId: 'job', payScheduleId: 'job',
  leavePlanId: 'job', employmentType: 'job', contractEnd: 'job', basicAmount: 'pay',
  method: 'account', bankCode: 'account', branchCode: 'account', accountNumber: 'account', mpesaPhone: 'account',
}

/** The API's field names ("job.payScheduleId", "account.mpesaPhone") to the form's. */
const fieldOf = (name: string): FieldPath<Values> | null => {
  const last = name.split('.').pop()!
  const map: Record<string, FieldPath<Values>> = { basicAmount: 'basicAmount', payBasis: 'basicAmount' }
  return (map[last] ?? (last in tabOf ? last : null)) as FieldPath<Values> | null
}

export function EmployeeDrawer({ id, companies, defaultCompany, onClose }: {
  id: string | null
  companies: Company[]
  defaultCompany: string
  onClose: () => void
}) {
  const { t } = useTranslation()
  const { api } = useServices()
  // ACC-05: the form shows someone's pay only to people who may change it (asking for it is recorded).
  const canManagePay = usePermissions().can(permissions.pay.manage)
  const detail = useQuery({
    queryKey: ['employee', id, 'edit', canManagePay],
    enabled: !!id,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees/{id}', { params: { path: { id: id! }, query: { includePay: canManagePay || undefined } } })
      if (error || !data) throw new Error('employee')
      return data
    },
  })
  if (id && !detail.data) {
    return <Drawer open size="wide" title={t('employees.editTitle')} onClose={onClose}><Loading rows={6} /></Drawer>
  }

  return <EmployeeForm employee={detail.data ?? null} companies={companies} defaultCompany={defaultCompany} onClose={onClose} />
}

function EmployeeForm({ employee, companies, defaultCompany, onClose }: {
  employee: Detail | null
  companies: Company[]
  defaultCompany: string
  onClose: () => void
}) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const formId = useId()
  const tabsId = useId()
  const [tab, setTab] = useState<TabKey>('personal')
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const canManagePay = usePermissions().can(permissions.pay.manage, employee?.companyId ?? defaultCompany)
  const formTabs = tabs.filter(k => canManagePay || (k !== 'pay' && k !== 'account'))
  const schedules = useSchedules()
  const plans = useLeavePlans()
  const locations = useLocations()
  const departments = useStructure('departments')
  const positions = useStructure('positions')
  const units = useStructure('units')

  const companyStart = employee?.companyId ?? defaultCompany
  const defaultSchedule = (company: string) => schedules.data?.find(s => s.defaultFor.includes(company))?.id ?? ''
  const defaultPlan = (company: string, schedule: string) =>
    plans.data?.find(p => p.defaultForSchedules.includes(schedule))?.id ?? plans.data?.find(p => p.defaultFor.includes(company))?.id ?? ''

  const { register, handleSubmit, control, setError, setValue, getValues, formState: { errors, isSubmitting, dirtyFields } } = useForm<Values>({
    // Each field is checked as soon as it is left, then as it changes.
    mode: 'onTouched',
    defaultValues: {
      firstName: employee?.firstName ?? '', middleName: employee?.middleName ?? '', lastName: employee?.lastName ?? '',
      dateOfBirth: employee?.dateOfBirth ?? '', phone: employee?.phone ?? '', email: employee?.email ?? '', address: employee?.address ?? '', staffNo: employee?.staffNo ?? '',
      hireDate: employee?.hireDate ?? '', exitDate: employee?.exitDate ?? '',
      nationalId: '', kraPin: '', nssfNo: employee?.nssfNo ?? '', shaNo: employee?.shaNo ?? '',
      companyId: companyStart,
      departmentId: employee?.job.departmentId ?? '', positionId: employee?.job.positionId ?? '', unitId: employee?.job.unitId ?? '',
      locationId: employee?.job.locationId ?? '', lineManagerId: employee?.job.lineManagerId ?? '',
      payScheduleId: employee?.job.payScheduleId ?? '', leavePlanId: employee?.job.leavePlanId ?? '',
      employmentType: employee?.job.employmentType ?? 'permanent', contractEnd: employee?.job.contractEnd ?? '',
      basicAmount: employee?.pay ? String(employee.pay.basicAmount) : '',
      method: employee?.account?.method ?? 'none', bankCode: employee?.account?.bankCode ?? '', branchCode: employee?.account?.branchCode ?? '',
      accountNumber: '', mpesaPhone: '',
    },
  })
  const [companyId, scheduleId, method, bankCode, type, departmentId] =
    useWatch({ control, name: ['companyId', 'payScheduleId', 'method', 'bankCode', 'employmentType', 'departmentId'] })
  // Date left only opens for someone who no longer works here.
  const [stillHere, setStillHere] = useState(!employee?.exitDate)
  const company = companies.find(c => c.id === companyId) ?? companies[0]

  // A new employee starts with the company's schedule, leave plan and main location, once they're loaded.
  useEffect(() => {
    if (employee || !schedules.data || !plans.data || !locations.data) return
    const schedule = getValues('payScheduleId') || defaultSchedule(companyId)
    setValue('payScheduleId', schedule)
    setValue('leavePlanId', defaultPlan(companyId, schedule))
    if (!getValues('locationId')) setValue('locationId', locations.data.find(l => l.mainFor.includes(companyId))?.id ?? '')
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [employee, schedules.data, plans.data, locations.data, companyId])

  const banks = useBanks(company.countryCode)
  const paysBy = usePaysBy(companyId).data
  const methods = paysBy?.methods ?? []
  // A new employee is paid the way the company usually pays.
  useEffect(() => {
    if (!employee && paysBy?.usual && getValues('method') === 'none') setValue('method', paysBy.usual)
  }, [employee, paysBy, getValues, setValue])
  const paysByMessage = () => (methods.length > 1 ? t('employees.rules.paysByEither')
    : t(methods[0] === 'bank' ? 'employees.rules.paysByBank' : 'employees.rules.paysByMpesa', { company: company.legalName }))
  const managers = useQuery({
    queryKey: ['employees', 'choices', companyId],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees/choices', { params: { query: { companyId } } })
      if (error || !data) throw new Error('choices')
      return data
    },
  })

  type Scoped = { id: string; allCompanies: boolean; companyIds: string[] }
  const serves = <T extends Scoped>(items: T[] | undefined) => (items ?? []).filter(i => i.allCompanies || i.companyIds.includes(companyId))
  const schedule = schedules.data?.find(s => s.id === scheduleId)
  const frequency = schedule?.frequency ?? 'monthly'
  const sameDepartmentFirst = [...(managers.data ?? [])].filter(m => m.id !== employee?.id)
    .sort((a, b) => Number(b.departmentId === departmentId && !!departmentId) - Number(a.departmentId === departmentId && !!departmentId))

  const invalidTabs = new Set(Object.keys(errors).map(name => tabOf[name]).filter(Boolean))

  const onSubmit = async (v: Values) => {
    setBanner(null)
    if (!v.hireDate) {
      setError('hireDate', { message: t('employees.hireDateRequired') })
      return setTab('personal')
    }
    if (!stillHere && !v.exitDate) {
      setError('exitDate', { message: t('employees.exitDateRequired') })
      return setTab('personal')
    }
    // People who work here are paid the way the company pays.
    if (canManagePay && stillHere && methods.length > 0 && !methods.includes(v.method as 'bank' | 'mpesa')) {
      setError('method', { message: paysByMessage() })
      return setTab('account')
    }
    const accountTouched = !employee || ['method', 'bankCode', 'branchCode', 'accountNumber', 'mpesaPhone'].some(k => dirtyFields[k as keyof Values])
    const body = {
      companyId: v.companyId, staffNo: v.staffNo.trim() || null, firstName: v.firstName, middleName: v.middleName || null, lastName: v.lastName,
      dateOfBirth: v.dateOfBirth || null, phone: v.phone, email: v.email, address: v.address,
      // Saved numbers stay unless retyped (they're never sent back to the screen).
      nationalId: v.nationalId.trim() || null, kraPin: v.kraPin.trim() || null, nssfNo: v.nssfNo, shaNo: v.shaNo,
      hireDate: v.hireDate, exitDate: v.exitDate || null,
      job: {
        positionId: v.positionId || null, departmentId: v.departmentId || null, unitId: v.unitId || null, locationId: v.locationId || null,
        lineManagerId: v.lineManagerId || null, payScheduleId: v.payScheduleId || null, leavePlanId: v.leavePlanId || null,
        employmentType: v.employmentType, contractEnd: v.employmentType === 'contract' && v.contractEnd ? v.contractEnd : null,
      },
      // Paid as often as their schedule runs (the API takes it from the schedule).
      pay: canManagePay && v.basicAmount.trim() ? { basicAmount: Number(v.basicAmount.replace(/kes|ksh|[\s,]/gi, '')), payBasis: null } : null,
      account: !canManagePay || !accountTouched || v.method === 'none' ? null
        : v.method === 'bank' ? { method: 'bank', bankCode: v.bankCode, branchCode: v.branchCode, accountNumber: v.accountNumber.trim() || null }
          : { method: 'mpesa', mpesaPhone: v.mpesaPhone.trim() || null },
    }
    try {
      const { data, error } = employee
        ? await api.PATCH('/api/employees/{id}', { params: { path: { id: employee.id } }, body: body as never })
        : await api.POST('/api/employees', { body: body as never })
      if (data) {
        void queryClient.invalidateQueries({ queryKey: ['employees'] })
        void queryClient.invalidateQueries({ queryKey: ['employee', employee?.id] })
        toast.show(employee ? t('employees.saved') : t('employees.added'))
        onClose()
        return
      }
      showError(error)
    } catch {
      setBanner('network')
    }
  }

  const showError = (error: unknown) => {
    if (!isApiError(error)) return setBanner('network')
    const known = Object.entries(error.fields ?? {}).map(([name, message]) => [fieldOf(name), message] as const).filter(([field]) => field)
    for (const [field, message] of known) setError(field!, { message })
    if (known.length === 0) setBanner(error)
    else setTab(tabOf[known[0][0]!])
  }

  /** A field's own check, as the API makes it: its message, or true. */
  const check = (rule: (value: string) => string | null) => ({
    validate: (value: string) => {
      const key = rule(value)
      return key ? t(`employees.rules.${key}`) : true
    },
  })
  const needed = (when: (v: Values) => boolean) => ({
    validate: (value: string, v: Values) => (when(v) && !value.trim() ? t('employees.rules.required') : true),
  })
  const saved = (masked: string | null | undefined, hint: string) => (employee && masked ? t('employees.saved_', { masked }) : hint)
  const select = (name: FieldPath<Values>, label: string, placeholder: string, options: { value: string; label: string }[],
    extra: { optional?: boolean; hint?: string; disabled?: boolean } = {}, required?: (v: Values) => boolean) => (
    <Controller control={control} name={name} rules={required ? needed(required) : undefined} render={({ field }) => (
      <SelectField label={label} placeholder={placeholder} error={errors[name]?.message} options={options} {...extra}
        value={String(field.value ?? '')} onChange={field.onChange} onBlur={field.onBlur} name={field.name} />
    )} />
  )
  const bank = banks.data?.find(b => b.code === bankCode)
  const keepsAccount = (m: string) => !!employee?.account && employee.account.method === m

  return (
    <Drawer open size="wide" title={employee ? t('employees.editTitle') : t('employees.addTitle')} onClose={onClose}
      tabs={<Tabs id={tabsId} label={t('employees.sections')} current={tab} onChange={k => setTab(k as TabKey)}
        tabs={formTabs.map(k => ({ key: k, label: t(`employees.tabs.${k}`), invalid: invalidTabs.has(k) }))} />}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{employee ? t('employees.save') : t('employees.add')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit, found => { const first = Object.keys(found)[0]; if (tabOf[first]) setTab(tabOf[first]) })} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}

        {!canManagePay && <Notice>{t('employees.payrollAddsPay')}</Notice>}
        <TabPanel id={tabsId} tab="personal" current={tab}>
          <div className="grid gap-4 sm:grid-cols-3">
            <TextField label={t('employees.firstName')} placeholder={t('employees.firstNamePlaceholder')} maxLength={60} error={errors.firstName?.message} {...register('firstName', check(rules.name))} />
            <TextField label={t('employees.middleName')} placeholder={t('employees.middleNamePlaceholder')} optional maxLength={60} error={errors.middleName?.message} {...register('middleName')} />
            <TextField label={t('employees.lastName')} placeholder={t('employees.lastNamePlaceholder')} maxLength={60} error={errors.lastName?.message} {...register('lastName', check(rules.name))} />
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField type="date" label={t('employees.hireDate')} placeholder={t('employees.hireDatePlaceholder')} error={errors.hireDate?.message} {...register('hireDate', check(rules.required))} />
            <div className="flex flex-col gap-2">
              <Checkbox label={t('employees.stillHere')} checked={stillHere}
                onChange={e => { setStillHere(e.target.checked); if (e.target.checked) setValue('exitDate', '', { shouldDirty: true }) }} />
              <TextField type="date" label={t('employees.exitDate')} placeholder={t('employees.exitDatePlaceholder')} disabled={stillHere}
                hint={t('employees.exitDateHint')} error={errors.exitDate?.message} {...register('exitDate')} />
            </div>
            <TextField type="date" label={t('employees.dateOfBirth')} placeholder={t('employees.dateOfBirthPlaceholder')} optional error={errors.dateOfBirth?.message} {...register('dateOfBirth')} />
            <TextField label={t('employees.staffNo')} placeholder={t('employees.staffNoPlaceholder')} optional hint={t('employees.staffNoHint')} maxLength={20}
              error={errors.staffNo?.message} {...register('staffNo', check(rules.staffNo))} />
            <TextField type="tel" label={t('employees.phone')} placeholder={t('employees.phonePlaceholder')} optional error={errors.phone?.message} {...register('phone', check(rules.phone))} />
            <TextField type="email" label={t('employees.email')} placeholder={t('employees.emailPlaceholder')} optional error={errors.email?.message} {...register('email', check(rules.email))} />
            <div className="sm:col-span-2">
              <TextField label={t('employees.address')} placeholder={t('employees.addressPlaceholder')} optional maxLength={200} hint={t('employees.addressHint')}
                error={errors.address?.message} {...register('address')} />
            </div>
          </div>
        </TabPanel>

        <TabPanel id={tabsId} tab="ids" current={tab}>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label={t('employees.nationalId')} placeholder={t('employees.nationalIdPlaceholder')} optional inputMode="numeric"
              hint={saved(employee?.nationalIdMasked, t('employees.nationalIdHint'))} error={errors.nationalId?.message} {...register('nationalId', check(rules.nationalId))} />
            <TextField label={t('employees.kraPin')} placeholder={t('employees.kraPinPlaceholder')} optional autoCapitalize="characters"
              hint={saved(employee?.kraPinMasked, t('employees.kraPinHint'))} error={errors.kraPin?.message} {...register('kraPin', check(rules.kraPin))} />
            <TextField label={t('employees.nssfNo')} placeholder={t('employees.nssfNoPlaceholder')} optional inputMode="numeric" hint={t('employees.nssfNoHint')}
              error={errors.nssfNo?.message} {...register('nssfNo', check(rules.nssfNo))} />
            <TextField label={t('employees.shaNo')} placeholder={t('employees.shaNoPlaceholder')} optional hint={t('employees.shaNoHint')}
              error={errors.shaNo?.message} {...register('shaNo', check(rules.shaNo))} />
          </div>
        </TabPanel>

        <TabPanel id={tabsId} tab="job" current={tab}>
          {companies.length > 1 && !employee && select('companyId', t('employees.company'), t('employees.companyPlaceholder'),
            companies.map(c => ({ value: c.id, label: c.legalName })))}
          <div className="grid gap-4 sm:grid-cols-2">
            {select('departmentId', t('employees.department'), t('employees.departmentPlaceholder'), serves(departments.data).map(d => ({ value: d.id, label: d.name ?? '' })), { optional: true })}
            {select('positionId', t('employees.position'), t('employees.positionPlaceholder'), serves(positions.data).map(p => ({ value: p.id, label: p.title ?? '' })), { optional: true })}
            {select('unitId', t('employees.unit'), t('employees.unitPlaceholder'), serves(units.data).map(u => ({ value: u.id, label: u.name ?? '' })), { optional: true })}
            {select('lineManagerId', t('employees.lineManager'), t('employees.lineManagerPlaceholder'),
              sameDepartmentFirst.map(m => ({ value: m.id, label: `${m.name} (${m.staffNo})` })), { optional: true, hint: t('employees.lineManagerHint') })}
            <Controller control={control} name="payScheduleId" render={({ field }) => (
              <SelectField label={t('employees.payrollSchedule')} placeholder={t('employees.payrollSchedulePlaceholder')} error={errors.payScheduleId?.message}
                options={serves(schedules.data).map(s => ({ value: s.id, label: s.name }))} value={field.value} name={field.name} onBlur={field.onBlur}
                // A schedule with its own leave plan brings it along.
                onChange={value => { field.onChange(value); setValue('leavePlanId', defaultPlan(companyId, value)) }} />
            )} />
            {select('leavePlanId', t('employees.leavePlan'), t('employees.leavePlanPlaceholder'), serves(plans.data).map(p => ({ value: p.id, label: p.name })),
              { hint: t('employees.leavePlanHint') })}
          </div>
          <Controller control={control} name="employmentType" render={({ field }) => (
            <RadioGroup label={t('employees.employmentType')} name={field.name} value={field.value}
              // Only a contract has an end date.
              onChange={value => { field.onChange(value); if (value !== 'contract') setValue('contractEnd', '') }}
              options={types.map(k => ({ value: k, label: t(`employees.types.${k}`) }))} />
          )} />
          <div className="max-w-sm">
            <TextField type="date" label={t('employees.contractEnd')} placeholder={t('employees.contractEndPlaceholder')} optional disabled={type !== 'contract'}
              error={errors.contractEnd?.message} {...register('contractEnd')} />
          </div>
        </TabPanel>

        <TabPanel id={tabsId} tab="pay" current={tab}>
          <div className="max-w-sm">
            <TextField label={t(`employees.amountLabels.${frequency}`)} placeholder={t('employees.amountPlaceholder')} optional inputMode="decimal"
              hint={t('employees.amountHint', { frequency: t(`employees.frequencies.${frequency}`) })}
              error={errors.basicAmount?.message} {...register('basicAmount', check(rules.amount))} />
          </div>
        </TabPanel>

        <TabPanel id={tabsId} tab="account" current={tab}>
          <Controller control={control} name="method" render={({ field }) => (
            <RadioGroup label={t('employees.method')} name={field.name} value={field.value} onChange={field.onChange} error={errors.method?.message}
              hint={methods.length === 1 && stillHere ? t('employees.paysByHint', { company: company.legalName, method: t(`employees.methods.${methods[0]}`) }) : undefined}
              options={(['bank', 'mpesa', 'none'] as const).map(m => ({ value: m, label: t(`employees.methods.${m}`) }))} />
          )} />
          {method === 'bank' && (
            <div className="grid gap-4 sm:grid-cols-2">
              <Controller control={control} name="bankCode" rules={needed(v => v.method === 'bank')} render={({ field }) => (
                <SelectField label={t('employees.bank')} placeholder={t('employees.bankPlaceholder')} error={errors.bankCode?.message}
                  options={(banks.data ?? []).map(b => ({ value: b.code, label: b.name }))} value={field.value} name={field.name} onBlur={field.onBlur}
                  // Another bank has other branches.
                  onChange={value => { field.onChange(value); setValue('branchCode', '') }} />
              )} />
              {select('branchCode', t('employees.branch'), t('employees.branchPlaceholder'), (bank?.branches ?? []).map(b => ({ value: b.code, label: `${b.name} (${b.code})` })),
                { disabled: !bankCode, hint: bankCode ? undefined : t('employees.rules.bankFirst') }, v => v.method === 'bank')}
              <TextField label={t('employees.accountNumber')} placeholder={t('employees.accountNumberPlaceholder')} inputMode="numeric"
                hint={employee?.account?.accountMasked ? t('employees.keepAccount', { masked: employee.account.accountMasked }) : undefined}
                error={errors.accountNumber?.message} {...register('accountNumber', {
                  validate: (value: string, v: Values) => {
                    if (v.method !== 'bank') return true
                    if (!value.trim()) return keepsAccount('bank') && v.bankCode === employee?.account?.bankCode ? true : t('employees.rules.required')
                    const problem = accountProblem(banks.data?.find(b => b.code === v.bankCode), value)
                    return problem ? t(`employees.rules.${problem.key}`, problem.values) : true
                  },
                })} />
            </div>
          )}
          {method === 'mpesa' && (
            <div className="max-w-sm">
              <TextField type="tel" label={t('employees.mpesa')} placeholder={t('employees.mpesaPlaceholder')}
                hint={employee?.account?.mpesaMasked ? t('employees.keepAccount', { masked: employee.account.mpesaMasked }) : t('employees.mpesaHint')}
                error={errors.mpesaPhone?.message} {...register('mpesaPhone', {
                  validate: (value: string, v: Values) => {
                    if (v.method !== 'mpesa') return true
                    if (!value.trim()) return keepsAccount('mpesa') ? true : t('employees.rules.required')
                    return rules.mpesa(value) ? t('employees.rules.mpesa') : true
                  },
                })} />
            </div>
          )}
        </TabPanel>
      </form>
    </Drawer>
  )
}
