import type { TFunction } from 'i18next'
import i18n from '../i18n'
import type { ApiError } from './services'

/** The error's message in the app's language: translated by code where we have it, else the API's English. */
export function errorMessage(t: TFunction, error: ApiError) {
  return i18n.exists(`apiErrors.${error.code}`) ? t(`apiErrors.${error.code}`) : error.message
}

/** Translation key for an error shown under a field. */
export const errorKey = (code: string) => `apiErrors.${code}`

export function isApiError(value: unknown): value is ApiError {
  return typeof value === 'object' && value !== null && 'code' in value && 'reference' in value
}
