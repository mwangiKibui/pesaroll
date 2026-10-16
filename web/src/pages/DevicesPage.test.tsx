import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, fakeApi, json, orgToken } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

// AUTH-05: where you're signed in.
const laptop = { id: 's-laptop', device: 'Chrome on Windows', ipAddress: '197.232.10.4', signedInAt: '2026-10-14T07:00:00Z', lastActiveAt: '2026-10-16T09:00:00Z', current: true }
const phone = { id: 's-phone', device: 'Safari on iPhone', ipAddress: '41.90.64.12', signedInAt: '2026-10-10T18:00:00Z', lastActiveAt: '2026-10-15T20:30:00Z', current: false }
const office = { id: 's-office', device: 'Edge on Windows', ipAddress: null, signedInAt: '2026-10-01T08:00:00Z', lastActiveAt: '2026-10-12T16:00:00Z', current: false }

function withDevices(routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({ 'POST /api/auth/refresh': () => orgToken(), 'GET /api/auth/sessions': () => json(200, [laptop, phone, office]), ...routes })
}

const table = () => screen.getByRole('region', { name: 'Signed-in devices' })
const rowOf = (device: string) => within(table()).getByText(device).closest('tr')!

it('lists where you are signed in, with this device marked', async () => {
  withDevices()
  const { container } = renderApp('/account/devices')

  expect(await screen.findByRole('heading', { name: "Where you're signed in" })).toBeInTheDocument()
  await within(await screen.findByRole('region', { name: 'Signed-in devices' })).findByText('Safari on iPhone')
  expect(within(table()).getAllByRole('columnheader').map(h => h.textContent)).toEqual(['Device', 'Where', 'Signed in', 'Last active', 'Actions'])
  expect(rowOf('Chrome on Windows')).toHaveTextContent('This device')
  expect(rowOf('Safari on iPhone')).toHaveTextContent('41.90.64.12')
  expect(rowOf('Safari on iPhone')).not.toHaveTextContent('This device')
  await expectNoA11yViolations(container)
})

it('signs out another device, and every other device after asking', async () => {
  const calls = withDevices({
    'POST /api/auth/sessions/s-phone/end': () => new Response(null, { status: 204 }),
    'POST /api/auth/sessions/end-others': () => new Response(null, { status: 204 }),
  })
  renderApp('/account/devices')
  await within(await screen.findByRole('region', { name: 'Signed-in devices' })).findByText('Safari on iPhone')

  await userEvent.click(within(rowOf('Safari on iPhone')).getByRole('button', { name: 'Sign out Safari on iPhone' }))
  await vi.waitFor(() => expect(calls.some(c => c.method === 'POST' && c.path === '/api/auth/sessions/s-phone/end')).toBe(true))
  expect(await screen.findByText('Safari on iPhone is signed out')).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: 'Sign out of all other devices' }))
  const confirm = screen.getByRole('alertdialog', { name: 'Sign out of all other devices?' })
  await userEvent.click(within(confirm).getByRole('button', { name: 'Sign out others' }))
  await vi.waitFor(() => expect(calls.some(c => c.method === 'POST' && c.path === '/api/auth/sessions/end-others')).toBe(true))
  expect(await screen.findByText('Your other devices are signed out')).toBeInTheDocument()
})

it('signing out this device signs you out here', async () => {
  const calls = withDevices()
  renderApp('/account/devices')
  await within(await screen.findByRole('region', { name: 'Signed-in devices' })).findByText('Chrome on Windows')

  await userEvent.click(within(rowOf('Chrome on Windows')).getByRole('button', { name: 'Sign out Chrome on Windows' }))
  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  expect(calls.some(c => c.method === 'POST' && c.path === '/api/auth/signout')).toBe(true)
})

it('a device that has already ended says so', async () => {
  withDevices({ 'POST /api/auth/sessions/s-office/end': () => apiError(404, 'AUTH-1017', "We couldn't find that sign-in. It may have ended already.") })
  renderApp('/account/devices')
  await within(await screen.findByRole('region', { name: 'Signed-in devices' })).findByText('Edge on Windows')

  await userEvent.click(within(rowOf('Edge on Windows')).getByRole('button', { name: 'Sign out Edge on Windows' }))
  expect(await screen.findByText(/We couldn't find that sign-in/)).toBeInTheDocument()
})

it('your name in the setup navbar opens where you are signed in', async () => {
  withDevices()
  renderApp('/setup/companies')
  const link = await screen.findByRole('link', { name: 'Wanjiru Kamau' })
  expect(link).toHaveAttribute('href', '/account/devices')
})
