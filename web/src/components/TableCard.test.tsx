import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { Button } from './Button'
import { Card, TableCard } from './TableCard'

it('has a title with the count, its actions and a table that scrolls on small screens', async () => {
  const { container } = render(
    <TableCard title="Locations" count={4} actions={<Button>Add location</Button>}>
      <table><thead><tr><th>Location</th></tr></thead><tbody><tr><td>Westlands Restaurant</td></tr></tbody></table>
    </TableCard>)

  expect(screen.getByRole('heading', { name: 'Locations · 4' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Add location' })).toBeInTheDocument()
  expect(screen.getByRole('region', { name: 'Locations' })).toContainElement(screen.getByRole('table'))
  await expectNoA11yViolations(container)
})

it('a card is a panel with an optional heading', () => {
  render(<Card title="Leave balance"><p>10.5 annual days left</p></Card>)
  expect(screen.getByRole('heading', { name: 'Leave balance' })).toBeInTheDocument()
})

it('a highlighted card stands out with a brand border', () => {
  render(<Card title="October 2026 payslip" highlight><p>KES 17,501</p></Card>)
  const card = screen.getByRole('heading', { name: 'October 2026 payslip' }).closest('section')
  expect(card).toHaveClass('border-2', 'border-brand')
  expect(card).not.toHaveClass('border-line')
})
