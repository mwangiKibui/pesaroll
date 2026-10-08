import { expect, it } from 'vitest'
import en from './locales/en.json'
import sw from './locales/sw.json'
import i18n from '.'

function keys(obj: object, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([k, v]) => (typeof v === 'object' ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`]))
}

function values(obj: object): string[] {
  return Object.values(obj).flatMap(v => (typeof v === 'object' ? values(v) : [v as string]))
}

it('English and Swahili have the same keys', () => {
  expect(keys(sw).sort()).toEqual(keys(en).sort())
})

it('no translation is empty', () => {
  expect([...values(en), ...values(sw)].filter(v => !v.trim())).toEqual([])
})

it('starts in English and can switch to Swahili', async () => {
  expect(i18n.language).toBe('en')
  await i18n.changeLanguage('sw')
  expect(i18n.t('language.label')).toBe('Lugha')
  await i18n.changeLanguage('en')
})
