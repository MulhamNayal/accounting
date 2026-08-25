import { getJson } from './client'

/** Mirrors LegalEntitySummary on the backend, field for field. */
export interface LegalEntitySummary {
  id: string
  code: string
  name: string
  registrationNo: string | null
  taxId: string | null
  functionalCurrency: string
  financialYearStartMonth: number
  isActive: boolean
}

export function getEntities(): Promise<LegalEntitySummary[]> {
  return getJson<LegalEntitySummary[]>('/api/entities')
}
