import { useQuery } from '@tanstack/react-query'
import { useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { useServices } from '../app/services'
import { Button } from '../components/Button'
import { Drawer } from '../components/Drawer'
import { Loading, Notice } from '../components/feedback'
import { Pagination } from '../components/Pagination'
import { TableCard } from '../components/TableCard'
import { TabPanel, Tabs } from '../components/Tabs'
import { usePaged } from '../components/usePaged'
import { formatMoney } from '../lib/money'

type PayItem = components['schemas']['PayItemResponse']
type Payee = components['schemas']['PayeeResponse']
type Assignment = components['schemas']['CompanyAssignmentResponse']

const none = <span className="text-muted">—</span>
const tabs = ['details', 'who'] as const

/** "15 Jan 2024". */
const shortDate = (iso: string | null | undefined) =>
  iso ? new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${iso}T00:00:00Z`)) : null

/**
 * An earning or deduction to read: its details (with who the money goes to and how they're paid)
 * and who gets it, company by company, with each person's amount or rate, member or account
 * number and dates. Edit when it's the organization's own.
 */
export function PayItemView({ item, companies, payee, authority, method, tax, onClose, onEdit }: {
  item: PayItem
  companies: { id: string; legalName: string }[]
  payee: Payee | undefined
  /** Who the law's items are paid to (KRA, SHA…). */
  authority: string | undefined
  method: ReactNode
  tax: ReactNode
  onClose: () => void
  onEdit?: () => void
}) {
  const { t } = useTranslation()
  const { api } = useServices()
  const tabsId = useId()
  const [tab, setTab] = useState<(typeof tabs)[number]>('details')
  const assignments = useQuery({
    queryKey: ['payItems', item.id, 'assignments'],
    enabled: !item.locked,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/pay-items/{id}/assignments', { params: { path: { id: item.id } } })
      if (error || !data) throw new Error('assignments')
      return data
    },
  })
  const companyName = (id: string) => companies.find(c => c.id === id)?.legalName ?? ''
  const money = (amount: number | string | null | undefined) => (amount != null ? formatMoney(amount) : null)

  const payeeDetails = (p: Payee) => {
    if (p.method === 'bank' && p.institution === 'sacco') return [p.saccoName, p.saccoBranchName, t('payees.accountShown', { number: p.accountNumber })]
    if (p.method === 'bank') return [p.bankName, p.branchName, t('payees.accountShown', { number: p.accountNumber })]
    if (p.method === 'mpesa_paybill') return [t('payees.paybillShown', { paybill: p.paybill }), p.paybillAccount && t('payees.accountShown', { number: p.paybillAccount })]
    if (p.method === 'cheque') return [t('payees.chequeShown', { name: p.chequePayableTo })]
    return [t('payees.methods.cash')]
  }
  const details: [string, ReactNode][] = [
    [t('payItems.name'), item.name],
    [t('payItems.code'), item.code],
    [t('payItems.kind'), t(`payItems.kindLabels.${item.kind}`)],
    [t('payItems.worked'), method],
    [t('payItems.defaultAmount'), item.calcMethod === 'fixed' ? money(item.defaultAmount) : null],
    [t('payItems.tax'), tax],
    [t('payItems.appliesTo'), item.allCompanies ? t('payItems.allCompanies') : item.companyIds.map(companyName).join(', ')],
    ...(item.kind !== 'earning' ? [
      [t('payees.paidTo'), authority ?? (payee ? <>
        <span className="block">{payee.name}</span>
        <span className="block text-label text-muted">{payeeDetails(payee).filter(Boolean).join(' · ')}</span>
        {payee.ourReference && <span className="block text-label text-muted">{t('payees.ourReferenceShown', { reference: payee.ourReference })}</span>}
      </> : null)] as [string, ReactNode],
      [t('payItems.referenceSource'), t(`payItems.sources.${item.referenceSource}`)] as [string, ReactNode],
    ] : []),
  ]

  return (
    <Drawer open size="wide" title={item.name} onClose={onClose}
      tabs={<Tabs id={tabsId} label={t('who.sections')} current={tab} onChange={k => setTab(k as typeof tab)}
        tabs={tabs.map(k => ({ key: k, label: t(`who.tabs.${k}`) }))} />}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.close')}</Button>
        {onEdit && <Button onClick={onEdit}>{t('payItems.editButton')}</Button>}
      </>}>
      <TabPanel id={tabsId} tab="details" current={tab}>
        <dl className="m-0 grid gap-x-6 gap-y-4 sm:grid-cols-2">
          {details.map(([label, value]) => (
            <div key={label} className="flex flex-col gap-0.5">
              <dt className="text-label text-muted">{label}</dt>
              <dd className="m-0 text-body text-ink">{value || none}</dd>
            </div>
          ))}
        </dl>
      </TabPanel>
      <TabPanel id={tabsId} tab="who" current={tab}>
        {item.locked ? <p className="m-0 text-body text-ink">{t('who.byLaw')}</p>
          : !assignments.data ? <Loading rows={3} />
            : assignments.data.length === 0 ? <p className="m-0 text-body text-muted">{t('who.noOne')}</p>
              : assignments.data.map(a => <CompanyWho key={a.companyId} assignment={a} company={companyName(a.companyId)} calcMethod={item.calcMethod}
                referenceSource={item.referenceSource} />)}
      </TabPanel>
    </Drawer>
  )
}

/** Who gets it at one company: everyone (with the shared amount and dates) or a table of people. */
function CompanyWho({ assignment: a, company, calcMethod, referenceSource }: { assignment: Assignment; company: string; calcMethod: string; referenceSource: string }) {
  const { t } = useTranslation()
  const paged = usePaged(a.people, 10)
  const value = (amount: number | string | null | undefined, rate: number | string | null | undefined) =>
    calcMethod === 'each_run' ? t('employees.payItemEachRun')
      : rate != null ? t('employees.payItemRate', { rate: Number(rate) })
        : amount != null ? formatMoney(amount) : t('who.itemDefault')
  const dates = (from: string | null | undefined, until: string | null | undefined) =>
    [from && t('employees.payItemFrom', { date: shortDate(from) }), until && t('employees.payItemUntil', { date: shortDate(until) })].filter(Boolean).join(' ')

  // Who lacks the number the item takes from profiles.
  const missing = referenceSource !== 'typed' && a.missing.length > 0 && (
    <Notice>{t('who.missing', { count: a.missing.length, what: t(`payItems.sourceWhat.${referenceSource}`), names: a.missing.map(m => m.name).join(', ') })}</Notice>
  )
  if (a.everyone) {
    return (
      <section aria-label={company} className="flex flex-col gap-1">
        {missing}
        <h3 className="m-0 text-label font-semibold text-ink">{t('who.everyone', { company })}</h3>
        <p className="m-0 text-body text-ink">{value(a.amount, a.rate)} · {dates(a.startsOn, a.endsOn)}</p>
        <p className="m-0 text-hint text-muted">{t('who.everyoneHint')}</p>
      </section>
    )
  }
  return (
    <>
    {missing}
    <TableCard title={company} count={a.people.length} footer={<Pagination label={t('pagination.of', { what: company })} {...paged.pagination} />}>
      <table>
        <thead>
          <tr>
            <th scope="col">{t('who.employee')}</th>
            <th scope="col">{t(calcMethod === 'percent_of_basic' ? 'who.rate' : 'who.amount')}</th>
            <th scope="col">{t('who.reference')}</th>
            <th scope="col">{t('who.dates')}</th>
          </tr>
        </thead>
        <tbody>
          {paged.items.map(p => (
            <tr key={p.employeeId}>
              <td>
                <span className="block font-medium text-ink">{p.name}</span>
                <span className="block font-mono text-hint text-muted">{p.staffNo}</span>
              </td>
              <td className="tabular-nums">{value(p.amount, p.rate)}</td>
              <td className="font-mono">{p.shownReference ? <>{p.shownReference}{p.fromProfile && <span className="font-sans text-hint text-muted"> {t('who.profileTag')}</span>}</> : none}</td>
              <td className="whitespace-nowrap">{dates(p.startsOn, p.endsOn)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </TableCard>
    </>
  )
}
