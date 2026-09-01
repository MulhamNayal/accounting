import { getJson, postJson } from './client'

export type PeriodStateName = 'Open' | 'SoftClosed' | 'HardClosed'

export interface FiscalYearSummary {
  id: string
  legalEntityId: string
  code: string
  startDate: string
  endDate: string
  state: PeriodStateName
  periodCount: number
  openPeriodCount: number
  /** The entry that transferred the year's result. Null until one has been posted. */
  closingEntryId: string | null
  closingEntryNo: string | null
  closingEntryIsReversed: boolean
  canFinalise: boolean
}

export interface CreateFiscalYearRequest {
  legalEntityId: string
  code: string
  startDate: string
  endDate: string
  /** Omit for calendar months. Give it to divide the year into equal spans instead. */
  periodCount?: number
}

export interface PeriodSummary {
  id: string
  fiscalYearId: string
  fiscalYearCode: string
  sequence: number
  startDate: string
  endDate: string
  state: PeriodStateName
  entryCount: number
}

export interface PeriodEventSummary {
  id: string
  periodId: string
  periodSequence: number
  fromState: PeriodStateName
  toState: PeriodStateName
  atUtc: string
  byUser: string
  reason: string
}

export interface DraftDocumentCount {
  documentType: string
  count: number
}

export interface PeriodReadiness {
  periodId: string
  sequence: number
  startDate: string
  endDate: string
  state: PeriodStateName
  postedEntryCount: number
  /** Reasons the close will be refused. Empty when it will succeed. */
  blockers: string[]
  /** Drafts dated inside the period. A warning, not a blocker. */
  drafts: DraftDocumentCount[]
  canSoftClose: boolean
  draftCount: number
}

export interface ClosingEntryLine {
  accountId: string
  accountCode: string
  accountName: string
  accountType: string
  direction: 'Debit' | 'Credit'
  amount: number
}

export interface ClosingEntryPreview {
  fiscalYearId: string
  fiscalYearCode: string
  entryDate: string
  currencyCode: string
  lines: ClosingEntryLine[]
  totalIncome: number
  totalExpense: number
  netResult: number
  retainedEarningsAccountCode: string
  blockers: string[]
  canPost: boolean
}

export interface JournalEntryRef {
  id: string
  entryNo: string
}

export function getFiscalYears(entityId: string): Promise<FiscalYearSummary[]> {
  return getJson<FiscalYearSummary[]>(`/api/fiscal-years?entityId=${entityId}`)
}

export function createFiscalYear(
  request: CreateFiscalYearRequest,
): Promise<FiscalYearSummary> {
  return postJson<FiscalYearSummary>('/api/fiscal-years', request)
}

export function getPeriods(entityId: string, fiscalYearId?: string): Promise<PeriodSummary[]> {
  const year = fiscalYearId ? `&fiscalYearId=${fiscalYearId}` : ''
  return getJson<PeriodSummary[]>(`/api/periods?entityId=${entityId}${year}`)
}

export function getPeriodEvents(
  entityId: string,
  fiscalYearId?: string,
): Promise<PeriodEventSummary[]> {
  const year = fiscalYearId ? `&fiscalYearId=${fiscalYearId}` : ''
  return getJson<PeriodEventSummary[]>(`/api/periods/events?entityId=${entityId}${year}`)
}

export function getPeriodReadiness(periodId: string): Promise<PeriodReadiness> {
  return getJson<PeriodReadiness>(`/api/periods/${periodId}/readiness`)
}

export function closePeriod(periodId: string, reason: string): Promise<PeriodSummary> {
  return postJson<PeriodSummary>(`/api/periods/${periodId}/close`, { reason })
}

export function reopenPeriod(periodId: string, reason: string): Promise<PeriodSummary> {
  return postJson<PeriodSummary>(`/api/periods/${periodId}/reopen`, { reason })
}

export function getClosingEntryPreview(fiscalYearId: string): Promise<ClosingEntryPreview> {
  return getJson<ClosingEntryPreview>(`/api/fiscal-years/${fiscalYearId}/closing-entry`)
}

export function postClosingEntry(
  fiscalYearId: string,
  memo?: string,
): Promise<JournalEntryRef> {
  return postJson<JournalEntryRef>(`/api/fiscal-years/${fiscalYearId}/closing-entry`, { memo })
}

export function finaliseFiscalYear(
  fiscalYearId: string,
  reason: string,
): Promise<FiscalYearSummary> {
  return postJson<FiscalYearSummary>(`/api/fiscal-years/${fiscalYearId}/finalise`, { reason })
}

/** How each state should read in a badge. */
export const periodStateColor: Record<PeriodStateName, 'success' | 'warning' | 'danger'> = {
  Open: 'success',
  SoftClosed: 'warning',
  HardClosed: 'danger',
}

export const periodStateLabel: Record<PeriodStateName, string> = {
  Open: 'Open',
  SoftClosed: 'Closed',
  HardClosed: 'Hard closed',
}
