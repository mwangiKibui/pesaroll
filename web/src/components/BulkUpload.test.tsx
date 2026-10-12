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

it('a big file is reviewed 100 rows a page; search and "Only rows to fix" look across every page', async () => {
  const big: BulkUploadRead = {
    rows: Array.from({ length: 250 }, (_, i) => row(i + 2, `Shop ${i + 1}`, 'Nairobi')),
    problems: [{ line: 240, column: 'county', message: 'Choose a county from the list.' }],
  }
  render(<Page read={async () => big} />)
  await userEvent.upload(screen.getByLabelText('Choose a file'), file())
  await screen.findByRole('dialog', { name: 'Review 250 locations' })

  expect(within(review()).getAllByRole('row')).toHaveLength(101)                 // the header and 100 rows
  expect(within(review()).getByText('Page 1 of 3')).toBeInTheDocument()
  expect(within(review()).getByRole('button', { name: 'Previous page' })).toBeDisabled()
  await userEvent.click(within(review()).getByRole('button', { name: 'Next page' }))
  await userEvent.click(within(review()).getByRole('button', { name: 'Next page' }))
  expect(within(review()).getByText('Page 3 of 3')).toBeInTheDocument()
  expect(within(review()).getAllByRole('row')).toHaveLength(51)
  expect(within(review()).getByRole('button', { name: 'Next page' })).toBeDisabled()

  // The problem is on page 3; the filter finds it from anywhere and starts at page 1.
  await userEvent.click(within(review()).getByRole('button', { name: 'Previous page' }))
  await userEvent.click(within(review()).getByRole('checkbox', { name: 'Only rows to fix' }))
  expect(within(review()).getAllByRole('row')).toHaveLength(2)
  expect(within(review()).getByText('Page 1 of 1')).toBeInTheDocument()
  await userEvent.click(within(review()).getByRole('checkbox', { name: 'Only rows to fix' }))

  // Pasted in one go: typing letter by letter redraws 100 editable rows each time.
  await userEvent.click(within(review()).getByRole('searchbox', { name: 'Search rows' }))
  await userEvent.paste('shop 245')
  expect(within(review()).getByText('Showing 1 of 250')).toBeInTheDocument()
  expect(cell('Location name', 246)).toHaveValue('Shop 245')
}, 60_000)

it('what a row names that does not exist yet is noted as "will be created", and does not stop the import', async () => {
  const withNotes: BulkUploadRead = {
    rows: [row(2, 'Westlands branch', 'Nairobi'), row(3, 'Kericho shop', 'Kericho'), row(4, 'Kericho depot', 'kericho')],
    problems: [],
    notes: [
      { line: 3, column: 'county', message: 'No county called "Kericho" yet: it will be created.' },
      { line: 4, column: 'county', message: 'No county called "kericho" yet: it will be created.' },
    ],
  }
  const importRows = vi.fn(async () => null)
  const { container } = render(<Page read={async () => withNotes} check={async () => ({ problems: [], notes: withNotes.notes })} importRows={importRows} />)
  await userEvent.upload(screen.getByLabelText('Choose a file'), file())
  await screen.findByRole('dialog', { name: 'Review 3 locations' })

  expect(cell('County', 3)).toHaveValue('Kericho')
  expect(cell('County', 3)).toHaveAccessibleDescription('No county called "Kericho" yet: it will be created.')
  expect(cell('County', 3)).not.toHaveAttribute('aria-invalid')
  expect(within(review()).getByText('1 new item will be created when you import.')).toBeInTheDocument()   // the same name once
  expect(within(review()).queryByText(/needs fixing/)).not.toBeInTheDocument()
  await expectNoA11yViolations(container)

  await userEvent.click(within(review()).getByRole('button', { name: 'Import 3 locations' }))
  expect(importRows).toHaveBeenCalled()
})

