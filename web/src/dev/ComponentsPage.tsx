import { useState, type ReactNode } from 'react'
import { Button, IconButton } from '../components/Button'
import { CompanySelect, type CompanySelection } from '../components/CompanySelect'
import { MultiSelectField } from '../components/MultiSelectField'
import { BulkUpload } from '../components/BulkUpload'
import { Drawer } from '../components/Drawer'
import { Modal } from '../components/Modal'
import { EmptyState, FormErrorBanner, Loading, Notice, Tag } from '../components/feedback'
import { Checkbox, RadioGroup, SelectField, Switch, TextField } from '../components/fields'
import { NewPasswordField } from '../components/NewPasswordField'
import { PasswordField } from '../components/PasswordField'
import { Money } from '../components/Money'
import { AppShell, PhoneShell } from '../components/shells'
import { SearchBox } from '../components/SearchBox'
import { LanguageSwitch, ThemeSwitch } from '../components/switches'
import { Pagination } from '../components/Pagination'
import { Card, TableCard } from '../components/TableCard'
import { useToast } from '../components/toast'

// Development only (/dev/components): every component in its states, in the current theme and
// language. The words here are sample content, so they aren't in the translation files.

const companies = [
  { id: 'ke', name: 'Mama Oliech Restaurants', code: 'KE' },
  { id: 'ug', name: 'Oliech Catering Uganda', code: 'UG' },
]

const locations = [
  { name: 'Westlands Restaurant', town: 'Nairobi', scope: 'Restaurants · KE', main: 'Main · KE' },
  { name: 'Group head office, Upper Hill', town: 'Nairobi', scope: 'All companies', main: '' },
  { name: 'Kampala Kitchen', town: 'Kampala', scope: 'Catering · UG', main: 'Main · UG' },
]

const editIcon = <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4z" /><path d="M14 6l4 4" /></svg>
const deleteIcon = <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13" /></svg>

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-4">
      <h2 className="m-0 border-b border-line pb-2 font-display text-drawer-title">{title}</h2>
      {children}
    </section>
  )
}

