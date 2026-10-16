import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, company, fakeApi, json, me, orgToken } from '../test/fakeApi'
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

const areaKeys = ['companies', 'employees', 'locations', 'structure', 'leave', 'schedules', 'pay_items', 'payment_accounts', 'payroll_history', 'team']
const areaNames: Record<string, string> = { companies: 'Companies', employees: 'Employees', locations: 'Locations', structure: 'Org structure', leave: 'Leave types and plans',
  schedules: 'Payroll schedules', pay_items: 'Earnings, deductions and payees', payment_accounts: 'Paying accounts', payroll_history: 'Payroll history', team: 'Team access' }
const levels = (set: Record<string, string>) => areaKeys.map(area => ({ area, level: set[area] ?? 'none' }))
const group = (key: string, name: string, people: number, set: Record<string, string>, standard = true) => ({ key, name, standard, people, areas: levels(set) })
const teamGroups = (hr = group('hr', 'HR', 1, { employees: 'manage', locations: 'manage', structure: 'manage', leave: 'manage', schedules: 'view' })) => ({
  areas: areaKeys.map(key => ({ key, name: { en: areaNames[key], sw: areaNames[key] } })),
  items: [group('administrators', 'Administrators', 1, Object.fromEntries(areaKeys.map(k => [k, 'manage']))), hr,
    group('payroll', 'Payroll', 0, { employees: 'manage', schedules: 'manage', pay_items: 'manage', payment_accounts: 'manage', payroll_history: 'manage', locations: 'view', structure: 'view', leave: 'view' }),
    group('approvers', 'Approvers', 0, {})],
})

function withTeam(routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [westlands, mombasa]),
    'GET /api/team/members': () => json(200, [amina, mary]),
    'GET /api/team/invitations': () => json(200, [otieno]),
    'GET /api/team/groups': () => json(200, teamGroups()),
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
  // Each group says what it can do in this organization (ACC-04).
  expect(within(drawer).getByText('Manage: employees, locations, org structure, leave types and plans · View: payroll schedules')).toBeInTheDocument()
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

// ACC-04: a pencil on every row changes access straight away: a member's, or an invitation's before it is accepted.
it('the pencil on a row changes a member\'s access, or an open invitation\'s groups and companies', async () => {
  const calls = withTeam({
    [`PUT /api/team/members/${mary.membershipId}/access`]: () => new Response(null, { status: 204 }),
    [`PUT /api/team/invitations/${otieno.id}/access`]: () => json(200, { ...otieno, groups: ['hr'], allCompanies: true, companyIds: [] }),
  })
  renderApp('/setup/team')
  await within(await screen.findByRole('region', { name: 'Team' })).findByText('Otieno Ouma')

  await userEvent.click(within(rowOf('Mary Wanjiku')).getByRole('button', { name: "Change Mary Wanjiku's access" }))
  const access = screen.getByRole('dialog', { name: "Change Mary Wanjiku's access" })
  await userEvent.click(within(access).getByRole('checkbox', { name: /^Payroll/ }))
  await userEvent.click(within(access).getByRole('button', { name: 'Save' }))
  await vi.waitFor(() => expect(calls.find(c => c.path === `/api/team/members/${mary.membershipId}/access`)?.body)
    .toEqual({ groups: ['hr', 'payroll'], allCompanies: false, companyIds: [westlands.id] }))
  expect(await screen.findByText("Mary Wanjiku's access is updated")).toBeInTheDocument()

  await userEvent.click(within(rowOf('Otieno Ouma')).getByRole('button', { name: "Change Otieno Ouma's invitation" }))
  const invitation = screen.getByRole('dialog', { name: "Change Otieno Ouma's invitation" })
  expect(within(invitation).getByText(/Their invitation link stays the same/)).toBeInTheDocument()
  expect(within(invitation).getByRole('checkbox', { name: /^Payroll/ })).toBeChecked()
  await userEvent.click(within(invitation).getByRole('checkbox', { name: /^Payroll/ }))
  await userEvent.click(within(invitation).getByRole('checkbox', { name: /^HR/ }))
  await userEvent.click(within(invitation).getByRole('button', { name: 'Save' }))
  await vi.waitFor(() => expect(calls.find(c => c.path === `/api/team/invitations/${otieno.id}/access`)?.body)
    .toEqual({ groups: ['hr'], allCompanies: false, companyIds: [mombasa.id] }))
  expect(await screen.findByText("Otieno Ouma's invitation is updated")).toBeInTheDocument()
})

