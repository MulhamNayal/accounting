import {
  Caption1,
  Card,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Title3,
  Toolbar,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { useCallback, useEffect, useState } from 'react'
import { todayLocal } from '../api/dates'
import { getProfitAndLoss, startOfYear } from '../api/financialStatements'
import type { FinancialStatementSection, ProfitAndLoss } from '../api/financialStatements'
import { formatMoney } from '../api/journalEntries'
import { useLayoutStyles } from '../theme'

const useStyles = makeStyles({
  mono: { fontFamily: tokens.fontFamilyMonospace },
  right: { textAlign: 'right' },
  sectionRow: {
    fontWeight: tokens.fontWeightSemibold,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  subtotalRow: {
    fontWeight: tokens.fontWeightSemibold,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  resultRow: {
    fontWeight: tokens.fontWeightSemibold,
    borderTop: `2px solid ${tokens.colorNeutralStroke1}`,
  },
  dates: { display: 'flex', gap: tokens.spacingHorizontalM, alignItems: 'flex-end' },
})

export function ProfitAndLossPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [from, setFrom] = useState(startOfYear())
  const [to, setTo] = useState(todayLocal())
  const [data, setData] = useState<ProfitAndLoss | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    if (!entityId) return
    setLoading(true)
    setError(null)
    getProfitAndLoss(entityId, from, to)
      .then(setData)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId, from, to])

  useEffect(load, [load])

  if (!entityId) {
    return (
      <div className={layout.page}>
        <Title3>Profit and loss</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  const section = (s: FinancialStatementSection, sign: 1 | -1) => (
    <>
      <TableRow className={styles.sectionRow}>
        <TableCell colSpan={3}>{s.title}</TableCell>
      </TableRow>

      {s.lines.length === 0 && (
        <TableRow>
          <TableCell colSpan={3}>
            <Caption1 className={layout.subtle}>Nothing in this period.</Caption1>
          </TableCell>
        </TableRow>
      )}

      {s.lines.map((line) => (
        <TableRow key={line.accountId}>
          <TableCell className={styles.mono}>{line.accountCode}</TableCell>
          <TableCell>{line.accountName}</TableCell>
          <TableCell className={`${styles.right} ${styles.mono}`}>
            {formatMoney(line.amount * sign)}
          </TableCell>
        </TableRow>
      ))}

      <TableRow className={styles.subtotalRow}>
        <TableCell colSpan={2}>Total {s.title.toLowerCase()}</TableCell>
        <TableCell className={`${styles.right} ${styles.mono}`}>
          {formatMoney(s.total * sign)}
        </TableCell>
      </TableRow>
    </>
  )

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Profit and loss</Title3>
        <Caption1 className={layout.subtle}>
          Computed from postings on every request. There is no stored figure here, so nothing
          can disagree with the ledger or go stale.
        </Caption1>
      </div>

      <Toolbar aria-label="Period">
        <div className={styles.dates}>
          <Field label="From">
            <Input type="date" value={from} onChange={(_, d) => setFrom(d.value)} />
          </Field>
          <Field label="To">
            <Input type="date" value={to} onChange={(_, d) => setTo(d.value)} />
          </Field>
        </div>
      </Toolbar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {loading && <Spinner label="Computing…" />}

      {!loading && data && (
        <Card>
          <Table size="small" aria-label="Profit and loss">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Code</TableHeaderCell>
                <TableHeaderCell>Account</TableHeaderCell>
                <TableHeaderCell className={styles.right}>
                  {data.currencyCode}
                </TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {section(data.income, 1)}
              {/* Expenses are shown negative so the column adds up to the result rather than
                  asking the reader to subtract one subtotal from another. */}
              {section(data.expenses, -1)}

              <TableRow className={styles.resultRow}>
                <TableCell colSpan={2}>
                  {data.netProfit >= 0 ? 'Net profit' : 'Net loss'}
                </TableCell>
                <TableCell className={`${styles.right} ${styles.mono}`}>
                  {formatMoney(data.netProfit)}
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
  )
}
