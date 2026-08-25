import { getJson } from './client'

export interface TaxRegimeSummary {
  id: string
  code: string
  name: string
  countryCode: string
  /** True for VAT/GST, false for a sales tax like SST. */
  inputReclaimable: boolean
  effectiveFrom: string
  effectiveTo: string | null
  isActive: boolean
}

export interface TaxCodeSummary {
  id: string
  code: string
  name: string
  regimeCode: string
  countryCode: string
  kind: string
  /** Percentage, so 8% is 8. */
  rate: number
  outputAccountId: string | null
  inputAccountId: string | null
  inputReclaimable: boolean
  effectiveFrom: string
  effectiveTo: string | null
}

export function getTaxRegimes(): Promise<TaxRegimeSummary[]> {
  return getJson<TaxRegimeSummary[]>('/api/tax/regimes')
}

/**
 * Codes usable on a document dated `asOf`.
 *
 * Effective-dated on the document's date, not today: back-dating an invoice into a period
 * when a different regime was in force must offer that regime's codes.
 */
export function getTaxCodes(asOf: string): Promise<TaxCodeSummary[]> {
  return getJson<TaxCodeSummary[]>(`/api/tax/codes?asOf=${asOf}`)
}