it('adding several opens the review with empty rows to type into; empty rows are left out', async () => {
  const importRows = vi.fn(async () => null)
  const check = vi.fn(async () => clean)
  render(
    <BulkUpload open startRows={3} title="Add several" columns={columns} onClose={() => {}} onDownloadTemplate={async () => {}}
      onRead={async () => good} onCheck={check} onImport={importRows}
      labels={{ review: n => `Add ${n} locations`, importButton: n => `Add ${n} locations`, templateHint: '' }} />,
  )

  const sheet = screen.getByRole('dialog', { name: /Add \d locations/ })
  expect(within(sheet).getAllByRole('textbox', { name: /Location name, row/ })).toHaveLength(3)
  await userEvent.click(within(sheet).getByRole('button', { name: 'Add row' }))
  expect(within(sheet).getAllByRole('textbox', { name: /Location name, row/ })).toHaveLength(4)
  await userEvent.type(within(sheet).getByRole('textbox', { name: 'Location name, row 1' }), 'Westlands branch')
  await userEvent.type(within(sheet).getByRole('textbox', { name: 'Location name, row 3' }), 'Mombasa shop')
  await userEvent.click(within(sheet).getByRole('button', { name: /^Add \d locations$/ }))

  expect(check).toHaveBeenCalledWith([{ line: 1, cells: { name: 'Westlands branch' } }, { line: 3, cells: { name: 'Mombasa shop' } }])
  expect(importRows).toHaveBeenCalledOnce()
})

it('a sheet can have checkbox columns, defaults, and cells that only open when another says so', async () => {
  const importRows = vi.fn(async () => null)
  const check = vi.fn(async () => clean)
  const cols = [
    { key: 'name', label: 'Name' },
    { key: 'stillHere', label: 'Still works here', type: 'checkbox' as const, defaultValue: 'Yes' },
    { key: 'exitDate', label: 'Date left', type: 'date' as const, disabledWhen: (cells: Record<string, string>) => cells.stillHere === 'Yes' },
  ]
  render(
    <BulkUpload open startRows={1} title="Add employee" columns={cols} onClose={() => {}} onDownloadTemplate={async () => {}}
      onRead={async () => good} onCheck={check} onImport={importRows}
      labels={{ review: n => `Add ${n} people`, importButton: n => `Add ${n} people`, templateHint: '' }} />,
  )
  const sheet = screen.getByRole('dialog', { name: /Add \d people/ })

  const stillHere = within(sheet).getByRole('checkbox', { name: 'Still works here, row 1' })
  const left = within(sheet).getByLabelText('Date left, row 1')
  expect(stillHere).toBeChecked()
  expect(left).toBeDisabled()
  expect(left).toHaveAttribute('type', 'date')

  await userEvent.click(within(sheet).getByRole('button', { name: 'Add row' }))   // an untouched row stays empty
  await userEvent.type(within(sheet).getByRole('textbox', { name: 'Name, row 1' }), 'Akinyi')
  await userEvent.click(stillHere)
  expect(left).toBeEnabled()
  await userEvent.type(left, '2025-06-30')
  await userEvent.click(within(sheet).getByRole('button', { name: /^Add \d people$/ }))

  expect(check).toHaveBeenCalledWith([{ line: 1, cells: { name: 'Akinyi', stillHere: 'No', exitDate: '2025-06-30' } }])
})

it('adding by hand happens in a drawer 97% of the screen wide', () => {
  render(
    <BulkUpload open startRows={1} title="Add employee" columns={columns} onClose={() => {}} onDownloadTemplate={async () => {}}
      onRead={async () => good} onCheck={async () => clean} onImport={async () => null}
      labels={{ review: n => `Add ${n} people`, importButton: n => `Add ${n} people`, templateHint: '' }} />,
  )

  expect(screen.getByRole('dialog', { name: 'Add 1 people' })).toHaveClass('max-w-[97vw]')
  expect(screen.getByTestId('drawer-scrim')).toBeInTheDocument()
})

