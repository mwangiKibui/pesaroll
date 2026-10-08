import { expect, it } from 'vitest'
import { passwordStrength } from './password'

it.each([
  ['', 'weak'],
  ['abc12', 'weak'],          // too short
  ['Ab3$xyz', 'weak'],        // 7 characters
  ['abcdefghij', 'weak'],     // letters only
  ['1234567890', 'weak'],     // digits only
  ['password1', 'weak'],      // a common password
  ['Password123', 'weak'],    // common, whatever the case
  ['jambo2026', 'fair'],      // 9 characters, letters and digits
  ['Mombasa-raha-7', 'strong'], // 14 characters, three kinds
  ['jamboraha kenya 2026 sana', 'strong'], // a long passphrase
])('%j is %s', (password, expected) => {
  expect(passwordStrength(password)).toBe(expected)
})
