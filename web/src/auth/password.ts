export type Strength = 'weak' | 'fair' | 'strong'

// The most common passwords people pick (lowercase). Matching is case-insensitive.
const common = new Set(`
123456 1234567 12345678 123456789 1234567890 12345 1234 111111 000000 123123 654321 666666 121212 112233 123321
qwerty qwerty123 qwertyuiop asdfgh asdfghjkl zxcvbnm 1q2w3e4r 1qaz2wsx qazwsx abc123 abcd1234 a1b2c3d4 aa123456
password password1 password12 password123 passw0rd p@ssword p@ssw0rd pass1234 password! password2026 password2025
iloveyou iloveyou1 letmein welcome welcome1 welcome123 admin admin123 administrator root toor login master
monkey dragon football baseball soccer basketball superman batman princess sunshine shadow michael jennifer
charlie trustno1 starwars freedom whatever hello123 hello hellohello computer internet secret secret123
kenya kenya123 kenya2026 kenya254 nairobi nairobi123 mombasa mombasa123 kisumu nakuru eldoret safaricom mpesa
mpesa123 equity equity123 jesus jesus123 jesus1 god123 godisgood blessed blessed1 amen123 grace grace123 faith
love123 loveyou mylove baby123 mama123 mamamia papa123 family family123 asante jambo jambo123 karibu karibu123
habari pesaroll pesaroll1 payroll payroll123 salary salary123 company company1 business business1 changeme
default guest test test123 testing testing123 user user123 demo demo123 sample letmein1 access access123 qwe123
zaq12wsx 1qazxsw2 q1w2e3r4 q1w2e3r4t5 asdf1234 zxcv1234 987654321 0987654321 11111111 00000000 88888888 99999999
12341234 11223344 123qwe 123abc abc12345 abcdef abcdefg abcdefgh abcdefghi aaaaaa aaaaaaaa
`.trim().split(/\s+/))

/**
 * Weak blocks sign-up: under 8 characters, only one kind of character (letters, digits or
 * symbols), or a common password. Strong: 12+ characters with three kinds, or 16+ with two.
 * The API only requires 6 characters; this screen asks for more.
 */
export function passwordStrength(password: string): Strength {
  const kinds = [/\p{L}/u, /\d/, /[^\p{L}\d]/u].filter(re => re.test(password)).length
  if (password.length < 8 || kinds < 2 || common.has(password.toLowerCase())) return 'weak'
  if ((password.length >= 12 && kinds >= 3) || password.length >= 16) return 'strong'
  return 'fair'
}
