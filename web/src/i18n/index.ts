import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from './locales/en.json'
import sw from './locales/sw.json'

/** English and Swahili. Every word on screen comes from these files; see docs/standards/ui.md. */
export const LANGUAGE_KEY = 'pesaroll.language'
export const languages = ['en', 'sw'] as const
export type Language = (typeof languages)[number]

function storedLanguage(): Language {
  try {
    return localStorage.getItem(LANGUAGE_KEY) === 'sw' ? 'sw' : 'en'
  } catch {
    return 'en'
  }
}

void i18n.use(initReactI18next).init({
  resources: { en: { translation: en }, sw: { translation: sw } },
  lng: storedLanguage(),
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
})

i18n.on('languageChanged', lng => {
  document.documentElement.lang = lng
  try {
    localStorage.setItem(LANGUAGE_KEY, lng)
  } catch {
    // Blocked storage: the choice lasts for this visit only.
  }
})

export default i18n
