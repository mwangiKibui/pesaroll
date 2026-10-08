import axe from 'axe-core'
import { expect } from 'vitest'

/**
 * Runs axe on a rendered component and fails with the rule ids it breaks.
 * Colour contrast can't be measured in jsdom; tokens.test.ts checks it on the token table instead.
 */
export async function expectNoA11yViolations(container: Element) {
  const results = await axe.run(container, { rules: { 'color-contrast': { enabled: false }, region: { enabled: false } } })
  expect(results.violations.map(v => `${v.id}: ${v.nodes.map(n => n.target.join(' ')).join(', ')}`)).toEqual([])
}
