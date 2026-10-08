import { expect, it } from 'vitest'
import { normalizeEmail, normalizePhone, splitContact } from './identifiers'

// Same cases as the API's IdentifiersTests, so the screen and the server agree.
it.each([
  [' Wanjiru@Example.COM ', 'wanjiru@example.com'],
  ['a.b@kampuni.co.ke', 'a.b@kampuni.co.ke'],
  ['no-at-sign', null],
  ['name@localhost', null],
  ['', null],
])('email %j becomes %j', (input, expected) => {
  expect(normalizeEmail(input)).toBe(expected)
})

it.each([
  ['0712345678', '+254712345678'],
  ['0112345678', '+254112345678'],
  ['+254 712 345 678', '+254712345678'],
  ['254712345678', '+254712345678'],
  ['712345678', '+254712345678'],
  ['0812345678', null],
  ['071234567', null],
  ['+256712345678', null],
])('phone %j becomes %j', (input, expected) => {
  expect(normalizePhone(input)).toBe(expected)
})

it('one "email or phone" box is sent as whichever it is', () => {
  expect(splitContact('Grace@Example.com')).toEqual({ email: 'grace@example.com' })
  expect(splitContact('0712 345 678')).toEqual({ phone: '+254712345678' })
  expect(splitContact('grace')).toBeNull()
})
