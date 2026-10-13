import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, company, fakeApi, json, orgToken } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

const westlands = company({ id: '0199c0de-0000-7000-8000-0000000000c1', legalName: 'Westlands Ltd' })
const mombasa = company({ id: '0199c0de-0000-7000-8000-0000000000c2', legalName: 'Mombasa Ltd' })
const amina = {
  membershipId: 'm-amina', userId: 'u-amina', firstName: 'Amina', lastName: 'Otieno', email: 'amina@example.com', phone: null, joinedAt: '2026-10-01T09:00:00Z',
  isYou: true, groups: [{ group: 'administrators', name: 'Administrators', allCompanies: true, companyIds: [] }],
}
const mary = {
  membershipId: 'm-mary', userId: 'u-mary', firstName: 'Mary', lastName: 'Wanjiku', email: 'mary@example.com', phone: null, joinedAt: '2026-10-10T09:00:00Z',
  isYou: false, groups: [{ group: 'hr', name: 'HR · Westlands Ltd', allCompanies: false, companyIds: [westlands.id] }],
}
const otieno = {
  id: 'inv-otieno', firstName: 'Otieno', lastName: 'Ouma', email: null, phone: '+254712345678', groups: ['payroll'], allCompanies: false,
  companyIds: [mombasa.id], status: 'open', expiresAt: '2026-10-20T09:00:00Z', sentAt: '2026-10-13T09:00:00Z', sendCount: 1,
}

function withTeam(routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [westlands, mombasa]),
    'GET /api/team/members': () => json(200, [amina, mary]),
    'GET /api/team/invitations': () => json(200, [otieno]),
    ...routes,
  })
}

const table = () => screen.getByRole('region', { name: 'Team' })
const rowOf = (name: string) => within(table()).getByText(name).closest('tr')!

