import {
  Button,
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
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { AddRegular, DeleteRegular } from '@fluentui/react-icons'
import { useMemo, useState } from 'react'
import type { AccountSummary } from '../api/accounts'
import { formatMoney } from '../api/journalEntries'
import { createSalesInvoice } from '../api/salesInvoices'
import type { CustomerSummary } from '../api/salesInvoices'

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  row: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'end' },
  grow: { flexGrow: 1, minWidth: '220px' },
  narrow: { width: '110px' },
  total: {
    display: 'flex',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
})

interface DraftLine {
  description: string
  quantity: string
  unitPrice: string
  revenueAccountId: string
}

const EMPTY: DraftLine = { description: '', quantity: '1', unitPrice: '', revenueAccountId: '' }

export function NewInvoiceDialog({ open, onOpenChange, entityId, customers, accounts, onCreated }: {
  open: boolean
  onOpenChange: (open: boolean) => void
  entityId: string
  customers: CustomerSummary[]
  accounts: AccountSummary[]
  onCreated: () => void
}) {
  const styles = useStyles()
  const [customerId, setCustomerId] = useState('')
  const [docDate, setDocDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [reference, setReference] = useState('')
  const [lines, setLines] = useState<DraftLine[]>([{ ...EMPTY }])
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  // Only income accounts make sense on an invoice line.
  const revenueAccounts = useMemo(
    () => accounts.filter((a) => a.isPostable && a.accountType === 'Income'),
    [accounts],
  )

  const customer = customers.find((c) => c.id === customerId)

  const total = useMemo(
    () => lines.reduce((sum, l) => sum + (Number(l.quantity) || 0) * (Number(l.unitPrice) || 0), 0),
    [lines],
  )

  const update = (index: number, patch: Partial<DraftLine>) =>
    setLines((current) => current.map((l, i) => (i === index ? { ...l, ...patch } : l)))

  const valid =
    customerId !== '' &&
    lines.length > 0 &&
    lines.every(
      (l) => l.description.trim() && Number(l.quantity) > 0 && Number(l.unitPrice) > 0 && l.revenueAccountId,
    )

  const submit = async () => {
    setError(null)
    setSaving(true)
    try {
      await createSalesInvoice({
        legalEntityId: entityId,
        customerId,
        docDate,
        reference: reference || undefined,
        lines: lines.map((l) => ({
          description: l.description,
          quantity: Number(l.quantity),
          unitPrice: Number(l.unitPrice),
          revenueAccountId: l.revenueAccountId,
        })),
      })
      setLines([{ ...EMPTY }])
      setCustomerId('')
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
    <Dialog open={open} onOpenChange={(_, data) => onOpenChange(data.open)}>
      <DialogSurface style={{ maxWidth: '900px' }}>
        <DialogBody>
          <DialogTitle>New invoice</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {error && (
                <MessageBar intent="error">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              )}

              <MessageBar intent="info">
                <MessageBarBody>
                  This creates a <b>draft</b>. Drafts are freely editable and are not in the
                  books — no number is taken and nothing is posted until you post it.
                </MessageBarBody>
              </MessageBar>

              <div className={styles.row}>
                <Field label="Customer" required className={styles.grow}>
                  <Dropdown
                    placeholder="Select customer"
                    value={customer ? `${customer.code} — ${customer.name}` : ''}
                    selectedOptions={customerId ? [customerId] : []}
                    onOptionSelect={(_, d) => setCustomerId(d.optionValue ?? '')}
                  >
                    {customers.map((c) => (
                      <Option key={c.id} value={c.id} text={`${c.code} — ${c.name}`}>
                        {c.code} — {c.name} ({c.currencyCode}, {c.creditTermDays}d)
                      </Option>
                    ))}
                  </Dropdown>
                </Field>
                <Field label="Invoice date">
                  <Input type="date" value={docDate} onChange={(_, d) => setDocDate(d.value)} />
                </Field>
                <Field label="Their reference">
                  <Input
                    value={reference}
                    onChange={(_, d) => setReference(d.value)}
                    placeholder="PO number"
                  />
                </Field>
              </div>

              {customer && (
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                  Due date will be set from this customer's {customer.creditTermDays}-day terms.
                </Text>
              )}

              {lines.map((line, index) => (
                <div className={styles.row} key={index}>
                  <Field label={index === 0 ? 'Description' : undefined} className={styles.grow}>
                    <Input
                      value={line.description}
                      onChange={(_, d) => update(index, { description: d.value })}
                      placeholder="What are you billing for?"
                    />
                  </Field>
                  <Field label={index === 0 ? 'Revenue account' : undefined} className={styles.grow}>
                    <Dropdown
                      placeholder="Account"
                      value={
                        revenueAccounts.find((a) => a.id === line.revenueAccountId)
                          ? `${revenueAccounts.find((a) => a.id === line.revenueAccountId)!.code} ${revenueAccounts.find((a) => a.id === line.revenueAccountId)!.name}`
                          : ''
                      }
                      selectedOptions={line.revenueAccountId ? [line.revenueAccountId] : []}
                      onOptionSelect={(_, d) => update(index, { revenueAccountId: d.optionValue ?? '' })}
                    >
                      {revenueAccounts.map((a) => (
                        <Option key={a.id} value={a.id} text={`${a.code} ${a.name}`}>
                          {a.code} — {a.name}
                        </Option>
                      ))}
                    </Dropdown>
                  </Field>
                  <Field label={index === 0 ? 'Qty' : undefined} className={styles.narrow}>
                    <Input
                      type="number"
                      value={line.quantity}
                      onChange={(_, d) => update(index, { quantity: d.value })}
                    />
                  </Field>
                  <Field label={index === 0 ? 'Unit price' : undefined} className={styles.narrow}>
                    <Input
                      type="number"
                      value={line.unitPrice}
                      onChange={(_, d) => update(index, { unitPrice: d.value })}
                      placeholder="0.00"
                    />
                  </Field>
                  <Button
                    icon={<DeleteRegular />}
                    appearance="subtle"
                    aria-label="Remove line"
                    disabled={lines.length <= 1}
                    onClick={() => setLines((c) => c.filter((_, i) => i !== index))}
                  />
                </div>
              ))}

              <Button
                icon={<AddRegular />}
                appearance="subtle"
                onClick={() => setLines((c) => [...c, { ...EMPTY }])}
              >
                Add line
              </Button>

              <div className={styles.total}>
                <Text>Total</Text>
                <Text weight="semibold">
                  {customer?.currencyCode ?? ''} {formatMoney(total)}
                </Text>
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
