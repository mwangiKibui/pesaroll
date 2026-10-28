import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { errorMessage, isApiError } from '../../app/apiErrors'
import { useServices, type ApiError } from '../../app/services'
import { Button } from '../../components/Button'
import { FormErrorBanner, Notice } from '../../components/feedback'
import { Checkbox, SelectField, TextField } from '../../components/fields'
import { Modal } from '../../components/Modal'
import { useToast } from '../../components/toast'
import { formatMoney } from '../../lib/money'
import { amountOf, deductionGroups, entryFor } from './review'

type Inputs = components['schemas']['RunTimeResponse']
type Problem = ApiError | 'network' | null
type Errors = Partial<Record<'payItemId' | 'amount' | 'reason', string>>

/**
 * Sends review changes and keeps what the API answers: the run is a draft again, its last figures
 * stay on show until it's worked out, and what's entered is the API's.
 */
function useReviewSave(runId: string) {
  const { t } = useTranslation()
  const { queryClient } = useServices()
  const toast = useToast()
  const [problem, setProblem] = useState<Problem>(null)
  const [busy, setBusy] = useState(false)
  const send = async (call: () => Promise<{ data?: Inputs; error?: unknown }>, fields: (error: ApiError) => Errors | null): Promise<Errors | true> => {
    setBusy(true)
    setProblem(null)
    try {
      const { data, error } = await call()
      if (data) {
        queryClient.setQueryData(['pay-run-inputs', runId], data)
        void queryClient.invalidateQueries({ queryKey: ['pay-run', runId] })
        void queryClient.invalidateQueries({ queryKey: ['pay-runs'] })
        toast.show(t('payRun.review.saved'))
        return true
      }
      if (!isApiError(error)) { setProblem('network'); return {} }
      const known = fields(error)
      if (!known) setProblem(error)
      return known ?? {}
    } catch {
      setProblem('network')
      return {}
    } finally {
      setBusy(false)
    }
  }
  const banner = problem === 'network' ? <Notice tone="danger">{t('apiErrors.network')}</Notice>
    : problem ? <FormErrorBanner error={{ ...problem, message: errorMessage(t, problem) }} /> : null
  return { send, busy, banner }
}

/** The API's problems with the one entry sent, by field; none known means a banner. */
const entryFields = (error: ApiError): Errors | null => {
  const found = Object.fromEntries(Object.entries(error.fields ?? {}).flatMap(([field, message]) => {
    const name = /^entries\[0\]\.(payItemId|amount|reason)$/.exec(field)?.[1]
    return name ? [[name, message]] : []
  })) as Errors
  return Object.keys(found).length ? found : null
}

/**
 * Changes one line for this run alone (PAY-04): basic pay (its days or hours stay), an item, or a
 * statutory amount, always saying why. 0 removes an item. A line already changed can go back.
 */
export function ChangeLine({ runId, employeeId, inputs, payItemId, name, amount, onClose }: {
  runId: string; employeeId: string; inputs: Inputs; payItemId: string | null; name: string; amount: number | string; onClose: () => void
}) {
  const { t } = useTranslation()
  const { api } = useServices()
  const entry = entryFor(inputs, employeeId, payItemId)
  const changed = entry?.amount != null
  const [value, setValue] = useState(changed ? String(Number(entry.amount)) : '')
  const [reason, setReason] = useState(changed ? entry.reason ?? '' : '')
  const [errors, setErrors] = useState<Errors>({})
  const { send, busy, banner } = useReviewSave(runId)
  // Basic pay's entry also holds the days or hours worked, which stay as they are.
  const quantity = payItemId === null ? entry?.quantity ?? null : null

  const put = async (body: { amount: number | null; reason: string | null }) => {
    const done = await send(() => api.PUT('/api/pay-runs/{id}/inputs', { params: { path: { id: runId } }, body: { entries: [{ employeeId, payItemId, quantity, ...body }] } }), entryFields)
    if (done === true) onClose()
    else setErrors(done)
  }
  const save = (e: FormEvent) => {
    e.preventDefault()
    const n = amountOf(value)
    const found: Errors = {
      ...(n === null ? { amount: t('payRun.inputs.amountInvalid') } : {}),
      ...(!reason.trim() ? { reason: t('payRun.review.reasonRequired') } : {}),
    }
    setErrors(found)
    if (Object.keys(found).length === 0) void put({ amount: n, reason: reason.trim() })
  }

  return (
    <Modal open title={t('payRun.review.changeTitle', { name })} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        {changed && <Button variant="secondary" busy={busy} onClick={() => void put({ amount: null, reason: null })}>{t('payRun.review.undo')}</Button>}
        <Button type="submit" form="change-line" busy={busy}>{t('payRun.review.saveChange')}</Button>
      </>}>
      <form id="change-line" noValidate onSubmit={save} className="flex flex-col gap-4">
        {banner}
        <p className="m-0 text-muted">{t('payRun.review.changeIntro', { amount: formatMoney(amount) })}</p>
        <TextField label={t('payRun.review.amount')} placeholder={formatMoney(amount, { currency: false })} inputMode="decimal" error={errors.amount}
          value={value} onChange={e => setValue(e.target.value)} />
        <TextField label={t('payRun.reason')} placeholder={t('payRun.review.reasonPlaceholder')} error={errors.reason} value={reason} onChange={e => setReason(e.target.value)} />
      </form>
    </Modal>
  )
}