export default function ComponentsPage() {
  const toast = useToast()
  const [drawer, setDrawer] = useState(false)
  const [modal, setModal] = useState(false)
  const [upload, setUpload] = useState(false)
  const [several, setSeveral] = useState<CompanySelection>({ all: false, ids: ['ke'] })
  const [one, setOne] = useState<CompanySelection>({ all: false, ids: ['ug'] })
  const [items, setItems] = useState(['overtime', 'leavers'])
  const [newPassword, setNewPassword] = useState('Mombasa-7')
  const [town, setTown] = useState('nbo')
  const [payFrom, setPayFrom] = useState('bank')
  const [isDefault, setIsDefault] = useState(true)

  return (
    <AppShell current="components" organization="Mama Oliech Group" person={{ name: 'Grace Wanjiru', detail: 'Mama Oliech Group' }}
      topBar={<SearchBox label="Search Pesaroll" placeholder="Search, like payroll or leave" empty="Nothing you can open matches."
        search={q => [{ key: 'payroll', label: 'Payroll schedules', group: 'Organization', href: '#' }, { key: 'leave', label: 'Leave plans', group: 'Organization', href: '#' }]
          .filter(r => r.label.toLowerCase().includes(q.toLowerCase()))} onPick={() => {}} />}
      personMenu={close => <Button variant="secondary" onClick={close}>Sign out</Button>}
      sections={[
        { key: 'main', links: [{ key: 'components', label: 'Components', href: '/dev/components' }] },
        { key: 'people', label: 'People', links: [{ key: 'employees', label: 'Employees', href: '#' }, { key: 'leave', label: 'Leave', href: '#', badge: 2 }] },
        { key: 'pay', label: 'Pay', links: [{ key: 'payroll', label: 'Payroll', href: '#', badge: 1 }] },
      ]}>
      <div className="mx-auto flex max-w-5xl flex-col gap-10">
        <header className="flex flex-col gap-4">
          <h1 className="m-0 font-display text-title">Components</h1>
          <p className="m-0 text-muted">Every building block, in the current theme and language. Rules: docs/standards/ui.md.</p>
          <div className="flex flex-wrap items-end gap-6"><ThemeSwitch /><LanguageSwitch /></div>
        </header>

        <Section title="Buttons">
          <div className="flex flex-wrap items-center gap-3">
            <Button>Add location</Button>
            <Button variant="secondary">Back</Button>
            <Button variant="ghost">Skip for now</Button>
            <Button variant="danger">Delete location</Button>
            <Button busy>Saving</Button>
            <Button disabled>Disabled</Button>
            <IconButton label="Edit Westlands Restaurant">{editIcon}</IconButton>
            <IconButton label="Delete Westlands Restaurant" tone="danger">{deleteIcon}</IconButton>
          </div>
          <div className="max-w-sm"><Button size="lg" fullWidth>Request leave (phone)</Button></div>
        </Section>

        <Section title="Form fields">
          <div className="grid gap-5 md:grid-cols-2">
            <TextField label="First name" placeholder="Your first name" defaultValue="Wanjiru" />
            <TextField label="Middle name" placeholder="Your middle name" optional hint="As on your national ID." />
            <TextField label="Email" placeholder="Your email" defaultValue="wanjiru@" error="Enter a valid email address or Kenyan phone number." />
            <SelectField label="Town" placeholder="Your town" hint="Searchable: type to filter. Sets the minimum-wage zone and public holidays." options={[{ value: 'nbo', label: 'Nairobi' }, { value: 'msa', label: 'Mombasa' }, { value: 'ksm', label: 'Kisumu' }, { value: 'nak', label: 'Nakuru' }, { value: 'eld', label: 'Eldoret' }, { value: 'mra', label: "Murang'a" }]} value={town} onChange={setTown} />
            <TextField label="Phone (phone layout)" placeholder="Your phone" size="lg" defaultValue="0723 456 789" />
            <PasswordField label="Password" placeholder="Your password" />
            <NewPasswordField label="New password (sign-up, invitations)" placeholder="Your password" value={newPassword} onChange={e => setNewPassword(e.target.value)} />
            <div className="flex flex-col"><Checkbox label="Main location for Mama Oliech Restaurants · KE" defaultChecked /><Checkbox label="Main location for Oliech Catering · UG" /></div>
            <RadioGroup label="Pay from" name="dev-pay-from" value={payFrom} onChange={setPayFrom}
              options={[{ value: 'bank', label: 'Bank account', hint: 'A bulk payment file for your bank.' }, { value: 'mpesa', label: 'M-Pesa', hint: 'A B2C shortcode that sends pay to phones.' }]} />
            <Switch label="Use as the default" hint="Payroll is paid from the default unless you choose another account." checked={isDefault} onChange={setIsDefault} />
          </div>
        </Section>

        <Section title="Company selector">
          <div className="grid gap-5 md:grid-cols-2">
            <CompanySelect label="Show companies" companies={companies} value={several} onChange={setSeveral} />
            <CompanySelect label="Company" companies={companies} value={one} onChange={setOne} multiple={false} />
          </div>
        </Section>

        <Section title="Multi-select">
          <div className="grid max-w-xl gap-4">
            <MultiSelectField label="What the cut-off applies to" hint="Changes to these after the cut-off go into the next payroll."
              placeholder="Your cut-off items" value={items} onChange={setItems} addLabel="Add your own" onAdd={() => setItems(v => [...v, `Item ${v.length + 1}`])}
              options={[
                { value: 'overtime', label: 'Overtime and extra hours' }, { value: 'new_employees', label: 'New employees' },
                { value: 'leavers', label: 'People leaving' }, { value: 'unpaid_leave', label: 'Unpaid leave and absences' },
                ...items.filter(i => i.startsWith('Item ')).map(i => ({ value: i, label: i })),
              ]} />
          </div>
        </Section>

        <Section title="Messages">
          <FormErrorBanner error={{ code: 'AUTH-1001', message: "That email or phone number and password don't match.", reference: '7F3K92QD' }} />
          <Notice><strong>Oliech Catering Uganda needs a main location.</strong> Edit a location and tick "Main location for" to fix it.</Notice>
          <div className="flex flex-wrap gap-2"><Tag>All companies</Tag><Tag tone="brand">Main · KE</Tag><Tag tone="warning">Not saved yet</Tag><Tag tone="danger">Locked</Tag></div>
          <div><Button variant="secondary" onClick={() => toast.show('Location added')}>Show a toast</Button></div>
        </Section>

        <Section title="Setup page pattern">
          <TableCard title="Locations" count={locations.length} status={<Tag tone="warning">Not saved yet</Tag>}
            footer={<Pagination label="Pages of locations" page={1} pageSize={20} total={locations.length} onPage={() => {}} onPageSize={() => {}} />}
            actions={<><Button variant="secondary" onClick={() => setUpload(true)}>Upload locations</Button><Button onClick={() => setDrawer(true)}>Add location</Button></>}>
            <table>
              <thead><tr><th>Location</th><th>Town</th><th>Applies to</th><th>Main location for</th><th className="text-right!">Actions</th></tr></thead>
              <tbody>
                {locations.map(l => (
                  <tr key={l.name}>
                    <td className="font-semibold">{l.name}</td><td>{l.town}</td><td><Tag>{l.scope}</Tag></td>
                    <td>{l.main ? <Tag tone="brand">{l.main}</Tag> : <span className="text-muted">—</span>}</td>
                    <td className="whitespace-nowrap text-right">
                      <IconButton label={`Edit ${l.name}`} onClick={() => setDrawer(true)}>{editIcon}</IconButton>
                      <IconButton label={`Delete ${l.name}`} tone="danger">{deleteIcon}</IconButton>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </TableCard>
          <Drawer open={drawer} title="Add location" onClose={() => setDrawer(false)}
            footer={<><Button variant="secondary" onClick={() => setDrawer(false)}>Cancel</Button><Button onClick={() => { setDrawer(false); toast.show('Location added') }}>Add location</Button></>}>
            <TextField label="Location name" placeholder="Your location name" defaultValue="Karen branch" />
            <SelectField label="Town" placeholder="Your town" hint="Sets the minimum-wage zone and public holidays." options={[{ value: 'nbo', label: 'Nairobi' }, { value: 'msa', label: 'Mombasa' }, { value: 'ksm', label: 'Kisumu' }, { value: 'nak', label: 'Nakuru' }, { value: 'eld', label: 'Eldoret' }, { value: 'mra', label: "Murang'a" }]} value={town} onChange={setTown} />
            <CompanySelect label="Applies to" companies={companies} value={several} onChange={setSeveral} />
            <Button variant="secondary" onClick={() => setModal(true)}>Open a modal on top</Button>
          </Drawer>
          {/* Example only: the file reads back with one problem; fix it in the review and import. */}
          {upload && (
            <BulkUpload open title="Upload locations" onClose={() => setUpload(false)} onDownloadTemplate={async () => {}}
              columns={[{ key: 'name', label: 'Location name' }, { key: 'county', label: 'County', options: [{ value: 'Nairobi', label: 'Nairobi' }, { value: 'Mombasa', label: 'Mombasa' }] }]}
              onRead={async () => ({
                rows: [{ line: 2, cells: { name: 'Westlands branch', county: 'Nairobi' } }, { line: 3, cells: { name: 'Atlantis office', county: 'Atlantis' } }],
                problems: [{ line: 3, column: 'county', message: '"Atlantis" isn\'t a county we know.' }],
              })}
              onCheck={async rows => ({ problems: rows.filter(r => !['Nairobi', 'Mombasa'].includes(r.cells.county)).map(r => ({ line: r.line, column: 'county', message: 'Choose a county from the list.' })) })}
              onImport={async rows => { toast.show(`${rows.length} locations imported`); return null }}
              labels={{ review: n => `Review ${n} locations`, importButton: n => `Import ${n} locations`, templateHint: 'One row per location.' }}>
              <p className="m-0 text-muted">The company selector goes here.</p>
            </BulkUpload>
          )}
          <Modal open={modal} title="Add paying account" onClose={() => setModal(false)}
            footer={<><Button variant="secondary" onClick={() => setModal(false)}>Cancel</Button><Button onClick={() => setModal(false)}>Add account</Button></>}>
            <TextField label="Account name" placeholder="Your account name" />
          </Modal>
          <div className="grid gap-4 md:grid-cols-2">
            <Card><EmptyState title="No locations yet" action={<Button>Add location</Button>}>Add your branches, or skip and add them later.</EmptyState></Card>
            <Card title="Loading"><Loading rows={4} /></Card>
          </div>
        </Section>

        <Section title="Money">
          <div className="flex flex-col gap-1 text-body">
            <div className="flex justify-between"><span>Basic salary</span><Money amount={19500} currency={false} /></div>
            <div className="flex justify-between"><span>NSSF</span><Money amount={-1170} currency={false} /></div>
            <div className="flex justify-between font-semibold"><span>Net pay</span><Money amount="17501.50" /></div>
          </div>
        </Section>

        <Section title="Phone portal (390 px)">
          <div className="w-[390px] max-w-full overflow-hidden rounded-card border border-line">
            <PhoneShell company="Mama Oliech Restaurants" title="Habari, Peter" current="home"
              tabs={[{ key: 'home', label: 'Home', href: '#' }, { key: 'payslips', label: 'Payslips', href: '#' }, { key: 'leave', label: 'Leave', href: '#' }, { key: 'profile', label: 'Profile', href: '#' }]}>
              <Card title="October 2026 payslip" highlight>
                <Money amount={17501} className="font-display text-amount" />
                <span className="text-hint text-muted">Net pay to M-Pesa ending 789</span>
              </Card>
              <Card title="Leave balance">
                <span className="font-display text-title-sm">10.5 <span className="font-sans text-hint text-muted">annual days left</span></span>
                <Button size="lg" fullWidth>Request leave</Button>
              </Card>
            </PhoneShell>
          </div>
        </Section>
      </div>
    </AppShell>
  )
}
