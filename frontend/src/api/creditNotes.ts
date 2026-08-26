import { getJson, postJson } from './client'

export interface CreditNoteLineDetail {
  id: string
  lineNo: number
  description: string
  quantity: number
  unitPrice: number
  lineTotal: number
  accountId: string
  accountCode: string
  accountName: string
  taxCodeId: string | null
  taxCodeLabel: string | null
  taxRate: number
  taxAmount: number
}

/** The account is the revenue account on a sales credit, the charge account on a purchase one. */
export interface CreateCreditNoteLine {
  description: string
  quantity: number
  unitPrice: number
  accountId: string
  taxCodeId?: string
  projectId?: string
}

export interface SalesCreditNoteDetail {
  id: string
  docNo: string | null
  docDate: string
  salesInvoiceId: string
  invoiceDocNo: string | null
  customerId: string
  customerName: string
  currencyCode: string
  fxRate: number
  reasonCode: string
  memo: string | null
  state: 'Draft' | 'Posted'
  journalEntryId: string | null
  total: number
  taxTotal: number
  totalWithTax: number
  lines: CreditNoteLineDetail[]
}

export interface SalesCreditNoteSummary {
  id: string
  docNo: string | null
  docDate: string
  invoiceDocNo: string | null
  customerName: string
  currencyCode: string
  total: number
  taxTotal: number
  totalWithTax: number
  reasonCode: string
  state: 'Draft' | 'Posted'
  journalEntryId: string | null
}

export interface CreateSalesCreditNoteRequest {
  legalEntityId: string
  salesInvoiceId: string
  docDate: string
  reasonCode: string
  lines: CreateCreditNoteLine[]
  memo?: string
}

export interface PurchaseCreditNoteSummary {
  id: string
  docNo: string | null
  supplierCreditNoteNo: string | null
  docDate: string
  supplierInvoiceNo: string
  supplierName: string
  currencyCode: string
  total: number
  taxTotal: number
  totalWithTax: number
  reasonCode: string
  state: 'Draft' | 'Posted'
  journalEntryId: string | null
}

export interface PurchaseCreditNoteDetail extends Omit<PurchaseCreditNoteSummary, 'supplierName'> {
  purchaseInvoiceId: string
  supplierId: string
  supplierName: string
  fxRate: number
  memo: string | null
  lines: CreditNoteLineDetail[]
}

export interface CreatePurchaseCreditNoteRequest {
  legalEntityId: string
  purchaseInvoiceId: string
  docDate: string
  reasonCode: string
  lines: CreateCreditNoteLine[]
  supplierCreditNoteNo?: string
  memo?: string
}

export function getSalesCreditNotes(entityId: string): Promise<SalesCreditNoteSummary[]> {
  return getJson<SalesCreditNoteSummary[]>(`/api/sales-credit-notes?entityId=${entityId}`)
}

export function createSalesCreditNote(
  request: CreateSalesCreditNoteRequest,
): Promise<SalesCreditNoteDetail> {
  return postJson<SalesCreditNoteDetail>('/api/sales-credit-notes', request)
}

export function postSalesCreditNote(id: string): Promise<SalesCreditNoteDetail> {
  return postJson<SalesCreditNoteDetail>(`/api/sales-credit-notes/${id}/post`, {})
}

export function getPurchaseCreditNotes(entityId: string): Promise<PurchaseCreditNoteSummary[]> {
  return getJson<PurchaseCreditNoteSummary[]>(`/api/purchase-credit-notes?entityId=${entityId}`)
}

export function createPurchaseCreditNote(
  request: CreatePurchaseCreditNoteRequest,
): Promise<PurchaseCreditNoteDetail> {
  return postJson<PurchaseCreditNoteDetail>('/api/purchase-credit-notes', request)
}

export function postPurchaseCreditNote(id: string): Promise<PurchaseCreditNoteDetail> {
  return postJson<PurchaseCreditNoteDetail>(`/api/purchase-credit-notes/${id}/post`, {})
}
