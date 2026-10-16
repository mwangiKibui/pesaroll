import { describe, expect, it } from 'vitest'
import { canDo, permissions } from './permissions'

const hrWestlands = [
  { permission: 'employees.view', allCompanies: false, companyIds: ['westlands'] },
  { permission: 'employees.manage', allCompanies: false, companyIds: ['westlands'] },
  { permission: 'schedules.view', allCompanies: true, companyIds: [] },
]

describe('canDo', () => {
  it('allows a permission held for every company, for any company', () => {
    expect(canDo(hrWestlands, permissions.schedules.view)).toBe(true)
    expect(canDo(hrWestlands, permissions.schedules.view, 'mombasa')).toBe(true)
  })

  it('allows a permission held for some companies only for those', () => {
    expect(canDo(hrWestlands, permissions.employees.manage, 'westlands')).toBe(true)
    expect(canDo(hrWestlands, permissions.employees.manage, 'mombasa')).toBe(false)
  })

  it('without a company, asks whether they have it anywhere', () => {
    expect(canDo(hrWestlands, permissions.employees.manage)).toBe(true)
    expect(canDo(hrWestlands, permissions.schedules.manage)).toBe(false)
  })

  it('allows nothing before the person is known', () => {
    expect(canDo(undefined, permissions.employees.view)).toBe(false)
  })
})
