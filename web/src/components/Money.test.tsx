import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { formatMoney } from '../lib/money'
import { Money } from './Money'

it('shows the currency first with thousand separators', () => {
  expect(formatMoney(17501)).toBe('KES 17,501')
  expect(formatMoney('1250000')).toBe('KES 1,250,000')
})

it('shows cents only when there are some', () => {
  expect(formatMoney(17501.5)).toBe('KES 17,501.50')
  expect(formatMoney('70442.35')).toBe('KES 70,442.35')
})

it('deductions use a true minus sign', () => {
  expect(formatMoney(-1170)).toBe('−KES 1,170')
  expect(formatMoney(-1170, { currency: false })).toBe('−1,170')
})

it('can leave the currency out in a column of figures', () => {
  render(<Money amount={19500} currency={false} />)
  expect(screen.getByText('19,500')).toHaveClass('tabular-nums')
})

it('in a table column, always shows cents so the figures line up', () => {
  expect(formatMoney(300, { currency: false, cents: true })).toBe('300.00')
  expect(formatMoney(-1170.5, { currency: false, cents: true })).toBe('−1,170.50')
})
