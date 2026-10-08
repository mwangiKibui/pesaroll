import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import panelArt from '../assets/auth-panel.svg'
import { Navbar } from '../components/Navbar'

/**
 * Sign-up and sign-in: the navbar across the top, then a deep green panel on the left (on top on
 * phones) with a decorative background, and the form on the right. Both sides are centred vertically.
 */
export function AuthLayout({ title, intro, children }: { title: string; intro: string; children: ReactNode }) {
  const { t } = useTranslation()
  return (
    <div className="flex min-h-dvh flex-col bg-page">
      <Navbar />
      <div className="flex flex-1 flex-wrap">
        <section style={{ backgroundImage: `url(${JSON.stringify(panelArt)})` }}
          className="flex flex-[1_1_420px] flex-col justify-center gap-5 bg-sidebar bg-cover bg-center px-6 py-10 md:px-12 md:py-14">
          <p className="m-0 max-w-[14ch] font-display text-title leading-tight text-sidebar-heading md:text-[44px]">{t('auth.panelTitle')}</p>
          <p className="m-0 max-w-[42ch] text-body text-sidebar-ink md:text-[17px]">{t('auth.panelText')}</p>
        </section>
        <main className="flex flex-[1_1_480px] items-center justify-center px-5 py-10 md:py-12">
          <div className="flex w-full max-w-[460px] flex-col gap-5">
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
