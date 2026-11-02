import { useQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { errorMessage, isApiError } from '../../app/apiErrors'
import { useServices, type ApiError } from '../../app/services'
import { Button } from '../../components/Button'
import { Drawer } from '../../components/Drawer'
import { FormErrorBanner, Notice } from '../../components/feedback'
import { TextField } from '../../components/fields'
import { useToast } from '../../components/toast'
import { dayMonthYear } from '../schedules/wording'

type Hours = components['schemas']['WorkingHoursResponse']
type Field = 'hoursPerDay' | 'hoursPerMonth'

/**
 * A company's working hours (PAY-11), which turn a daily rate or a salary into an hourly one for
 * overtime: a button on Pay runs saying what's used, opening a drawer to see whose they are (the
 * company's own or the wage order's) and, with Manage on pay, to set or clear them.
 */
export function WorkingHours({ companyId, companyName, canManage }: { companyId: string; companyName: string; canManage: boolean }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const [open, setOpen] = useState(false)
  const hours = useQuery({
    queryKey: ['working-hours', companyId],
    queryFn: async () => {
      const { data } = await api.GET('/api/companies/{companyId}/working-hours', { params: { path: { companyId } } })
      if (!data) throw new Error('working-hours')
      return data
    },
  })
  if (!hours.data) return null
  const h = hours.data
  return <>
    <Button variant="ghost" onClick={() => setOpen(true)}>
      {h.usedPerDay == null ? t('workingHours.buttonNone') : t('workingHours.button', { day: Number(h.usedPerDay), month: Number(h.usedPerMonth) })}
    </Button>
    {open && <HoursDrawer hours={h} companyName={companyName} canManage={canManage} onClose={() => setOpen(false)} />}
  </>
}

function HoursDrawer({ hours: h, companyName, canManage, onClose }: { hours: Hours; companyName: string; canManage: boolean; onClose: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const own = h.hoursPerDay != null || h.hoursPerMonth != null
  const [day, setDay] = useState(h.hoursPerDay == null ? '' : String(Number(h.hoursPerDay)))
  const [month, setMonth] = useState(h.hoursPerMonth == null ? '' : String(Number(h.hoursPerMonth)))
  const [errors, setErrors] = useState<Partial<Record<Field, string>>>({})
  const [problem, setProblem] = useState<ApiError | 'network' | null>(null)
  const [busy, setBusy] = useState(false)
  const used = { company: companyName, day: Number(h.usedPerDay), month: Number(h.usedPerMonth) }

  const save = async (e: FormEvent) => {
    e.preventDefault()
    const read = (text: string, max: number) => text.trim() ? (Number(text) > 0 && Number(text) <= max ? Number(text) : undefined) : null
    const body = { hoursPerDay: read(day, 24), hoursPerMonth: read(month, 744) }
    const found: Partial<Record<Field, string>> = {
      ...(body.hoursPerDay === undefined ? { hoursPerDay: t('workingHours.dayInvalid') } : {}),
      ...(body.hoursPerMonth === undefined ? { hoursPerMonth: t('workingHours.monthInvalid') } : {}),
    }
    setErrors(found)
    if (Object.keys(found).length > 0) return
    setBusy(true)
    setProblem(null)
    try {
      const { data, error } = await api.PUT('/api/companies/{companyId}/working-hours', { params: { path: { companyId: h.companyId } }, body: body as { hoursPerDay: number | null; hoursPerMonth: number | null } })
      if (data) {
        queryClient.setQueryData(['working-hours', h.companyId], data)
        toast.show(t('workingHours.saved'))
        onClose()
        return
      }
      if (!isApiError(error)) return setProblem('network')
      const fields = Object.fromEntries(Object.entries(error.fields ?? {}).filter(([k]) => k === 'hoursPerDay' || k === 'hoursPerMonth'))
      if (Object.keys(fields).length > 0) setErrors(fields)
      else setProblem(error)
    } catch {
      setProblem('network')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Drawer open title={t('workingHours.title')} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{canManage ? t('common.cancel') : t('common.close')}</Button>
        {canManage && <Button type="submit" form="working-hours" busy={busy}>{t('common.save')}</Button>}
      </>}>
      <form id="working-hours" noValidate onSubmit={e => void save(e)} className="flex flex-col gap-4">
        {problem === 'network' ? <Notice tone="danger">{t('apiErrors.network')}</Notice>
          : problem && <FormErrorBanner error={{ ...problem, message: errorMessage(t, problem) }} />}
        <p className="m-0 text-muted">{t('workingHours.intro')}</p>
        <p className="m-0 text-body text-ink">
          {h.usedPerDay == null ? t('workingHours.none', { company: companyName })
            : own ? t('workingHours.own', { ...used, date: h.setAt ? dayMonthYear(new Date(h.setAt), 'Africa/Nairobi') : '' })
              : t('workingHours.wageOrder', used)}
        </p>
        {canManage && <>
          <TextField label={t('workingHours.day')} placeholder={t('workingHours.dayPlaceholder')} inputMode="decimal" optional hint={t('workingHours.emptyHint')}
            error={errors.hoursPerDay} value={day} onChange={e => setDay(e.target.value)} />
          <TextField label={t('workingHours.month')} placeholder={t('workingHours.monthPlaceholder')} inputMode="decimal" optional hint={t('workingHours.emptyHint')}
            error={errors.hoursPerMonth} value={month} onChange={e => setMonth(e.target.value)} />
        </>}
      </form>
    </Drawer>
  )
}
