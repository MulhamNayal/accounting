import {
  Badge,
  Body1,
  Caption1,
  Spinner,
  Subtitle1,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { useEffect, useState } from 'react'
import { getAccounts } from './api/accounts'
import type { AccountSummary } from './api/accounts'
import { getEntities } from './api/entities'
import type { LegalEntitySummary } from './api/entities'

const useStyles = makeStyles({
  page: {
    maxWidth: '1100px',
    margin: '0 auto',
    padding: '32px 24px 64px',
    display: 'flex',
    flexDirection: 'column',
    gap: '32px',
  },
  header: { display: 'flex', flexDirection: 'column', gap: '4px' },
  section: { display: 'flex', flexDirection: 'column', gap: '12px' },
  card: {
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    overflowX: 'auto',
  },
  indent: { paddingLeft: '24px' },
  muted: { color: tokens.colorNeutralForeground3 },
  error: { color: tokens.colorPaletteRedForeground1 },
})

function App() {
  const styles = useStyles()
  const [entities, setEntities] = useState<LegalEntitySummary[]>([])
  const [accounts, setAccounts] = useState<AccountSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([getEntities(), getAccounts()])
      .then(([loadedEntities, loadedAccounts]) => {
        setEntities(loadedEntities)
        setAccounts(loadedAccounts)
      })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : String(err)))
      .finally(() => setLoading(false))
  }, [])

  if (loading) {
    return (
      <div className={styles.page}>
        <Spinner label="Loading…" />
      </div>
    )
  }

  if (error) {
    return (
      <div className={styles.page}>
        <Title2>ClearWise</Title2>
        <Body1 className={styles.error}>Could not reach the API: {error}</Body1>
        <Caption1 className={styles.muted}>
          Start it with: dotnet run --urls http://localhost:5100
        </Caption1>
      </div>
    )
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <Title2>ClearWise</Title2>
        <Caption1 className={styles.muted}>
          Layer 0 — tenancy, entities and the chart of accounts. Nothing is posted yet.
        </Caption1>
      </div>

      <section className={styles.section}>
        <Subtitle1>Entities</Subtitle1>
        <Caption1 className={styles.muted}>
          Separate books, one tenant. Each keeps its own financial year and tax identity.
        </Caption1>
        <div className={styles.card}>
          <Table size="small" aria-label="Entities">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Code</TableHeaderCell>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Currency</TableHeaderCell>
                <TableHeaderCell>FY starts</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {entities.map((entity) => (
                <TableRow key={entity.id}>
                  <TableCell>{entity.code}</TableCell>
                  <TableCell>{entity.name}</TableCell>
                  <TableCell>{entity.functionalCurrency}</TableCell>
                  <TableCell>{monthName(entity.financialYearStartMonth)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </section>

      <section className={styles.section}>
        <Subtitle1>Chart of accounts</Subtitle1>
        <Caption1 className={styles.muted}>
          Shared across both entities, which is what makes consolidation a sum rather than a
          mapping exercise. Only leaf accounts can be posted to.
        </Caption1>
        <div className={styles.card}>
          <Table size="small" aria-label="Chart of accounts">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Code</TableHeaderCell>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Type</TableHeaderCell>
                <TableHeaderCell>Normal balance</TableHeaderCell>
                <TableHeaderCell>Control</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {accounts.map((account) => (
                <TableRow key={account.id}>
                  <TableCell className={account.isPostable ? styles.indent : undefined}>
                    {account.code}
                  </TableCell>
                  <TableCell className={account.isPostable ? undefined : styles.muted}>
                    {account.name}
                    {!account.isPostable && ' (heading)'}
                  </TableCell>
                  <TableCell>{account.accountType}</TableCell>
                  <TableCell>{account.normalBalance}</TableCell>
                  <TableCell>
                    {account.controlType === 'None' ? (
                      <span className={styles.muted}>—</span>
                    ) : (
                      <Badge appearance="tint" color="informative">
                        {account.controlType}
                      </Badge>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </section>
    </div>
  )
}

function monthName(month: number): string {
  return new Date(2000, month - 1, 1).toLocaleString('en-GB', { month: 'long' })
}

export default App
