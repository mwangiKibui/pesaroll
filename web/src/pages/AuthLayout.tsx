import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { LanguageSwitch, ThemeSwitch } from '../components/switches'

/** Sign-up and sign-in: the green welcome panel on the left (on top on phones), the form on the right. */
export function AuthLayout({ title, intro, children }: { title: string; intro: string; children: ReactNode }) {
  const { t } = useTranslation()
  const facts = [['fact1Value', 'fact1'], ['fact2Value', 'fact2'], ['fact3Value', 'fact3']] as const
  return (
    <div className="flex min-h-dvh flex-wrap bg-page">
      <section className="flex flex-[1_1_420px] flex-col gap-6 bg-sidebar px-6 py-8 text-sidebar-ink md:gap-7 md:px-12 md:py-14">
        <span className="font-display text-drawer-title text-sidebar-accent">Pesaroll</span>
        <p className="m-0 max-w-[14ch] font-display text-title leading-tight text-sidebar-heading md:text-[44px]">{t('auth.panelTitle')}</p>
        <p className="m-0 hidden max-w-[40ch] text-[17px] md:block">{t('auth.panelText')}</p>
        <dl className="m-0 hidden flex-col gap-3.5 md:flex">
          {facts.map(([value, text]) => (
            <div key={value} className="flex items-baseline gap-3">
              <dt className="min-w-16 font-display text-drawer-title text-sidebar-accent">{t(`auth.${value}`)}</dt>
              <dd className="m-0">{t(`auth.${text}`)}</dd>
            </div>
          ))}
        </dl>
      </section>
      <main className="flex flex-[1_1_480px] items-center justify-center px-5 py-10 md:py-12">
        <div className="flex w-full max-w-[460px] flex-col gap-5">
          <div className="flex flex-col gap-1.5">
            <h1 className="m-0 font-display text-title text-ink">{title}</h1>
            <p className="m-0 text-muted">{intro}</p>
          </div>
          {children}
          <div className="flex flex-col gap-3 border-t border-line pt-5">
            <LanguageSwitch />
            <ThemeSwitch />
          </div>
        </div>
      </main>
    </div>
  )
}
