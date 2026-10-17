import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { AppShell, PhoneShell, type ShellSection } from './shells'

const sections: ShellSection[] = [
  { key: 'main', links: [{ key: 'home', label: 'Home', href: '/' }] },
  { key: 'people', label: 'People', links: [{ key: 'employees', label: 'Employees', href: '/employees' }, { key: 'leave', label: 'Leave', href: '/leave', badge: 2 }] },
  { key: 'pay', label: 'Pay', links: [{ key: 'payRuns', label: 'Pay runs', href: '/pay/runs', badge: 3 }] },
]

function shell(props: Partial<Parameters<typeof AppShell>[0]> = {}) {
  return render(
    <AppShell sections={sections} current="employees" organization="Mama Oliech Group" person={{ name: 'Grace Wanjiru', detail: 'grace@example.com' }}
      topBar={<input aria-label="Search Pesaroll" placeholder="Your search" />} personMenu={() => <p>Menu for Grace</p>} {...props}>
      <h1>Employees</h1>
    </AppShell>)
}

it('has a top bar with the organization, a named main menu with the current page marked, and the page', async () => {
  const { container } = shell()

  const banner = screen.getByRole('banner')
  expect(banner).toHaveTextContent('Mama Oliech Group')
  expect(within(banner).getByRole('textbox', { name: 'Search Pesaroll' })).toBeInTheDocument()
  const nav = screen.getByRole('navigation', { name: 'Main' })
  expect(within(nav).getByRole('link', { name: 'Employees' })).toHaveAttribute('aria-current', 'page')
  expect(screen.getByRole('main')).toHaveTextContent('Employees')
  await expectNoA11yViolations(container)
})

it('sections open and close; the one holding the current page opens by itself, and a closed one adds up its counts', async () => {
  shell()
  const nav = screen.getByRole('navigation', { name: 'Main' })
  const people = within(nav).getByRole('button', { name: /People/ })
  const pay = within(nav).getByRole('button', { name: /Pay/ })
  expect(people).toHaveAttribute('aria-expanded', 'true')
  expect(pay).toHaveAttribute('aria-expanded', 'false')
  expect(pay).toHaveTextContent('3')
  expect(within(nav).queryByRole('link', { name: /Pay runs/ })).not.toBeInTheDocument()

  await userEvent.click(pay)
  expect(pay).toHaveAttribute('aria-expanded', 'true')
  expect(within(nav).getByRole('link', { name: /Pay runs/ })).toBeInTheDocument()

  await userEvent.click(people)
  expect(people).toHaveAttribute('aria-expanded', 'false')
  expect(within(nav).queryByRole('link', { name: 'Employees' })).not.toBeInTheDocument()
  expect(people).toHaveTextContent('2')
})

it('following a link goes through onNavigate, so the app doesn\'t reload', async () => {
  const onNavigate = vi.fn()
  shell({ onNavigate })
  await userEvent.click(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: /Leave/ }))
  expect(onNavigate).toHaveBeenCalledWith('/leave')
})

it('on a phone the menu opens from a button and closes from the scrim', async () => {
  shell()
  const toggle = screen.getByRole('button', { name: 'Menu' })
  expect(toggle).toHaveAttribute('aria-expanded', 'false')

  await userEvent.click(toggle)
  expect(toggle).toHaveAttribute('aria-expanded', 'true')

  await userEvent.click(screen.getByRole('button', { name: 'Close the menu' }))
  expect(toggle).toHaveAttribute('aria-expanded', 'false')
})

it('the person at the bottom of the sidebar opens their menu, and Escape closes it', async () => {
  shell()
  const person = screen.getByRole('button', { name: /Grace Wanjiru/ })
  expect(person).toHaveAttribute('aria-expanded', 'false')

  await userEvent.click(person)
  expect(within(screen.getByRole('dialog', { name: 'Your account' })).getByText('Menu for Grace')).toBeInTheDocument()

  await userEvent.keyboard('{Escape}')
  expect(screen.queryByRole('dialog', { name: 'Your account' })).not.toBeInTheDocument()
  expect(person).toHaveFocus()
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