it('checks a cell as soon as it is left, offers choices that follow the row, and checks every cell before importing', async () => {
  const check = vi.fn(async () => clean)
  const branches: Record<string, { value: string; label: string }[]> = {
    Equity: [{ value: 'Kilimani', label: 'Kilimani' }], KCB: [{ value: 'Moi Avenue', label: 'Moi Avenue' }],
  }
  const cols = [
    { key: 'name', label: 'Name', validate: (v: string) => (v.trim() ? null : 'Enter their name.') },
    { key: 'pin', label: 'KRA PIN', validate: (v: string) => (!v || /^A\d{9}[A-Z]$/.test(v) ? null : 'Enter a PIN like A012345678Z.') },
    { key: 'bank', label: 'Bank', options: [{ value: 'Equity', label: 'Equity' }, { value: 'KCB', label: 'KCB' }] },
    { key: 'branch', label: 'Branch', optionsFor: (cells: Record<string, string>) => branches[cells.bank] ?? [], disabledWhen: (cells: Record<string, string>) => !cells.bank },
  ]
  render(
    <BulkUpload open startRows={1} title="Add employee" columns={cols} onClose={() => {}} onDownloadTemplate={async () => {}}
      onRead={async () => good} onCheck={check} onImport={async () => null}
      onCellsChange={(cells, changed) => (changed === 'bank' ? { ...cells, branch: '' } : cells)}
      labels={{ review: n => `Add ${n} people`, importButton: n => `Add ${n} people`, templateHint: '' }} />,
  )
  const sheet = screen.getByRole('dialog', { name: /Add \d people/ })
  const pin = within(sheet).getByRole('textbox', { name: 'KRA PIN, row 1' })

  await userEvent.type(pin, 'P123')
  expect(within(sheet).queryByText('Enter a PIN like A012345678Z.')).not.toBeInTheDocument()   // not while typing
  await userEvent.tab()
  expect(within(sheet).getByText('Enter a PIN like A012345678Z.')).toBeInTheDocument()
  await userEvent.clear(pin)
  await userEvent.type(pin, 'A012345678Z')
  await userEvent.tab()
  expect(within(sheet).queryByText('Enter a PIN like A012345678Z.')).not.toBeInTheDocument()

  const branch = within(sheet).getByRole('combobox', { name: 'Branch, row 1' })
  expect(branch).toBeDisabled()
  await userEvent.click(within(sheet).getByRole('combobox', { name: 'Bank, row 1' }))
  await userEvent.click(screen.getByRole('option', { name: 'KCB' }))
  expect(branch).toBeEnabled()
  await userEvent.click(branch)
  expect(screen.queryByRole('option', { name: 'Kilimani' })).not.toBeInTheDocument()
  await userEvent.click(screen.getByRole('option', { name: 'Moi Avenue' }))
  expect(branch).toHaveValue('Moi Avenue')
  await userEvent.click(within(sheet).getByRole('combobox', { name: 'Bank, row 1' }))
  await userEvent.click(screen.getByRole('option', { name: 'Equity' }))
  expect(branch).toHaveValue('')   // a new bank clears the branch

  await userEvent.click(within(sheet).getByRole('button', { name: /^Add \d people$/ }))
  expect(within(sheet).getByText('Enter their name.')).toBeInTheDocument()   // never left, still checked
  expect(check).not.toHaveBeenCalled()
})

it('the arrow keys scroll the rows sideways, except while typing in a cell', async () => {
  render(
    <BulkUpload open startRows={1} title="Add employee" columns={columns} onClose={() => {}} onDownloadTemplate={async () => {}}
      onRead={async () => good} onCheck={async () => clean} onImport={async () => null}
      labels={{ review: n => `Add ${n} people`, importButton: n => `Add ${n} people`, templateHint: '' }} />,
  )
  const sheet = screen.getByRole('dialog', { name: 'Add 1 people' })
  const rows = within(sheet).getByRole('region', { name: 'Rows to import' })
  rows.scrollBy = vi.fn(({ left }: ScrollToOptions) => { rows.scrollLeft += left ?? 0 }) as never   // jsdom doesn't scroll

  await userEvent.click(within(sheet).getByRole('columnheader', { name: 'County' }))   // out of the cells
  await userEvent.keyboard('{ArrowRight}{ArrowRight}')
  expect(rows.scrollLeft).toBe(640)
  await userEvent.keyboard('{ArrowLeft}')
  expect(rows.scrollLeft).toBe(320)

  await userEvent.click(within(sheet).getByRole('textbox', { name: 'Location name, row 1' }))
  await userEvent.keyboard('{ArrowRight}')
  expect(rows.scrollLeft).toBe(320)   // the cursor moves in the cell instead
})

