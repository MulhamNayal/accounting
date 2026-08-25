import {
  Badge,
  Button,
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
  Toolbar,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import {
  AddRegular,
  CheckmarkCircleRegular,
  ChevronDownRegular,
  ChevronRightRegular,
} from '@fluentui/react-icons'
import { Fragment, useCallback, useEffect, useState } from 'react'
import { getAccounts } from '../api/accounts'
import type { AccountSummary } from '../api/accounts'
import { formatMoney } from '../api/journalEntries'
import {
  getCustomers,
  getSalesInvoice,
  getSalesInvoices,
  postSalesInvoice,
} from '../api/salesInvoices'
import type {
  CustomerSummary,
  SalesInvoiceDetail,
  SalesInvoiceSummary,
} from '../api/salesInvoices'
import { NewInvoiceDialog } from '../components/NewInvoiceDialog'
import { useLayoutStyles } from '../theme'

const useStyles = makeStyles({
  mono: { fontFamily: tokens.fontFamilyMonospace },
  right: { textAlign: 'right' },
  lineRow: { backgroundColor: tokens.colorNeutralBackground2 },
  actions: { display: 'flex', gap: tokens.spacingHorizontalXS, justifyContent: 'flex-end' },
})

export function InvoicesPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [invoices, setInvoices] = useState<SalesInvoiceSummary[]>([])
  const [customers, setCustomers] = useState<CustomerSummary[]>([])
  const [accounts, setAccounts] = useState<AccountSummary[]>([])
  const [expanded, setExpanded] = useState<Record<string, SalesInvoiceDetail>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showNew, setShowNew] = useState(false)
  const [posting, setPosting] = useState<string | null>(null)

  const load = useCallback(() => {
    if (!entityId) return
    setLoading(true)
    Promise.all([getSalesInvoices(entityId), getCustomers(), getAccounts()])
      .then(([loadedInvoices, loadedCustomers, loadedAccounts]) => {
        setInvoices(loadedInvoices)
        setCustomers(loadedCustomers)
        setAccounts(loadedAccounts)
        setExpanded({})
        setError(null)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  useEffect(load, [load])

  const toggle = async (invoice: SalesInvoiceSummary) => {
    if (expanded[invoice.id]) {
      setExpanded((current) => {
        const next = { ...current }
        delete next[invoice.id]
        return next
      })
      return
    }
    const detail = await getSalesInvoice(invoice.id)
    setExpanded((current) => ({ ...current, [invoice.id]: detail }))
  }

  const post = async (invoice: SalesInvoiceSummary) => {
    setPosting(invoice.id)
    setError(null)
    try {
      await postSalesInvoice(invoice.id)
      load()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPosting(null)
    }
  }

  if (!entityId) {
    return (
      <div className={layout.page}>
        <Title3>Invoices</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  const drafts = invoices.filter((i) => i.state === 'Draft').length

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Invoices</Title3>
        <Caption1 className={layout.subtle}>
          A draft has no number and is not in the books. Posting takes the next number from a
          gapless series and writes the journal entry — after which the invoice cannot change.
        </Caption1>
      </div>

      <Toolbar aria-label="Invoice actions">
        <Button icon={<AddRegular />} appearance="primary" onClick={() => setShowNew(true)}>
          New invoice
        </Button>
        <div className={layout.spacer} />
        <Caption1 className={layout.subtle}>
          {invoices.length} invoices{drafts > 0 && `, ${drafts} unposted`}
        </Caption1>
      </Toolbar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {loading ? (
        <Spinner label="Loading invoices…" />
      ) : (
        <Card>
          <Table size="small" aria-label="Invoices">
            <TableHeader>
              <TableRow>
                <TableHeaderCell style={{ width: '40px' }} />
                <TableHeaderCell>Number</TableHeaderCell>
                <TableHeaderCell>Date</TableHeaderCell>
                <TableHeaderCell>Customer</TableHeaderCell>
                <TableHeaderCell>Due</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Net</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Tax</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Total</TableHeaderCell>
                <TableHeaderCell>State</TableHeaderCell>
                <TableHeaderCell />
              </TableRow>
            </TableHeader>
            <TableBody>
              {invoices.length === 0 && (
                <TableRow>
                  <TableCell colSpan={10}>
                    <Caption1 className={layout.subtle}>
                      No invoices yet. Create one with “New invoice”.
                    </Caption1>
                  </TableCell>
                </TableRow>
              )}

              {invoices.map((invoice) => (
                <Fragment key={invoice.id}>
                  <TableRow>
                    <TableCell>
                      <Button
                        appearance="subtle"
                        size="small"
                        aria-label="Show lines"
                        icon={expanded[invoice.id] ? <ChevronDownRegular /> : <ChevronRightRegular />}
                        onClick={() => void toggle(invoice)}
                      />
                    </TableCell>
                    <TableCell className={styles.mono}>
                      {invoice.docNo ?? <span className={layout.subtle}>— not yet issued</span>}
                    </TableCell>
                    <TableCell>{invoice.docDate}</TableCell>
                    <TableCell>{invoice.customerName}</TableCell>
                    <TableCell>{invoice.dueDate}</TableCell>
                    <TableCell className={`${styles.right} ${styles.mono}`}>
                      {formatMoney(invoice.total)}
                    </TableCell>
                    <TableCell className={`${styles.right} ${styles.mono}`}>
                      {invoice.taxTotal ? formatMoney(invoice.taxTotal) : ''}
                    </TableCell>
                    <TableCell className={`${styles.right} ${styles.mono}`}>
                      {invoice.currencyCode} {formatMoney(invoice.totalWithTax)}
                    </TableCell>
                    <TableCell>
                      <Badge
                        appearance="tint"
                        color={invoice.state === 'Posted' ? 'success' : 'warning'}
                      >
                        {invoice.state}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <div className={styles.actions}>
                        {invoice.state === 'Draft' ? (
                          <Tooltip
                            content="Post — assigns a number and writes the journal entry. One way."
                            relationship="label"
                          >
                            <Button
                              appearance="subtle"
                              size="small"
                              icon={<CheckmarkCircleRegular />}
                              disabled={posting === invoice.id}
                              onClick={() => void post(invoice)}
                            >
                              {posting === invoice.id ? 'Posting…' : 'Post'}
                            </Button>
                          </Tooltip>
                        ) : (
                          <Caption1 className={layout.subtle}>posted</Caption1>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>

                  {expanded[invoice.id]?.lines.map((line) => (
                    <TableRow key={line.id} className={styles.lineRow}>
                      <TableCell />
                      <TableCell className={styles.mono}>{line.revenueAccountCode}</TableCell>
                      <TableCell colSpan={2}>{line.description}</TableCell>
                      <TableCell className={styles.mono}>
                        {line.quantity} × {formatMoney(line.unitPrice)}
                      </TableCell>
                      <TableCell className={`${styles.right} ${styles.mono}`}>
                        {formatMoney(line.lineTotal)}
                      </TableCell>
                      <TableCell className={`${styles.right} ${styles.mono}`}>
                        {line.taxAmount ? formatMoney(line.taxAmount) : ''}
                      </TableCell>
                      <TableCell colSpan={3}>
                        <Caption1 className={layout.subtle}>
                          {line.revenueAccountName}
                          {line.taxCodeName && ` · ${line.taxCodeName}`}
                        </Caption1>
                      </TableCell>
                    </TableRow>
                  ))}
                </Fragment>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}

      <NewInvoiceDialog
        open={showNew}
        onOpenChange={setShowNew}
        entityId={entityId}
        customers={customers}
        accounts={accounts}
        onCreated={load}
      />
    </div>
  )
}
