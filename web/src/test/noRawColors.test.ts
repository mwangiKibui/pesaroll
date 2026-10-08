import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'
import { expect, it } from 'vitest'

// Screens use colour tokens (bg-card, text-muted…), never raw colours, so light and dark
// both work. Only src/styles/tokens.css may hold colour values. See docs/standards/ui.md.
const src = resolve(process.cwd(), 'src')

function files(dir: string): string[] {
  return readdirSync(dir).flatMap(name => {
    const path = join(dir, name)
    return statSync(path).isDirectory() ? files(path) : [path]
  })
}

// Tailwind's own palette (bg-green-700, text-white…) is switched off in tokens.css; this names the mistake.
const palette = /\b(bg|text|border|ring|fill|stroke|from|via|to|outline|accent|decoration|divide|shadow)-(slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|black|white)\b/

// Comments can't style anything, and "#141" in a comment is an issue number, not a colour.
function withoutComments(line: string) {
  const code = line.replace(/\/\/.*$/, '').replace(/\/\*.*?\*\//g, '')
  return /^\s*(\*|\/\*)/.test(code) ? '' : code
}

it('no raw colours outside tokens.css', () => {
  const offenders = files(src)
    .filter(f => /\.(tsx?|css)$/.test(f) && !f.endsWith('tokens.css') && !/\.test\.tsx?$/.test(f))
    .flatMap(f => readFileSync(f, 'utf8').split('\n')
      .map((line, i) => ({ line, where: `${relative(src, f)}:${i + 1}` }))
      .map(({ line, where }) => ({ where, line, code: withoutComments(line) }))
      .filter(({ code }) => /#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(/.test(code) || palette.test(code))
      .map(({ where, line }) => `${where}  ${line.trim()}`))
  expect(offenders).toEqual([])
})
