import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { fakeApi, token } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

it('signed out, the welcome page sends you to sign in, then back after signing in', async () => {
  fakeApi({ 'POST /api/auth/signin': () => token() })
  renderApp('/welcome')

  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  await userEvent.type(screen.getByRole('textbox', { name: 'Email or phone number' }), 'wanjiru@example.com')
  await userEvent.type(screen.getByLabelText('Password'), 'Mombasa-raha-7')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
})

it('with a live session, a reload stays signed in and sign-in skips to welcome', async () => {
  fakeApi({ 'POST /api/auth/refresh': () => token('from-cookie') })
  renderApp('/signin')

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
})

it('the welcome page says what comes next, and signing out ends the session', async () => {
  const calls = fakeApi({ 'POST /api/auth/refresh': () => token('from-cookie') })
  renderApp('/')

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
  expect(screen.getByText(/Create your organization/)).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: 'Sign out' }))

  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  expect(calls.some(c => c.method === 'POST' && c.path === '/api/auth/signout' && c.auth === 'Bearer from-cookie')).toBe(true)
})

it.each(['//evil.example/phish', 'https://evil.example/phish'])('after sign-in, a planted redirect to %s is ignored', async target => {
  fakeApi({ 'POST /api/auth/signin': () => token() })
  const { services } = renderApp(`/signin?redirect=${encodeURIComponent(target)}`)

  await userEvent.type(await screen.findByRole('textbox', { name: 'Email or phone number' }), 'wanjiru@example.com')
  await userEvent.type(screen.getByLabelText('Password'), 'Mombasa-raha-7')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
  expect(services.auth.status()).toBe('signedIn')
})
