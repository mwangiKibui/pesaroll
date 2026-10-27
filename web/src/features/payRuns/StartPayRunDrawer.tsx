import { useQuery } from '@tanstack/react-query'
import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { errorMessage, isApiError } from '../../app/apiErrors'
import { permissions, usePermissions } from '../../app/permissions'
import { useServices, type ApiError } from '../../app/services'
import { Button } from '../../components/Button'
import { Drawer } from '../../components/Drawer'
import { FormErrorBanner, Notice } from '../../components/feedback'
import { SelectField } from '../../components/fields'
import { useToast } from '../../components/toast'
import { todayInKenya } from '../schedules/deductions'
import { longDate } from '../schedules/wording'
import { monthName, monthOf } from './wording'

type Company = components['schemas']['CompanyResponse']
type Schedule = components['schemas']['PayScheduleResponse']
type Field = 'companyId' | 'payScheduleId' | 'month' | 'payDate'

/**
 * Starts a pay run (PAY-03, OP-18): the company (the top bar's to start with, among those the person
 * runs payroll for), one of its payroll schedules (its default to start with), and the month, or for
 * weekly, fortnightly and daily schedules the pay date. What changes each run is entered after.
 */
export function StartPayRunDrawer({ companies, companyId, onClose }: { companies: Company[]; companyId: string; onClose: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const formId = useId()
  const { can } = usePermissions()
  const [today] = useState(todayInKenya)
  const [company, setCompany] = useState(companyId)
  const [scheduleId, setScheduleId] = useState<string | null>(null)
  const [month, setMonth] = useState(monthOf(today))
  const [payDate, setPayDate] = useState<string | null>(null)
  const [errors, setErrors] = useState<Partial<Record<Field, string>>>({})
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const [busy, setBusy] = useState(false)

  const schedules = useQuery({
    queryKey: ['schedules', company],
    queryFn: async () => {
      const { data } = await api.GET('/api/pay-schedules', { params: { query: { companyId: company } } })
      if (!data) throw new Error('schedules')
      return data
    },
  })
  const list: Schedule[] = schedules.data ?? []
  // The company's default schedule to start with, then the first.
  const schedule = list.find(s => s.id === scheduleId) ?? list.find(s => s.defaultFor.includes(company)) ?? list[0]
  const monthly = !schedule || schedule.frequency === 'monthly'
  const date = payDate && schedule?.nextPayDates.includes(payDate) ? payDate : schedule?.nextPayDates[0] ?? null
  const months = [-1, 0, 1].map(n => monthOf(today, n))

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!schedule) return setErrors({ payScheduleId: t('payRuns.scheduleRequired') })
    setBusy(true)
    setBanner(null)
    setErrors({})
    try {
      const { data, error } = await api.POST('/api/pay-runs', {
        body: { companyId: company, payScheduleId: schedule.id, month: monthly ? month : null, payDate: monthly ? null : date },
      })
      if (data) {
        void queryClient.invalidateQueries({ queryKey: ['pay-runs'] })
        toast.show(t('payRuns.started'))
        onClose()
        return
      }
      if (!isApiError(error)) return setBanner('network')
      const known = Object.entries(error.fields ?? {}).filter(([name]) => ['companyId', 'payScheduleId', 'month', 'payDate'].includes(name))
      setErrors(Object.fromEntries(known))
      if (known.length === 0) setBanner(error)
    } catch {
      setBanner('network')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Drawer open title={t('payRuns.start')} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={busy}>{t('payRuns.start')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={submit} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <SelectField label={t('payRuns.company')} placeholder={t('payRuns.companyPlaceholder')} error={errors.companyId} value={company}
          options={companies.filter(c => can(permissions.payRuns.manage, c.id)).map(c => ({ value: c.id, label: c.legalName }))}
          onChange={id => { setCompany(id); setScheduleId(null); setPayDate(null) }} />
        <SelectField label={t('payRuns.schedule')} placeholder={t('payRuns.schedulePlaceholder')} error={errors.payScheduleId} value={schedule?.id ?? ''}
          options={list.map(s => ({ value: s.id, label: s.name }))} onChange={id => { setScheduleId(id); setPayDate(null) }} />
        {monthly
          ? <SelectField label={t('payRuns.month')} placeholder={t('payRuns.monthPlaceholder')} error={errors.month} value={month}
              options={months.map(m => ({ value: m, label: monthName(`${m}-01`) }))} onChange={setMonth} />
          : <SelectField label={t('payRuns.payDate')} placeholder={t('payRuns.payDatePlaceholder')} error={errors.payDate} value={date ?? ''}
              options={(schedule?.nextPayDates ?? []).map(d => ({ value: d, label: longDate(d) }))} onChange={setPayDate} />}
        <Notice tone="brand">{t('payRuns.startHint')}</Notice>
      </form>
    </Drawer>
  )
}
