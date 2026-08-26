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
  Tab,
  TabList,
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
import {
  createPurchaseCreditNote,
  createSalesCreditNote,
  getPurchaseCreditNotes,
  getSalesCreditNotes,
  postPurchaseCreditNote,
  postSalesCreditNote,
} from '../api/creditNotes'
import type {
  CreateCreditNoteLine,
  PurchaseCreditNoteSummary,
  SalesCreditNoteSummary,
} from '../api/creditNotes'
import { todayLocal } from '../api/dates'
import { formatMoney } from '../api/journalEntries'
import { getOpenBills } from '../api/payables'
import type { OpenPurchaseInvoice } from '../api/payables'
import { getOpenInvoices } from '../api/receivables'
import type { OpenInvoice } from '../api/receivables'
import { getTaxCodes } from '../api/tax'
import type { TaxCodeSummary } from '../api/tax'
import { useLayoutStyles } from '../theme'

const useStyles = makeStyles({
  mono: { fontFamily: tokens.fontFamilyMonospace },
  right: { textAlign: 'right' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  row: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'end' },
  grow: { flexGrow: 1, minWidth: '200px' },
  totals: {
    display: 'flex',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
})

type Side = 'sales' | 'purchases'

export function CreditNotesPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const [side, setSide] = useState<Side>('sales')

  if (!entityId) {
    return (
      <div className={layout.page}>
        <Title3>Credit notes</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Credit notes</Title3>
        <Caption1 className={layout.subtle}>
          How a posted invoice is undone. The invoice itself never changes — a credit note
          posts the opposite way and both documents stay visible, with the reason attached.
        </Caption1>
      </div>

      <TabList selectedValue={side} onTabSelect={(_, d) => setSide(d.value as Side)}>
        <Tab value="sales">To customers</Tab>
        <Tab value="purchases">From suppliers</Tab>
      </TabList>

      {side === 'sales'
        ? <SalesSide entityId={entityId} />
        : <PurchaseSide entityId={entityId} />}
    </div>
  )
}

// ---------------------------------------------------------------- sales

function SalesSide({ entityId }: { entityId: string }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [notes, setNotes] = useState<SalesCreditNoteSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showNew, setShowNew] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    getSalesCreditNotes(entityId)
      .then((n) => { setNotes(n); setError(null) })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  useEffect(load, [load])

  const post = async (id: string) => {
    setBusy(id)
    setError(null)
    try {
      await postSalesCreditNote(id)
      load()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(null)
    }
  }

  return (
    <>
      <Toolbar aria-label="Credit note actions">
        <Button icon={<AddRegular />} appearance="primary" onClick={() => setShowNew(true)}>
          New credit note
        </Button>
        <div className={layout.spacer} />
        <Caption1 className={layout.subtle}>{notes.length} notes</Caption1>
      </Toolbar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {loading ? <Spinner label="Loading…" /> : (
        <Card>
          <Table size="small" aria-label="Sales credit notes">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Number</TableHeaderCell>
                <TableHeaderCell>Date</TableHeaderCell>
                <TableHeaderCell>Against</TableHeaderCell>
                <TableHeaderCell>Customer</TableHeaderCell>
                <TableHeaderCell>Reason</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Total</TableHeaderCell>
                <TableHeaderCell>State</TableHeaderCell>
                <TableHeaderCell />
              </TableRow>
            </TableHeader>
            <TableBody>
              {notes.length === 0 && (
                <TableRow>
                  <TableCell colSpan={8}>
                    <Caption1 className={layout.subtle}>
                      No credit notes yet.
                    </Caption1>
                  </TableCell>
                </TableRow>
              )}
              {notes.map((n) => (
                <TableRow key={n.id}>
                  <TableCell className={styles.mono}>
                    {n.docNo ?? <span className={layout.subtle}>— draft</span>}
                  </TableCell>
                  <TableCell>{n.docDate}</TableCell>
                  <TableCell className={styles.mono}>{n.invoiceDocNo}</TableCell>
                  <TableCell>{n.customerName}</TableCell>
                  <TableCell>{n.reasonCode}</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {n.currencyCode} {formatMoney(n.totalWithTax)}
                  </TableCell>
                  <TableCell>
                    <Badge appearance="tint" color={n.state === 'Posted' ? 'success' : 'warning'}>
                      {n.state}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    {n.state === 'Draft' ? (
                      <Tooltip content="Post — writes the journal entry. One way." relationship="label">
                        <Button
                          appearance="subtle"
                          size="small"
                          icon={<CheckmarkCircleRegular />}
                          disabled={busy === n.id}
                          onClick={() => void post(n.id)}
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

      {showNew && (
        <NewCreditNoteDialog
          side="sales"
          entityId={entityId}
          onClose={() => setShowNew(false)}
          onCreated={load}
        />
      )}
    </>
  )
}

// ---------------------------------------------------------------- purchases

function PurchaseSide({ entityId }: { entityId: string }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [notes, setNotes] = useState<PurchaseCreditNoteSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showNew, setShowNew] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    getPurchaseCreditNotes(entityId)
      .then((n) => { setNotes(n); setError(null) })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  useEffect(load, [load])

  const post = async (id: string) => {
    setBusy(id)
    setError(null)
    try {
      await postPurchaseCreditNote(id)
      load()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(null)
    }
  }

  return (
    <>
      <Toolbar aria-label="Credit note actions">
        <Button icon={<AddRegular />} appearance="primary" onClick={() => setShowNew(true)}>
          New credit note
        </Button>
        <div className={layout.spacer} />
        <Caption1 className={layout.subtle}>{notes.length} notes</Caption1>
      </Toolbar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {loading ? <Spinner label="Loading…" /> : (
        <Card>
          <Table size="small" aria-label="Purchase credit notes">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Number</TableHeaderCell>
                <TableHeaderCell>Their ref</TableHeaderCell>
                <TableHeaderCell>Date</TableHeaderCell>
                <TableHeaderCell>Against</TableHeaderCell>
                <TableHeaderCell>Supplier</TableHeaderCell>
                <TableHeaderCell>Reason</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Total</TableHeaderCell>
                <TableHeaderCell>State</TableHeaderCell>
                <TableHeaderCell />
              </TableRow>
            </TableHeader>
            <TableBody>
              {notes.length === 0 && (
                <TableRow>
                  <TableCell colSpan={9}>
                    <Caption1 className={layout.subtle}>No credit notes yet.</Caption1>
                  </TableCell>
                </TableRow>
              )}
              {notes.map((n) => (
                <TableRow key={n.id}>
                  <TableCell className={styles.mono}>
                    {n.docNo ?? <span className={layout.subtle}>— draft</span>}
                  </TableCell>
                  <TableCell className={styles.mono}>
                    {n.supplierCreditNoteNo ?? <span className={layout.subtle}>ours</span>}
                  </TableCell>
                  <TableCell>{n.docDate}</TableCell>
                  <TableCell className={styles.mono}>{n.supplierInvoiceNo}</TableCell>
                  <TableCell>{n.supplierName}</TableCell>
                  <TableCell>{n.reasonCode}</TableCell>
                  <TableCell className={`${styles.right} ${styles.mono}`}>
                    {n.currencyCode} {formatMoney(n.totalWithTax)}
                  </TableCell>
                  <TableCell>
                    <Badge appearance="tint" color={n.state === 'Posted' ? 'success' : 'warning'}>
                      {n.state}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    {n.state === 'Draft' ? (
                      <Tooltip content="Post — writes the journal entry. One way." relationship="label">
                        <Button
                          appearance="subtle"
                          size="small"
                          icon={<CheckmarkCircleRegular />}
                          disabled={busy === n.id}
                          onClick={() => void post(n.id)}
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

      {showNew && (
        <NewCreditNoteDialog
          side="purchases"
          entityId={entityId}
          onClose={() => setShowNew(false)}
          onCreated={load}
        />
      )}
    </>
  )
}

// ---------------------------------------------------------------- shared dialog

interface DraftLine {
  key: number
  description: string
  quantity: string
  unitPrice: string
  accountId: string
  taxCodeId: string
}

let nextKey = 1
const emptyLine = (): DraftLine => ({
  key: nextKey++,
  description: '',
  quantity: '1',
  unitPrice: '',
  accountId: '',
  taxCodeId: '',
})

/**
 * One dialog for both sides. They differ only in which documents can be credited and which
 * accounts a line may name; everything else — reason, lines, tax, the outstanding check the
 * server applies — is identical, and two near-copies would drift apart.
 */
function NewCreditNoteDialog({ side, entityId, onClose, onCreated }: {
  side: Side
  entityId: string
  onClose: () => void
  onCreated: () => void
}) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [openInvoices, setOpenInvoices] = useState<OpenInvoice[]>([])
  const [openBills, setOpenBills] = useState<OpenPurchaseInvoice[]>([])
  const [accounts, setAccounts] = useState<AccountSummary[]>([])
  const [taxCodes, setTaxCodes] = useState<TaxCodeSummary[]>([])

  const [documentId, setDocumentId] = useState('')
  const [docDate, setDocDate] = useState(todayLocal)
  const [reasonCode, setReasonCode] = useState('')
  const [theirRef, setTheirRef] = useState('')
  const [lines, setLines] = useState<DraftLine[]>([emptyLine()])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    const documents = side === 'sales'
      ? getOpenInvoices(entityId).then(setOpenInvoices)
      : getOpenBills(entityId).then(setOpenBills)

    Promise.all([documents, getAccounts().then(setAccounts), getTaxCodes(docDate).then(setTaxCodes)])
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
    // Loaded once: changing the date afterwards does not change which documents are open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entityId, side])

  const creditable = useMemo(
    () => accounts.filter((a) =>
      a.isPostable
      && a.isActive
      && a.controlType !== 'AccountsReceivable'
      && a.controlType !== 'AccountsPayable'
      // A sales credit reverses revenue; a purchase credit reverses a cost or an asset.
      && (side === 'sales' ? a.accountType === 'Income' : a.accountType !== 'Income')),
    [accounts, side],
  )

  const outstanding = side === 'sales'
    ? openInvoices.find((i) => i.id === documentId)?.outstanding
    : openBills.find((b) => b.id === documentId)?.outstanding

  const net = lines.reduce(
    (sum, l) => sum + (Number(l.quantity) || 0) * (Number(l.unitPrice) || 0), 0)

  const tax = lines.reduce((sum, l) => {
    const code = taxCodes.find((c) => c.id === l.taxCodeId)
    if (!code) return sum
    const lineNet = (Number(l.quantity) || 0) * (Number(l.unitPrice) || 0)
    return sum + Math.round(lineNet * code.rate) / 100
  }, 0)

  const total = net + tax
  const overCredited = outstanding !== undefined && total > outstanding

  const valid =
    documentId !== ''
    && reasonCode.trim() !== ''
    && !overCredited
    && lines.every((l) =>
      l.description.trim() !== ''
      && Number(l.quantity) > 0
      && Number(l.unitPrice) > 0
      && l.accountId !== '')

  const update = (key: number, patch: Partial<DraftLine>) =>
    setLines((current) => current.map((l) => (l.key === key ? { ...l, ...patch } : l)))

  const submit = async () => {
    setError(null)
    setSaving(true)
    try {
      const payload: CreateCreditNoteLine[] = lines.map((l) => ({
        description: l.description,
        quantity: Number(l.quantity),
        unitPrice: Number(l.unitPrice),
        accountId: l.accountId,
        taxCodeId: l.taxCodeId || undefined,
      }))

      if (side === 'sales') {
        await createSalesCreditNote({
          legalEntityId: entityId,
          salesInvoiceId: documentId,
          docDate,
          reasonCode: reasonCode.trim(),
          lines: payload,
        })
      } else {
        await createPurchaseCreditNote({
          legalEntityId: entityId,
          purchaseInvoiceId: documentId,
          docDate,
          reasonCode: reasonCode.trim(),
          supplierCreditNoteNo: theirRef.trim() || undefined,
          lines: payload,
        })
      }

      onClose()
      onCreated()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  const documentLabel = (id: string) => {
    if (side === 'sales') {
      const i = openInvoices.find((x) => x.id === id)
      return i ? `${i.docNo} — ${formatMoney(i.outstanding)} outstanding` : ''
    }
    const b = openBills.find((x) => x.id === id)
    return b ? `${b.supplierInvoiceNo} — ${formatMoney(b.outstanding)} outstanding` : ''
  }

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface style={{ maxWidth: '900px' }}>
        <DialogBody>
          <DialogTitle>
            {side === 'sales' ? 'New credit note to a customer' : 'New credit note from a supplier'}
          </DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {error && (
                <MessageBar intent="error">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              )}

              {loading ? <Spinner label="Loading…" /> : (
                <>
                  <div className={styles.row}>
                    <Field
                      label={side === 'sales' ? 'Credit against invoice' : 'Credit against bill'}
                      required
                      className={styles.grow}
                    >
                      <Dropdown
                        placeholder="Select a document with something outstanding"
                        value={documentLabel(documentId)}
                        selectedOptions={documentId ? [documentId] : []}
                        onOptionSelect={(_, d) => setDocumentId(d.optionValue ?? '')}
                      >
                        {side === 'sales'
                          ? openInvoices.map((i) => (
                            <Option key={i.id} value={i.id} text={documentLabel(i.id)}>
                              {i.docNo} — {formatMoney(i.outstanding)} outstanding
                            </Option>
                          ))
                          : openBills.map((b) => (
                            <Option key={b.id} value={b.id} text={documentLabel(b.id)}>
                              {b.supplierInvoiceNo} — {formatMoney(b.outstanding)} outstanding
                            </Option>
                          ))}
                      </Dropdown>
                    </Field>
                    <Field label="Date">
                      <Input type="date" value={docDate} onChange={(_, d) => setDocDate(d.value)} />
                    </Field>
                  </div>

                  <div className={styles.row}>
                    <Field
                      label="Reason"
                      required
                      className={styles.grow}
                      hint="Recorded on the entry. An unexplained credit is what an auditor asks about first."
                    >
                      <Input
                        value={reasonCode}
                        onChange={(_, d) => setReasonCode(d.value)}
                        placeholder="Goods returned damaged"
                      />
                    </Field>
                    {side === 'purchases' && (
                      <Field label="Their reference" hint="Blank if we raised it">
                        <Input value={theirRef} onChange={(_, d) => setTheirRef(d.value)} />
                      </Field>
                    )}
                  </div>

                  <Table size="small" aria-label="Credit note lines">
                    <TableHeader>
                      <TableRow>
                        <TableHeaderCell>Description</TableHeaderCell>
                        <TableHeaderCell>
                          {side === 'sales' ? 'Reverse revenue' : 'Reverse charge'}
                        </TableHeaderCell>
                        <TableHeaderCell>Tax</TableHeaderCell>
                        <TableHeaderCell className={styles.right}>Qty</TableHeaderCell>
                        <TableHeaderCell className={styles.right}>Price</TableHeaderCell>
                        <TableHeaderCell />
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {lines.map((line) => {
                        const account = creditable.find((a) => a.id === line.accountId)
                        const code = taxCodes.find((c) => c.id === line.taxCodeId)
                        return (
                          <TableRow key={line.key}>
                            <TableCell>
                              <Input
                                size="small"
                                value={line.description}
                                onChange={(_, d) => update(line.key, { description: d.value })}
                                placeholder="What is being credited"
                              />
                            </TableCell>
                            <TableCell>
                              <Dropdown
                                size="small"
                                placeholder="Account"
                                style={{ minWidth: '150px' }}
                                value={account ? `${account.code} — ${account.name}` : ''}
                                selectedOptions={line.accountId ? [line.accountId] : []}
                                onOptionSelect={(_, d) =>
                                  update(line.key, { accountId: d.optionValue ?? '' })}
                              >
                                {creditable.map((a) => (
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
                                style={{ minWidth: '110px' }}
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

                  {overCredited && (
                    <MessageBar intent="warning">
                      <MessageBarBody>
                        This credit is {formatMoney(total - (outstanding ?? 0))} more than is
                        outstanding. Crediting past zero would leave a balance on account,
                        which is a separate decision and is not supported yet.
                      </MessageBarBody>
                    </MessageBar>
                  )}

                  <div className={styles.totals}>
                    <Text className={layout.subtle}>Net {formatMoney(net)}</Text>
                    <Text className={layout.subtle}>Tax {formatMoney(tax)}</Text>
                    <Text weight="semibold">Credit {formatMoney(total)}</Text>
                    {outstanding !== undefined && (
                      <Text className={layout.subtle}>
                        of {formatMoney(outstanding)} outstanding
                      </Text>
                    )}
                  </div>
                </>
              )}
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onClose}>Cancel</Button>
            <Button appearance="primary" disabled={!valid || saving} onClick={submit}>
              {saving ? 'Saving…' : 'Save draft'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
