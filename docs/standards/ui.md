# UI standards (PLAT-08)

How every Pesaroll screen looks and is built: the admin app, the back office and the phone-first employee portal. The look comes from the Onboarding Prototype; from now on the code and the components page are the source of truth.

**See it:** run `docker compose up -d`, then open http://localhost:5173/dev/components (development only). It shows every component in every state; switch theme and language at the top.

## The rules

1. **Build screens from the components** in `web/src/components/`. Need something new? Add it there test-first, show it on the components page, then use it.
2. **Colours come from tokens only:** `bg-card`, `text-muted`, `border-line`, `bg-brand`… never a hex code, `rgb()` or a Tailwind palette colour (`bg-green-700`). The values live in `web/src/styles/tokens.css`, once for light and once for dark. A test fails on raw colours anywhere else.
3. **Light only for now.** Every token still has a light and a dark value, and both stay contrast-tested, so dark mode can return later. The app always shows light (`THEME_CHOICE_ENABLED` in `web/src/theme/theme.ts`; turning it on also needs `ThemeMenu` back in the navbar). Payslip PDFs and exported files are always light.
4. **Every field has a placeholder: "Your" and the field name**, such as "Your first name", "Your email or phone number", "Your password". No examples and no "e.g.". `TextField` won't compile without one. The label still says what the field is; the placeholder never replaces it.
5. **Password fields have an eye icon inside the field** (on the right) to show or hide what was typed: use `PasswordField`, never a "Show password" link under the field.
6. **Every dropdown is styled and searchable, everywhere:** admin app, employee portal on phones, and any app we build later. Use `SelectField` (one choice) or `CompanySelect` (companies); never a native `<select>`, which can't be styled or searched. Typing filters the options (ignoring case and accents), arrows move, Enter picks, Escape closes, and "No matches" shows when nothing fits. Every filter and search on a list uses the same components. A test fails on a native `<select>` outside the component.
7. **Every word through translations,** in English and Swahili (`web/src/i18n/locales/`). The keys must match; a test checks. **For now the app is English only:** people can't choose Swahili until a native speaker has reviewed it (to turn it on: `initialLanguage()` in `web/src/i18n/index.ts` and `LanguageSwitch` in the navbar).
8. **Phone first for anything employees use.** Design the portal at 390 px wide; check every admin screen at phone width too (menus stack, tables scroll sideways inside their card).
9. **Accessible as built:**
   - real `<button>`, `<a href>` and labelled inputs
   - icon-only buttons carry a label (`IconButton label="Edit Westlands Restaurant"`)
   - tap targets at least 44 px (50 px for main buttons on the portal)
   - a visible focus ring
   - the pointer hand on everything clickable (buttons, menu items, options, radio and checkbox labels), "not allowed" on disabled buttons: set once in `web/src/index.css`, never per component
   - text contrast at least 4.5:1 in both themes (tested on the tokens)
   - every component test runs axe
10. **Every table has pagination,** shown even when everything fits on one page: put `Pagination` in the `TableCard` footer. It reads "Showing 21–40 of 132", offers rows per page (10, 20, 50, 100; 20 to start, 10 for Employees), and Previous / Next with "Page 2 of 7"; changing rows per page goes back to page 1. Lists that can grow large page on the server (employees: `page` and `pageSize` in the request); setup lists page in the browser with `usePaged`. A search or filter goes back to page 1. The upload and add sheet pages its rows the same way (50, 100 or 200 a page; 100 to start).
11. **Test-first:** component behaviour, keyboard use and axe, with Vitest and React Testing Library, next to the component (`Component.test.tsx`). Test what people see and do, not class names.

## Look

