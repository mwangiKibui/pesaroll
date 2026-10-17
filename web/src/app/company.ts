import { createContext, useContext } from 'react'

/**
 * The company chosen in the top bar (DASH-03): one at a time, the person's first to start. Every
 * page of the organization portal reads it (`useCurrentCompany`); setup pages keep their own
 * multi-company `CompanySelect`. It is remembered per person on this device as a convenience only;
 * the API still checks every company a page asks for.
 */
export interface CurrentCompany { companyId: string | null; setCompanyId: (id: string) => void }

export const CurrentCompanyContext = createContext<CurrentCompany>({ companyId: null, setCompanyId: () => {} })

export const useCurrentCompany = () => useContext(CurrentCompanyContext)

/** The one they chose while they still have it, else their first company, else none. */
export function chooseCompany(companies: { id: string }[], saved: string | null): string | null {
  return companies.find(c => c.id === saved)?.id ?? companies[0]?.id ?? null
}

const key = (userId: string) => `pesaroll.company.${userId}`

export function rememberedCompany(userId: string): string | null {
  try {
    return localStorage.getItem(key(userId))
  } catch {
    return null   // storage blocked (private window): start on the first company
  }
}

export function rememberCompany(userId: string, companyId: string) {
  try {
    localStorage.setItem(key(userId), companyId)
  } catch {
    // storage blocked: the choice lasts until the page reloads
  }
}
