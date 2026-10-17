import { useSyncExternalStore } from 'react'
import { useTranslation } from 'react-i18next'
import { OrgShell } from '../app/OrgShell'
import { useServices } from '../app/services'
import { Loading } from '../components/feedback'
import { Card } from '../components/TableCard'
import { nextSetupPath, useMe, useOrganization, useSetupDone } from '../setup/hooks'
import { setupSteps } from '../setup/steps'

/** Home for someone with an organization, in the organization portal's shell (OP-01). Until the HR home (OP-30): the setup progress. */
export default function HomePage() {
  const { t } = useTranslation()
  const { auth } = useServices()
  const me = useMe()
  const organization = useOrganization()
  const doneSteps = useSetupDone(!!useSyncExternalStore(auth.subscribe, auth.organizationId))
  const done = doneSteps.length
  // The first step not done yet (steps can be done out of order: earnings and deductions come with the first company).
  const next = t(`setup.steps.${(setupSteps.find(s => !doneSteps.includes(s.key)) ?? setupSteps[setupSteps.length - 1]).key}`)

  return (
    <OrgShell current="home" setup={{ done, total: setupSteps.length, next, href: nextSetupPath(doneSteps) }}>
      <div className="mx-auto flex max-w-3xl flex-col gap-5">
        {me.data && organization.data ? (
          <>
            <h1 className="m-0 font-display text-title text-ink">{t('home.title', { name: me.data.firstName })}</h1>
            <Card title={organization.data.name}>
              <p className="m-0 text-muted">{t('home.next', { step: next })}</p>
            </Card>
          </>
        ) : <Loading rows={3} />}
      </div>
    </OrgShell>
  )
}
