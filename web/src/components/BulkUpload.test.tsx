import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { BulkUpload, type BulkUploadCheck, type BulkUploadRead, type BulkUploadRow } from './BulkUpload'
import type { ApiError } from './feedback'

const columns = [
  { key: 'name', label: 'Location name' },
  { key: 'county', label: 'County', options: [{ value: 'Nairobi', label: 'Nairobi' }, { value: 'Mombasa', label: 'Mombasa' }] },
]
const row = (line: number, name: string, county: string): BulkUploadRow => ({ line, cells: { name, county } })
const file = (name = 'locations.xlsx') => new File(['x'], name)
const good: BulkUploadRead = { rows: [row(2, 'Westlands branch', 'Nairobi'), row(3, 'Mombasa shop', 'Mombasa')], problems: [] }
const withProblems: BulkUploadRead = {
  rows: [row(2, 'Westlands branch', 'Nairobi'), row(3, 'Atlantis office', 'Atlantis')],
  problems: [{ line: 3, column: 'county', message: '"Atlantis" isn\'t a county we know.' }],
}
const clean: BulkUploadCheck = { problems: [] }

function Page({ read = async () => good, check = async () => clean, importRows = async () => null, download = async () => {} }: {
  read?: (file: File) => Promise<BulkUploadRead | ApiError>
  check?: (rows: BulkUploadRow[]) => Promise<BulkUploadCheck | ApiError>
  importRows?: (rows: BulkUploadRow[]) => Promise<ApiError | null>
  download?: () => Promise<void>
}) {
  const [open, setOpen] = useState(true)
  return (
    <BulkUpload open={open} title="Upload locations" columns={columns} onClose={() => setOpen(false)}
      onDownloadTemplate={download} onRead={read} onCheck={check} onImport={importRows}
      labels={{ review: n => `Review ${n} locations`, importButton: n => `Import ${n} ${n === 1 ? 'location' : 'locations'}`, templateHint: 'One row per location.' }}>
      <p>Apply to all companies</p>
    </BulkUpload>
  )
}

const review = () => screen.getByRole('dialog', { name: /Review/ })
const cell = (column: string, line: number) => within(review()).getByRole(column === 'County' ? 'combobox' : 'textbox', { name: `${column}, row ${line}` })

it('after the upload, a large review shows every column and row, editable, before anything is saved', async () => {
  const download = vi.fn(async () => {})
  const read = vi.fn(async () => good)
  const check = vi.fn(async () => clean)
  const importRows = vi.fn(async () => null)
  const { container } = render(<Page download={download} read={read} check={check} importRows={importRows} />)
  const drawer = screen.getByRole('dialog', { name: 'Upload locations' })
  expect(within(drawer).getAllByRole('heading', { level: 3 }).map(h => h.textContent)).toEqual(['1. Download the template', '2. Upload your file'])

  await userEvent.click(within(drawer).getByRole('button', { name: 'Download template' }))
  expect(download).toHaveBeenCalled()
  await userEvent.upload(within(drawer).getByLabelText('Choose a file'), file())

  expect(await screen.findByRole('dialog', { name: 'Review 2 locations' })).toBeInTheDocument()
  expect(read).toHaveBeenCalledWith(expect.any(File))
  expect(within(review()).getByText('Apply to all companies')).toBeInTheDocument()
  expect(within(review()).getAllByRole('columnheader').map(h => h.textContent)).toEqual(['Row', 'Location name', 'County', 'Actions'])
  expect(cell('Location name', 2)).toHaveValue('Westlands branch')
  expect(cell('County', 3)).toHaveValue('Mombasa')
  await expectNoA11yViolations(container)

  // A typo fixed in place, and a county picked from the searchable dropdown.
  await userEvent.clear(cell('Location name', 2))
  await userEvent.type(cell('Location name', 2), 'Westlands shop')
  await userEvent.clear(cell('County', 3))
  await userEvent.type(cell('County', 3), 'nai')
  await userEvent.click(within(review()).getByRole('option', { name: 'Nairobi' }))
  await userEvent.click(within(review()).getByRole('button', { name: 'Import 2 locations' }))

  const edited = [row(2, 'Westlands shop', 'Nairobi'), row(3, 'Mombasa shop', 'Nairobi')]
  expect(check).toHaveBeenCalledWith(edited)
  expect(importRows).toHaveBeenCalledWith(edited)
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
})

