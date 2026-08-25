import { getJson } from './client'

/** Mirrors AccountSummary on the backend, field for field. */
export interface AccountSummary {
  id: string
  code: string
  name: string
  accountType: 'Asset' | 'Liability' | 'Equity' | 'Income' | 'Expense'
  parentId: string | null
  isPostable: boolean
  controlType: 'None' | 'AccountsReceivable' | 'AccountsPayable' | 'Stock' | 'Tax' | 'Bank'
  /** Derived on the server from accountType; never stored. */
  normalBalance: 'Debit' | 'Credit'
  isActive: boolean
}

export function getAccounts(): Promise<AccountSummary[]> {
  return getJson<AccountSummary[]>('/api/accounts')
}
