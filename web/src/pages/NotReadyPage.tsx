import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'

/** A link whose feature flag is on before its page exists (OP-01), or an address that isn't a page. */
export function NotReadyPage() {
  const { t } = useTranslation()
  return (
    <div className="mx-auto flex max-w-md flex-col gap-3 p-8">
      <p className="m-0 text-body text-ink">{t('nav.comingSoon')}</p>
      <Link to="/">{t('nav.backHome')}</Link>
    </div>
  )
}
