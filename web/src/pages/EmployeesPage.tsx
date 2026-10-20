import { useNavigate } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { EmployeesList } from '../features/employees/EmployeesList'
import { useSetupDone } from '../setup/hooks'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

/**
 * ONB-09, the setup step's Employees: every company at once, with the company selector. The list,
 * profile, form and upload are shared with People → Employees (OP-07, features/employees).
 */
export default function EmployeesPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const done = useSetupDone(true)

  return (
    <SetupLayout current="employees" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('employees.title')}</h1>
        <p className="m-0 text-muted">{t('employees.intro')}</p>
      </div>
      <EmployeesList after={<Proceed label={t('setup.proceed', { step: t('setup.steps.payItems') })}
        onProceed={() => void navigate({ to: '/setup/earnings-and-deductions' })} />} />
    </SetupLayout>
  )
}
