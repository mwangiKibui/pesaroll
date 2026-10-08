import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { ThemeMenu } from './ThemeMenu'

it('an icon button opens a small menu with System, Light and Dark, the current one checked', async () => {
  const { container } = render(<ThemeMenu />)
  const button = screen.getByRole('button', { name: 'Theme' })
  expect(button).toHaveAttribute('aria-expanded', 'false')

  await userEvent.click(button)

  expect(button).toHaveAttribute('aria-expanded', 'true')
  expect(screen.getAllByRole('menuitemradio').map(i => i.textContent)).toEqual(['System', 'Light', 'Dark'])
  expect(screen.getByRole('menuitemradio', { name: 'System' })).toHaveAttribute('aria-checked', 'true')
  await expectNoA11yViolations(container)
})

it('picking one applies it, closes the menu and returns focus to the button', async () => {
  render(<ThemeMenu />)
  await userEvent.click(screen.getByRole('button', { name: 'Theme' }))

  await userEvent.click(screen.getByRole('menuitemradio', { name: 'Dark' }))

  expect(document.documentElement.dataset.theme).toBe('dark')
  expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Theme' })).toHaveFocus()
})

it('works from the keyboard: arrows move, Enter picks, Escape closes', async () => {
  render(<ThemeMenu />)
  screen.getByRole('button', { name: 'Theme' }).focus()

  await userEvent.keyboard('{Enter}')
  expect(screen.getByRole('menuitemradio', { name: 'System' })).toHaveFocus()
  await userEvent.keyboard('{ArrowDown}{Enter}')
  expect(document.documentElement.dataset.theme).toBe('light')

  await userEvent.keyboard('{Enter}{Escape}')
  expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Theme' })).toHaveFocus()
})

it('clicking elsewhere closes it', async () => {
  render(<><ThemeMenu /><p>Elsewhere</p></>)
  await userEvent.click(screen.getByRole('button', { name: 'Theme' }))

  await userEvent.click(screen.getByText('Elsewhere'))

  expect(screen.queryByRole('menu')).not.toBeInTheDocument()
})
