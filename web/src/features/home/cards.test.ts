import { describe, expect, it } from 'vitest'
import type { Feature } from '../../app/features'
import { canDo } from '../../app/permissions'
import { homeCards, visibleCards } from './cards'

const grant = (permission: string, companyIds: string[] = []) => ({ permission, allCompanies: companyIds.length === 0, companyIds })
const both = (area: string) => [grant(`${area}.view`), grant(`${area}.manage`)]
const hr = [...both('employees'), ...both('locations'), ...both('structure'), ...both('leave'), grant('schedules.view')]
const payroll = [...both('employees'), ...both('pay'), ...both('schedules'), ...both('pay_items')]

const noFlags = () => false
const shown = (grants: ReturnType<typeof grant>[], flagOn: (f: Feature) => boolean = noFlags, company?: string) =>
  visibleCards((p, c) => canDo(grants, p, c), flagOn, company).map(c => c.key)

describe('Home cards', () => {
  it('HR gets Add employee, Employees, Joined this month and Ready for payroll', () => {
    expect(shown(hr)).toEqual(['addEmployee', 'employees', 'joinedThisMonth', 'readyForPayroll'])
  })

  it('someone who may only view employees gets no Add employee', () => {
    expect(shown([grant('employees.view')])).toEqual(['employees', 'joinedThisMonth', 'readyForPayroll'])
  })

  it('an Approver (no employees.view) gets none of them', () => {
    expect(shown([])).toEqual([])
  })

  it('a group for one company gives its cards only when that company is chosen', () => {
    const westlandsOnly = [grant('employees.view', ['westlands'])]
    expect(shown(westlandsOnly, noFlags, 'westlands')).toEqual(['employees', 'joinedThisMonth', 'readyForPayroll'])
    expect(shown(westlandsOnly, noFlags, 'mombasa')).toEqual([])
  })

  it('later cards wait behind their flags, and still need their permission', () => {
    const all = () => true
    expect(shown(hr, all)).toEqual(['addEmployee', 'employees', 'joinedThisMonth', 'readyForPayroll', 'waitingForYou', 'offToday', 'whosOffThisWeek',
      'joinerChecklists', 'midYearReviews', 'announcements'])
    // Pay below the minimum wage shows amounts: only with View on pay (ACC-05), so never for HR.
    expect(shown(payroll, all)).toContain('belowMinimumWage')
  })

  it('every card checks a permission key or waits behind a flag, never a role name', () => {
    expect(homeCards.filter(c => !c.permission && !c.feature)).toEqual([])
    expect(homeCards.map(c => c.permission).filter(Boolean).every(p => /^[a-z_]+\.(view|manage)$/.test(p!))).toBe(true)
  })
})
