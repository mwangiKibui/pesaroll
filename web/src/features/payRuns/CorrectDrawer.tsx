import { useQuery } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { errorMessage, isApiError } from '../../app/apiErrors'
import { useServices, type ApiError } from '../../app/services'
import { Button } from '../../components/Button'
import { Drawer } from '../../components/Drawer'
import { FormErrorBanner, Notice } from '../../components/feedback'
import { MultiSelectField } from '../../components/MultiSelectField'
import { useToast } from '../../components/toast'

/**
 * Corrects an approved run (PAY-03, OP-18): the people whose pay was wrong, chosen from those it paid.
 * The correction is a run of its own, opened once started, and pays only the difference.
 */
export function CorrectDrawer({ runId, onClose }: { runId: string; onClose: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const navigate = useNavigate()
  const formId = useId()
  const [chosen, setChosen] = useState<string[]>([])
  const [error, setError] = useState<string>()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const [busy, setBusy] = useState(false)
  const people = useQuery({
    queryKey: ['pay-run-people', runId, 'all'],
    queryFn: async () => {
      const { data } = await api.GET('/api/pay-runs/{id}/people', { params: { path: { id: runId }, query: { page: 1, pageSize: 100 } } })
      if (!data) throw new Error('pay-run-people')
      return data.items
    },
  })

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (chosen.length === 0) return setError(t('payRun.correctNobody'))
    setBusy(true)
    setBanner(null)
    setError(undefined)
    try {
      const { data, error: problem } = await api.POST('/api/pay-runs/{id}/corrections', { params: { path: { id: runId } }, body: { employeeIds: chosen } })
      if (data) {
        void queryClient.invalidateQueries({ queryKey: ['pay-runs'] })
        toast.show(t('payRun.correctionStarted'))
        onClose()
        void navigate({ to: '/pay/runs/$id', params: { id: data.id } })
        return
      }
      setBanner(isApiError(problem) ? problem : 'network')
    } catch {
      setBanner('network')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Drawer open title={t('payRun.correctTitle')} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={busy}>{t('payRun.startCorrection')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={submit} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <p className="m-0 text-muted">{t('payRun.correctIntro')}</p>
        <MultiSelectField label={t('payRun.correctPeople')} placeholder={t('payRun.correctPeoplePlaceholder')} error={error} value={chosen}
          options={(people.data ?? []).map(p => ({ value: p.employeeId, label: p.name }))} onChange={setChosen} />
      </form>
    </Drawer>
  )
}
