import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, fakeApi, token } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

const signedUp = () => ({ 'POST /api/auth/refresh': () => token('no-org-yet') })

it('asks for the organization name, with the setup steps and who is signed in', async () => {
  fakeApi(signedUp())
  const { container } = renderApp('/setup/organization')

  expect(await screen.findByRole('heading', { name: "What's your organization called?" })).toBeInTheDocument()
  expect(screen.getByRole('textbox', { name: 'Organization name' })).toHaveAttribute('placeholder', 'Your organization name')
  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getAllByRole('listitem')).toHaveLength(10)
  expect(within(steps).getByText('Organization').closest('li')).toHaveAttribute('aria-current', 'step')
  const navbar = screen.getByRole('banner')
  expect(await within(navbar).findByText('Wanjiru Kamau')).toBeInTheDocument()
  expect(within(navbar).getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /skip/i })).not.toBeInTheDocument()
  await expectNoA11yViolations(container)
})

it('checks the name before sending anything', async () => {
  const calls = fakeApi(signedUp())
  renderApp('/setup/organization')

  await userEvent.type(await screen.findByRole('textbox', { name: 'Organization name' }), ' A ')
  await userEvent.click(screen.getByRole('button', { name: 'Create organization' }))

  expect(screen.getByRole('textbox', { name: 'Organization name' })).toHaveAccessibleDescription(/Enter your organization's name \(2 to 120 characters\)/)
  expect(calls.filter(c => c.path === '/api/organizations')).toHaveLength(0)
})

it('creates the organization with the name as typed, trimmed, and goes home', async () => {
  const calls = fakeApi(signedUp())
  renderApp('/setup/organization')

  await userEvent.type(await screen.findByRole('textbox', { name: 'Organization name' }), '  Mama Oliech Group ')
  await userEvent.click(screen.getByRole('button', { name: 'Create organization' }))

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/organizations')?.body).toEqual({ name: 'Mama Oliech Group' })
  expect(screen.getAllByText('Mama Oliech Group').length).toBeGreaterThan(0)
})

it('a name the server refuses shows under the field; other refusals show as a banner with the reference', async () => {
  fakeApi({ ...signedUp(), 'POST /api/organizations': () => apiError(400, 'ONB-3501', "Enter your organization's name (2 to 120 characters).") })
  renderApp('/setup/organization')
  await userEvent.type(await screen.findByRole('textbox', { name: 'Organization name' }), 'Mama Oliech Group')
  await userEvent.click(screen.getByRole('button', { name: 'Create organization' }))
  expect(await screen.findByText("Enter your organization's name (2 to 120 characters).")).toBeInTheDocument()
})

it('already having an organization shows the message with its code and reference', async () => {
  fakeApi({ ...signedUp(), 'POST /api/organizations': () => apiError(409, 'ONB-3502', 'You already have an organization.') })
  renderApp('/setup/organization')
  await userEvent.type(await screen.findByRole('textbox', { name: 'Organization name' }), 'Mama Oliech Group')
  await userEvent.click(screen.getByRole('button', { name: 'Create organization' }))

  const alert = await screen.findByRole('alert')
  expect(alert).toHaveTextContent('You already have an organization.')
  expect(alert).toHaveTextContent('ONB-3502')
  expect(alert).toHaveTextContent('7F3K92QD')
})
