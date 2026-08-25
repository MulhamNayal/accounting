import { getJson, postJson } from './client'
import { todayLocal } from './dates'

export interface JournalEntrySummary {
  id: string
  entryNo: string
  entryDate: string
  sourceDocumentType: string
  memo: string | null
  totalDebit: number
  lineCount: number
  isReversal: boolean
  isReversed: boolean
}

export interface PostingLine {
  id: string
  lineNo: number
  accountId: string
  accountCode: string
  accountName: string
  direction: 'Debit' | 'Credit'
  amount: number
  currencyCode: string
  functionalAmount: number
  fxRate: number
  customerId: string | null
  description: string | null
}

export interface JournalEntryDetail {
  id: string
  entryNo: string
  entryDate: string
  sourceDocumentType: string
  memo: string | null
  postedAtUtc: string
  reversesEntryId: string | null
  supersedesEntryId: string | null
  reversedByEntryId: string | null
  reasonCode: string | null
  lines: PostingLine[]
}

export interface PostingLineRequest {
  accountId: string
  direction: 'Debit' | 'Credit'
  amount: number
  currencyCode?: string
  fxRate?: number
  customerId?: string
  description?: string
}

export interface PostJournalEntryRequest {
  legalEntityId: string
  entryDate: string
  lines: PostingLineRequest[]
  memo?: string
}

export interface TrialBalanceLine {
  accountId: string
  accountCode: string
  accountName: string
  accountType: string
  debit: number
  credit: number
  balance: number
}

export interface TrialBalance {
  asOf: string
  lines: TrialBalanceLine[]
  totalDebit: number
  totalCredit: number
  isBalanced: boolean
}

export function getJournalEntries(entityId: string): Promise<JournalEntrySummary[]> {
  return getJson<JournalEntrySummary[]>(`/api/journal-entries?entityId=${entityId}`)
}

export function getJournalEntry(id: string): Promise<JournalEntryDetail> {
  return getJson<JournalEntryDetail>(`/api/journal-entries/${id}`)
}

export function postJournalEntry(request: PostJournalEntryRequest): Promise<JournalEntryDetail> {
  return postJson<JournalEntryDetail>('/api/journal-entries', request)
}

export function reverseJournalEntry(id: string, reasonCode: string): Promise<JournalEntryDetail> {
  return postJson<JournalEntryDetail>(`/api/journal-entries/${id}/reverse`, { reasonCode })
}

/**
 * The date is always sent explicitly. Omitting it makes the server fall back to UTC today,
 * which east of Greenwich is yesterday for part of every day — and a trial balance dated
 * yesterday silently omits everything posted today.
 */
export function getTrialBalance(entityId: string, asOf: string = todayLocal()): Promise<TrialBalance> {
  return getJson<TrialBalance>(`/api/trial-balance?entityId=${entityId}&asOf=${asOf}`)
}

export function formatMoney(value: number): string {
  return value.toLocaleString('en-MY', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}
