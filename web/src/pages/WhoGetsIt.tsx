import { useQuery } from '@tanstack/react-query'
import { useDeferredValue, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useServices } from '../app/services'
import { Button, IconButton } from '../components/Button'
import { Notice } from '../components/feedback'
import { RadioGroup, SelectField, TextField } from '../components/fields'
import { Pagination } from '../components/Pagination'
import { TableCard } from '../components/TableCard'
import { usePaged } from '../components/usePaged'
import { formatMoney } from '../lib/money'
import { emptyDraft, number, type CompanyDraft, type PersonDraft } from './whoDrafts'

/**
 * The item drawer's "Who gets it": a company first (one of those the item serves), then no one
 * yet, everyone there (people hired later too) or specific people, found by any identifier, each
 * with their own amount or rate (the item's unless changed) and From / Until dates. Items entered
 * each payroll only say who can have them.
 */
export function WhoGetsIt({ companies, itemName, method, defaultAmount, rate, drafts, errors, onChange }: {
  companies: { id: string; legalName: string }[]
  itemName: string
  method: string
  defaultAmount: string
  rate: string
  drafts: Record<string, CompanyDraft>
  /** The server's problems, per company, by field ("amount", "people[0].endsOn"). */
  errors: Record<string, Record<string, string>>
  onChange: (companyId: string, draft: CompanyDraft) => void
}) {
  const { t } = useTranslation()
  const [picked, setPicked] = useState(companies[0]?.id ?? '')
  const company = companies.find(c => c.id === picked) ?? companies[0]
  if (!company) return <Notice>{t('who.noCompany')}</Notice>
  const draft = drafts[company.id] ?? emptyDraft()
  const problems = errors[company.id] ?? {}
  const change = (next: Partial<CompanyDraft>) => onChange(company.id, { ...draft, ...next, dirty: true })
  const amounts = method === 'fixed' ? 'amount' : method === 'percent_of_basic' ? 'rate' : null
  const placeholder = amounts === 'amount'
    ? (number(defaultAmount) != null ? formatMoney(number(defaultAmount)!, { currency: '' }) : t('who.amountPlaceholder'))
    : rate.trim() || t('who.ratePlaceholder')
  const summary = (id: string) => {
    const d = drafts[id] ?? emptyDraft()
    return d.mode === 'everyone' ? t('who.everyoneShort') : d.mode === 'people' ? t('who.people', { count: d.people.length }) : t('who.noOne')
  }

  return (
    <div className="flex flex-col gap-4">
      {companies.length > 1 && (
        <div className="max-w-md">
          <SelectField label={t('who.company')} placeholder={t('who.companyPlaceholder')} value={company.id} onChange={value => value && setPicked(value)}
            hint={companies.map(c => `${c.legalName}: ${summary(c.id)}`).join(' · ')}
            options={companies.map(c => ({ value: c.id, label: c.legalName }))} />
        </div>
      )}
      <RadioGroup label={t('who.question', { company: company.legalName })} name={`who-${company.id}`} value={draft.mode}
        onChange={value => change({ mode: value as CompanyDraft['mode'] })}
        options={[
          { value: 'everyone', label: t('who.everyone', { company: company.legalName }), hint: t('who.everyoneHint') },
          { value: 'people', label: t('who.specific') },
          { value: 'none', label: t('who.none') },
        ]} />
      {method === 'each_run' && draft.mode !== 'none' && <Notice>{t('who.eachRun', { name: itemName })}</Notice>}

      {draft.mode === 'everyone' && (
        <div className="grid gap-4 sm:grid-cols-3">
          {amounts && (
            <TextField label={t(amounts === 'amount' ? 'who.amount' : 'who.rate')} placeholder={placeholder} optional inputMode="decimal"
              hint={t(amounts === 'amount' ? 'who.amountHint' : 'who.rateHint')} error={problems[amounts]}
              value={amounts === 'amount' ? draft.amount : draft.rate} onChange={e => change({ [amounts]: e.target.value })} />
          )}
          <TextField type="date" label={t('who.from')} placeholder={t('who.fromPlaceholder')} optional hint={t('who.fromHint')} error={problems.startsOn}
            value={draft.startsOn} onChange={e => change({ startsOn: e.target.value })} />
          <TextField type="date" label={t('who.until')} placeholder={t('who.untilPlaceholder')} optional hint={t('who.untilHint')} error={problems.endsOn}
            value={draft.endsOn} onChange={e => change({ endsOn: e.target.value })} />
        </div>
      )}

      {draft.mode === 'people' && (
        <People companyId={company.id} draft={draft} amounts={amounts} placeholder={placeholder} problems={problems} change={change} />
      )}
    </div>
  )
}

