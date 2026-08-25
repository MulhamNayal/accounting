import {
  Badge,
  Button,
  Caption1,
  Card,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Dropdown,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Option,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  Title3,
  Toolbar,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { AddRegular, CheckmarkCircleRegular, LinkRegular } from '@fluentui/react-icons'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { getAccounts } from '../api/accounts'
import type { AccountSummary } from '../api/accounts'
import { formatMoney } from '../api/journalEntries'
import {
  allocate,
  createReceipt,
  getOpenInvoices,
  getReceipts,
  postReceipt,
} from '../api/receivables'
import type { OpenInvoice, ReceiptSummary } from '../api/receivables'
import { getCustomers } from '../api/salesInvoices'
import type { CustomerSummary } from '../api/salesInvoices'
import { useLayoutStyles } from '../theme'

const useStyles = makeStyles({
  mono: { fontFamily: tokens.fontFamilyMonospace },
  right: { textAlign: 'right' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  row: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'end' },
  grow: { flexGrow: 1, minWidth: '220px' },
  narrow: { width: '130px' },
  totals: {
    display: 'flex',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
})

export function ReceiptsPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [receipts, setReceipts] = useState<ReceiptSummary[]>([])
  const [customers, setCustomers] = useState<CustomerSummary[]>([])
  const [accounts, setAccounts] = useState<AccountSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showNew, setShowNew] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)
  const [allocating, setAllocating] = useState<ReceiptSummary | null>(null)

  const load = useCallback(() => {
    if (!entityId) return
    setLoading(true)
    Promise.all([getReceipts(entityId), getCustomers(), getAccounts()])
      .then(([r, c, a]) => {
        setReceipts(r)
        setCustomers(c)
        setAccounts(a)
        setError(null)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  useEffect(load, [load])

  const post = async (receipt: ReceiptSummary) => {
    setBusy(receipt.id)
    setError(null)
    try {
      await postReceipt(receipt.id)
      load()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(null)
    }
  }

  if (!entityId) {
    return (
      <div className={layout.page}>
        <Title3>Receipts</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Receipts</Title3>
        <Caption1 className={layout.subtle}>
          Receiving money and deciding which invoices it settles are two separate decisions,
          so they are two separate records. Post a receipt first, then allocate it.
        </Caption1>
      </div>

      <Toolbar aria-label="Receipt actions">
        <Button icon={<AddRegular />} appearance="primary" onClick={() => setShowNew(true)}>
          New receipt
        </Button>
        <div className={layout.spacer} />
        <Caption1 className={layout.subtle}>{receipts.length} receipts</Caption1>
      </Toolbar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {loading ? (
        <Spinner label="Loading receipts…" />
      ) : (
        <Card>
          <Table size="small" aria-label="Receipts">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Number</TableHeaderCell>
                <TableHeaderCell>Date</TableHeaderCell>
                <TableHeaderCell>Customer</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Amount</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Unallocated</TableHeaderCell>
                <TableHeaderCell>State</TableHeaderCell>
                <TableHeaderCell />
              </TableRow>
            </TableHeader>
            <TableBody>
              {receipts.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7}>
                    <Caption1 className={layout.subtle}>
                      No receipts yet. Record one with “New receipt”.
                    </Caption1>
                  </TableCell>
                </TableRow>
              )}

              {receipts.map((receipt) => (
                <TableRow key={receipt.id}>
                  <TableCell className={styles.mono}>
                    {receipt.docNo ?? <span className={layout.subtle}>— not yet issued</span>}
                  </TableCell>
                  <TableCell>{receipt.receiptDate}</TableCell>
                  <TableCell>{receipt.customerName}</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {receipt.currencyCode} {formatMoney(receipt.amount)}
                  </TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {receipt.unallocated > 0 ? (
                      <Text weight="semibold">{formatMoney(receipt.unallocated)}</Text>
                    ) : (
                      <span className={layout.subtle}>fully applied</span>
                    )}
                  </TableCell>
                  <TableCell>
                    <Badge
                      appearance="tint"
                      color={receipt.state === 'Posted' ? 'success' : 'warning'}
                    >
                      {receipt.state}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    {receipt.state === 'Draft' ? (
                      <Tooltip content="Post — writes the journal entry. One way." relationship="label">
                        <Button
                          appearance="subtle"
                          size="small"
                          icon={<CheckmarkCircleRegular />}
                          disabled={busy === receipt.id}
                          onClick={() => void post(receipt)}
                        >
                          Post
                        </Button>
                      </Tooltip>
                    ) : receipt.unallocated > 0 ? (
                      <Tooltip content="Apply this money to outstanding invoices" relationship="label">
                        <Button
                          appearance="subtle"
                          size="small"
                          icon={<LinkRegular />}
                          onClick={() => setAllocating(receipt)}
                        >
                          Allocate
                        </Button>
                      </Tooltip>
                    ) : (
                      <Caption1 className={layout.subtle}>settled</Caption1>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}

      <NewReceiptDialog
        open={showNew}
        onOpenChange={setShowNew}
        entityId={entityId}
        customers={customers}
        accounts={accounts}
        onCreated={load}
      />

      {allocating && (
        <AllocateDialog
          receipt={allocating}
          entityId={entityId}
          onClose={() => setAllocating(null)}
          onAllocated={load}
        />
      )}
    </div>
  )
}

function NewReceiptDialog({ open, onOpenChange, entityId, customers, accounts, onCreated }: {
  open: boolean
  onOpenChange: (open: boolean) => void
  entityId: string
  customers: CustomerSummary[]
  accounts: AccountSummary[]
  onCreated: () => void
}) {
  const styles = useStyles()
  const [customerId, setCustomerId] = useState('')
  const [bankAccountId, setBankAccountId] = useState('')
  const [receiptDate, setReceiptDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [amount, setAmount] = useState('')
  const [reference, setReference] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  // Money has to land somewhere that represents money.
  const bankAccounts = useMemo(
    () => accounts.filter((a) => a.isPostable && a.controlType === 'Bank'),
    [accounts],
  )

  const customer = customers.find((c) => c.id === customerId)
  const bank = bankAccounts.find((a) => a.id === bankAccountId)
  const valid = customerId !== '' && bankAccountId !== '' && Number(amount) > 0

  const submit = async () => {
    setError(null)
    setSaving(true)
    try {
      await createReceipt({
        legalEntityId: entityId,
        customerId,
        bankAccountId,
        receiptDate,
        amount: Number(amount),
        reference: reference || undefined,
      })
      setCustomerId('')
      setBankAccountId('')
      setAmount('')
      setReference('')
      onOpenChange(false)
      onCreated()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(_, d) => onOpenChange(d.open)}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>New receipt</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {error && (
                <MessageBar intent="error">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              )}

              <MessageBar intent="info">
                <MessageBarBody>
                  This records that money arrived. Which invoices it settles is a separate
                  step, because a customer often pays a round figure against several.
                </MessageBarBody>
              </MessageBar>

              <Field label="Customer" required>
                <Dropdown
                  placeholder="Select customer"
                  value={customer ? `${customer.code} — ${customer.name}` : ''}
                  selectedOptions={customerId ? [customerId] : []}
                  onOptionSelect={(_, d) => setCustomerId(d.optionValue ?? '')}
                >
                  {customers.map((c) => (
                    <Option key={c.id} value={c.id} text={`${c.code} — ${c.name}`}>
                      {c.code} — {c.name}
                    </Option>
                  ))}
                </Dropdown>
              </Field>

              <Field label="Into account" required>
                <Dropdown
                  placeholder="Bank or cash account"
                  value={bank ? `${bank.code} — ${bank.name}` : ''}
                  selectedOptions={bankAccountId ? [bankAccountId] : []}
                  onOptionSelect={(_, d) => setBankAccountId(d.optionValue ?? '')}
                >
                  {bankAccounts.map((a) => (
                    <Option key={a.id} value={a.id} text={`${a.code} — ${a.name}`}>
                      {a.code} — {a.name}
                    </Option>
                  ))}
                </Dropdown>
              </Field>

              <div className={styles.row}>
                <Field label="Date">
                  <Input type="date" value={receiptDate} onChange={(_, d) => setReceiptDate(d.value)} />
                </Field>
                <Field label="Amount" required className={styles.narrow}>
                  <Input
                    type="number"
                    value={amount}
                    onChange={(_, d) => setAmount(d.value)}
                    placeholder="0.00"
                  />
                </Field>
                <Field label="Reference" className={styles.grow}>
                  <Input
                    value={reference}
                    onChange={(_, d) => setReference(d.value)}
                    placeholder="Cheque or transfer reference"
                  />
                </Field>
              </div>
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button appearance="primary" disabled={!valid || saving} onClick={submit}>
              {saving ? 'Saving…' : 'Save draft'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

function AllocateDialog({ receipt, entityId, onClose, onAllocated }: {
  receipt: ReceiptSummary
  entityId: string
  onClose: () => void
  onAllocated: () => void
}) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [open, setOpen] = useState<OpenInvoice[]>([])
  const [amounts, setAmounts] = useState<Record<string, string>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    getOpenInvoices(entityId)
      .then(setOpen)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  const applied = Object.values(amounts).reduce((sum, v) => sum + (Number(v) || 0), 0)
  const remaining = receipt.unallocated - applied
  const valid = applied > 0 && remaining >= 0

  const submit = async () => {
    setError(null)
    setSaving(true)
    try {
      const lines = Object.entries(amounts)
        .filter(([, v]) => Number(v) > 0)
        .map(([salesInvoiceId, v]) => ({ salesInvoiceId, amount: Number(v) }))

      await allocate(receipt.id, lines)
      onClose()
      onAllocated()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface style={{ maxWidth: '760px' }}>
        <DialogBody>
          <DialogTitle>Allocate {receipt.docNo}</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {error && (
                <MessageBar intent="error">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              )}

              <Caption1 className={layout.subtle}>
                {receipt.currencyCode} {formatMoney(receipt.unallocated)} unapplied from{' '}
                {receipt.customerName}. Settling at a different rate to the invoice posts the
                exchange difference automatically.
              </Caption1>

              {loading ? (
                <Spinner label="Loading open invoices…" />
              ) : open.length === 0 ? (
                <MessageBar intent="info">
                  <MessageBarBody>This customer has nothing outstanding.</MessageBarBody>
                </MessageBar>
              ) : (
                <Table size="small" aria-label="Open invoices">
                  <TableHeader>
                    <TableRow>
                      <TableHeaderCell>Invoice</TableHeaderCell>
                      <TableHeaderCell>Due</TableHeaderCell>
                      <TableHeaderCell className={styles.right}>Outstanding</TableHeaderCell>
                      <TableHeaderCell className={styles.right}>Apply</TableHeaderCell>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {open.map((invoice) => (
                      <TableRow key={invoice.id}>
                        <TableCell className={styles.mono}>{invoice.docNo}</TableCell>
                        <TableCell>
                          {invoice.dueDate}
                          {invoice.daysOverdue > 0 && (
                            <Badge appearance="tint" color="danger" style={{ marginLeft: 8 }}>
                              {invoice.daysOverdue}d late
                            </Badge>
                          )}
                        </TableCell>
                        <TableCell className={`${styles.right} ${styles.mono}`}>
                          {formatMoney(invoice.outstanding)}
                        </TableCell>
                        <TableCell className={styles.right}>
                          <Input
                            type="number"
                            size="small"
                            style={{ width: '110px' }}
                            value={amounts[invoice.id] ?? ''}
                            onChange={(_, d) =>
                              setAmounts((c) => ({ ...c, [invoice.id]: d.value }))}
                            placeholder="0.00"
                          />
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}

              <div className={styles.totals}>
                <Text>Applying <b>{formatMoney(applied)}</b></Text>
                <Text
                  style={{
                    color: remaining < 0
                      ? tokens.colorPaletteRedForeground1
                      : tokens.colorNeutralForeground3,
                  }}
                >
                  {remaining < 0
                    ? `Over by ${formatMoney(-remaining)}`
                    : `${formatMoney(remaining)} left unapplied`}
                </Text>
              </div>
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onClose}>Cancel</Button>
            <Button appearance="primary" disabled={!valid || saving} onClick={submit}>
              {saving ? 'Allocating…' : 'Allocate'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
