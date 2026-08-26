import { getJson } from './client'
import { todayLocal } from './dates'

export interface FinancialStatementLine {
  accountId: string
  accountCode: string
  accountName: string
  amount: number
}

export interface FinancialStatementSection {
  title: string
  lines: FinancialStatementLine[]
  total: number
}

export interface ProfitAndLoss {
  from: string
  to: string
  currencyCode: string
  income: FinancialStatementSection
  expenses: FinancialStatementSection
  netProfit: number
}

export interface BalanceSheet {
  asOf: string
  currencyCode: string
  assets: FinancialStatementSection
  liabilities: FinancialStatementSection
  equity: FinancialStatementSection
  retainedEarningsBroughtForward: number
  resultForThePeriod: number
  totalEquity: number
  totalLiabilitiesAndEquity: number
  isBalanced: boolean
}

/**
 * Dates are always sent explicitly. Letting the server fall back to UTC today means that
 * east of Greenwich it is yesterday for part of every day, and a statement dated yesterday
 * silently omits everything posted today.
 */
export function getProfitAndLoss(
  entityId: string,
  from: string,
  to: string = todayLocal(),
): Promise<ProfitAndLoss> {
  return getJson<ProfitAndLoss>(
    `/api/profit-and-loss?entityId=${entityId}&from=${from}&to=${to}`,
  )
}

export function getBalanceSheet(
  entityId: string,
  asOf: string = todayLocal(),
): Promise<BalanceSheet> {
  return getJson<BalanceSheet>(`/api/balance-sheet?entityId=${entityId}&asOf=${asOf}`)
}

/** First of January in the same year as the given date, as the default period start. */
export function startOfYear(date: string = todayLocal()): string {
  return `${date.slice(0, 4)}-01-01`
}
