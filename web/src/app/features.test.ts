import { describe, expect, it } from 'vitest'
import { featureOn, features } from './features'

describe('feature flags', () => {
  it('are off unless switched on', () => {
    expect(featureOn('approvals', '')).toBe(false)
    expect(featureOn('approvals', undefined)).toBe(false)
  })

  it('a comma list switches on the ones named, ignoring spaces and case', () => {
    expect(featureOn('approvals', 'tasks, Approvals')).toBe(true)
    expect(featureOn('reports', 'tasks, Approvals')).toBe(false)
  })

  it('"all" switches every one on', () => {
    expect(features.every(f => featureOn(f, 'all'))).toBe(true)
  })

  it('an unknown name does nothing', () => {
    expect(features.some(f => featureOn(f, 'payroll-magic'))).toBe(false)
  })
})
