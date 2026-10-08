/**
 * The same rules as the API's Identifiers.cs, so the screen catches mistakes before sending
 * and the server and screen always agree on what a valid email or phone is.
 */
export function normalizeEmail(input: string): string | null {
  const email = input.trim().toLowerCase()
  if (!email || email.length > 254) return null
  return /^[^\s@<>()",;]+@[^\s@<>()",;]+\.[^\s@<>()",;]+$/.test(email) ? email : null
}

/** A Kenyan mobile number as +254 and 9 digits (07…, 01…, 254…, +254… accepted). */
export function normalizePhone(input: string): string | null {
  const digits = input.replace(/\D/g, '')
  let local: string | null = null
  if (digits.length === 10 && digits.startsWith('0')) local = digits.slice(1)
  else if (digits.length === 12 && digits.startsWith('254')) local = digits.slice(3)
  else if (digits.length === 9) local = digits
  return local && (local.startsWith('7') || local.startsWith('1')) ? `+254${local}` : null
}

/** One "email or phone" box: sent as whichever it is, or null when it is neither. */
export function splitContact(input: string): { email: string } | { phone: string } | null {
  if (input.includes('@')) {
    const email = normalizeEmail(input)
    return email ? { email } : null
  }
  const phone = normalizePhone(input)
  return phone ? { phone } : null
}
