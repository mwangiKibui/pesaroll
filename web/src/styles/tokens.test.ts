import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

// The colour tokens live in tokens.css, once for light and once for dark. These tests read the
// file itself, so a colour change that breaks contrast in either theme fails here.
const css = readFileSync(resolve(process.cwd(), 'src/styles/tokens.css'), 'utf8')

function block(selector: string): Record<string, string> {
  const start = css.indexOf(`${selector} {`)
  if (start < 0) throw new Error(`no ${selector} block in tokens.css`)
  const body = css.slice(start, css.indexOf('}', start))
  return Object.fromEntries([...body.matchAll(/--pr-([\w-]+):\s*(#[0-9a-fA-F]{6})\s*;/g)].map(m => [m[1], m[2]]))
}

const themes = { light: block(':root'), dark: block(':root[data-theme="dark"]') }

function luminance(hex: string) {
  const [r, g, b] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map(c => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

export function contrast(a: string, b: string) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}

// [foreground, background]: text must reach 4.5:1 (WCAG AA).
const textPairs = [
  ['ink', 'page'], ['ink', 'card'], ['ink', 'raised'],
  ['muted', 'page'], ['muted', 'card'], ['muted', 'raised'],
  ['brand', 'page'], ['brand', 'card'],
  ['on-brand', 'brand'], ['on-brand', 'brand-hover'],
  ['brand-soft-ink', 'brand-soft'], ['warning-ink', 'warning-soft'], ['danger-ink', 'danger-soft'],
  ['danger-ink', 'card'], ['on-danger', 'danger'],
  ['sidebar-ink', 'sidebar'], ['sidebar-ink', 'sidebar-raise'], ['sidebar-muted', 'sidebar'],
  ['sidebar-accent', 'sidebar'], ['sidebar-current-ink', 'sidebar-current'],
] as const

// Borders of controls and the focus ring must reach 3:1 against what they sit on (WCAG 1.4.11).
const uiPairs = [['field-border', 'card'], ['field-border', 'page'], ['brand', 'card'], ['brand', 'page']] as const

describe.each(Object.entries(themes))('%s theme', (_, tokens) => {
  it.each(textPairs)('text %s on %s reaches 4.5:1', (fg, bg) => {
    expect(tokens[fg], `--pr-${fg}`).toBeDefined()
    expect(tokens[bg], `--pr-${bg}`).toBeDefined()
    expect(contrast(tokens[fg], tokens[bg])).toBeGreaterThanOrEqual(4.5)
  })

  it.each(uiPairs)('control %s on %s reaches 3:1', (fg, bg) => {
    expect(contrast(tokens[fg], tokens[bg])).toBeGreaterThanOrEqual(3)
  })
})

it('every colour token has a light and a dark value', () => {
  expect(Object.keys(themes.dark).sort()).toEqual(Object.keys(themes.light).sort())
})
