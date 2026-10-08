import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from './locales/en.json'
import sw from './locales/sw.json'

/** English and Swahili. Every word on screen comes from these files; see docs/standards/ui.md. */
export const LANGUAGE_KEY = 'pesaroll.language'
export const languages = ['en', 'sw'] as const
export type Language = (typeof languages)[number]

/**
 * English only for now: Swahili stays in the code (and its keys stay in step, see i18n.test.ts)
 * but people can't choose it until the copy is reviewed. To turn it back on, return the stored
 * choice here and put LanguageSwitch back in the navbar.
 */
export function initialLanguage(): Language {
  return 'en'
}

void i18n.use(initReactI18next).init({
  resources: { en: { translation: en }, sw: { translation: sw } },
  lng: initialLanguage(),
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
})

if (typeof document !== 'undefined') document.documentElement.lang = i18n.language

i18n.on('languageChanged', lng => {
  document.documentElement.lang = lng
  try {
    localStorage.setItem(LANGUAGE_KEY, lng)
  } catch {
    // Blocked storage: the choice lasts for this visit only.
  }
})

export default i18n