it('lists people and invitations with their group, companies and status, paged, with Invite on the table', async () => {
  withTeam()
  const { container } = renderApp('/setup/team')

  expect(await screen.findByRole('heading', { name: 'Who else will use Pesaroll?' })).toBeInTheDocument()
  await within(await screen.findByRole('region', { name: 'Team' })).findByText('Mary Wanjiku')
  expect(within(table()).getAllByRole('columnheader').map(h => h.textContent)).toEqual(['Name', 'Group', 'Companies', 'Status', 'Actions'])
  expect(within(rowOf('Amina Otieno')).getAllByRole('cell').map(c => c.textContent).slice(0, 4))
    .toEqual(['Amina Otieno (you)amina@example.com', 'Administrator', 'All companies', 'Active'])
  expect(within(rowOf('Mary Wanjiku')).getAllByRole('cell').map(c => c.textContent).slice(1, 4)).toEqual(['HR', 'Westlands Ltd', 'Active'])
  expect(within(rowOf('Otieno Ouma')).getAllByRole('cell').map(c => c.textContent).slice(0, 4))
    .toEqual(['Otieno Ouma+254712345678', 'Payroll', 'Mombasa Ltd', 'Invited'])
  expect(screen.getByRole('navigation', { name: 'Pages of Team' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Invite' })).toBeInTheDocument()
  // Nobody removes themselves by accident.
  expect(within(rowOf('Amina Otieno')).queryByRole('button', { name: /Remove/ })).not.toBeInTheDocument()

  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getByText('Team access').closest('a')).toHaveAttribute('aria-current', 'page')
  // Payroll history is optional: once you're past it, it's ticked like the steps before.
  expect(within(steps).getByText('Payroll history').closest('a')).toHaveTextContent(/^✓/)
  expect(within(steps).getByText('Team access').closest('a')).toHaveTextContent(/^10/)
  await expectNoA11yViolations(container)
})

it('invites someone as HR for chosen companies, from a drawer', async () => {
  const calls = withTeam({ 'POST /api/team/invitations': () => json(201, { ...otieno, id: 'inv-new', firstName: 'Njeri', lastName: 'Kamau' }) })
  renderApp('/setup/team')
  await userEvent.click(await screen.findByRole('button', { name: 'Invite' }))
  const drawer = screen.getByRole('dialog', { name: 'Invite someone' })
  expect(within(drawer).getByRole('textbox', { name: 'First name' })).toHaveAttribute('placeholder', 'Your first name')

  await userEvent.type(within(drawer).getByRole('textbox', { name: 'First name' }), 'Njeri')
  await userEvent.type(within(drawer).getByRole('textbox', { name: 'Last name' }), 'Kamau')
  await userEvent.type(within(drawer).getByRole('textbox', { name: 'Email' }), 'njeri@example.com')
  // One or more groups: HR and Payroll together. Payroll with Approver warns about approving your own pay runs.
  expect(within(drawer).getByText(/Employees, structure, locations and leave/)).toBeInTheDocument()
  await userEvent.click(within(drawer).getByRole('checkbox', { name: /^HR/ }))
  await userEvent.click(within(drawer).getByRole('checkbox', { name: /^Payroll/ }))
  await userEvent.click(within(drawer).getByRole('checkbox', { name: /^Approver/ }))
  expect(within(drawer).getByText(/could approve pay runs they prepare/)).toBeInTheDocument()
  await userEvent.click(within(drawer).getByRole('checkbox', { name: /^Approver/ }))
  expect(within(drawer).queryByText(/could approve pay runs they prepare/)).not.toBeInTheDocument()
  // Every company starts ticked; untick Mombasa.
  await userEvent.click(within(drawer).getByRole('button', { name: /Companies/ }))
  await userEvent.click(screen.getByRole('option', { name: /Mombasa Ltd/ }))
  await userEvent.keyboard('{Escape}')
  await userEvent.click(within(drawer).getByRole('button', { name: 'Send invitation' }))

  await vi.waitFor(() => expect(calls.find(c => c.method === 'POST' && c.path === '/api/team/invitations')?.body).toEqual({
    firstName: 'Njeri', lastName: 'Kamau', email: 'njeri@example.com', phone: null, groups: ['hr', 'payroll'], allCompanies: false, companyIds: [westlands.id],
  }))
  expect(await screen.findByText('Invitation sent to Njeri Kamau')).toBeInTheDocument()
})

it('an invitation needs an email or phone, and the server\'s problems show under their fields', async () => {
  const calls = withTeam({
    'POST /api/team/invitations': () => json(409, { code: 'ACC-8003', message: 'njeri@example.com already has an invitation waiting. Resend it instead.', reference: 'R1' }),
  })
  renderApp('/setup/team')
  await userEvent.click(await screen.findByRole('button', { name: 'Invite' }))
  const drawer = screen.getByRole('dialog', { name: 'Invite someone' })
  await userEvent.type(within(drawer).getByRole('textbox', { name: 'First name' }), 'Njeri')
  await userEvent.type(within(drawer).getByRole('textbox', { name: 'Last name' }), 'Kamau')
  await userEvent.click(within(drawer).getByRole('checkbox', { name: /^Payroll/ }))
  await userEvent.click(within(drawer).getByRole('button', { name: 'Send invitation' }))
  expect(await within(drawer).findAllByText('Enter their email address or mobile number, so the invitation can reach them.')).not.toHaveLength(0)
  expect(calls.some(c => c.method === 'POST' && c.path === '/api/team/invitations')).toBe(false)

  await userEvent.type(within(drawer).getByRole('textbox', { name: 'Email' }), 'njeri@example.com')
  await userEvent.click(within(drawer).getByRole('button', { name: 'Send invitation' }))
  expect(await within(drawer).findByText(/already has an invitation waiting/)).toBeInTheDocument()
})

it('resends and revokes invitations, changes someone\'s access in a modal, and removes people after asking', async () => {
  const calls = withTeam({
    [`POST /api/team/invitations/${otieno.id}/resend`]: () => json(200, { ...otieno, sendCount: 2 }),
    [`DELETE /api/team/invitations/${otieno.id}`]: () => new Response(null, { status: 204 }),
    [`PUT /api/team/members/${mary.membershipId}/access`]: () => new Response(null, { status: 204 }),
    [`DELETE /api/team/members/${mary.membershipId}`]: () => new Response(null, { status: 204 }),
    [`DELETE /api/team/members/${amina.membershipId}`]: () => apiError(409, 'ACC-8007', 'Someone must stay an administrator. Make another person an administrator first.'),
  })
  renderApp('/setup/team')
  await within(await screen.findByRole('region', { name: 'Team' })).findByText('Otieno Ouma')

  await userEvent.click(within(rowOf('Otieno Ouma')).getByRole('button', { name: 'Resend invitation to Otieno Ouma' }))
  expect(await screen.findByText('Invitation sent again to Otieno Ouma')).toBeInTheDocument()

  await userEvent.click(within(rowOf('Otieno Ouma')).getByRole('button', { name: 'Cancel invitation to Otieno Ouma' }))
  const cancel = screen.getByRole('alertdialog', { name: "Cancel Otieno Ouma's invitation?" })
  await userEvent.click(within(cancel).getByRole('button', { name: 'Cancel invitation' }))
  await vi.waitFor(() => expect(calls.some(c => c.method === 'DELETE' && c.path === `/api/team/invitations/${otieno.id}`)).toBe(true))

  // Change access: the view drawer, then a small form on top of it.
  await userEvent.click(within(rowOf('Mary Wanjiku')).getByRole('button', { name: 'View Mary Wanjiku' }))
  const view = screen.getByRole('dialog', { name: 'Mary Wanjiku' })
  expect(within(view).getByText('HR · Westlands Ltd')).toBeInTheDocument()
  await userEvent.click(within(view).getByRole('button', { name: 'Change access' }))
  const modal = screen.getByRole('dialog', { name: "Change Mary Wanjiku's access" })
  expect(within(modal).getByRole('checkbox', { name: /^HR/ })).toBeChecked()
  await userEvent.click(within(modal).getByRole('checkbox', { name: /^Payroll/ }))   // HR and Payroll
  await userEvent.click(within(modal).getByRole('button', { name: 'Save' }))
  await vi.waitFor(() => expect(calls.find(c => c.method === 'PUT')?.body).toEqual({ groups: ['hr', 'payroll'], allCompanies: false, companyIds: [westlands.id] }))
  expect(await screen.findByText("Mary Wanjiku's access is updated")).toBeInTheDocument()
  await userEvent.keyboard('{Escape}')

  await userEvent.click(within(rowOf('Mary Wanjiku')).getByRole('button', { name: 'Remove Mary Wanjiku' }))
  const remove = screen.getByRole('alertdialog', { name: 'Remove Mary Wanjiku from the organization?' })
  await userEvent.click(within(remove).getByRole('button', { name: 'Remove' }))
  await vi.waitFor(() => expect(calls.some(c => c.method === 'DELETE' && c.path === `/api/team/members/${mary.membershipId}`)).toBe(true))
  expect(await screen.findByText('Mary Wanjiku is removed')).toBeInTheDocument()
})

it('the company selector shows who covers the chosen companies', async () => {
  withTeam()
  renderApp('/setup/team')
  await within(await screen.findByRole('region', { name: 'Team' })).findByText('Mary Wanjiku')
  await userEvent.click(screen.getByRole('button', { name: /Show/ }))
  await userEvent.click(screen.getByRole('option', { name: /Westlands Ltd/ }))
  await userEvent.keyboard('{Escape}')
  // Unticking Westlands leaves Mombasa: Amina (all companies) and Otieno's invitation, not Mary.
  expect(within(table()).queryByText('Mary Wanjiku')).not.toBeInTheDocument()
  expect(within(table()).getByText('Amina Otieno')).toBeInTheDocument()
  expect(within(table()).getByText('Otieno Ouma')).toBeInTheDocument()
})

it('someone who isn\'t an administrator is told who can manage the team', async () => {
  withTeam({ 'GET /api/team/members': () => apiError(403, 'ACC-8001', "You don't have permission to do this. Ask an administrator in your organization.") })
  renderApp('/setup/team')
  expect(await screen.findByText('Only administrators can see and change who uses Pesaroll. Ask one of them to add or change people.')).toBeInTheDocument()
})
