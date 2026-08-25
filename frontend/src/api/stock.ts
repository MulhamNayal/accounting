import { getJson, postJson } from './client'

export interface ItemSummary {
  id: string
  code: string
  name: string
  baseUom: string
  inventoryAccountId: string
  costOfSalesAccountId: string
  isActive: boolean
}

export interface StockOnHand {
  itemId: string
  itemCode: string
  itemName: string
  baseUom: string
  quantityOnHand: number
  valueOnHand: number
  /** Null when nothing is on hand. A FIFO design can always report an average. */
  averageUnitCost: number | null
}

export interface CostLayerDetail {
  id: string
  sequence: number
  receivedOn: string
  quantityReceived: number
  /** Derived from the consumptions, never stored. */
  quantityRemaining: number
  unitCost: number
  valueRemaining: number
  /** Set when this layer revises another's cost rather than being a fresh receipt. */
  adjustsLayerId: string | null
}

export interface ConsumptionDetail {
  costLayerId: string
  layerSequence: number
  quantity: number
  unitCost: number
  amount: number
}

export interface StockIssueResult {
  moveId: string
  journalEntryId: string | null
  quantity: number
  totalCost: number
  /** Which receipts the cost came from — what makes it explainable. */
  consumed: ConsumptionDetail[]
}

export interface CostAdjustmentResult {
  newLayerId: string
  journalEntryId: string
  difference: number
  quantityStillOnHand: number
  quantityAlreadySold: number
  inventoryAdjustment: number
  costOfSalesAdjustment: number
}

export interface StockMoveSummary {
  id: string
  itemId: string
  itemCode: string
  itemName: string
  direction: 'In' | 'Out'
  quantity: number
  movedOn: string
  sourceDocumentType: string
  journalEntryId: string | null
  description: string | null
}

export function getItems(): Promise<ItemSummary[]> {
  return getJson<ItemSummary[]>('/api/items')
}

export function createItem(request: {
  code: string
  name: string
  baseUom: string
  inventoryAccountId: string
  costOfSalesAccountId: string
}): Promise<ItemSummary> {
  return postJson<ItemSummary>('/api/items', request)
}

export function getOnHand(entityId: string): Promise<StockOnHand[]> {
  return getJson<StockOnHand[]>(`/api/stock/on-hand?entityId=${entityId}`)
}

export function getLayers(entityId: string, itemId: string): Promise<CostLayerDetail[]> {
  return getJson<CostLayerDetail[]>(`/api/stock/layers?entityId=${entityId}&itemId=${itemId}`)
}

export function getMoves(entityId: string, itemId?: string): Promise<StockMoveSummary[]> {
  const suffix = itemId ? `&itemId=${itemId}` : ''
  return getJson<StockMoveSummary[]>(`/api/stock/moves?entityId=${entityId}${suffix}`)
}

export function receiveStock(request: {
  legalEntityId: string
  itemId: string
  quantity: number
  unitCost: number
  movedOn: string
  creditAccountId: string
  description?: string
}): Promise<StockMoveSummary> {
  return postJson<StockMoveSummary>('/api/stock/receive', request)
}

export function issueStock(request: {
  legalEntityId: string
  itemId: string
  quantity: number
  movedOn: string
  description?: string
}): Promise<StockIssueResult> {
  return postJson<StockIssueResult>('/api/stock/issue', request)
}

export function adjustCost(request: {
  legalEntityId: string
  costLayerId: string
  correctedUnitCost: number
  adjustedOn: string
  counterAccountId: string
  reason?: string
}): Promise<CostAdjustmentResult> {
  return postJson<CostAdjustmentResult>('/api/stock/adjust-cost', request)
}
