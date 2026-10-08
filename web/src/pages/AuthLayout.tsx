import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import panelArt from '../assets/auth-panel.svg'
import { Navbar } from '../components/Navbar'

/**
 * Sign-up and sign-in: the navbar across the top, then a deep green panel on the left (on top on
 * phones) with a decorative background, and the form on the right. Both sides are centred vertically; the panel lists what Pesaroll does.
 */
export function AuthLayout({ title, intro, children }: { title: string; intro: string; children: ReactNode }) {
  const { t } = useTranslation()
  const features = ['featurePayroll', 'featurePhones', 'featurePerformance', 'featureTasks'] as const
  return (
    <div className="flex min-h-dvh flex-col bg-page">
      <Navbar />
      <div className="flex flex-1 flex-wrap">
        <section style={{ backgroundImage: `url(${JSON.stringify(panelArt)})` }}
          className="flex flex-[1_1_420px] flex-col justify-center gap-5 bg-sidebar bg-cover bg-center px-6 py-10 md:sticky md:top-16 md:h-[calc(100dvh-4rem)] md:self-start md:px-12 md:py-14">
          <div className="flex w-full max-w-[36rem] flex-col gap-5">
            <p className="m-0 font-display text-title leading-tight text-sidebar-heading md:text-[44px]">{t('auth.panelTitle')}</p>
            <p className="m-0 text-body text-sidebar-ink md:text-[17px]">{t('auth.panelText')}</p>
            <ul aria-label={t('auth.featuresLabel')} className="m-0 flex list-none flex-col gap-3 p-0">
              {features.map(key => (
                <li key={key} className="flex items-start gap-3 text-body text-sidebar-ink">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" className="mt-0.5 flex-none text-sidebar-accent"><path d="M5 12l5 5 9-10" /></svg>
                  {t(`auth.${key}`)}
                </li>
              ))}
            </ul>
          </div>
        </section>
        <main className="flex flex-[1_1_480px] items-center justify-center px-5 py-10 md:py-12">
          <div className="@container flex w-full max-w-[560px] flex-col gap-5">
            <div className="flex flex-col gap-1.5">
              <h1 className="m-0 font-display text-title text-ink">{title}</h1>
              <p className="m-0 text-muted">{intro}</p>
            </div>
            {children}
          </div>
        </main>
      </div>
    </div>
  )
}
