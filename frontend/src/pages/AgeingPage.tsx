import {
  Caption1,
  Card,
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
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { useEffect, useState } from 'react'
import { formatMoney, getTrialBalance } from '../api/journalEntries'
import { getAgeing } from '../api/receivables'
import type { AgeingReport } from '../api/receivables'
import { useLayoutStyles } from '../theme'

const useStyles = makeStyles({
  mono: { fontFamily: tokens.fontFamilyMonospace },
  right: { textAlign: 'right' },
  overdue: { color: tokens.colorPaletteRedForeground1 },
  totalRow: {
    fontWeight: tokens.fontWeightSemibold,
    borderTop: `2px solid ${tokens.colorNeutralStroke1}`,
  },
})

export function AgeingPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [report, setReport] = useState<AgeingReport | null>(null)
  const [controlBalance, setControlBalance] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!entityId) return
    setLoading(true)
    // Both come from the same postings, so fetching them together lets the page prove the
    // subledger ties to the ledger rather than merely asserting it.
    Promise.all([getAgeing(entityId), getTrialBalance(entityId)])
      .then(([ageing, trialBalance]) => {
        setReport(ageing)
        const receivables = trialBalance.lines.filter((l) => l.accountType === 'Asset')
        setControlBalance(
          receivables.reduce(
            (sum, l) => (l.accountName.toLowerCase().includes('receivable') ? sum + l.balance : sum),
            0,
          ),
        )
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  if (!entityId) {
    return (
      <div className={layout.page}>
        <Title3>Ageing</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  if (loading) return <div className={layout.page}><Spinner label="Computing…" /></div>

  if (error || !report) {
    return (
      <div className={layout.page}>
        <Title3>Ageing</Title3>
        <MessageBar intent="error">
          <MessageBarBody>{error ?? 'No data.'}</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  const ties = controlBalance !== null && Math.abs(controlBalance - report.total) < 0.005

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Ageing</Title3>
        <Caption1 className={layout.subtle}>
          What each customer owes as at {report.asOf}, derived from receivables postings.
          There is no stored balance anywhere — nothing to reconcile.
        </Caption1>
      </div>

      {controlBalance !== null && (
        <MessageBar intent={ties ? 'success' : 'error'}>
          <MessageBarBody>
            {ties
              ? `Ties to the ledger — the receivables control account also shows ${formatMoney(report.total)}.`
              : `Does not tie: ageing shows ${formatMoney(report.total)} but the control account `
                + `shows ${formatMoney(controlBalance)}. This should be impossible, because both `
                + 'are the same postings summed differently. Treat it as a defect.'}
          </MessageBarBody>
        </MessageBar>
      )}

      <Card>
        <Table size="small" aria-label="Ageing">
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Customer</TableHeaderCell>
              <TableHeaderCell className={styles.right}>Not yet due</TableHeaderCell>
              <TableHeaderCell className={styles.right}>1–30</TableHeaderCell>
              <TableHeaderCell className={styles.right}>31–60</TableHeaderCell>
              <TableHeaderCell className={styles.right}>61–90</TableHeaderCell>
              <TableHeaderCell className={styles.right}>90+</TableHeaderCell>
              <TableHeaderCell className={styles.right}>Total</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {report.customers.length === 0 && (
              <TableRow>
                <TableCell colSpan={7}>
                  <Caption1 className={layout.subtle}>
                    Nothing outstanding. Post an invoice and it will appear here.
                  </Caption1>
                </TableCell>
              </TableRow>
            )}

            {report.customers.map((customer) => (
              <TableRow key={customer.customerId}>
                <TableCell>
                  <span className={styles.mono}>{customer.customerCode}</span>{' '}
                  {customer.customerName}
                </TableCell>
                <TableCell className={`${styles.right} ${styles.mono}`}>
                  {customer.current ? formatMoney(customer.current) : ''}
                </TableCell>
                <TableCell className={`${styles.right} ${styles.mono}`}>
                  {customer.days1To30 ? formatMoney(customer.days1To30) : ''}
                </TableCell>
                <TableCell className={`${styles.right} ${styles.mono}`}>
                  {customer.days31To60 ? formatMoney(customer.days31To60) : ''}
                </TableCell>
                <TableCell className={`${styles.right} ${styles.mono} ${styles.overdue}`}>
                  {customer.days61To90 ? formatMoney(customer.days61To90) : ''}
                </TableCell>
                <TableCell className={`${styles.right} ${styles.mono} ${styles.overdue}`}>
                  {customer.over90 ? formatMoney(customer.over90) : ''}
                </TableCell>
                <TableCell className={`${styles.right} ${styles.mono}`}>
                  {formatMoney(customer.balance)}
                </TableCell>
              </TableRow>
            ))}

            {report.customers.length > 0 && (
              <TableRow className={styles.totalRow}>
                <TableCell>Total</TableCell>
                <TableCell colSpan={5} />
                <TableCell className={`${styles.right} ${styles.mono}`}>
                  {formatMoney(report.total)}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </Card>
    </div>
  )
}
