import { getJson, postJson } from './client'

export interface CustomerSummary {
  id: string
  code: string
  name: string
  taxId: string | null
  currencyCode: string
  creditTermDays: number
  isActive: boolean
}

export interface SalesInvoiceSummary {
  id: string
  /** Null while a draft — a number is only taken at posting. */
  docNo: string | null
  docDate: string
  dueDate: string
  customerName: string
  currencyCode: string
  /** Net of tax. */
  total: number
  taxTotal: number
  /** What the customer owes — the figure that matters for settlement. */
  totalWithTax: number
  state: 'Draft' | 'Posted'
  journalEntryId: string | null
}

export interface SalesInvoiceLineDetail {
  id: string
  lineNo: number
  description: string
  quantity: number
  unitPrice: number
  lineTotal: number
  revenueAccountId: string
  revenueAccountCode: string
  revenueAccountName: string
  /** Null means outside the tax regime — not the same as zero-rated. */
  taxCodeId: string | null
  taxCodeName: string | null
  taxRate: number
  taxAmount: number
}

export interface SalesInvoiceDetail {
  id: string
  docNo: string | null
  docDate: string
  dueDate: string
  customerId: string
  customerCode: string
  customerName: string
  currencyCode: string
  fxRate: number
  reference: string | null
  memo: string | null
  state: 'Draft' | 'Posted'
  journalEntryId: string | null
  total: number
  taxTotal: number
  totalWithTax: number
  lines: SalesInvoiceLineDetail[]
}

export interface CreateSalesInvoiceLineRequest {
  description: string
  quantity: number
  unitPrice: number
  revenueAccountId: string
  taxCodeId?: string
}

export interface CreateSalesInvoiceRequest {
  legalEntityId: string
  customerId: string
  docDate: string
  lines: CreateSalesInvoiceLineRequest[]
  dueDate?: string
  currencyCode?: string
  fxRate?: number
  reference?: string
  memo?: string
}

export function getCustomers(): Promise<CustomerSummary[]> {
  return getJson<CustomerSummary[]>('/api/customers')
}

export function getSalesInvoices(entityId: string): Promise<SalesInvoiceSummary[]> {
  return getJson<SalesInvoiceSummary[]>(`/api/sales-invoices?entityId=${entityId}`)
}

export function getSalesInvoice(id: string): Promise<SalesInvoiceDetail> {
  return getJson<SalesInvoiceDetail>(`/api/sales-invoices/${id}`)
}

export function createSalesInvoice(request: CreateSalesInvoiceRequest): Promise<SalesInvoiceDetail> {
  return postJson<SalesInvoiceDetail>('/api/sales-invoices', request)
}

export function postSalesInvoice(id: string): Promise<SalesInvoiceDetail> {
  return postJson<SalesInvoiceDetail>(`/api/sales-invoices/${id}/post`, {})
}
