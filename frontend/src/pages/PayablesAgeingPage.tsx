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
import { formatMoney } from '../api/journalEntries'
import { getPayablesAgeing } from '../api/payables'
import type { PayablesAgeingReport } from '../api/payables'
import { useLayoutStyles } from '../theme'

const useStyles = makeStyles({
  mono: { fontFamily: tokens.fontFamilyMonospace },
  right: { textAlign: 'right' },
  totalRow: {
    fontWeight: tokens.fontWeightSemibold,
    borderTop: `2px solid ${tokens.colorNeutralStroke1}`,
  },
  overdue: { color: tokens.colorPaletteRedForeground1 },
})

export function PayablesAgeingPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [asOf, setAsOf] = useState(todayLocal)
  const [data, setData] = useState<PayablesAgeingReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    if (!entityId) return
    setLoading(true)
    setError(null)
    getPayablesAgeing(entityId, asOf)
      .then(setData)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId, asOf])

  useEffect(load, [load])

  if (!entityId) {
    return (
      <div className={layout.page}>
        <Title3>Payables ageing</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Payables ageing</Title3>
        <Caption1 className={layout.subtle}>
          What is owed to each supplier, by how overdue it is. The total equals the payables
          control account exactly — both are the same postings, summed differently, so there is
          nothing here to reconcile.
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
        <Card>
          <Table size="small" aria-label="Payables ageing">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Code</TableHeaderCell>
                <TableHeaderCell>Supplier</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Not yet due</TableHeaderCell>
                <TableHeaderCell className={styles.right}>1–30</TableHeaderCell>
                <TableHeaderCell className={styles.right}>31–60</TableHeaderCell>
                <TableHeaderCell className={styles.right}>61–90</TableHeaderCell>
                <TableHeaderCell className={styles.right}>90+</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Balance</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.rows.length === 0 && (
                <TableRow>
                  <TableCell colSpan={8}>
                    <Caption1 className={layout.subtle}>
                      Nothing outstanding. Post a bill and it will appear here.
                    </Caption1>
                  </TableCell>
                </TableRow>
              )}

              {data.rows.map((row) => (
                <TableRow key={row.supplierId}>
                  <TableCell className={styles.mono}>{row.supplierCode}</TableCell>
                  <TableCell>{row.supplierName}</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {row.notYetDue ? formatMoney(row.notYetDue) : ''}
                  </TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {row.days1To30 ? formatMoney(row.days1To30) : ''}
                  </TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {row.days31To60 ? formatMoney(row.days31To60) : ''}
                  </TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {row.days61To90 ? formatMoney(row.days61To90) : ''}
                  </TableCell>
                  <TableCell
                    className={`${styles.right} ${styles.mono} ${row.over90 ? styles.overdue : ''}`}
                  >
                    {row.over90 ? formatMoney(row.over90) : ''}
                  </TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {formatMoney(row.balance)}
                  </TableCell>
                </TableRow>
              ))}

              {data.rows.length > 0 && (
                <TableRow className={styles.totalRow}>
                  <TableCell colSpan={7}>Total owed</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {formatMoney(data.totalOutstanding)}
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
  )
}