it('search narrows the rows; the table scrolls inside the review with its header in place', async () => {
  const many: BulkUploadRead = { rows: Array.from({ length: 40 }, (_, i) => row(i + 2, `Shop ${i + 1}`, i % 2 ? 'Mombasa' : 'Nairobi')), problems: [] }
  render(<Page read={async () => many} />)
  await userEvent.upload(screen.getByLabelText('Choose a file'), file())
  await screen.findByRole('dialog', { name: 'Review 40 locations' })

  expect(within(review()).getByRole('region', { name: 'Rows to import' })).toHaveAttribute('tabindex', '0')
  await userEvent.type(within(review()).getByRole('searchbox', { name: 'Search rows' }), 'shop 1')

  expect(within(review()).getByText('Showing 11 of 40')).toBeInTheDocument()   // Shop 1, Shop 10–19
  expect(within(review()).getAllByRole('row')).toHaveLength(12)                 // the header and 11 rows
})

it('problems show in their cells; fixing a cell clears it; import checks again and saves only when all is well', async () => {
  const check = vi.fn().mockResolvedValueOnce({ problems: [{ line: 2, column: 'name', message: 'Enter the location\'s name.' }] }).mockResolvedValueOnce(clean)
  const importRows = vi.fn(async () => null)
  render(<Page read={async () => withProblems} check={check} importRows={importRows} />)
  await userEvent.upload(screen.getByLabelText('Choose a file'), file())
  await screen.findByRole('dialog', { name: 'Review 2 locations' })

  expect(cell('County', 3)).toHaveValue('Atlantis')   // what was typed stays visible
  expect(cell('County', 3)).toHaveAccessibleDescription('"Atlantis" isn\'t a county we know.')
  expect(within(review()).getByText('1 row needs fixing.')).toBeInTheDocument()
  await userEvent.click(within(review()).getByRole('checkbox', { name: 'Only rows to fix' }))
  expect(within(review()).getAllByRole('row')).toHaveLength(2)

  await userEvent.clear(cell('County', 3))
  await userEvent.type(cell('County', 3), 'mom')
  await userEvent.click(within(review()).getByRole('option', { name: 'Mombasa' }))
  expect(cell('County', 3)).not.toHaveAccessibleDescription(/Atlantis/)

  await userEvent.click(within(review()).getByRole('checkbox', { name: 'Only rows to fix' }))
  await userEvent.clear(cell('Location name', 2))
  await userEvent.click(within(review()).getByRole('button', { name: 'Import 2 locations' }))
  expect(await within(review()).findByText("Enter the location's name.")).toBeInTheDocument()
  expect(importRows).not.toHaveBeenCalled()

  await userEvent.type(cell('Location name', 2), 'Westlands branch')
  await userEvent.click(within(review()).getByRole('button', { name: 'Import 2 locations' }))
  expect(importRows).toHaveBeenCalledWith([row(2, 'Westlands branch', 'Nairobi'), row(3, 'Atlantis office', 'Mombasa')])
})

it('a row can be taken out before importing', async () => {
  const importRows = vi.fn(async () => null)
  render(<Page importRows={importRows} />)
  await userEvent.upload(screen.getByLabelText('Choose a file'), file())
  await screen.findByRole('dialog', { name: 'Review 2 locations' })

  await userEvent.click(within(review()).getByRole('button', { name: 'Remove row 3' }))
  await userEvent.click(within(review()).getByRole('button', { name: 'Import 1 location' }))

  expect(importRows).toHaveBeenCalledWith([good.rows[0]])
})

it('a file the server refuses stays in the drawer; a failed import shows in the review with its code', async () => {
  const refused = { code: 'ONB-3516', message: "We couldn't read this file.", reference: '7F3K92QD' }
  const read = vi.fn().mockResolvedValueOnce(refused).mockResolvedValueOnce(good)
  const importRows = vi.fn(async () => ({ code: 'ONB-3518', message: 'Row 3 needs fixing.', reference: 'AB12CD34' }))
  render(<Page read={read} importRows={importRows} />)
  const drawer = screen.getByRole('dialog', { name: 'Upload locations' })

  await userEvent.upload(within(drawer).getByLabelText('Choose a file'), file())
  expect(await within(drawer).findByRole('alert')).toHaveTextContent('ONB-3516')
  expect(screen.queryByRole('dialog', { name: /Review/ })).not.toBeInTheDocument()

  await userEvent.upload(within(drawer).getByLabelText('Choose another file'), file())
  await userEvent.click(await within(await screen.findByRole('dialog', { name: /Review/ })).findByRole('button', { name: 'Import 2 locations' }))
  expect(await within(review()).findByRole('alert')).toHaveTextContent('AB12CD34')
})
