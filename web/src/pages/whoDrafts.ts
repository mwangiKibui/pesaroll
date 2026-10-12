import type { components } from '../api/schema'

// Who gets an earning or deduction, as the item drawer edits it (WhoGetsIt), and to and from the API.

type Assignment = components['schemas']['CompanyAssignmentResponse']
type AssignmentRequest = components['schemas']['AssignmentRequest']

/** One chosen person, as typed (empty amount or rate: the item's; empty From: today). */
export interface PersonDraft {
  employeeId: string; name: string; staffNo: string; amount: string; rate: string; startsOn: string; endsOn: string; reference: string
  /** Their number from their profile, as the item says (masked where sensitive); empty when typed or not there. */
  profile: string
}

/** Who gets the item at one company; dirty once changed, so only changed companies are saved. */
export interface CompanyDraft {
  mode: 'none' | 'everyone' | 'people'
  amount: string
  rate: string
  startsOn: string
  endsOn: string
  people: PersonDraft[]
  /** People whose profile lacks the number the item takes from it (as last saved). */
  missing: string[]
  dirty: boolean
}

export const emptyDraft = (): CompanyDraft => ({ mode: 'none', amount: '', rate: '', startsOn: '', endsOn: '', people: [], missing: [], dirty: false })

const text = (value: number | string | null | undefined) => (value == null ? '' : String(value))

/** What the API says, company by company, as drafts to edit. */
export function draftsFrom(assignments: Assignment[]): Record<string, CompanyDraft> {
  return Object.fromEntries(assignments.map(a => [a.companyId, {
    mode: a.everyone ? 'everyone' : a.people.length ? 'people' : 'none',
    amount: text(a.amount), rate: text(a.rate), startsOn: a.startsOn ?? '', endsOn: a.endsOn ?? '', dirty: false,
    missing: (a.missing ?? []).map(m => m.name),
    people: a.people.map(p => ({
      employeeId: p.employeeId, name: p.name, staffNo: p.staffNo, amount: text(p.amount), rate: text(p.rate), startsOn: p.startsOn, endsOn: p.endsOn ?? '',
      reference: p.reference ?? '', profile: p.fromProfile ? p.shownReference ?? '' : '',
    })),
  } satisfies CompanyDraft]))
}

export const number = (value: string) => (value.trim() ? Number(value.replace(/[\s,]/g, '')) : null)

/** The request for one company: everyone, the people listed, or (none) nobody. */
export function requestFor(companyId: string, d: CompanyDraft): AssignmentRequest {
  return {
    companyId, everyone: d.mode === 'everyone',
    amount: d.mode === 'everyone' ? number(d.amount) : null, rate: d.mode === 'everyone' ? number(d.rate) : null,
    startsOn: d.mode === 'everyone' ? d.startsOn || null : null, endsOn: d.mode === 'everyone' ? d.endsOn || null : null,
    people: d.mode === 'people'
      ? d.people.map(p => ({ employeeId: p.employeeId, amount: number(p.amount), rate: number(p.rate), startsOn: p.startsOn || null, endsOn: p.endsOn || null,
        reference: p.reference.trim() || null }))
      : [],
  }
}
