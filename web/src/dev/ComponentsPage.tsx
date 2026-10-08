import { useState, type ReactNode } from 'react'
import { Button, IconButton } from '../components/Button'
import { CompanySelect, type CompanySelection } from '../components/CompanySelect'
import { Drawer } from '../components/Drawer'
import { EmptyState, FormErrorBanner, Loading, Notice, Tag } from '../components/feedback'
import { Checkbox, SelectField, TextField } from '../components/fields'
import { Money } from '../components/Money'
import { AppShell, PhoneShell } from '../components/shells'
import { LanguageSwitch, ThemeSwitch } from '../components/switches'
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
  const [several, setSeveral] = useState<CompanySelection>({ all: false, ids: ['ke'] })
  const [one, setOne] = useState<CompanySelection>({ all: false, ids: ['ug'] })

  return (
    <AppShell current="components" organization="Mama Oliech Group" person="Grace Wanjiru (Owner)"
      setup={{ done: 8, total: 10, next: 'Payroll history', href: '#' }}
      groups={[
        { items: [{ key: 'components', label: 'Components', href: '/dev/components' }] },
        { label: 'People', items: [{ key: 'employees', label: 'Employees', href: '#' }, { key: 'leave', label: 'Leave', href: '#', badge: 2 }] },
        { label: 'Pay', items: [{ key: 'payroll', label: 'Payroll', href: '#' }] },
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
            <TextField label="First name" defaultValue="Wanjiru" />
            <TextField label="Middle name" optional hint="As on your national ID." />
            <TextField label="Email" defaultValue="wanjiru@" error="Enter a valid email address or Kenyan phone number." />
            <SelectField label="Town" hint="Sets the minimum-wage zone and public holidays." options={[{ value: 'nbo', label: 'Nairobi' }, { value: 'msa', label: 'Mombasa' }]} />
            <TextField label="Phone (phone layout)" size="lg" defaultValue="0723 456 789" />
            <div className="flex flex-col"><Checkbox label="Main location for Mama Oliech Restaurants · KE" defaultChecked /><Checkbox label="Main location for Oliech Catering · UG" /></div>
          </div>
        </Section>

        <Section title="Company selector">
          <div className="grid gap-5 md:grid-cols-2">
            <CompanySelect label="Show companies" companies={companies} value={several} onChange={setSeveral} />
            <CompanySelect label="Company" companies={companies} value={one} onChange={setOne} multiple={false} />
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
            actions={<><Button variant="secondary">Bulk upload</Button><Button onClick={() => setDrawer(true)}>Add location</Button></>}>
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
            <TextField label="Location name" defaultValue="Karen branch" />
            <SelectField label="Town" hint="Sets the minimum-wage zone and public holidays." options={[{ value: 'nbo', label: 'Nairobi' }]} />
            <CompanySelect label="Applies to" companies={companies} value={several} onChange={setSeveral} />
          </Drawer>
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
