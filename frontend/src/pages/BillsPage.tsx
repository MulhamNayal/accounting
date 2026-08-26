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
import { AddRegular, CheckmarkCircleRegular, DeleteRegular } from '@fluentui/react-icons'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { getAccounts } from '../api/accounts'
import type { AccountSummary } from '../api/accounts'
import { todayLocal } from '../api/dates'
import { formatMoney } from '../api/journalEntries'
import {
  createPurchaseInvoice,
  getPurchaseInvoices,
  getSuppliers,
  postPurchaseInvoice,
} from '../api/payables'
import type { PurchaseInvoiceSummary, SupplierSummary } from '../api/payables'
import { getTaxCodes } from '../api/tax'
import type { TaxCodeSummary } from '../api/tax'
import { useLayoutStyles } from '../theme'

const useStyles = makeStyles({
  mono: { fontFamily: tokens.fontFamilyMonospace },
  right: { textAlign: 'right' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  row: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'end' },
  grow: { flexGrow: 1, minWidth: '200px' },
  narrow: { width: '110px' },
  totals: {
    display: 'flex',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
})

export function BillsPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [bills, setBills] = useState<PurchaseInvoiceSummary[]>([])
  const [suppliers, setSuppliers] = useState<SupplierSummary[]>([])
  const [accounts, setAccounts] = useState<AccountSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showNew, setShowNew] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)

  const load = useCallback(() => {
    if (!entityId) return
    setLoading(true)
    Promise.all([getPurchaseInvoices(entityId), getSuppliers(), getAccounts()])
      .then(([b, s, a]) => {
        setBills(b)
        setSuppliers(s)
        setAccounts(a)
        setError(null)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  useEffect(load, [load])

  const post = async (bill: PurchaseInvoiceSummary) => {
    setBusy(bill.id)
    setError(null)
    try {
      await postPurchaseInvoice(bill.id)
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
        <Title3>Bills</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Bills</Title3>
        <Caption1 className={layout.subtle}>
          What suppliers have invoiced. The supplier's own invoice number is recorded and must
          be unique for that supplier — entering the same bill twice is refused rather than
          discovered after it has been paid.
        </Caption1>
      </div>

      <Toolbar aria-label="Bill actions">
        <Button icon={<AddRegular />} appearance="primary" onClick={() => setShowNew(true)}>
          New bill
        </Button>
        <div className={layout.spacer} />
        <Caption1 className={layout.subtle}>{bills.length} bills</Caption1>
      </Toolbar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {loading ? (
        <Spinner label="Loading bills…" />
      ) : (
        <Card>
          <Table size="small" aria-label="Bills">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Our ref</TableHeaderCell>
                <TableHeaderCell>Supplier ref</TableHeaderCell>
                <TableHeaderCell>Supplier</TableHeaderCell>
                <TableHeaderCell>Due</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Net</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Tax</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Total</TableHeaderCell>
                <TableHeaderCell>State</TableHeaderCell>
                <TableHeaderCell />
              </TableRow>
            </TableHeader>
            <TableBody>
              {bills.length === 0 && (
                <TableRow>
                  <TableCell colSpan={9}>
                    <Caption1 className={layout.subtle}>
                      No bills yet. Record one with “New bill”.
                    </Caption1>
                  </TableCell>
                </TableRow>
              )}

              {bills.map((bill) => (
                <TableRow key={bill.id}>
                  <TableCell className={styles.mono}>
                    {bill.docNo ?? <span className={layout.subtle}>— draft</span>}
                  </TableCell>
                  <TableCell className={styles.mono}>{bill.supplierInvoiceNo}</TableCell>
                  <TableCell>{bill.supplierName}</TableCell>
                  <TableCell>{bill.dueDate}</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {formatMoney(bill.total)}
                  </TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {bill.taxTotal ? formatMoney(bill.taxTotal) : ''}
                  </TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {bill.currencyCode} {formatMoney(bill.totalWithTax)}
                  </TableCell>
                  <TableCell>
                    <Badge
                      appearance="tint"
                      color={bill.state === 'Posted' ? 'success' : 'warning'}
                    >
                      {bill.state}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    {bill.state === 'Draft' ? (
                      <Tooltip
                        content="Post — writes the journal entry. One way."
                        relationship="label"
                      >
                        <Button
                          appearance="subtle"
                          size="small"
                          icon={<CheckmarkCircleRegular />}
                          disabled={busy === bill.id}
                          onClick={() => void post(bill)}
                        >
                          Post
                        </Button>
                      </Tooltip>
                    ) : (
                      <Caption1 className={layout.subtle}>in the books</Caption1>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}

      <NewBillDialog
        open={showNew}
        onOpenChange={setShowNew}
        entityId={entityId}
        suppliers={suppliers}
        accounts={accounts}
        onCreated={load}
      />
    </div>
  )
}

interface DraftLine {
  key: number
  description: string
  quantity: string
  unitPrice: string
  chargeAccountId: string
  taxCodeId: string
}

let nextKey = 1
const emptyLine = (): DraftLine => ({
  key: nextKey++,
  description: '',
  quantity: '1',
  unitPrice: '',
  chargeAccountId: '',
  taxCodeId: '',
})

function NewBillDialog({ open, onOpenChange, entityId, suppliers, accounts, onCreated }: {
  open: boolean
  onOpenChange: (open: boolean) => void
  entityId: string
  suppliers: SupplierSummary[]
  accounts: AccountSummary[]
  onCreated: () => void
}) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [supplierId, setSupplierId] = useState('')
  const [supplierInvoiceNo, setSupplierInvoiceNo] = useState('')
  const [docDate, setDocDate] = useState(todayLocal)
  const [lines, setLines] = useState<DraftLine[]>([emptyLine()])
  const [taxCodes, setTaxCodes] = useState<TaxCodeSummary[]>([])
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  // Codes are fetched for the document's date, not today: a back-dated bill must use the
  // regime that was in force then.
  useEffect(() => {
    getTaxCodes(docDate).then(setTaxCodes).catch(() => setTaxCodes([]))
  }, [docDate])

  // A bill can be charged to anything postable that is not a subledger control account.
  const chargeable = useMemo(
    () => accounts.filter((a) =>
      a.isPostable
      && a.isActive
      && a.controlType !== 'AccountsReceivable'
      && a.controlType !== 'AccountsPayable'),
    [accounts],
  )

  const supplier = suppliers.find((s) => s.id === supplierId)

  const net = lines.reduce(
    (sum, l) => sum + (Number(l.quantity) || 0) * (Number(l.unitPrice) || 0), 0)

  const tax = lines.reduce((sum, l) => {
    const code = taxCodes.find((c) => c.id === l.taxCodeId)
    if (!code) return sum
    const lineNet = (Number(l.quantity) || 0) * (Number(l.unitPrice) || 0)
    return sum + Math.round(lineNet * code.rate) / 100
  }, 0)

  // Any code whose regime does not allow a reclaim puts its tax into the cost instead of onto
  // an asset. Worth saying before the bill is posted rather than explaining afterwards.
  const irrecoverable = lines.some((l) => {
    const code = taxCodes.find((c) => c.id === l.taxCodeId)
    return code !== undefined && code.rate > 0 && code.inputAccountId === null
  })

  const valid =
    supplierId !== ''
    && supplierInvoiceNo.trim() !== ''
    && lines.length > 0
    && lines.every((l) =>
      l.description.trim() !== ''
      && Number(l.quantity) > 0
      && Number(l.unitPrice) > 0
      && l.chargeAccountId !== '')

  const submit = async () => {
    setError(null)
    setSaving(true)
    try {
      await createPurchaseInvoice({
        legalEntityId: entityId,
        supplierId,
        supplierInvoiceNo: supplierInvoiceNo.trim(),
        docDate,
        lines: lines.map((l) => ({
          description: l.description,
          quantity: Number(l.quantity),
          unitPrice: Number(l.unitPrice),
          chargeAccountId: l.chargeAccountId,
          taxCodeId: l.taxCodeId || undefined,
        })),
      })
      setSupplierId('')
      setSupplierInvoiceNo('')
      setLines([emptyLine()])
      onOpenChange(false)
      onCreated()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  const update = (key: number, patch: Partial<DraftLine>) =>
    setLines((current) => current.map((l) => (l.key === key ? { ...l, ...patch } : l)))

  return (
    <Dialog open={open} onOpenChange={(_, d) => onOpenChange(d.open)}>
      <DialogSurface style={{ maxWidth: '900px' }}>
        <DialogBody>
          <DialogTitle>New bill</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {error && (
                <MessageBar intent="error">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              )}

              <div className={styles.row}>
                <Field label="Supplier" required className={styles.grow}>
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
                <Field
                  label="Supplier's invoice number"
                  required
                  hint="As printed on the bill"
                >
                  <Input
                    value={supplierInvoiceNo}
                    onChange={(_, d) => setSupplierInvoiceNo(d.value)}
                  />
                </Field>
                <Field label="Date">
                  <Input type="date" value={docDate} onChange={(_, d) => setDocDate(d.value)} />
                </Field>
              </div>

              <Table size="small" aria-label="Bill lines">
                <TableHeader>
                  <TableRow>
                    <TableHeaderCell>Description</TableHeaderCell>
                    <TableHeaderCell>Charge to</TableHeaderCell>
                    <TableHeaderCell>Tax</TableHeaderCell>
                    <TableHeaderCell className={styles.right}>Qty</TableHeaderCell>
                    <TableHeaderCell className={styles.right}>Price</TableHeaderCell>
                    <TableHeaderCell />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {lines.map((line) => {
                    const account = chargeable.find((a) => a.id === line.chargeAccountId)
                    const code = taxCodes.find((c) => c.id === line.taxCodeId)
                    return (
                      <TableRow key={line.key}>
                        <TableCell>
                          <Input
                            size="small"
                            value={line.description}
                            onChange={(_, d) => update(line.key, { description: d.value })}
                            placeholder="What was bought"
                          />
                        </TableCell>
                        <TableCell>
                          <Dropdown
                            size="small"
                            placeholder="Account"
                            style={{ minWidth: '150px' }}
                            value={account ? `${account.code} — ${account.name}` : ''}
                            selectedOptions={line.chargeAccountId ? [line.chargeAccountId] : []}
                            onOptionSelect={(_, d) =>
                              update(line.key, { chargeAccountId: d.optionValue ?? '' })}
                          >
                            {chargeable.map((a) => (
                              <Option key={a.id} value={a.id} text={`${a.code} — ${a.name}`}>
                                {a.code} — {a.name}
                              </Option>
                            ))}
                          </Dropdown>
                        </TableCell>
                        <TableCell>
                          <Dropdown
                            size="small"
                            placeholder="None"
                            style={{ minWidth: '120px' }}
                            value={code ? `${code.code} ${code.rate}%` : ''}
                            selectedOptions={line.taxCodeId ? [line.taxCodeId] : []}
                            onOptionSelect={(_, d) =>
                              update(line.key, { taxCodeId: d.optionValue ?? '' })}
                          >
                            <Option value="" text="None">None</Option>
                            {taxCodes.map((c) => (
                              <Option key={c.id} value={c.id} text={`${c.code} ${c.rate}%`}>
                                {c.code} {c.rate}%
                              </Option>
                            ))}
                          </Dropdown>
                        </TableCell>
                        <TableCell className={styles.right}>
                          <Input
                            size="small"
                            type="number"
                            style={{ width: '70px' }}
                            value={line.quantity}
                            onChange={(_, d) => update(line.key, { quantity: d.value })}
                          />
                        </TableCell>
                        <TableCell className={styles.right}>
                          <Input
                            size="small"
                            type="number"
                            style={{ width: '100px' }}
                            value={line.unitPrice}
                            onChange={(_, d) => update(line.key, { unitPrice: d.value })}
                            placeholder="0.00"
                          />
                        </TableCell>
                        <TableCell>
                          <Button
                            appearance="subtle"
                            size="small"
                            icon={<DeleteRegular />}
                            disabled={lines.length === 1}
                            onClick={() =>
                              setLines((c) => c.filter((l) => l.key !== line.key))}
                          />
                        </TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>

              <Button
                appearance="subtle"
                size="small"
                icon={<AddRegular />}
                onClick={() => setLines((c) => [...c, emptyLine()])}
              >
                Add line
              </Button>

              {irrecoverable && (
                <MessageBar intent="info">
                  <MessageBarBody>
                    A line uses a code whose regime does not allow an input reclaim, so its tax
                    becomes part of the cost rather than an asset. That is the correct treatment
                    and it is why the charge will exceed the net.
                  </MessageBarBody>
                </MessageBar>
              )}

              <div className={styles.totals}>
                <Text className={layout.subtle}>Net {formatMoney(net)}</Text>
                <Text className={layout.subtle}>Tax {formatMoney(tax)}</Text>
                <Text weight="semibold">Total {formatMoney(net + tax)}</Text>
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
