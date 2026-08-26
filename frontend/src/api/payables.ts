import { getJson, postJson } from './client'
import { todayLocal } from './dates'

export interface SupplierSummary {
  id: string
  code: string
  name: string
  currencyCode: string
  creditTermDays: number
  isActive: boolean
}

export interface PurchaseInvoiceLineDetail {
  id: string
  lineNo: number
  description: string
  quantity: number
  unitPrice: number
  lineTotal: number
  chargeAccountId: string
  chargeAccountCode: string
  chargeAccountName: string
  taxCodeId: string | null
  taxCodeLabel: string | null
  taxRate: number
  taxAmount: number
  /** False when the regime treats input tax as final, in which case it lands in the cost. */
  taxReclaimable: boolean
  /** Net, plus any tax that could not be reclaimed. This is what reaches the charge account. */
  chargeAmount: number
}

export interface PurchaseInvoiceDetail {
  id: string
  docNo: string | null
  supplierInvoiceNo: string
  docDate: string
  dueDate: string
  supplierId: string
  supplierCode: string
  supplierName: string
  currencyCode: string
  fxRate: number
  memo: string | null
  state: 'Draft' | 'Posted'
  journalEntryId: string | null
  total: number
  taxTotal: number
  totalWithTax: number
  lines: PurchaseInvoiceLineDetail[]
}

export interface PurchaseInvoiceSummary {
  id: string
  docNo: string | null
  supplierInvoiceNo: string
  docDate: string
  dueDate: string
  supplierName: string
  currencyCode: string
  total: number
  taxTotal: number
  totalWithTax: number
  state: 'Draft' | 'Posted'
  journalEntryId: string | null
}

export interface CreatePurchaseInvoiceLine {
  description: string
  quantity: number
  unitPrice: number
  chargeAccountId: string
  taxCodeId?: string
  projectId?: string
}

export interface CreatePurchaseInvoiceRequest {
  legalEntityId: string
  supplierId: string
  supplierInvoiceNo: string
  docDate: string
  lines: CreatePurchaseInvoiceLine[]
  dueDate?: string
  currencyCode?: string
  fxRate?: number
  memo?: string
}

export interface PaymentSummary {
  id: string
  docNo: string | null
  paymentDate: string
  supplierName: string
  currencyCode: string
  amount: number
  allocated: number
  unallocated: number
  state: 'Draft' | 'Posted'
  journalEntryId: string | null
}

export interface CreatePaymentRequest {
  legalEntityId: string
  supplierId: string
  bankAccountId: string
  paymentDate: string
  amount: number
  currencyCode?: string
  fxRate?: number
  reference?: string
  memo?: string
}

export interface PaymentAllocationDetail {
  id: string
  supplierPaymentId: string
  paymentDocNo: string | null
  purchaseInvoiceId: string
  invoiceDocNo: string | null
  amount: number
  functionalAmount: number
  /** Non-zero when the bill and the payment were at different rates. Positive is a gain. */
  fxGainLossFunctional: number
  journalEntryId: string | null
  allocatedAtUtc: string
  reversesAllocationId: string | null
}

export interface OpenPurchaseInvoice {
  id: string
  docNo: string | null
  supplierInvoiceNo: string
  docDate: string
  dueDate: string
  currencyCode: string
  gross: number
  allocated: number
  outstanding: number
  daysOverdue: number
}

export interface SupplierBalance {
  supplierId: string
  supplierCode: string
  supplierName: string
  balance: number
  notYetDue: number
  days1To30: number
  days31To60: number
  days61To90: number
  over90: number
}

export interface PayablesAgeingReport {
  asOf: string
  rows: SupplierBalance[]
  totalOutstanding: number
}

export function getSuppliers(): Promise<SupplierSummary[]> {
  return getJson<SupplierSummary[]>('/api/suppliers')
}

export function getPurchaseInvoices(entityId: string): Promise<PurchaseInvoiceSummary[]> {
  return getJson<PurchaseInvoiceSummary[]>(`/api/purchase-invoices?entityId=${entityId}`)
}

export function createPurchaseInvoice(
  request: CreatePurchaseInvoiceRequest,
): Promise<PurchaseInvoiceDetail> {
  return postJson<PurchaseInvoiceDetail>('/api/purchase-invoices', request)
}

export function postPurchaseInvoice(id: string): Promise<PurchaseInvoiceDetail> {
  return postJson<PurchaseInvoiceDetail>(`/api/purchase-invoices/${id}/post`, {})
}

export function getPayments(entityId: string): Promise<PaymentSummary[]> {
  return getJson<PaymentSummary[]>(`/api/payments?entityId=${entityId}`)
}

export function createPayment(request: CreatePaymentRequest): Promise<PaymentSummary> {
  return postJson<PaymentSummary>('/api/payments', request)
}

export function postPayment(id: string): Promise<PaymentSummary> {
  return postJson<PaymentSummary>(`/api/payments/${id}/post`, {})
}

export function allocatePayment(
  paymentId: string,
  lines: { purchaseInvoiceId: string; amount: number }[],
): Promise<PaymentAllocationDetail[]> {
  return postJson<PaymentAllocationDetail[]>('/api/payment-allocations', { paymentId, lines })
}

export function unallocatePayment(id: string): Promise<PaymentAllocationDetail> {
  return postJson<PaymentAllocationDetail>(`/api/payment-allocations/${id}/unallocate`, {})
}

export function getOpenBills(
  entityId: string,
  supplierId?: string,
): Promise<OpenPurchaseInvoice[]> {
  const suffix = supplierId ? `&supplierId=${supplierId}` : ''
  return getJson<OpenPurchaseInvoice[]>(
    `/api/payables/open-invoices?entityId=${entityId}${suffix}`,
  )
}

/** The date is sent explicitly for the same reason as the trial balance — see getTrialBalance. */
export function getPayablesAgeing(
  entityId: string,
  asOf: string = todayLocal(),
): Promise<PayablesAgeingReport> {
  return getJson<PayablesAgeingReport>(
    `/api/payables/ageing?entityId=${entityId}&asOf=${asOf}`,
  )
}
