import { describe, expect, it } from 'vitest'
import { canDo } from './permissions'
import { createAppRouter } from './router'
import { createServices } from './services'
import { navigation, searchPages, visibleNavigation, type NavLink } from './navigation'
import type { Feature } from './features'

const grant = (permission: string, companyIds: string[] = []) => ({ permission, allCompanies: companyIds.length === 0, companyIds })
const both = (area: string) => [grant(`${area}.view`), grant(`${area}.manage`)]
const hr = [...both('employees'), ...both('locations'), ...both('structure'), ...both('leave'), grant('schedules.view')]
const payroll = [...both('employees'), ...both('pay'), ...both('pay_runs'), ...both('schedules'), ...both('pay_items'), ...both('payment_accounts'), ...both('payroll_history'),
  grant('locations.view'), grant('structure.view'), grant('leave.view')]
const admin = ['companies', 'employees', 'pay', 'locations', 'structure', 'leave', 'schedules', 'pay_items', 'payment_accounts', 'payroll_history', 'team'].flatMap(both)

const noFlags = () => false
const shown = (grants: ReturnType<typeof grant>[], flagOn: (f: Feature) => boolean = noFlags, company?: string) =>
  visibleNavigation((p, c) => canDo(grants, p, c), flagOn, company).map(s => `${s.key}: ${s.links.map(l => l.key).join(', ')}`)

describe('navigation', () => {
  it('HR sees Home, People and HR\'s organization pages, and no Pay', () => {
    expect(shown(hr)).toEqual([
      'main: home',
      'people: employees',
      'organization: locations, structure, leavePlans',
    ])
  })

  it('Payroll schedules is for people who manage them (OP-05): HR may view schedules, to pick one for an employee, but has no link', () => {
    expect(hr.map(g => g.permission)).toContain('schedules.view')
    expect(shown(hr).join()).not.toContain('schedules')
    expect(navigation.flatMap(s => s.links).find(l => l.key === 'schedules')?.href).toBe('/organization/payroll-schedules')
  })

  it('Payroll sees Pay', () => {
    expect(shown(payroll)).toEqual([
      'main: home',
      'pay: earnings, payrollHistory',
      'people: employees',
      'organization: locations, structure, schedules, leavePlans',
    ])
  })

  it('an administrator sees every built page, and Setup', () => {
    expect(shown(admin)).toEqual([
      'main: home',
      'pay: earnings, payrollHistory',
      'people: employees',
      'organization: companies, locations, structure, schedules, leavePlans',
      'admin: team, setup',
    ])
  })

  it('someone with no access (an Approver today) sees only Home', () => {
    expect(shown([])).toEqual(['main: home'])
  })

  it('a group for one company gives its pages only when that company is chosen', () => {
    const hrWestlands = [grant('employees.view', ['westlands'])]
    expect(shown(hrWestlands, noFlags, 'westlands')).toContain('people: employees')
    expect(shown(hrWestlands, noFlags, 'mombasa')).not.toContain('people: employees')
  })

  it('unbuilt pages show only when their flag is on (and the person may open them)', () => {
    expect(shown([], f => f === 'approvals')).toEqual(['main: home, approvals'])
    expect(shown(hr, f => f === 'payouts')).not.toContain('pay: payouts')
  })

  it('Pay runs needs View on pay runs (OP-18), not pay', () => {
    const all = () => true
    expect(shown([grant('pay_runs.view')], all).find(s => s.startsWith('pay:'))).toBe('pay: payRuns')
    expect(shown([grant('pay.view')], all).find(s => s.startsWith('pay:'))).not.toContain('payRuns')
  })

  it('pages that show people\'s pay amounts need the pay permission (ACC-05), not just the pay setup areas', () => {
    const all = () => true
    const withoutPay = payroll.filter(g => !g.permission.startsWith('pay.'))
    expect(shown(payroll, all).find(s => s.startsWith('pay:'))).toBe('pay: payRuns, earnings, payouts, expenses, filings, payrollHistory')
    expect(shown(withoutPay, all).find(s => s.startsWith('pay:'))).toBe('pay: payRuns, earnings, expenses, payrollHistory')
  })

  it('every link has a page or a flag', () => {
    const { auth, api } = createServices({ channelName: `nav-${crypto.randomUUID()}` })
    const paths = Object.keys(createAppRouter(auth, api).routesByPath)
    const links: NavLink[] = navigation.flatMap(s => s.links)
    expect(links.filter(l => !l.feature && !paths.includes(l.href)).map(l => l.key)).toEqual([])
    auth.dispose()
  })

  it('search finds pages by name or by the words people use, among those they may open', () => {
    const can = (p: string, c?: string) => canDo(payroll, p, c)
    expect(searchPages('payroll', can, noFlags).map(l => l.key)).toEqual(['earnings', 'payrollHistory', 'schedules'])
    expect(searchPages('SACCO', can, noFlags).map(l => l.key)).toEqual(['earnings'])
    expect(searchPages('team', (p, c) => canDo(hr, p, c), noFlags)).toEqual([])
    expect(searchPages('', can, noFlags)).toEqual([])
  })
})
