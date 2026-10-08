import { useCallback, useEffect, useSyncExternalStore } from 'react'

export type ThemePreference = 'system' | 'light' | 'dark'
export type Theme = 'light' | 'dark'

/** Stored on the device for now; moves to the person's account settings later. index.html reads it too. */
export const THEME_KEY = 'pesaroll.theme'
const SYSTEM_DARK = '(prefers-color-scheme: dark)'

// Used only when storage is blocked (private windows), so the choice still lasts for the visit.
let memory: ThemePreference | null = null
const listeners = new Set<() => void>()

export function readPreference(): ThemePreference {
  try {
    const value = localStorage.getItem(THEME_KEY)
    return value === 'light' || value === 'dark' ? value : 'system'
  } catch {
    return memory ?? 'system'
  }
}

function setStoredPreference(preference: ThemePreference) {
  memory = preference
  try {
    if (preference === 'system') localStorage.removeItem(THEME_KEY)
    else localStorage.setItem(THEME_KEY, preference)
  } catch {
    // Blocked storage: kept in memory above.
  }
  listeners.forEach(notify => notify())
}

function subscribePreference(notify: () => void) {
  listeners.add(notify)
  return () => { listeners.delete(notify) }
}

function subscribeSystem(notify: () => void) {
  const media = window.matchMedia(SYSTEM_DARK)
  media.addEventListener('change', notify)
  return () => media.removeEventListener('change', notify)
}

const systemDark = () => window.matchMedia(SYSTEM_DARK).matches

export function resolveTheme(preference: ThemePreference, isSystemDark: boolean): Theme {
  return preference === 'system' ? (isSystemDark ? 'dark' : 'light') : preference
}

/** Sets data-theme on <html>, which switches every token in tokens.css. */
export function applyTheme(preference: ThemePreference): Theme {
  const theme = resolveTheme(preference, systemDark())
  document.documentElement.dataset.theme = theme
  return theme
}

/** The person's choice (System, Light, Dark) and the theme in use. Every caller stays in step. */
export function useTheme() {
  const preference = useSyncExternalStore(subscribePreference, readPreference, () => 'system' as const)
  const isSystemDark = useSyncExternalStore(subscribeSystem, systemDark, () => false)
  const resolved = resolveTheme(preference, isSystemDark)

  useEffect(() => { document.documentElement.dataset.theme = resolved }, [resolved])

  const setPreference = useCallback((next: ThemePreference) => setStoredPreference(next), [])
  return { preference, resolved, setPreference }
}
