import { getJson, postJson } from './client'
import { todayLocal } from './dates'

export interface ReceiptSummary {
  id: string
  docNo: string | null
  receiptDate: string
  customerName: string
  currencyCode: string
  amount: number
  allocated: number
  unallocated: number
  state: 'Draft' | 'Posted'
  journalEntryId: string | null
}

export interface CreateReceiptRequest {
  legalEntityId: string
  customerId: string
  bankAccountId: string
  receiptDate: string
  amount: number
  currencyCode?: string
  fxRate?: number
  reference?: string
  memo?: string
}

export interface AllocationDetail {
  id: string
  customerReceiptId: string
  receiptDocNo: string | null
  salesInvoiceId: string
  invoiceDocNo: string | null
  amount: number
  functionalAmount: number
  /** Non-zero when the invoice and receipt rates differed. */
  fxGainLossFunctional: number
  journalEntryId: string | null
  allocatedAtUtc: string
  reversesAllocationId: string | null
}

export interface OpenInvoice {
  id: string
  docNo: string | null
  docDate: string
  dueDate: string
  currencyCode: string
  total: number
  allocated: number
  outstanding: number
  daysOverdue: number
}

export interface CustomerBalance {
  customerId: string
  customerCode: string
  customerName: string
  balance: number
  current: number
  days1To30: number
  days31To60: number
  days61To90: number
  over90: number
}

export interface AgeingReport {
  asOf: string
  customers: CustomerBalance[]
  total: number
}

export interface StatementLine {
  date: string
  documentType: string
  docNo: string | null
  description: string | null
  debit: number
  credit: number
  runningBalance: number
}

export interface CustomerStatement {
  customerId: string
  customerName: string
  asOf: string
  lines: StatementLine[]
  closingBalance: number
}

export function getReceipts(entityId: string): Promise<ReceiptSummary[]> {
  return getJson<ReceiptSummary[]>(`/api/receipts?entityId=${entityId}`)
}

export function createReceipt(request: CreateReceiptRequest): Promise<ReceiptSummary> {
  return postJson<ReceiptSummary>('/api/receipts', request)
}

export function postReceipt(id: string): Promise<ReceiptSummary> {
  return postJson<ReceiptSummary>(`/api/receipts/${id}/post`, {})
}

export function allocate(
  receiptId: string,
  lines: { salesInvoiceId: string; amount: number }[],
): Promise<AllocationDetail[]> {
  return postJson<AllocationDetail[]>('/api/allocations', { receiptId, lines })
}

export function unallocate(id: string): Promise<AllocationDetail> {
  return postJson<AllocationDetail>(`/api/allocations/${id}/unallocate`, {})
}

export function getOpenInvoices(entityId: string, customerId?: string): Promise<OpenInvoice[]> {
  const suffix = customerId ? `&customerId=${customerId}` : ''
  return getJson<OpenInvoice[]>(`/api/receivables/open-invoices?entityId=${entityId}${suffix}`)
}

/** The date is sent explicitly for the same reason as the trial balance — see getTrialBalance. */
export function getAgeing(entityId: string, asOf: string = todayLocal()): Promise<AgeingReport> {
  return getJson<AgeingReport>(`/api/receivables/ageing?entityId=${entityId}&asOf=${asOf}`)
}

export function getStatement(
  entityId: string,
  customerId: string,
  asOf: string = todayLocal(),
): Promise<CustomerStatement> {
  return getJson<CustomerStatement>(
    `/api/receivables/statement?entityId=${entityId}&customerId=${customerId}&asOf=${asOf}`,
  )
}
