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
10. **Test-first:** component behaviour, keyboard use and axe, with Vitest and React Testing Library, next to the component (`Component.test.tsx`). Test what people see and do, not class names.

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

- **Admin layout (`AppShell`):**
  - dark sidebar grouped Home · People · Pay · Organization · Admin
  - current page highlighted, counts as badges
  - a "Finish setting up · 8 of 10" card while setup is incomplete
  - on phones the sidebar opens from a Menu button
- **Setup and list pages:**
  1. A title phrased as a question ("Where do your people work?"), one sentence of help, and a skip link.
  2. The company selector as a filter.
  3. A `TableCard` showing the title with count, then Bulk upload, then "Add <thing>".
  4. Labelled edit and delete `IconButton`s in each row.
  5. At the foot, on the right, an outlined "Proceed to <next step>" with an arrow (`Proceed`). No "Save and continue": every change is saved where it is made (drawers, modals, confirmations), so the foot only moves on.
- **Add and edit happen in a `Drawer` from the right:** 480 px, full width on phones. Its title is "Add location" or "Edit location"; the footer has Cancel, then a button that names the action ("Add location", "Save changes"). Never a separate page or a centred pop-up.
  - Records with many details (a company) use the wide drawer (960 px) with `Tabs` under the title: the record's own form on the first tab, in headed sections ("Company", "Statutory numbers", "Address and contact"), and things that are saved separately (paying accounts) on their own tabs. Hidden tabs keep what was typed; saving with a problem goes back to its tab, and a tab with something to fix is marked. The footer (Cancel, then the save button) is the same on every tab. Tabs for things that need the record to exist first (paying accounts) are shown but unavailable while adding; once it is added, the drawer stays open on that tab.
  - Lists inside a drawer (a company's paying accounts) are a table; adding to them opens a `Modal` on top (a centred dialog, never a drawer on a drawer). Escape closes only the modal.
- **Uploading many records (`BulkUpload`), the same for every import on every platform:** an "Upload <things>" button beside "Add <thing>" opens a drawer with 1 download the template (Excel, dropdowns from the same data as the screen) and 2 upload the filled file (.xlsx or .csv). Nothing is ever imported blindly: the file opens in a **large review modal** (`Modal size="wide"`) showing every column and row of the spreadsheet in an editable table (list columns as searchable dropdowns), with a search box, "Only rows to fix", a fixed header and its own scrolling, problems shown in their cells, and a remove-row button. Where the rows apply (the company selector, whose list scrolls) sits above the table. "Import N <things>" checks the edited rows on the server again and saves them all or none, then a toast.
- **Company selector (`CompanySelect`)** on every list and setup screen where companies can differ:
  - "All companies" first ("Includes companies added later")
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
