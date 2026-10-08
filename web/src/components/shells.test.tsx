import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { AppShell, PhoneShell } from './shells'

const groups = [
  { items: [{ key: 'home', label: 'Home', href: '/' }] },
  { label: 'People', items: [{ key: 'employees', label: 'Employees', href: '/employees' }, { key: 'leave', label: 'Leave', href: '/leave', badge: 2 }] },
]

it('the admin layout has a named main menu with the current page marked', async () => {
  const { container } = render(
    <AppShell groups={groups} current="employees" organization="Mama Oliech Group" person="Grace Wanjiru"><h1>Employees</h1></AppShell>)

  const nav = screen.getByRole('navigation', { name: 'Main' })
  expect(within(nav).getByRole('link', { name: 'Employees' })).toHaveAttribute('aria-current', 'page')
  expect(within(nav).getByRole('link', { name: /Leave/ })).toHaveTextContent('2')
  expect(screen.getByRole('main')).toHaveTextContent('Employees')
  await expectNoA11yViolations(container)
})

it('on a phone the menu opens from a button', async () => {
  render(<AppShell groups={groups} current="home" organization="Mama Oliech Group" person="Grace Wanjiru"><p>Home</p></AppShell>)
  const toggle = screen.getByRole('button', { name: 'Menu' })
  expect(toggle).toHaveAttribute('aria-expanded', 'false')

  await userEvent.click(toggle)

  expect(toggle).toHaveAttribute('aria-expanded', 'true')
})

it('the phone layout has a bottom bar with the current tab marked', async () => {
  const { container } = render(
    <PhoneShell company="Mama Oliech Restaurants" title="Habari, Peter" tabs={[{ key: 'home', label: 'Home', href: '/' }, { key: 'leave', label: 'Leave', href: '/leave' }]} current="home">
      <p>October payslip</p>
    </PhoneShell>)

  const bar = screen.getByRole('navigation', { name: 'Main' })
  expect(within(bar).getByRole('link', { name: 'Home' })).toHaveAttribute('aria-current', 'page')
  expect(screen.getByRole('heading', { name: 'Habari, Peter' })).toBeInTheDocument()
  await expectNoA11yViolations(container)
})

it('the sidebar shows the person with a Sign out button', async () => {
  const onSignOut = vi.fn()
  render(<AppShell groups={groups} current="home" organization="Mama Oliech Group" person="Grace Wanjiru" onSignOut={onSignOut}><p>Home</p></AppShell>)

  await userEvent.click(screen.getByRole('button', { name: 'Sign out' }))

  expect(onSignOut).toHaveBeenCalledOnce()
})
