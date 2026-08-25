import {
  Badge,
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
import type { TrialBalance } from '../api/journalEntries'
import { accountTypeColor, useLayoutStyles } from '../theme'
import type { AccountTypeName } from '../theme'

const useStyles = makeStyles({
  mono: { fontFamily: tokens.fontFamilyMonospace },
  right: { textAlign: 'right' },
  totalRow: {
    fontWeight: tokens.fontWeightSemibold,
    borderTop: `2px solid ${tokens.colorNeutralStroke1}`,
  },
})

export function TrialBalancePage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [data, setData] = useState<TrialBalance | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!entityId) return
    setLoading(true)
    getTrialBalance(entityId)
      .then(setData)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  if (!entityId) {
    return (
      <div className={layout.page}>
        <Title3>Trial balance</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  if (loading) {
    return <div className={layout.page}><Spinner label="Computing…" /></div>
  }

  if (error || !data) {
    return (
      <div className={layout.page}>
        <Title3>Trial balance</Title3>
        <MessageBar intent="error">
          <MessageBarBody>{error ?? 'No data.'}</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Trial balance</Title3>
        <Caption1 className={layout.subtle}>
          As at {data.asOf}. Computed from postings — nothing is stored, so there is nothing
          to reconcile and nothing that can go stale.
        </Caption1>
      </div>

      <MessageBar intent={data.isBalanced ? 'success' : 'error'}>
        <MessageBarBody>
          {data.isBalanced
            ? `Balanced — debits and credits both total ${formatMoney(data.totalDebit)}.`
            : `Out of balance by ${formatMoney(data.totalDebit - data.totalCredit)}. `
              + 'This should be impossible; the database enforces it at commit.'}
        </MessageBarBody>
      </MessageBar>

      <Card>
        <Table size="small" aria-label="Trial balance">
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Code</TableHeaderCell>
              <TableHeaderCell>Account</TableHeaderCell>
              <TableHeaderCell>Type</TableHeaderCell>
              <TableHeaderCell className={styles.right}>Debit</TableHeaderCell>
              <TableHeaderCell className={styles.right}>Credit</TableHeaderCell>
              <TableHeaderCell className={styles.right}>Balance</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.lines.length === 0 && (
              <TableRow>
                <TableCell colSpan={6}>
                  <Caption1 className={layout.subtle}>
                    Nothing posted yet — post a journal entry and it will appear here.
                  </Caption1>
                </TableCell>
              </TableRow>
            )}

            {data.lines.map((line) => (
              <TableRow key={line.accountId}>
                <TableCell className={styles.mono}>{line.accountCode}</TableCell>
                <TableCell>{line.accountName}</TableCell>
                <TableCell>
                  <Badge
                    appearance="tint"
                    color={accountTypeColor[line.accountType as AccountTypeName]}
                  >
                    {line.accountType}
                  </Badge>
                </TableCell>
                <TableCell className={`${styles.right} ${styles.mono}`}>
                  {line.debit ? formatMoney(line.debit) : ''}
                </TableCell>
                <TableCell className={`${styles.right} ${styles.mono}`}>
                  {line.credit ? formatMoney(line.credit) : ''}
                </TableCell>
                <TableCell className={`${styles.right} ${styles.mono}`}>
                  {formatMoney(line.balance)}
                </TableCell>
              </TableRow>
            ))}

            {data.lines.length > 0 && (
              <TableRow className={styles.totalRow}>
                <TableCell colSpan={3}>Total</TableCell>
                <TableCell className={`${styles.right} ${styles.mono}`}>
                  {formatMoney(data.totalDebit)}
                </TableCell>
                <TableCell className={`${styles.right} ${styles.mono}`}>
                  {formatMoney(data.totalCredit)}
                </TableCell>
                <TableCell />
              </TableRow>
            )}
          </TableBody>
        </Table>
      </Card>
    </div>
  )
}