it('each column can be searched and filtered by its values, together, across every page', async () => {
  const many: BulkUploadRead = {
    rows: [row(2, 'Westlands branch', 'Nairobi'), row(3, 'Mombasa shop', 'Mombasa'), row(4, 'Westgate kiosk', 'Nairobi'), row(5, 'Kilimani office', '')],
    problems: [],
  }
  render(<Page read={async () => many} />)
  await userEvent.upload(screen.getByLabelText('Choose a file'), file())
  const sheet = review()
  const shownNames = () => within(sheet).getAllByRole('textbox', { name: /^Location name, row/ }).map(c => (c as HTMLInputElement).value)

  // Values: untick one, and its rows go.
  await userEvent.click(within(sheet).getByRole('button', { name: 'Filter County' }))
  const panel = screen.getByRole('dialog', { name: 'Filter County' })
  expect(within(panel).getAllByRole('checkbox').map(c => c.closest('label')?.textContent)).toEqual(['All', '(Empty)1', 'Mombasa1', 'Nairobi2'])
  await userEvent.click(within(panel).getByRole('checkbox', { name: /^Mombasa/ }))
  await userEvent.click(within(panel).getByRole('checkbox', { name: /^\(Empty\)/ }))
  expect(shownNames()).toEqual(['Westlands branch', 'Westgate kiosk'])
  await userEvent.keyboard('{Escape}')   // closes the filter, not the sheet
  expect(screen.queryByRole('dialog', { name: 'Filter County' })).not.toBeInTheDocument()
  expect(within(sheet).getByRole('button', { name: 'Filter County' })).toHaveAttribute('aria-pressed', 'true')

  // Search within a column, on top of the other filter.
  await userEvent.click(within(sheet).getByRole('button', { name: 'Filter Location name' }))
  await userEvent.type(within(screen.getByRole('dialog', { name: 'Filter Location name' })).getByRole('searchbox', { name: 'Search Location name' }), 'westg')
  expect(shownNames()).toEqual(['Westgate kiosk'])
  expect(within(screen.getByRole('dialog', { name: 'Filter Location name' })).getAllByRole('checkbox', { name: /kiosk|branch|shop|office/ })
    .map(c => c.closest('label')?.textContent)).toEqual(['Westgate kiosk1'])   // the list narrows too
  await userEvent.keyboard('{Escape}')

  expect(within(sheet).getByText('Showing 1 of 4')).toBeInTheDocument()
  await userEvent.click(within(sheet).getByRole('button', { name: 'Clear filters' }))
  expect(shownNames()).toHaveLength(4)
})

it('each column sorts A to Z, Z to A, then back to the file order; a row being fixed stays put', async () => {
  const many: BulkUploadRead = {
    rows: [row(2, 'Westlands branch', 'Nairobi'), row(3, 'Mombasa shop', 'Mombasa'), row(4, '', 'Nairobi'), row(5, 'Kilimani office', 'Nairobi')],
    problems: [],
  }
  render(<Page read={async () => many} />)
  await userEvent.upload(screen.getByLabelText('Choose a file'), file())
  const sheet = review()
  const names = () => within(sheet).getAllByRole('textbox', { name: /^Location name, row/ }).map(c => (c as HTMLInputElement).value)
  const sort = () => within(sheet).getByRole('button', { name: 'Sort Location name' })
  const header = () => within(sheet).getByRole('columnheader', { name: /Location name/ })

  expect(header()).not.toHaveAttribute('aria-sort')
  await userEvent.click(sort())
  expect(names()).toEqual(['Kilimani office', 'Mombasa shop', 'Westlands branch', ''])   // empty last
  expect(header()).toHaveAttribute('aria-sort', 'ascending')

  await userEvent.type(within(sheet).getByRole('textbox', { name: 'Location name, row 5' }), ' Z')   // "Kilimani office Z" stays first
  expect(names()[0]).toBe('Kilimani office Z')

  await userEvent.click(sort())
  expect(names()).toEqual(['Westlands branch', 'Mombasa shop', 'Kilimani office Z', ''])
  expect(header()).toHaveAttribute('aria-sort', 'descending')

  await userEvent.click(sort())
  expect(names()).toEqual(['Westlands branch', 'Mombasa shop', '', 'Kilimani office Z'])   // the file's order
  expect(header()).not.toHaveAttribute('aria-sort')
})
