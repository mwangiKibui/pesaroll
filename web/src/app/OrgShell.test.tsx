import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { company, fakeApi, json, me, orgToken } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => { vi.unstubAllGlobals(); localStorage.clear() })

const westlands = company({ id: '0199c0de-0000-7000-8000-0000000000c1', legalName: 'Westlands Ltd' })
const mombasa = company({ id: '0199c0de-0000-7000-8000-0000000000c2', legalName: 'Mombasa Ltd' })
const grant = (permission: string, companyIds: string[] = []) => ({ permission, allCompanies: companyIds.length === 0, companyIds })
const both = (area: string) => [grant(`${area}.view`), grant(`${area}.manage`)]
const hr = [...both('employees'), ...both('locations'), ...both('structure'), ...both('leave'), grant('schedules.view')]

function signedIn(routes: Parameters<typeof fakeApi>[0] = {}, permissions?: ReturnType<typeof grant>[]) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [westlands, mombasa]),
    ...(permissions ? { 'GET /api/auth/me': () => json(200, { ...me, permissions }) } : {}),
    ...routes,
  })
}

const nav = () => screen.getByRole('navigation', { name: 'Main' })

it('after setup, home opens in the shell: organization, company switcher, search, sections and the person', async () => {
  signedIn()
  const { container } = renderApp('/')

  expect(await screen.findByRole('heading', { name: 'Home', level: 1 })).toBeInTheDocument()
  const banner = screen.getByRole('banner')
  expect(await within(banner).findByText('Mama Oliech Group')).toBeInTheDocument()
  expect(within(banner).getByRole('combobox', { name: 'Company' })).toHaveValue('Westlands Ltd')
  expect(within(banner).getByRole('combobox', { name: 'Search Pesaroll' })).toBeInTheDocument()
  expect(within(nav()).getByRole('link', { name: 'Home' })).toHaveAttribute('aria-current', 'page')
  expect(within(nav()).getAllByRole('button', { name: /^(Pay|People|Organization|Admin)/ }).map(b => b.textContent?.trim())).toEqual(['Pay', 'People', 'Organization', 'Admin'])
  expect(screen.getByRole('button', { name: /Wanjiru Kamau/ })).toBeInTheDocument()
  await expectNoA11yViolations(container)
})

it('HR sees People and HR\'s organization pages, and no Pay', async () => {
  signedIn({}, hr)
  renderApp('/')
  await within(await screen.findByRole('navigation', { name: 'Main' })).findByRole('button', { name: /People/ })
  expect(within(nav()).getAllByRole('button', { name: /^(Pay|People|Organization|Admin)/ }).map(b => b.textContent?.trim())).toEqual(['People', 'Organization'])
  await userEvent.click(within(nav()).getByRole('button', { name: /People/ }))
  expect(within(nav()).getByRole('link', { name: 'Employees' })).toHaveAttribute('href', '/people/employees')
})

it('Setup is a button in the top bar for people who manage companies, with no progress card', async () => {
  signedIn()
  renderApp('/')
  const banner = await screen.findByRole('banner')
  expect(await within(banner).findByRole('link', { name: 'Setup' })).toHaveAttribute('href', '/setup/organization')
  expect(screen.queryByText(/Finish setting up/)).not.toBeInTheDocument()
})

it('HR has no Setup button', async () => {
  signedIn({}, hr)
  renderApp('/')
  await within(await screen.findByRole('navigation', { name: 'Main' })).findByRole('button', { name: /People/ })
  expect(within(screen.getByRole('banner')).queryByRole('link', { name: 'Setup' })).not.toBeInTheDocument()
})

it('the company switcher is remembered, and a group for one company shows its pages only there', async () => {
  signedIn({}, [grant('employees.view', [mombasa.id])])
  renderApp('/')
  const switcher = await within(await screen.findByRole('banner')).findByRole('combobox', { name: 'Company' })
  await within(await screen.findByRole('navigation', { name: 'Main' })).findByRole('link', { name: 'Home' })
  expect(within(nav()).queryByRole('button', { name: /People/ })).not.toBeInTheDocument()

  await userEvent.click(switcher)
  await userEvent.click(screen.getByRole('option', { name: 'Mombasa Ltd' }))
  expect(switcher).toHaveValue('Mombasa Ltd')
  expect(within(nav()).getByRole('button', { name: /People/ })).toBeInTheDocument()
  expect(localStorage.getItem(`pesaroll.company.${me.id}`)).toBe(mombasa.id)
})

it('search offers only pages the person may open, and opens one', async () => {
  signedIn({}, hr)
  renderApp('/')
  const search = await within(await screen.findByRole('banner')).findByRole('combobox', { name: 'Search Pesaroll' })
  await within(await screen.findByRole('navigation', { name: 'Main' })).findByRole('button', { name: /People/ })

  // HR may view schedules (to pick one for an employee) but the page is for people who manage them (OP-05).
  await userEvent.type(search, 'payroll')
  expect(screen.queryAllByRole('option')).toEqual([])
  await userEvent.clear(search)
  await userEvent.type(search, 'branch')
  expect(screen.getAllByRole('option').map(o => o.textContent)).toEqual(['Locations'])
  await userEvent.keyboard('{Enter}')
  expect(await screen.findByRole('heading', { name: 'Where do your people work?' })).toBeInTheDocument()
})

it('the person menu shows how they use Pesaroll and signs out', async () => {
  const calls = signedIn()
  renderApp('/')
  await userEvent.click(await screen.findByRole('button', { name: /Wanjiru Kamau/ }))
  const menu = screen.getByRole('dialog', { name: 'Your account' })
  expect(menu).toHaveTextContent('wanjiru@example.com')
  expect(within(menu).getByText('Organization portal')).toBeInTheDocument()
  expect(within(menu).getByRole('link', { name: "Where you're signed in" })).toHaveAttribute('href', '/account/devices')

  await userEvent.click(within(menu).getByRole('button', { name: 'Sign out' }))
  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  expect(calls.some(c => c.method === 'POST' && c.path === '/api/auth/signout')).toBe(true)
})
