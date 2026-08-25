import { getJson } from './client'

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

/** The API returns a raw JSON string for 400/404/409, so the message is read directly. */
async function send<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(body),
  })

  const text = await response.text()

  if (!response.ok) {
    let message = text
    try {
      const parsed: unknown = JSON.parse(text)
      if (typeof parsed === 'string') message = parsed
      else if (parsed && typeof parsed === 'object' && 'detail' in parsed) {
        message = String((parsed as { detail: unknown }).detail)
      }
    } catch {
      // Not JSON; the raw text is the best message available.
    }
    throw new Error(message || `${response.status} ${response.statusText}`)
  }

  return JSON.parse(text) as T
}

export function getJournalEntries(entityId: string): Promise<JournalEntrySummary[]> {
  return getJson<JournalEntrySummary[]>(`/api/journal-entries?entityId=${entityId}`)
}

export function getJournalEntry(id: string): Promise<JournalEntryDetail> {
  return getJson<JournalEntryDetail>(`/api/journal-entries/${id}`)
}

export function postJournalEntry(request: PostJournalEntryRequest): Promise<JournalEntryDetail> {
  return send<JournalEntryDetail>('/api/journal-entries', request)
}

export function reverseJournalEntry(id: string, reasonCode: string): Promise<JournalEntryDetail> {
  return send<JournalEntryDetail>(`/api/journal-entries/${id}/reverse`, { reasonCode })
}

export function getTrialBalance(entityId: string): Promise<TrialBalance> {
  return getJson<TrialBalance>(`/api/trial-balance?entityId=${entityId}`)
}

export function formatMoney(value: number): string {
  return value.toLocaleString('en-MY', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}
