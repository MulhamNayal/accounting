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
import { getBalanceSheet } from '../api/financialStatements'
import type { BalanceSheet, FinancialStatementSection } from '../api/financialStatements'
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
  grandTotalRow: {
    fontWeight: tokens.fontWeightSemibold,
    borderTop: `2px solid ${tokens.colorNeutralStroke1}`,
  },
  derived: { color: tokens.colorNeutralForeground3 },
})

export function BalanceSheetPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [asOf, setAsOf] = useState(todayLocal())
  const [data, setData] = useState<BalanceSheet | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    if (!entityId) return
    setLoading(true)
    setError(null)
    getBalanceSheet(entityId, asOf)
      .then(setData)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId, asOf])

  useEffect(load, [load])

  if (!entityId) {
    return (
      <div className={layout.page}>
        <Title3>Balance sheet</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  const section = (s: FinancialStatementSection) => (
    <>
      <TableRow className={styles.sectionRow}>
        <TableCell colSpan={3}>{s.title}</TableCell>
      </TableRow>

      {s.lines.length === 0 && (
        <TableRow>
          <TableCell colSpan={3}>
            <Caption1 className={layout.subtle}>Nothing at this date.</Caption1>
          </TableCell>
        </TableRow>
      )}

      {s.lines.map((line) => (
        <TableRow key={line.accountId}>
          <TableCell className={styles.mono}>{line.accountCode}</TableCell>
          <TableCell>{line.accountName}</TableCell>
          <TableCell className={`${styles.right} ${styles.mono}`}>
            {formatMoney(line.amount)}
          </TableCell>
        </TableRow>
      ))}
    </>
  )

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Balance sheet</Title3>
        <Caption1 className={layout.subtle}>
          Computed from postings on every request. Retained earnings are derived rather than
          held in an account, because there is no year-end close yet.
        </Caption1>
      </div>

      <Toolbar aria-label="Date">
        <Field label="As at">
          <Input type="date" value={asOf} onChange={(_, d) => setAsOf(d.value)} />
        </Field>
      </Toolbar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {loading && <Spinner label="Computing…" />}

      {!loading && data && (
        <>
          <MessageBar intent={data.isBalanced ? 'success' : 'error'}>
            <MessageBarBody>
              {data.isBalanced
                ? `Balanced — assets and the other side both total ${formatMoney(data.assets.total)} ${data.currencyCode}.`
                : `Out of balance by ${formatMoney(data.assets.total - data.totalLiabilitiesAndEquity)}. `
                  + 'This should be impossible: every entry balances at commit, so either an '
                  + 'entry escaped that or this calculation is wrong.'}
            </MessageBarBody>
          </MessageBar>

          <Card>
            <Table size="small" aria-label="Balance sheet">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Code</TableHeaderCell>
                  <TableHeaderCell>Account</TableHeaderCell>
                  <TableHeaderCell className={styles.right}>{data.currencyCode}</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {section(data.assets)}
                <TableRow className={styles.grandTotalRow}>
                  <TableCell colSpan={2}>Total assets</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {formatMoney(data.assets.total)}
                  </TableCell>
                </TableRow>

                {section(data.liabilities)}
                <TableRow className={styles.subtotalRow}>
                  <TableCell colSpan={2}>Total liabilities</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {formatMoney(data.liabilities.total)}
                  </TableCell>
                </TableRow>

                {section(data.equity)}

                {/* Both of these are computed from profit and loss balances rather than read
                    from an account, which is worth saying on the face of the statement. */}
                <TableRow>
                  <TableCell />
                  <TableCell className={styles.derived}>
                    Retained earnings brought forward
                  </TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {formatMoney(data.retainedEarningsBroughtForward)}
                  </TableCell>
                </TableRow>
                <TableRow>
                  <TableCell />
                  <TableCell className={styles.derived}>Result for the period</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {formatMoney(data.resultForThePeriod)}
                  </TableCell>
                </TableRow>

                <TableRow className={styles.subtotalRow}>
                  <TableCell colSpan={2}>Total equity</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {formatMoney(data.totalEquity)}
                  </TableCell>
                </TableRow>

                <TableRow className={styles.grandTotalRow}>
                  <TableCell colSpan={2}>Total liabilities and equity</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {formatMoney(data.totalLiabilitiesAndEquity)}
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </Card>
        </>
      )}
    </div>
  )
}
