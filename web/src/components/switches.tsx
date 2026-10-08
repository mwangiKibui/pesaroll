import { useTranslation } from 'react-i18next'
import { languages } from '../i18n'
import { useTheme, type ThemePreference } from '../theme/theme'

const themes: ThemePreference[] = ['system', 'light', 'dark']

/** System, Light or Dark. Lives in the user menu and on the phone portal's profile page. */
export function ThemeSwitch() {
  const { t } = useTranslation()
  const { preference, setPreference } = useTheme()
  return (
    <fieldset className="m-0 flex flex-col gap-1.5 border-0 p-0">
      <legend className="mb-1.5 p-0 text-label font-medium text-ink">{t('theme.label')}</legend>
      <div className="inline-flex w-fit rounded-control border border-field-border bg-card p-0.5">
        {themes.map(theme => (
          <label key={theme} className={`flex min-h-10 cursor-pointer items-center rounded-[6px] px-3.5 text-label has-focus-visible:outline-2 has-focus-visible:outline-brand ${preference === theme ? 'bg-brand font-semibold text-on-brand' : 'text-ink hover:bg-raised'}`}>
            <input type="radio" name="theme" value={theme} checked={preference === theme} onChange={() => setPreference(theme)} className="sr-only" />
            {t(`theme.${theme}`)}
          </label>
        ))}
      </div>
    </fieldset>
  )
}

/** "Lugha: English · Kiswahili" */
export function LanguageSwitch() {
  const { t, i18n } = useTranslation()
  return (
    <div className="flex items-center gap-2 text-label text-muted">
      <span>{t('language.label')}:</span>
      {languages.map((lng, i) => (
        <span key={lng} className="flex items-center gap-2">
          {i > 0 && <span aria-hidden="true">·</span>}
          <button type="button" aria-pressed={i18n.language === lng} onClick={() => void i18n.changeLanguage(lng)}
            className={`min-h-11 cursor-pointer bg-transparent text-label ${i18n.language === lng ? 'font-semibold text-ink' : 'text-brand underline'}`}>{t(`language.${lng}`)}</button>
        </span>
      ))}
    </div>
  )
}
