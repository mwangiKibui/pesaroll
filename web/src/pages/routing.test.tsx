import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { fakeApi, json, jwt, me, orgToken, token } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

async function signIn() {
  await userEvent.type(await screen.findByRole('textbox', { name: 'Email or phone number' }), 'wanjiru@example.com')
  await userEvent.type(screen.getByLabelText('Password'), 'Mombasa-raha-7')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
}

it('signed out, a page that needs sign-in sends you to sign in, then back to it', async () => {
  fakeApi({ 'POST /api/auth/signin': () => token() })
  renderApp('/setup/organization')

  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  await signIn()

  expect(await screen.findByRole('heading', { name: "What's your organization called?" })).toBeInTheDocument()
})

it('with a live session and an organization, a reload stays signed in and sign-in skips to home', async () => {
  fakeApi({ 'POST /api/auth/refresh': () => orgToken() })
  renderApp('/signin')

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
})

it('without an organization, every signed-in page leads to the organization step', async () => {
  fakeApi({ 'POST /api/auth/refresh': () => token('from-cookie') })
  renderApp('/')

  expect(await screen.findByRole('heading', { name: "What's your organization called?" })).toBeInTheDocument()
})

it('with an organization, the old welcome page leads home', async () => {
  fakeApi({ 'POST /api/auth/refresh': () => orgToken() })
  renderApp('/welcome')

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
})

it('home names the organization and the next setup step, and signing out ends the session', async () => {
  const calls = fakeApi({ 'POST /api/auth/refresh': () => orgToken() })
  renderApp('/')

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
  expect(await screen.findByText('Finish setting up · 1 of 10')).toBeInTheDocument()
  expect(screen.getAllByText('Next: Companies').length).toBeGreaterThan(0)
  expect(screen.getByRole('link', { name: /Finish setting up/ })).toHaveAttribute('href', '/setup/companies')

  await userEvent.click(screen.getByRole('button', { name: 'Sign out' }))

  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  expect(calls.some(c => c.method === 'POST' && c.path === '/api/auth/signout' && c.auth === `Bearer ${jwt()}`)).toBe(true)
})

it.each(['//evil.example/phish', 'https://evil.example/phish'])('after sign-in, a planted redirect to %s is ignored', async target => {
  fakeApi({ 'POST /api/auth/signin': () => orgToken() })
  const { services } = renderApp(`/signin?redirect=${encodeURIComponent(target)}`)

  await signIn()

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
  expect(services.auth.status()).toBe('signedIn')
})

it.each(['/', '/setup/organization'])('without an organization and unable to onboard one (invited or removed), %s says so', async path => {
  fakeApi({
    'POST /api/auth/refresh': () => token('no-org'),
    'GET /api/auth/me': () => json(200, { ...me, signedUpVia: 'invitation', canOnboardOrganization: false }),
  })
  renderApp(path)

  expect(await screen.findByRole('heading', { name: "You're not part of an organization" })).toBeInTheDocument()
  expect(screen.getByText(/ask an administrator in your organization to add you back/)).toBeInTheDocument()
  expect(screen.queryByRole('textbox', { name: 'Organization name' })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
})

it('someone who signed up and comes back later, still without an organization, is taken to that step', async () => {
  fakeApi({ 'POST /api/auth/refresh': () => token('back-later') })
  renderApp('/no-organization')

  expect(await screen.findByRole('heading', { name: "What's your organization called?" })).toBeInTheDocument()
})
