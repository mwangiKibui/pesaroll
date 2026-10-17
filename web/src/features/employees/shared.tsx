import { useQuery } from '@tanstack/react-query'
import type { components } from '../../api/schema'
import { useServices } from '../../app/services'

/**
 * What the employee pieces share (OP-07): the setup step's Employees and People → Employees use the
 * same list, profile, form and upload, with the company as an input.
 */
export type Company = components['schemas']['CompanyResponse']
export type Row = components['schemas']['EmployeeRow']
export type Detail = components['schemas']['EmployeeResponse']
export type ImportRow = components['schemas']['ImportRow']

export const types = ['permanent', 'contract', 'casual', 'intern'] as const
export const tabs = ['personal', 'ids', 'job', 'pay', 'account'] as const
export type TabKey = (typeof tabs)[number]

export const none = <span className="text-muted">—</span>

/** "15 Jan 2024". */
export const formatDate = (iso: string) =>
  new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${iso}T00:00:00Z`))
export const shortDate = (iso: string | null | undefined) => (!iso ? none : formatDate(iso))

/** How the company pays people: the methods of its paying accounts (none before one is set), and its default's. */
export function usePaysBy(companyId: string) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['payment-accounts', companyId],
    enabled: !!companyId,
    // Another company's answer stays until this one's arrives, so screens don't fall back to loading.
    placeholderData: previous => previous,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/companies/{companyId}/payment-accounts', { params: { path: { companyId } } })
      if (error || !data) throw new Error('payment-accounts')
      return data
    },
    select: accounts => ({
      methods: [...new Set(accounts.map(a => a.method))] as ('bank' | 'mpesa')[],
      usual: (accounts.find(a => a.isDefault)?.method ?? null) as 'bank' | 'mpesa' | null,
    }),
  })
}