| | Rule |
| --- | --- |
| Colour | Brand green for main actions, links and focus; deep green sidebar; grey-green page, white cards. Amber = warning or "not saved yet", red = delete and errors, green tint = main or done. |
| Type | Bricolage Grotesque for page titles and big numbers; IBM Plex Sans for everything else; IBM Plex Mono for codes (one-time codes, error references). Sizes: tag 12, hint 13, label and table 14, body 15, drawer title 22, page title 30 (24 on the portal), amounts 34. |
| Shape | Corners 8 for controls (`rounded-control`), 12 for cards (`rounded-card`), round for tags. Spacing on a 4 px grid. Shadows only on things that float (menus `shadow-float`, drawers `shadow-drawer`). |
| Fonts | Self-hosted, Latin only, just the weights we use, to keep phone data costs down. |

Colour tokens (light / dark):

| Token | Light | Dark | Used for |
| --- | --- | --- | --- |
| `page` | #F3F5F2 | #101513 | Page background |
| `card` | #FFFFFF | #18201C | Cards, inputs, drawers |
| `raised` | #F7FAF8 | #1D2622 | Table headers, hover |
| `ink` / `muted` | #18201D / #56635D | #E5EBE8 / #9BA8A2 | Text / secondary text |
| `line` / `field-border` | #DCE2DE / #7E8A84 | #2B3631 / #66746D | Dividers / control borders |
| `brand` / `on-brand` | #0E7A5F / #FFFFFF | #43C39B / #0B1712 | Main buttons, links, focus / text on them |
| `brand-soft` / `brand-soft-ink` | #E3F1EB / #0A5C47 | #15302A / #8FE0C3 | Tags, highlights |
| `warning-soft` / `warning-ink` | #FBEFDC / #6B3A00 | #3A2A12 / #F2C27B | Warnings, "Not saved yet" |
| `danger` / `danger-soft` / `danger-ink` | #B42318 / #FDE8E6 / #9B1C12 | #F08A80 / #3B1A18 / #F4A39B | Delete, errors |
| `sidebar…` | #0E3B30 and family | #0B1210 and family | Admin sidebar |

## Screens