// ACC-04: what each group can do, per area, changed for the organization.
it('the Groups tab shows what each group can do; a group\'s drawer changes it, or puts it back to standard', async () => {
  const hrChanged = group('hr', 'HR', 1, { employees: 'manage', locations: 'manage', structure: 'manage', leave: 'manage', schedules: 'view', payroll_history: 'view' }, false)
  const calls = withTeam({
    'PUT /api/team/groups/hr/access': () => json(200, hrChanged),
    'DELETE /api/team/groups/hr/access': () => json(200, teamGroups().items[1]),
    'PUT /api/team/groups/administrators/access': () => apiError(409, 'ACC-8007', 'Someone must stay an administrator. Make another person an administrator first.'),
  })
  renderApp('/setup/team')
  await within(await screen.findByRole('region', { name: 'Team' })).findByText('Mary Wanjiku')

  await userEvent.click(screen.getByRole('tab', { name: 'Groups' }))
  const groupsTable = await screen.findByRole('region', { name: 'Groups' })
  const hrRow = within(groupsTable).getByText('HR').closest('tr')!
  expect(hrRow).toHaveTextContent('Manage: employees, locations, org structure, leave types and plans · View: payroll schedules')
  expect(within(groupsTable).getByText('Administrators').closest('tr')).toHaveTextContent('Everything, including team access')

  await userEvent.click(within(hrRow).getByRole('button', { name: 'Change what HR can do' }))
  const drawer = screen.getByRole('dialog', { name: 'What HR can do' })
  expect(within(drawer).queryByRole('button', { name: 'Back to standard' })).not.toBeInTheDocument()
  await userEvent.click(within(drawer).getByRole('combobox', { name: 'Payroll history' }))
  await userEvent.click(screen.getByRole('option', { name: 'View' }))
  await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }))
  await vi.waitFor(() => expect(calls.find(c => c.method === 'PUT' && c.path === '/api/team/groups/hr/access')?.body).toEqual({
    areas: { companies: 'none', employees: 'manage', locations: 'manage', structure: 'manage', leave: 'manage', schedules: 'view', pay_items: 'none',
      payment_accounts: 'none', payroll_history: 'view', team: 'none' },
  }))
  expect(await screen.findByText("HR's access is updated")).toBeInTheDocument()
})

it('Back to standard puts a changed group back, and a change that would lock everyone out shows its code', async () => {
  const hrChanged = group('hr', 'HR', 1, { employees: 'view' }, false)
  const calls = withTeam({
    'GET /api/team/groups': () => json(200, teamGroups(hrChanged)),
    'DELETE /api/team/groups/hr/access': () => json(200, teamGroups().items[1]),
    'PUT /api/team/groups/administrators/access': () => apiError(409, 'ACC-8007', 'Someone must stay an administrator. Make another person an administrator first.'),
  })
  renderApp('/setup/team')
  await within(await screen.findByRole('region', { name: 'Team' })).findByText('Mary Wanjiku')
  await userEvent.click(screen.getByRole('tab', { name: 'Groups' }))
  const groupsTable = await screen.findByRole('region', { name: 'Groups' })
  expect(within(groupsTable).getByText('HR').closest('tr')).toHaveTextContent('Changed for your organization')

  await userEvent.click(within(groupsTable).getByRole('button', { name: 'Change what HR can do' }))
  await userEvent.click(within(screen.getByRole('dialog', { name: 'What HR can do' })).getByRole('button', { name: 'Back to standard' }))
  await vi.waitFor(() => expect(calls.some(c => c.method === 'DELETE' && c.path === '/api/team/groups/hr/access')).toBe(true))
  expect(await screen.findByText('HR is back to standard')).toBeInTheDocument()

  await userEvent.click(within(groupsTable).getByRole('button', { name: 'Change what Administrators can do' }))
  const admins = screen.getByRole('dialog', { name: 'What Administrators can do' })
  await userEvent.click(within(admins).getByRole('combobox', { name: 'Team access' }))
  await userEvent.click(screen.getByRole('option', { name: 'View' }))
  await userEvent.click(within(admins).getByRole('button', { name: 'Save' }))
  expect(await within(admins).findByText(/ACC-8007/)).toBeInTheDocument()
})

it('with View on team access only, shows people, invitations and groups with nothing that changes them', async () => {
  withTeam({ 'GET /api/auth/me': () => json(200, { ...me, permissions: [{ permission: 'team.view', allCompanies: true, companyIds: [] }] }) })
  renderApp('/setup/team')
  await within(await screen.findByRole('region', { name: 'Team' })).findByText('Otieno Ouma')

  expect(screen.queryByRole('button', { name: 'Invite' })).not.toBeInTheDocument()
  expect(within(table()).getAllByRole('button').map(b => b.getAttribute('aria-label'))).toEqual(['View Amina Otieno', 'View Mary Wanjiku', 'View Otieno Ouma'])
  await userEvent.click(within(rowOf('Mary Wanjiku')).getByRole('button', { name: 'View Mary Wanjiku' }))
  expect(within(screen.getByRole('dialog', { name: 'Mary Wanjiku' })).queryByRole('button', { name: /Change|Remove/ })).not.toBeInTheDocument()
  await userEvent.keyboard('{Escape}')

  await userEvent.click(screen.getByRole('tab', { name: 'Groups' }))
  const groupsTable = await screen.findByRole('region', { name: 'Groups' })
  expect(within(groupsTable).queryByRole('button')).not.toBeInTheDocument()
})

it('the groups to choose from in Invite say what each can do in this organization', async () => {
  withTeam({ 'GET /api/team/groups': () => json(200, teamGroups(group('hr', 'HR', 1, { employees: 'manage', team: 'view' }, false))) })
  renderApp('/setup/team')
  await within(await screen.findByRole('region', { name: 'Team' })).findByText('Mary Wanjiku')
  await userEvent.click(screen.getByRole('button', { name: 'Invite' }))
  const invite = screen.getByRole('dialog')
  expect(within(invite).getByRole('checkbox', { name: /^HR/ }).closest('label')).toHaveTextContent('Manage: employees · View: team access')
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

it('someone without View on team access is told who can manage the team', async () => {
  withTeam({ 'GET /api/team/members': () => apiError(403, 'ACC-8001', "You don't have permission to do this. Ask an administrator in your organization.") })
  renderApp('/setup/team')
  expect(await screen.findByText("You can't see who uses Pesaroll. Ask someone who manages team access.")).toBeInTheDocument()
})
