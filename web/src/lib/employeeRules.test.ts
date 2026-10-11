import { expect, it } from 'vitest'
import { accountProblem, isMpesa, rules } from './employeeRules'

it('checks statutory numbers the way the API does', () => {
  expect(rules.kraPin('a012345678z')).toBeNull()
  expect(rules.kraPin('P051234567X')).toBe('kraPin')
  expect(rules.nationalId('12345678')).toBeNull()
  expect(rules.nationalId('123456789')).toBeNull()   // Maisha Namba
  expect(rules.nationalId('123456')).toBe('nationalId')
  expect(rules.nssfNo('201234567')).toBeNull()
  expect(rules.nssfNo('12345678901')).toBe('nssfNo')
  expect(rules.kraPin('')).toBeNull()   // empty is fine unless required
})

it('M-Pesa needs a Safaricom line', () => {
  for (const ok of ['0712 345 678', '0748123456', '0110 123 456', '+254799000111', '0141123456']) expect(isMpesa(ok)).toBe(true)
  for (const no of ['0733 123 456', '0100123456', '0744123456', '071234567', '0201234567']) expect(isMpesa(no)).toBe(false)
})

it('bank account numbers fit their bank', () => {
  const equity = { code: '68', name: 'Equity Bank', accountDigitsMin: 13, accountDigitsMax: 13 }
  const ncba = { code: '07', name: 'NCBA Bank', accountDigitsMin: 10, accountDigitsMax: 12 }
  expect(accountProblem(equity, '0123 4567 89012')).toBeNull()
  expect(accountProblem(equity, '0123456789')).toEqual({ key: 'accountExactly', values: { bank: 'Equity Bank', count: 13 } })
  expect(accountProblem(ncba, '12345678901')).toBeNull()
  expect(accountProblem(ncba, '123456789')).toEqual({ key: 'accountBetween', values: { bank: 'NCBA Bank', min: 10, max: 12 } })
})
