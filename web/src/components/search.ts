// "Kéroka" matches "kero": case and accents don't count when searching a list.
const fold = (text: string) => text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()

export const matches = (text: string, query: string) => fold(text).includes(fold(query.trim()))
