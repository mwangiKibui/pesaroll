import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'
import { expect, it } from 'vitest'

// Every dropdown is styled and searchable (docs/standards/ui.md, rule 6): SelectField or
// CompanySelect, never the browser's own <select>, which can't be styled or searched.
const src = resolve(process.cwd(), 'src')

function files(dir: string): string[] {
  return readdirSync(dir).flatMap(name => {
    const path = join(dir, name)
    return statSync(path).isDirectory() ? files(path) : [path]
  })
}

it('no native <select> anywhere', () => {
  const offenders = files(src)
    .filter(f => /\.tsx$/.test(f) && !/\.test\.tsx$/.test(f))
    .flatMap(f => readFileSync(f, 'utf8').split('\n')
      .map((line, i) => ({ line, where: `${relative(src, f)}:${i + 1}` }))
      .filter(({ line }) => /<select[\s>]/.test(line))
      .map(({ where, line }) => `${where}  ${line.trim()}`))
  expect(offenders).toEqual([])
})
