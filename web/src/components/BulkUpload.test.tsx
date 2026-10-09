import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { BulkUpload, type BulkUploadCheck } from './BulkUpload'
import type { ApiError } from './feedback'

interface Row { line: number; name: string }

const file = (name = 'locations.xlsx') => new File(['x'], name, { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
const good: BulkUploadCheck<Row> = { rows: [{ line: 2, name: 'Westlands' }, { line: 3, name: 'Karen' }], problems: [] }
const bad: BulkUploadCheck<Row> = {
  rows: [{ line: 2, name: 'Westlands' }],
  problems: [{ line: 3, column: 'County', message: '"Atlantis" isn\'t a county we know.' }, { line: 4, column: null, message: 'Enter the name.' }],
}

function Page({ check = async () => good, importRows = async () => null, download = async () => {} }: {
  check?: (file: File) => Promise<BulkUploadCheck<Row> | ApiError>
  importRows?: (rows: Row[]) => Promise<ApiError | null>
  download?: () => Promise<void>
}) {
  const [open, setOpen] = useState(true)
  return (
    <BulkUpload<Row> open={open} title="Upload locations" onClose={() => setOpen(false)} onDownloadTemplate={download}
      onCheck={check} onImport={importRows}
      labels={{ ready: n => `${n} locations ready`, importButton: n => `Import ${n} locations`, templateHint: 'One row per location.' }}>
      <p>Choose the companies</p>
    </BulkUpload>
  )
}

it('walks through the template, the file and the import', async () => {
  const download = vi.fn(async () => {})
  const check = vi.fn(async () => good)
  const importRows = vi.fn(async () => null)
  const { container } = render(<Page download={download} check={check} importRows={importRows} />)

  const drawer = screen.getByRole('dialog', { name: 'Upload locations' })
  expect(within(drawer).getAllByRole('heading', { level: 3 }).map(h => h.textContent))
    .toEqual(['1. Download the template', '2. Upload your file', '3. Choose where they apply'])
  expect(within(drawer).getByRole('button', { name: /Import/ })).toBeDisabled()
  await expectNoA11yViolations(container)

  await userEvent.click(within(drawer).getByRole('button', { name: 'Download template' }))
  expect(download).toHaveBeenCalled()

  await userEvent.upload(within(drawer).getByLabelText('Choose a file'), file())
  expect(check).toHaveBeenCalledWith(expect.any(File))
  expect(await within(drawer).findByText('2 locations ready')).toBeInTheDocument()
  expect(within(drawer).getByText('locations.xlsx')).toBeInTheDocument()

  await userEvent.click(within(drawer).getByRole('button', { name: 'Import 2 locations' }))
  expect(importRows).toHaveBeenCalledWith(good.rows)
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
})

it('lists the problems by row and only imports once the file is fixed', async () => {
  const check = vi.fn().mockResolvedValueOnce(bad).mockResolvedValueOnce(good)
  render(<Page check={check} />)
  const drawer = screen.getByRole('dialog')

  await userEvent.upload(within(drawer).getByLabelText('Choose a file'), file())

  const problems = await within(drawer).findByRole('list', { name: '2 problems to fix' })
  expect(within(problems).getAllByRole('listitem').map(i => i.textContent))
    .toEqual(['Row 3 · County: "Atlantis" isn\'t a county we know.', 'Row 4: Enter the name.'])
  expect(within(drawer).getByRole('button', { name: /Import/ })).toBeDisabled()

  await userEvent.upload(within(drawer).getByLabelText('Choose another file'), file('fixed.xlsx'))
  expect(await within(drawer).findByText('2 locations ready')).toBeInTheDocument()
  expect(within(drawer).queryByRole('list', { name: /problems to fix/ })).not.toBeInTheDocument()
  expect(within(drawer).getByRole('button', { name: 'Import 2 locations' })).toBeEnabled()
})

it('a file the server refuses, or a failed import, shows the code and reference', async () => {
  const refused = { code: 'ONB-3516', message: "We couldn't read this file.", reference: '7F3K92QD' }
  const check = vi.fn().mockResolvedValueOnce(refused).mockResolvedValueOnce(good)
  const importRows = vi.fn(async () => ({ code: 'ONB-3518', message: 'Some rows need fixing.', reference: 'AB12CD34' }))
  render(<Page check={check} importRows={importRows} />)
  const drawer = screen.getByRole('dialog')

  await userEvent.upload(within(drawer).getByLabelText('Choose a file'), file())
  expect(await within(drawer).findByRole('alert')).toHaveTextContent('ONB-3516')

  await userEvent.upload(within(drawer).getByLabelText('Choose another file'), file())
  await userEvent.click(await within(drawer).findByRole('button', { name: 'Import 2 locations' }))
  expect(await within(drawer).findByRole('alert')).toHaveTextContent('AB12CD34')
  expect(screen.getByRole('dialog')).toBeInTheDocument()
})