/** Specific people: found on the server by any identifier, then a table of the chosen with their own amounts and dates. */
function People({ companyId, draft, amounts, placeholder, problems, change }: {
  companyId: string
  draft: CompanyDraft
  amounts: 'amount' | 'rate' | null
  placeholder: string
  problems: Record<string, string>
  change: (next: Partial<CompanyDraft>) => void
}) {
  const { t } = useTranslation()
  const { api } = useServices()
  const [search, setSearch] = useState('')
  const term = useDeferredValue(search.trim())
  const found = useQuery({
    queryKey: ['employees', 'picker', companyId, term],
    enabled: !!term,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees', { params: { query: { companyIds: [companyId], search: term, status: 'current', pageSize: 8 } } })
      if (error || !data) throw new Error('employees')
      return data.items
    },
  })
  const chosen = new Set(draft.people.map(p => p.employeeId))
  const paged = usePaged(draft.people.map((p, index) => ({ ...p, index })), 10)
  const edit = (index: number, next: Partial<PersonDraft>) => change({ people: draft.people.map((p, i) => (i === index ? { ...p, ...next } : p)) })

  return (
    <div className="flex flex-col gap-3">
      <div className="max-w-md">
        <TextField type="search" label={t('who.find')} placeholder={t('who.findPlaceholder')} hint={t('who.findHint')} value={search}
          onChange={e => setSearch(e.target.value)} />
      </div>
      {term && (
        <ul aria-label={t('who.results')} className="m-0 flex list-none flex-col gap-1 p-0">
          {(found.data ?? []).map(e => (
            <li key={e.id}>
              <Button variant="secondary" disabled={chosen.has(e.id)}
                onClick={() => change({ people: [...draft.people, { employeeId: e.id, name: e.name, staffNo: e.staffNo, amount: '', rate: '', startsOn: '', endsOn: '', reference: '' }] })}>
                {t('who.add', { name: `${e.name} (${e.staffNo})` })}
              </Button>
            </li>
          ))}
          {found.data?.length === 0 && <li className="text-label text-muted">{t('who.noMatches')}</li>}
        </ul>
      )}
      {draft.people.length > 0 && <p className="m-0 text-hint text-muted">{t('who.noEndHint')}</p>}
      <TableCard title={t('who.chosen')} count={draft.people.length}
        footer={<Pagination label={t('pagination.of', { what: t('who.chosen') })} {...paged.pagination} />}>
        {draft.people.length === 0 ? <p className="m-0 px-4 py-3 text-label text-muted">{t('who.nobodyChosen')}</p> : (
          <table>
            <thead>
              <tr>
                <th scope="col">{t('who.employee')}</th>
                {amounts && <th scope="col">{t(amounts === 'amount' ? 'who.amount' : 'who.rate')}</th>}
                <th scope="col">{t('who.reference')}</th>
                <th scope="col">{t('who.from')}</th>
                <th scope="col">{t('who.untilOptional')}</th>
                <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
              </tr>
            </thead>
            <tbody>
              {paged.items.map(p => {
                const at = `people[${p.index}].`
                return (
                  <tr key={p.employeeId}>
                    <td>
                      <span className="block font-medium text-ink">{p.name}</span>
                      <span className="block font-mono text-hint text-muted">{p.staffNo}</span>
                      {problems[`${at}employeeId`] && <span className="block text-hint font-medium text-danger-ink">{problems[`${at}employeeId`]}</span>}
                    </td>
                    {amounts && (
                      <td className="min-w-36">
                        <TextField size="sm" hideLabel inputMode="decimal" placeholder={placeholder} error={problems[`${at}${amounts}`]}
                          label={`${t(amounts === 'amount' ? 'who.amount' : 'who.rate')} ${t('who.for', { name: p.name })}`}
                          value={amounts === 'amount' ? p.amount : p.rate} onChange={e => edit(p.index, { [amounts]: e.target.value })} />
                      </td>
                    )}
                    <td className="min-w-40">
                      <TextField size="sm" hideLabel placeholder={t('who.referencePlaceholder')} maxLength={40} label={`${t('who.reference')} ${t('who.for', { name: p.name })}`}
                        error={problems[`${at}reference`]} value={p.reference} onChange={e => edit(p.index, { reference: e.target.value })} />
                    </td>
                    <td className="min-w-40">
                      <TextField size="sm" hideLabel type="date" placeholder={t('who.fromPlaceholder')} label={`${t('who.from')} ${t('who.for', { name: p.name })}`}
                        error={problems[`${at}startsOn`]} value={p.startsOn} onChange={e => edit(p.index, { startsOn: e.target.value })} />
                    </td>
                    <td className="min-w-40">
                      <TextField size="sm" hideLabel type="date" placeholder={t('who.untilPlaceholder')} label={`${t('who.until')} ${t('who.for', { name: p.name })}`}
                        error={problems[`${at}endsOn`]} value={p.endsOn} onChange={e => edit(p.index, { endsOn: e.target.value })} />
                    </td>
                    <td className="text-right">
                      <IconButton tone="danger" label={t('who.remove', { name: p.name })}
                        onClick={() => change({ people: draft.people.filter((_, i) => i !== p.index) })}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                      </IconButton>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </TableCard>
    </div>
  )
}
