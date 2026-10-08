import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { applyTheme, readPreference, resolveTheme, THEME_KEY, useTheme } from './theme'

function mockSystemDark(dark: boolean) {
  const listeners: ((e: { matches: boolean }) => void)[] = []
  const mql = {
    matches: dark, media: '(prefers-color-scheme: dark)',
    addEventListener: (_: string, fn: (e: { matches: boolean }) => void) => listeners.push(fn),
    removeEventListener: () => {},
  }
  vi.spyOn(window, 'matchMedia').mockReturnValue(mql as unknown as MediaQueryList)
  return (next: boolean) => { mql.matches = next; listeners.forEach(fn => fn({ matches: next })) }
}

afterEach(() => vi.restoreAllMocks())

describe('theme', () => {
  it('follows the device until the person picks one', () => {
    expect(readPreference()).toBe('system')
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('system', false)).toBe('light')
    expect(resolveTheme('light', true)).toBe('light')
    expect(resolveTheme('dark', false)).toBe('dark')
  })

  it('ignores a stored value it does not know', () => {
    localStorage.setItem(THEME_KEY, 'purple')
    expect(readPreference()).toBe('system')
  })

  it('sets data-theme on the page', () => {
    mockSystemDark(true)
    expect(applyTheme('system')).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(applyTheme('light')).toBe('light')
    expect(document.documentElement.dataset.theme).toBe('light')
  })

  it('remembers the choice and applies it', () => {
    mockSystemDark(false)
    const { result } = renderHook(() => useTheme())
    expect(result.current.preference).toBe('system')
    expect(result.current.resolved).toBe('light')

    act(() => result.current.setPreference('dark'))

    expect(localStorage.getItem(THEME_KEY)).toBe('dark')
    expect(result.current.resolved).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
  })

  it('on System, switches when the device switches', () => {
    const setSystemDark = mockSystemDark(false)
    const { result } = renderHook(() => useTheme())

    act(() => setSystemDark(true))

    expect(result.current.resolved).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
  })

  it('keeps working when storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked') })
    mockSystemDark(false)
    const { result } = renderHook(() => useTheme())

    act(() => result.current.setPreference('dark'))

    expect(result.current.resolved).toBe('dark')
  })
})
