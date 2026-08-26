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
import { todayLocal } from '../api/dates'
import { formatMoney } from '../api/journalEntries'
import {
  allocatePayment,
  createPayment,
  getOpenBills,
  getPayments,
  getSuppliers,
  postPayment,
} from '../api/payables'
import type {
  OpenPurchaseInvoice,
  PaymentSummary,
  SupplierSummary,
} from '../api/payables'
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

export function SupplierPaymentsPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [payments, setPayments] = useState<PaymentSummary[]>([])
  const [suppliers, setSuppliers] = useState<SupplierSummary[]>([])
  const [accounts, setAccounts] = useState<AccountSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showNew, setShowNew] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)
  const [allocating, setAllocating] = useState<PaymentSummary | null>(null)

  const load = useCallback(() => {
    if (!entityId) return
    setLoading(true)
    Promise.all([getPayments(entityId), getSuppliers(), getAccounts()])
      .then(([p, s, a]) => {
        setPayments(p)
        setSuppliers(s)
        setAccounts(a)
        setError(null)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  useEffect(load, [load])

  const post = async (payment: PaymentSummary) => {
    setBusy(payment.id)
    setError(null)
    try {
      await postPayment(payment.id)
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
        <Title3>Payments</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Payments</Title3>
        <Caption1 className={layout.subtle}>
          Paying money out and deciding which bills it settles are two separate decisions, so
          they are two separate records. Post a payment first, then allocate it.
        </Caption1>
      </div>

      <Toolbar aria-label="Payment actions">
        <Button icon={<AddRegular />} appearance="primary" onClick={() => setShowNew(true)}>
          New payment
        </Button>
        <div className={layout.spacer} />
        <Caption1 className={layout.subtle}>{payments.length} payments</Caption1>
      </Toolbar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {loading ? (
        <Spinner label="Loading payments…" />
      ) : (
        <Card>
          <Table size="small" aria-label="Payments">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Number</TableHeaderCell>
                <TableHeaderCell>Date</TableHeaderCell>
                <TableHeaderCell>Supplier</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Amount</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Unallocated</TableHeaderCell>
                <TableHeaderCell>State</TableHeaderCell>
                <TableHeaderCell />
              </TableRow>
            </TableHeader>
            <TableBody>
              {payments.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7}>
                    <Caption1 className={layout.subtle}>
                      No payments yet. Record one with “New payment”.
                    </Caption1>
                  </TableCell>
                </TableRow>
              )}

              {payments.map((payment) => (
                <TableRow key={payment.id}>
                  <TableCell className={styles.mono}>
                    {payment.docNo ?? <span className={layout.subtle}>— draft</span>}
                  </TableCell>
                  <TableCell>{payment.paymentDate}</TableCell>
                  <TableCell>{payment.supplierName}</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {payment.currencyCode} {formatMoney(payment.amount)}
                  </TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {payment.unallocated > 0 ? (
                      <Text weight="semibold">{formatMoney(payment.unallocated)}</Text>
                    ) : (
                      <span className={layout.subtle}>fully applied</span>
                    )}
                  </TableCell>
                  <TableCell>
                    <Badge
                      appearance="tint"
                      color={payment.state === 'Posted' ? 'success' : 'warning'}
                    >
                      {payment.state}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    {payment.state === 'Draft' ? (
                      <Tooltip
                        content="Post — writes the journal entry. One way."
                        relationship="label"
                      >
                        <Button
                          appearance="subtle"
                          size="small"
                          icon={<CheckmarkCircleRegular />}
                          disabled={busy === payment.id}
                          onClick={() => void post(payment)}
                        >
                          Post
                        </Button>
                      </Tooltip>
                    ) : payment.unallocated > 0 ? (
                      <Tooltip content="Apply this to outstanding bills" relationship="label">
                        <Button
                          appearance="subtle"
                          size="small"
                          icon={<LinkRegular />}
                          onClick={() => setAllocating(payment)}
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

      <NewPaymentDialog
        open={showNew}
        onOpenChange={setShowNew}
        entityId={entityId}
        suppliers={suppliers}
        accounts={accounts}
        onCreated={load}
      />

      {allocating && (
        <AllocatePaymentDialog
          payment={allocating}
          entityId={entityId}
          onClose={() => setAllocating(null)}
          onAllocated={load}
        />
      )}
    </div>
  )
}

function NewPaymentDialog({ open, onOpenChange, entityId, suppliers, accounts, onCreated }: {
  open: boolean
  onOpenChange: (open: boolean) => void
  entityId: string
  suppliers: SupplierSummary[]
  accounts: AccountSummary[]
  onCreated: () => void
}) {
  const styles = useStyles()
  const [supplierId, setSupplierId] = useState('')
  const [bankAccountId, setBankAccountId] = useState('')
  const [paymentDate, setPaymentDate] = useState(todayLocal)
  const [amount, setAmount] = useState('')
  const [reference, setReference] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  // Money has to leave from somewhere that represents money.
  const bankAccounts = useMemo(
    () => accounts.filter((a) => a.isPostable && a.controlType === 'Bank'),
    [accounts],
  )

  const supplier = suppliers.find((s) => s.id === supplierId)
  const bank = bankAccounts.find((a) => a.id === bankAccountId)
  const valid = supplierId !== '' && bankAccountId !== '' && Number(amount) > 0

  const submit = async () => {
    setError(null)
    setSaving(true)
    try {
      await createPayment({
        legalEntityId: entityId,
        supplierId,
        bankAccountId,
        paymentDate,
        amount: Number(amount),
        reference: reference || undefined,
      })
      setSupplierId('')
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
          <DialogTitle>New payment</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {error && (
                <MessageBar intent="error">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              )}

              <MessageBar intent="info">
                <MessageBarBody>
                  This records that money went out. Which bills it settles is a separate step,
                  because a payment run often covers several with one transfer.
                </MessageBarBody>
              </MessageBar>

              <Field label="Supplier" required>
                <Dropdown
                  placeholder="Select supplier"
                  value={supplier ? `${supplier.code} — ${supplier.name}` : ''}
                  selectedOptions={supplierId ? [supplierId] : []}
                  onOptionSelect={(_, d) => setSupplierId(d.optionValue ?? '')}
                >
                  {suppliers.map((s) => (
                    <Option key={s.id} value={s.id} text={`${s.code} — ${s.name}`}>
                      {s.code} — {s.name}
                    </Option>
                  ))}
                </Dropdown>
              </Field>

              <Field label="Paid from" required>
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
                  <Input
                    type="date"
                    value={paymentDate}
                    onChange={(_, d) => setPaymentDate(d.value)}
                  />
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

function AllocatePaymentDialog({ payment, entityId, onClose, onAllocated }: {
  payment: PaymentSummary
  entityId: string
  onClose: () => void
  onAllocated: () => void
}) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [bills, setBills] = useState<OpenPurchaseInvoice[]>([])
  const [amounts, setAmounts] = useState<Record<string, string>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    getOpenBills(entityId)
      .then(setBills)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  const applied = Object.values(amounts).reduce((sum, v) => sum + (Number(v) || 0), 0)
  const remaining = payment.unallocated - applied
  const valid = applied > 0 && remaining >= 0

  const submit = async () => {
    setError(null)
    setSaving(true)
    try {
      const lines = Object.entries(amounts)
        .filter(([, v]) => Number(v) > 0)
        .map(([purchaseInvoiceId, v]) => ({ purchaseInvoiceId, amount: Number(v) }))

      await allocatePayment(payment.id, lines)
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
      <DialogSurface style={{ maxWidth: '780px' }}>
        <DialogBody>
          <DialogTitle>Allocate {payment.docNo}</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {error && (
                <MessageBar intent="error">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              )}

              <Caption1 className={layout.subtle}>
                {payment.currencyCode} {formatMoney(payment.unallocated)} unapplied to{' '}
                {payment.supplierName}. Settling at a different rate to the bill posts the
                exchange difference automatically.
              </Caption1>

              {loading ? (
                <Spinner label="Loading open bills…" />
              ) : bills.length === 0 ? (
                <MessageBar intent="info">
                  <MessageBarBody>Nothing is outstanding to any supplier.</MessageBarBody>
                </MessageBar>
              ) : (
                <Table size="small" aria-label="Open bills">
                  <TableHeader>
                    <TableRow>
                      <TableHeaderCell>Supplier ref</TableHeaderCell>
                      <TableHeaderCell>Due</TableHeaderCell>
                      <TableHeaderCell className={styles.right}>Outstanding</TableHeaderCell>
                      <TableHeaderCell className={styles.right}>Apply</TableHeaderCell>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {bills.map((bill) => (
                      <TableRow key={bill.id}>
                        <TableCell className={styles.mono}>{bill.supplierInvoiceNo}</TableCell>
                        <TableCell>
                          {bill.dueDate}
                          {bill.daysOverdue > 0 && (
                            <Badge appearance="tint" color="danger" style={{ marginLeft: 8 }}>
                              {bill.daysOverdue}d late
                            </Badge>
                          )}
                        </TableCell>
                        <TableCell className={`${styles.right} ${styles.mono}`}>
                          {formatMoney(bill.outstanding)}
                        </TableCell>
                        <TableCell className={styles.right}>
                          <Input
                            type="number"
                            size="small"
                            style={{ width: '110px' }}
                            value={amounts[bill.id] ?? ''}
                            onChange={(_, d) =>
                              setAmounts((c) => ({ ...c, [bill.id]: d.value }))}
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
