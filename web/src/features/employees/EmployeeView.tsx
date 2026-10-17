import { useQuery } from '@tanstack/react-query'
import { useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { permissions, usePermissions } from '../../app/permissions'
import { useServices } from '../../app/services'
import { Button } from '../../components/Button'
import { Drawer } from '../../components/Drawer'
import { Loading, Notice } from '../../components/feedback'
import { TabPanel, Tabs } from '../../components/Tabs'
import { formatMoney } from '../../lib/money'
import { useBanks, useLeavePlans, useLocations, useSchedules, useStructure, type StructureItem } from '../../setup/hooks'
import { none, shortDate, tabs, type Company, type TabKey } from './shared'

/**
 * An employee to read: the same tabs as editing, each detail as saved (ID number, KRA PIN and the
 * account masked), what payroll still needs on top, and Edit to change them.
 */
export function EmployeeView({ id, companies, onClose, onEdit }: { id: string; companies: Company[]; onClose: () => void; onEdit?: () => void }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const tabsId = useId()
  const [tab, setTab] = useState<TabKey>('personal')
  // ACC-05: pay and where it's paid only with View on pay, and only after Show amounts.
  const canViewPay = usePermissions().can(permissions.pay.view)
  const [showPay, setShowPay] = useState(false)
  const shownTabs = tabs.filter(k => canViewPay || (k !== 'pay' && k !== 'account'))
  const detail = useQuery({
    queryKey: ['employee', id, showPay],
    placeholderData: previous => previous,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees/{id}', { params: { path: { id }, query: { includePay: showPay || undefined } } })
      if (error || !data) throw new Error('employee')
      return data
    },
  })
  const e = detail.data
  const company = companies.find(c => c.id === e?.companyId) ?? companies[0]
  const schedules = useSchedules()
  const plans = useLeavePlans()
  const locations = useLocations()
  const departments = useStructure('departments')
  const positions = useStructure('positions')
  const units = useStructure('units')
  const banks = useBanks(company.countryCode)
  // What they get besides basic pay: their own items and their company's "everyone" items.
  const payItems = useQuery({
    queryKey: ['employee', id, 'payItems', showPay],
    enabled: canViewPay,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees/{id}/pay-items', { params: { path: { id }, query: { includePay: showPay || undefined } } })
      if (error || !data) throw new Error('payItems')
      return data
    },
  })
  const managers = useQuery({
    queryKey: ['employees', 'choices', company.id],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees/choices', { params: { query: { companyId: company.id } } })
      if (error || !data) throw new Error('choices')
      return data
    },
  })

  if (!e) {
    return <Drawer open size="wide" title={t('employees.viewTitle')} onClose={onClose}><Loading rows={6} /></Drawer>
  }
  const name = [e.firstName, e.middleName, e.lastName].filter(Boolean).join(' ')
  const nameOf = (items: StructureItem[] | undefined, itemId: string | null | undefined) => {
    const item = items?.find(i => i.id === itemId)
    return item ? item.name ?? item.title ?? null : null
  }
  const manager = managers.data?.find(m => m.id === e.job.lineManagerId)
  const bank = banks.data?.find(b => b.code === e.account?.bankCode)
  const branch = bank?.branches.find(b => b.code === e.account?.branchCode)
  const fields: Record<TabKey, [string, ReactNode][]> = {
    personal: [
      [t('employees.staffNo'), e.staffNo], [t('employees.firstName'), e.firstName], [t('employees.middleName'), e.middleName], [t('employees.lastName'), e.lastName],
      [t('employees.hireDate'), e.hireDate], [t('employees.exitDate'), e.exitDate ?? t('employees.stillHere')],
      [t('employees.dateOfBirth'), e.dateOfBirth], [t('employees.phone'), e.phone], [t('employees.email'), e.email], [t('employees.address'), e.address],
    ],
    ids: [
      [t('employees.nationalId'), e.nationalIdMasked], [t('employees.kraPin'), e.kraPinMasked], [t('employees.nssfNo'), e.nssfNo], [t('employees.shaNo'), e.shaNo],
    ],
    job: [
      [t('employees.company'), company.legalName], [t('employees.department'), nameOf(departments.data, e.job.departmentId)],
      [t('employees.position'), nameOf(positions.data, e.job.positionId)], [t('employees.unit'), nameOf(units.data, e.job.unitId)],
      [t('employees.location'), locations.data?.find(l => l.id === e.job.locationId)?.name],
      [t('employees.lineManager'), manager ? `${manager.name} (${manager.staffNo})` : null],
      [t('employees.payrollSchedule'), schedules.data?.find(s => s.id === e.job.payScheduleId)?.name],
      [t('employees.leavePlan'), plans.data?.find(p => p.id === e.job.leavePlanId)?.name],
      [t('employees.employmentType'), t(`employees.types.${e.job.employmentType}`)], [t('employees.contractEnd'), e.job.contractEnd],
    ],
    pay: [
      [t('employees.basicPay'), e.payHidden ? t('employees.payDots')
        : e.pay ? t(`employees.per.${e.pay.payBasis}`, { amount: formatMoney(e.pay.basicAmount, { currency: e.pay.currency }) }) : null],
    ],
    account: e.payHidden ? [[t('employees.method'), t('employees.accountDots')]] : [
      [t('employees.method'), t(`employees.methods.${e.account?.method ?? 'none'}`)],
      ...(e.account?.method === 'bank' ? [
        [t('employees.bank'), bank?.name ?? e.account.bankCode], [t('employees.branch'), branch ? `${branch.name} (${branch.code})` : e.account.branchCode],
        [t('employees.accountNumber'), e.account.accountMasked],
      ] as [string, ReactNode][] : []),
      ...(e.account?.method === 'mpesa' ? [[t('employees.mpesa'), e.account.mpesaMasked]] as [string, ReactNode][] : []),
    ],
  }

  return (
    <Drawer open size="wide" title={name} onClose={onClose}
      tabs={<Tabs id={tabsId} label={t('employees.sections')} current={tab} onChange={k => setTab(k as TabKey)}
        tabs={shownTabs.map(k => ({ key: k, label: t(`employees.tabs.${k}`) }))} />}
      footer={<>
        {canViewPay && <Button variant="ghost" className="mr-auto" onClick={() => setShowPay(s => !s)}>{showPay ? t('employees.hideAmounts') : t('employees.showAmounts')}</Button>}
        <Button variant="secondary" onClick={onClose}>{t('common.close')}</Button>
        {onEdit && <Button onClick={onEdit}>{t('employees.editButton')}</Button>}
      </>}>
      {!e.readiness.ready && (
        <Notice>{t('employees.needs', { items: e.readiness.missing.map(m => m.label.en).join(', ') })}</Notice>
      )}
      {shownTabs.map(k => (
        <TabPanel key={k} id={tabsId} tab={k} current={tab}>
          <dl className="m-0 grid gap-x-6 gap-y-4 sm:grid-cols-2">
            {fields[k].map(([label, value]) => (
              <div key={label} className="flex flex-col gap-0.5">
                <dt className="text-label text-muted">{label}</dt>
                <dd className="m-0 text-body text-ink">{value || none}</dd>
              </div>
            ))}
          </dl>
          {k === 'pay' && (
            <section aria-label={t('employees.payItemsTitle')} className="flex flex-col gap-2">
              <h3 className="m-0 text-label font-semibold text-ink">{t('employees.payItemsTitle')}</h3>
              {payItems.data?.length === 0 && <p className="m-0 text-label text-muted">{t('employees.payItemsNone')}</p>}
              <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
                {(payItems.data ?? []).map(i => (
                  <li key={i.payItemId} className="text-body text-ink">
                    <span className="font-medium">{i.name}</span>{' · '}
                    {i.calcMethod === 'each_run' ? t('employees.payItemEachRun')
                      : e.payHidden ? t('employees.payDots')
                        : i.rate != null ? t('employees.payItemRate', { rate: Number(i.rate) })
                          : i.amount != null ? formatMoney(i.amount) : none}
                    {' · '}{t('employees.payItemFrom', { date: shortDate(i.startsOn) })}
                    {i.endsOn && <> {t('employees.payItemUntil', { date: shortDate(i.endsOn) })}</>}
                    {(i.payeeName || i.reference) && <>{' · '}{i.payeeName && i.reference ? t('employees.payItemPayeeMember', { payee: i.payeeName, reference: i.reference })
                      : i.payeeName ? t('employees.payItemPayee', { payee: i.payeeName }) : t('employees.payItemMember', { reference: i.reference })}</>}
                    {i.everyone && <span className="text-muted"> {t('employees.payItemEveryone')}</span>}
                  </li>
                ))}
              </ul>
            </section>
          )}
        </TabPanel>
      ))}
    </Drawer>
  )
}
