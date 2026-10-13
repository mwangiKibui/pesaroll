import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, fakeApi, json, orgToken } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

const invitation = {
  organization: 'Mama Oliech Group', invitedBy: 'Amina Otieno', firstName: 'Mary', lastName: 'Wanjiku', email: 'mary@example.com', phone: null,
  group: 'hr', allCompanies: false, companies: ['Westlands Ltd', 'Mombasa Ltd'], expiresAt: '2026-10-20T09:00:00Z', hasAccount: false,
}

it('a new colleague sees who invited them to what, confirms their name, sets a password and lands in the app', async () => {
  const calls = fakeApi({
    'GET /api/invitations/tok-1': () => json(200, invitation),
    'POST /api/invitations/tok-1/accept': () => orgToken(),
  })
  const { container } = renderApp('/invite/tok-1')

  expect(await screen.findByRole('heading', { name: 'Join Mama Oliech Group' })).toBeInTheDocument()
  expect(screen.getByText('Amina Otieno invited you to join as HR, for Westlands Ltd and Mombasa Ltd.')).toBeInTheDocument()
  expect(screen.getByText('mary@example.com')).toBeInTheDocument()
  expect(screen.getByRole('textbox', { name: 'First name' })).toHaveValue('Mary')
  expect(screen.getByRole('textbox', { name: 'Last name' })).toHaveValue('Wanjiku')
  expect(screen.getByRole('textbox', { name: 'First name' })).toHaveAttribute('placeholder', 'Your first name')
  await expectNoA11yViolations(container)

  // The same password rules as signing up.
  await userEvent.type(screen.getByLabelText('Password'), 'short')
  await userEvent.click(screen.getByRole('button', { name: 'Accept and join' }))
  expect(await screen.findByText(/Use at least 8 characters/)).toBeInTheDocument()
  expect(calls.some(c => c.path === '/api/invitations/tok-1/accept')).toBe(false)

  await userEvent.clear(screen.getByLabelText('Password'))
  await userEvent.type(screen.getByLabelText('Password'), 'Mombasa-raha-7')
  await userEvent.clear(screen.getByRole('textbox', { name: 'Last name' }))
  await userEvent.type(screen.getByRole('textbox', { name: 'Last name' }), 'Wanjiku Kariuki')
  await userEvent.click(screen.getByRole('button', { name: 'Accept and join' }))
  await vi.waitFor(() => expect(calls.find(c => c.path === '/api/invitations/tok-1/accept')?.body)
    .toEqual({ firstName: 'Mary', lastName: 'Wanjiku Kariuki', password: 'Mombasa-raha-7' }))
  // Signed in, on into the app.
  await vi.waitFor(() => expect(screen.queryByRole('heading', { name: 'Join Mama Oliech Group' })).not.toBeInTheDocument())
})

it('someone with an account enters their password to join', async () => {
  const calls = fakeApi({
    'GET /api/invitations/tok-2': () => json(200, { ...invitation, group: 'administrators', allCompanies: true, companies: [], hasAccount: true }),
    'POST /api/invitations/tok-2/accept': () => apiError(401, 'AUTH-1001', "That email or phone number and password don't match."),
  })
  renderApp('/invite/tok-2')

  expect(await screen.findByText('Amina Otieno invited you to join as an administrator, for every company.')).toBeInTheDocument()
  expect(screen.getByText('You already have a Pesaroll account with mary@example.com. Enter its password to join.')).toBeInTheDocument()
  expect(screen.queryByRole('textbox', { name: 'First name' })).not.toBeInTheDocument()
  await userEvent.type(screen.getByLabelText('Password'), 'wrong')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in and join' }))
  expect(await screen.findByText(/don't match/)).toBeInTheDocument()
  expect(calls.find(c => c.path === '/api/invitations/tok-2/accept')?.body).toEqual({ firstName: null, lastName: null, password: 'wrong' })
})

it('a link that no longer works says why, with its code, and offers sign-in', async () => {
  fakeApi({ 'GET /api/invitations/old': () => apiError(410, 'AUTH-1012', 'This invitation has expired. Ask the person who invited you to send it again.') })
  renderApp('/invite/old')

  expect(await screen.findByRole('heading', { name: "This invitation can't be used" })).toBeInTheDocument()
  const alert = screen.getByRole('alert')
  expect(within(alert).getByText(/has expired/)).toBeInTheDocument()
  expect(within(alert).getByText(/AUTH-1012/)).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/signin')
})