- **Organization portal layout (`OrgShell` in `web/src/app/OrgShell.tsx`, over `AppShell`; OP-01):**
  - a white top bar: logo, organization, the **company switcher** (one company at a time, the person's first to start, remembered on their device) and **search** (pages the person may open, by name or the words people use), and a **Setup** button back to company setup for people who may manage companies (no progress or counts)
  - a green sidebar of sections (Pay · People · Leave · Organization · Reports · Admin) that open and close; the one holding the current page opens by itself, a closed one adds up its counts; current page highlighted
  - the person at the bottom of the sidebar opens their menu: how they use Pesaroll, Where you're signed in, Sign out
  - on phones the sidebar slides in from the Menu button over a scrim
  - **Pages read the chosen company with `useCurrentCompany()`** (`web/src/app/company.ts`). The switcher holds one company; setup pages keep the multi-company `CompanySelect`.
  - **Navigation is data** (`web/src/app/navigation.ts`): each link names the permission it needs (an area's View) and, until its page is built, its feature flag. A link shows when the person may open it for the chosen company and its flag is on. A test fails if a link has neither a page nor a flag.
  - **Feature flags** (`web/src/app/features.ts`) hide unfinished pages and cards: off unless named in `VITE_FEATURES` (a comma list, or `all`). A task that finishes a page removes its flag.
- **Home is cards chosen by permission and flag (OP-30):** `web/src/features/home/cards.ts` lists the cards like the navigation: each names the permission it needs for the chosen company and, until its task brings the data, its feature flag. Someone in HR and Payroll gets both sets on one page; code checks permission keys, never role names. Every card follows the top bar's company. On wide screens the cards sit in one row of four columns (a number card takes one, Ready for payroll two); two columns on tablets, one on phones. Above the cards: Add employee for people who may manage employees; no greeting or date (the page is titled Home for screen readers only). Cards that show pay amounts need `pay.view` (ACC-05); HR's cards show counts and names only. Numbers are worked out by the API (`GET /api/home/people`), never by loading every employee. A card that lists problems groups them by what's missing, with its code (`PAY-2001`), and a Fix them link to the filtered list, never a list of everyone.
- **What a person can do (ACC-04):** `usePermissions().can(permissions.<area>.manage)` (from `web/src/app/permissions.ts`, over `/api/auth/me`) hides Add, Edit, Delete and Upload from people who can only view, and pages they can't view aren't linked. Check permission keys, never group names. Hiding is never the protection: the server checks every request.
- **Setup and list pages:**
  1. A title phrased as a question ("Where do your people work?"), one sentence of help, and a skip link.
  2. The company selector as a filter.
  3. A `TableCard` showing the title with count, then Bulk upload, then "Add <thing>".
  4. Labelled edit and delete `IconButton`s in each row.
  5. At the foot, on the right, an outlined "Proceed to <next step>" with an arrow (`Proceed`). No "Save and continue": every change is saved where it is made (drawers, modals, confirmations), so the foot only moves on.
- **Add and edit happen in a `Drawer` from the right:** 480 px, full width on phones. Its title is "Add location" or "Edit location"; the footer has Cancel, then a button that names the action ("Add location", "Save changes"). Never a separate page or a centred pop-up.
  - Records with many details (a company) use the wide drawer (960 px) with `Tabs` under the title: the record's own form on the first tab, in headed sections ("Company", "Statutory numbers", "Address and contact"), and things that are saved separately (paying accounts) on their own tabs. Hidden tabs keep what was typed; saving with a problem goes back to its tab, and a tab with something to fix is marked. The footer (Cancel, then the save button) is the same on every tab. Tabs for things that need the record to exist first (paying accounts) are shown but unavailable while adding; once it is added, the drawer stays open on that tab.
  - Lists inside a drawer (a company's paying accounts) are a table; adding to them opens a `Modal` on top (a centred dialog, never a drawer on a drawer). Escape closes only the modal.
- **Uploading many records (`BulkUpload`), the same for every import on every platform:** an "Upload <things>" button beside "Add <thing>" opens a drawer with 1 download the template (Excel, dropdowns from the same data as the screen) and 2 upload the filled file (.xlsx or .csv). Nothing is ever imported blindly: the file opens in a **large review modal** (`Modal size="wide"`) showing every column and row of the spreadsheet in an editable table (list columns as searchable dropdowns), with a search box, "Only rows to fix", a fixed header and its own scrolling, 100 rows a page (search and the filter look across every page), problems shown in their cells, and a remove-row button. Where the rows apply (the company selector, whose list scrolls) sits above the table. "Import N <things>" checks the edited rows on the server again and saves them all or none, then a toast. **Higher levels are never a precondition:** when a row names something that doesn't exist yet (a department's division, a position's department, an employee's department or position), the review notes it under the cell ("No division called \"Restaurants\" yet: it will be created.") with a count of new items, and the import creates each once, for the same companies. Such columns are plain text in the template and the review (a name, existing or new), never dropdowns. Each template carries the whole chain above its rows (a unit or position names its department and that department's division), so a new department lands in the right division; a division that contradicts an existing department's, or two divisions for one new department, is a problem.
- **One set of pieces for a record's setup step and its page in the app (OP-07):** when a setup step and an app page show the same records, the list, profile drawer, add/edit form and upload live once in `web/src/features/<records>/` with the company as an input, and both pages are thin wrappers. Employees: `features/employees/EmployeesList` without `companyId` is the setup step (every company, `CompanySelect`, Company and Start date columns); with the top bar's `companyId` it is People → Employees (no Company column, Joined / Leaving on under the name, Ready for payroll with each missing item's code, e.g. PAY-2001, and Status and Ready for payroll filters above the table). Add, Edit, Upload and pay follow the permissions for that company, and forms start with that company.
  Payroll schedules (OP-05) share less: `features/schedules/ScheduleDrawer` and the pay day and cut-off wording (`useScheduleWording`) are shared, while each page keeps its own table. The setup step's table has every column the setup needs (reminders, default for). Organization → Payroll schedules (`/organization/payroll-schedules`) shows the top bar's company: Schedule with its frequency, Pay day with the cut-off ("Last working day · cut-off 25th"), Companies, People and Next pay dates. A new schedule starts with that company only. Its link needs `schedules.manage`, because HR's `schedules.view` is only for picking a schedule on the employee form. A refused delete stays in the confirm dialog with its code (`ConfirmDialog`'s `problem`). For weekly, fortnightly and daily schedules the drawer also has Statutory deductions (PAY-10, `features/schedules/StatutoryDeductions`): whether it makes them, which, for everyone or chosen people, from a date. It shows a month of a sample wage worked out by the API and the earlier choices. The choice is saved with the schedule's Save, after the schedule, and only when it changed. Only someone who manages schedules for every company can change it.
  - **Filters other pages link to live in the address** (validated in the route's `validateSearch`, unknown values dropped): `/people/employees?status=leaving` (or `former`, `all`), `?missing=kra_pin` (or `national_id`, `basic_pay`, `payment_account`, `any` for not ready, `none` for ready), `?hiredFrom=2026-10-01` (started on or after); they combine.
- **Company selector (`CompanySelect`)** on every list and setup screen where companies can differ:
  - every company ticked by default; no separate "All companies" option. Every company ticked means all of them, including companies added later; unticking narrows the choice
  - checkboxes for several, round options for one
  - a search box at the top of the list, like every dropdown (rule 6)
- **Suggest, don't ask:**
  - fill in sensible defaults and let people skip any step
  - unsaved work shows a "Not saved yet" `Tag`
  - problems that block progress show as an amber `Notice` that says how to fix them
- **Sign-in and sign-up:** on phones and tablets, only the navbar and the form; the green side panel shows from laptop width (1024 px) up, where it fits beside the form.
- **Employee portal (`PhoneShell`):**
  - one column of cards with a bottom bar (Home · Payslips · Leave · Profile)
  - main buttons full width (`size="lg"`), inputs `size="lg"`
  - the most important thing first: this month's net pay in a `highlight` card, the breakdown one tap away
- **States:** every screen has loading (`Loading`: placeholder shapes, no spinners), empty (`EmptyState`: one sentence and the add button) and error states.
- **Safety:**
  - deletes confirm or offer undo; anything that affects pay always confirms
  - salaries stay hidden on shared screens until someone with the permission reveals them

## Forms and errors

- Labels above fields, hints below in secondary text, "optional" beside the label; required fields carry no marker.
- **Action buttons on steps lead with an icon** left of their words: a plus for Add, an upload arrow for Upload, a download arrow for Download template (`components/icons.tsx`). Add comes before Upload. On a tabbed step (Org structure) one Add and one Upload sit on the tab row, on the right, and ask for the type first ("What are you adding?", "Choose what to upload"), starting on the tab you're on.
- Buttons say what they do: "Send code", "Verify and continue", never "Submit" or "OK". While waiting, a button is `busy`.
- Choosing from a list uses the searchable `SelectField`, with a placeholder like any field ("Your county").
- A field's error appears under it and is read out with it (`TextField error=…`).
- Problems with the whole form or page use `FormErrorBanner`, which shows the message plus the code and reference from the API, so customers can quote them to support (see `errors.md`).
- Short confirmations ("Location added") are toasts (`useToast().show(…)`), everywhere on the platform: top right (full width on phones), green with white text and a tick; a failure that isn't about a field is a red toast (`show(message, 'danger')`). They slide in from the right and fade out after 5 seconds (no movement when the device asks for reduced motion). Never an inline banner for these.

## Money

`Money` / `formatMoney`:
- "KES 17,501": the currency first, then thousand separators
- cents only when there are some
- a true minus sign for deductions (−1,170)
- digits line up in columns (`tabular-nums`)

Amounts come from the API as decimals and are never rounded for display beyond cents.

## Commands

```bash
docker compose up -d                 # app on :5173, components page on /dev/components
scripts/check.sh                     # includes the web tests, lint and build
```