/** Adds one of the organization's items for this person in this run alone, saying why. */
export function AddItem({ runId, employeeId, items, onClose }: {
  runId: string; employeeId: string; items: { payItemId: string; name: string }[]; onClose: () => void
}) {
  const { t } = useTranslation()
  const { api } = useServices()
  const [payItemId, setPayItemId] = useState('')
  const [value, setValue] = useState('')
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<Errors>({})
  const { send, busy, banner } = useReviewSave(runId)

  const save = async (e: FormEvent) => {
    e.preventDefault()
    const n = amountOf(value)
    const found: Errors = {
      ...(!payItemId ? { payItemId: t('payRun.review.itemRequired') } : {}),
      ...(n === null ? { amount: t('payRun.inputs.amountInvalid') } : {}),
      ...(!reason.trim() ? { reason: t('payRun.review.reasonRequired') } : {}),
    }
    setErrors(found)
    if (Object.keys(found).length > 0) return
    const done = await send(() => api.PUT('/api/pay-runs/{id}/inputs', {
      params: { path: { id: runId } }, body: { entries: [{ employeeId, payItemId, quantity: null, amount: n, reason: reason.trim() }] },
    }), entryFields)
    if (done === true) onClose()
    else setErrors(done)
  }

  return (
    <Modal open title={t('payRun.review.add')} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form="add-item" busy={busy}>{t('payRun.review.addButton')}</Button>
      </>}>
      <form id="add-item" noValidate onSubmit={e => void save(e)} className="flex flex-col gap-4">
        {banner}
        <SelectField label={t('payRun.review.item')} placeholder={t('payRun.review.itemPlaceholder')} error={errors.payItemId} value={payItemId}
          options={items.map(i => ({ value: i.payItemId, label: i.name }))} onChange={setPayItemId} />
        <TextField label={t('payRun.review.amount')} placeholder={t('payRun.inputs.amount')} inputMode="decimal" error={errors.amount}
          value={value} onChange={e => setValue(e.target.value)} />
        <TextField label={t('payRun.reason')} placeholder={t('payRun.review.reasonPlaceholder')} error={errors.reason} value={reason} onChange={e => setReason(e.target.value)} />
      </form>
    </Modal>
  )
}

/**
 * The statutory deductions for this person in this run: their usual ones (theirs or the schedule's),
 * or ones chosen for this run with why. Going back to the usual ones needs no reason.
 */
export function RunDeductions({ runId, employeeId, name, inputs, canEdit }: { runId: string; employeeId: string; name: string; inputs: Inputs; canEdit: boolean }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const chosen = inputs.people.find(p => p.employeeId === employeeId)?.deductions ?? null
  const [choosing, setChoosing] = useState(false)
  const [ticked, setTicked] = useState<string[]>([])
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<Partial<Record<'deductions' | 'reason', string>>>({})
  const { send, busy, banner } = useReviewSave(runId)
  const label = (g: string) => t(`payRun.review.groups.${g}`)
  const put = (deductions: string[] | null, why: string | null) =>
    send(() => api.PUT('/api/pay-runs/{id}/people/{employeeId}/deductions', { params: { path: { id: runId, employeeId } }, body: { deductions, reason: why } }),
      error => {
        const found = Object.fromEntries(Object.entries(error.fields ?? {}).filter(([f]) => f === 'deductions' || f === 'reason'))
        return Object.keys(found).length ? found : null
      })

  const open = () => {
    setTicked(chosen ? [...chosen.deductions] : [...deductionGroups])
    setReason(chosen?.reason ?? '')
    setErrors({})
    setChoosing(true)
  }
  const save = async (e: FormEvent) => {
    e.preventDefault()
    if (!reason.trim()) return setErrors({ reason: t('payRun.review.deductionsReason') })
    const done = await put(deductionGroups.filter(g => ticked.includes(g)), reason.trim())
    if (done === true) setChoosing(false)
    else setErrors(done)
  }

  return (
    <section aria-labelledby="run-deductions" className="flex flex-col gap-2">
      <h3 id="run-deductions" className="m-0 text-label font-medium text-ink">{t('payRun.review.deductions')}</h3>
      {!choosing && banner}
      <p className="m-0 text-body text-ink">
        {chosen
          ? t('payRun.review.chosen', { list: chosen.deductions.length ? chosen.deductions.map(label).join(', ') : t('payRun.review.noneChosen'), reason: chosen.reason })
          : t('payRun.review.usual', { name })}
      </p>
      {canEdit && <div className="flex flex-wrap gap-2">
        <Button variant="secondary" onClick={open}>{t('payRun.review.choose')}</Button>
        {chosen && <Button variant="ghost" busy={busy && !choosing} onClick={() => void put(null, null)}>{t('payRun.review.useUsual')}</Button>}
      </div>}
      <Modal open={choosing} title={t('payRun.review.deductionsTitle')} onClose={() => setChoosing(false)}
        footer={<>
          <Button variant="secondary" onClick={() => setChoosing(false)}>{t('common.cancel')}</Button>
          <Button type="submit" form="run-deductions-form" busy={busy}>{t('payRun.inputs.save')}</Button>
        </>}>
        <form id="run-deductions-form" noValidate onSubmit={e => void save(e)} className="flex flex-col gap-4">
          {banner}
          <fieldset className="m-0 flex flex-col border-0 p-0">
            <legend className="mb-1 text-label font-medium text-ink">{t('payRun.review.made')}</legend>
            {deductionGroups.map(g => (
              <Checkbox key={g} name={g} label={label(g)} checked={ticked.includes(g)}
                onChange={e => setTicked(v => e.target.checked ? [...v, g] : v.filter(x => x !== g))} />
            ))}
            {errors.deductions && <p className="m-0 text-hint font-medium text-danger-ink">{errors.deductions}</p>}
          </fieldset>
          <TextField label={t('payRun.reason')} placeholder={t('payRun.review.deductionsPlaceholder')} error={errors.reason} value={reason} onChange={e => setReason(e.target.value)} />
        </form>
      </Modal>
    </section>
  )
}
