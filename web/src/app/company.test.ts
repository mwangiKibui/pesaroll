import { afterEach, describe, expect, it, vi } from 'vitest'
import { chooseCompany, rememberCompany, rememberedCompany } from './company'

const westlands = { id: 'westlands' }
const mombasa = { id: 'mombasa' }

afterEach(() => { localStorage.clear(); vi.restoreAllMocks() })

describe('the chosen company', () => {
  it('starts on the person\'s first company', () => {
    expect(chooseCompany([westlands, mombasa], null)).toBe('westlands')
  })

  it('keeps the one they chose while they still have it', () => {
    expect(chooseCompany([westlands, mombasa], 'mombasa')).toBe('mombasa')
  })

  it('falls back to their first when they lose the one they chose', () => {
    expect(chooseCompany([westlands], 'mombasa')).toBe('westlands')
  })

  it('is nothing before they have a company', () => {
    expect(chooseCompany([], 'mombasa')).toBeNull()
  })

  it('is remembered per person on this device', () => {
    rememberCompany('amina', 'mombasa')
    expect(rememberedCompany('amina')).toBe('mombasa')
    expect(rememberedCompany('otieno')).toBeNull()
  })

  it('works without browser storage (private windows)', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked') })
    expect(() => rememberCompany('amina', 'mombasa')).not.toThrow()
    expect(rememberedCompany('amina')).toBeNull()
  })
})
