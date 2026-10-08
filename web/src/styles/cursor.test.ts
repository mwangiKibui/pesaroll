import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { expect, it } from 'vitest'

// Everything people can click shows the pointer hand (Tailwind's reset gives buttons the arrow).
// docs/standards/ui.md
const css = readFileSync(resolve(process.cwd(), 'src/index.css'), 'utf8').replace(/\s+/g, ' ')

it('buttons and things that act like them show the pointer; disabled buttons show not-allowed', () => {
  for (const selector of ['button:not(:disabled)', '[role="button"]', '[role="menuitemradio"]', '[role="option"]', 'label:has(> input[type="radio"])', 'label:has(> input[type="checkbox"])']) {
    expect(css, selector).toMatch(new RegExp(`${selector.replace(/[[\]()":>.*]/g, m => `\\${m}`)}[^{]*\\{[^}]*cursor: pointer`))
  }
  expect(css).toMatch(/button:disabled[^{]*\{[^}]*cursor: not-allowed/)
})
