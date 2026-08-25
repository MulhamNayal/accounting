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
import { todayLocal } from '../api/dates'
import { formatMoney, postJournalEntry } from '../api/journalEntries'
import type { PostingLineRequest } from '../api/journalEntries'

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  row: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'end' },
  account: { flexGrow: 1, minWidth: '260px' },
  narrow: { width: '120px' },
  totals: {
    display: 'flex',
    gap: tokens.spacingHorizontalL,
    padding: tokens.spacingVerticalS,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  ok: { color: tokens.colorPaletteGreenForeground1 },
  bad: { color: tokens.colorPaletteRedForeground1 },
})

interface DraftLine {
  accountId: string
  direction: 'Debit' | 'Credit'
  amount: string
  description: string
}

const EMPTY: DraftLine = { accountId: '', direction: 'Debit', amount: '', description: '' }

export function NewEntryDialog({ open, onOpenChange, entityId, accounts, onPosted }: {
  open: boolean
  onOpenChange: (open: boolean) => void
  entityId: string
  accounts: AccountSummary[]
  onPosted: () => void
}) {
  const styles = useStyles()
  const [entryDate, setEntryDate] = useState(todayLocal)
  const [memo, setMemo] = useState('')
  const [lines, setLines] = useState<DraftLine[]>([
    { ...EMPTY, direction: 'Debit' },
    { ...EMPTY, direction: 'Credit' },
  ])
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const postable = useMemo(() => accounts.filter((a) => a.isPostable), [accounts])

  const totals = useMemo(() => {
    let debit = 0
    let credit = 0
    for (const line of lines) {
      const value = Number(line.amount) || 0
      if (line.direction === 'Debit') debit += value
      else credit += value
    }
    return { debit, credit, difference: debit - credit }
  }, [lines])

  const update = (index: number, patch: Partial<DraftLine>) =>
    setLines((current) => current.map((l, i) => (i === index ? { ...l, ...patch } : l)))

  const reset = () => {
    setLines([{ ...EMPTY, direction: 'Debit' }, { ...EMPTY, direction: 'Credit' }])
    setMemo('')
    setError(null)
  }

  const submit = async () => {
    setError(null)
    setSaving(true)
    try {
      const payload: PostingLineRequest[] = lines
        .filter((l) => l.accountId && Number(l.amount) > 0)
        .map((l) => ({
          accountId: l.accountId,
          direction: l.direction,
          amount: Number(l.amount),
          description: l.description || undefined,
        }))

      await postJournalEntry({ legalEntityId: entityId, entryDate, lines: payload, memo: memo || undefined })
      reset()
      onOpenChange(false)
      onPosted()
    } catch (e: unknown) {
      // The server's message is the useful one — it explains which rule was broken.
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  const balanced = totals.difference === 0 && totals.debit > 0

  return (
    <Dialog open={open} onOpenChange={(_, data) => onOpenChange(data.open)}>
      <DialogSurface style={{ maxWidth: '860px' }}>
        <DialogBody>
          <DialogTitle>New journal entry</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {error && (
                <MessageBar intent="error">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              )}

              <div className={styles.row}>
                <Field label="Entry date">
                  <Input type="date" value={entryDate} onChange={(_, d) => setEntryDate(d.value)} />
                </Field>
                <Field label="Memo" style={{ flexGrow: 1 }}>
                  <Input value={memo} onChange={(_, d) => setMemo(d.value)} placeholder="What is this for?" />
                </Field>
              </div>

              {lines.map((line, index) => (
                <div className={styles.row} key={index}>
                  <Field label={index === 0 ? 'Account' : undefined} className={styles.account}>
                    <Dropdown
                      placeholder="Select account"
                      value={
                        postable.find((a) => a.id === line.accountId)
                          ? `${postable.find((a) => a.id === line.accountId)!.code} ${postable.find((a) => a.id === line.accountId)!.name}`
                          : ''
                      }
                      selectedOptions={line.accountId ? [line.accountId] : []}
                      onOptionSelect={(_, d) => update(index, { accountId: d.optionValue ?? '' })}
                    >
                      {postable.map((account) => (
                        <Option key={account.id} value={account.id} text={`${account.code} ${account.name}`}>
                          {account.code} — {account.name}
                        </Option>
                      ))}
                    </Dropdown>
                  </Field>

                  <Field label={index === 0 ? 'Side' : undefined} className={styles.narrow}>
                    <Dropdown
                      value={line.direction}
                      selectedOptions={[line.direction]}
                      onOptionSelect={(_, d) =>
                        update(index, { direction: (d.optionValue as 'Debit' | 'Credit') ?? 'Debit' })}
                    >
                      <Option value="Debit">Debit</Option>
                      <Option value="Credit">Credit</Option>
                    </Dropdown>
                  </Field>

                  <Field label={index === 0 ? 'Amount' : undefined} className={styles.narrow}>
                    <Input
                      type="number"
                      value={line.amount}
                      onChange={(_, d) => update(index, { amount: d.value })}
                      placeholder="0.00"
                    />
                  </Field>

                  <Button
                    icon={<DeleteRegular />}
                    appearance="subtle"
                    aria-label="Remove line"
                    disabled={lines.length <= 2}
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

              <div className={styles.totals}>
                <Text>Debits <b>{formatMoney(totals.debit)}</b></Text>
                <Text>Credits <b>{formatMoney(totals.credit)}</b></Text>
                <Text className={balanced ? styles.ok : styles.bad}>
                  {balanced
                    ? 'Balanced'
                    : `Out by ${formatMoney(Math.abs(totals.difference))}`}
                </Text>
              </div>
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button appearance="primary" disabled={!balanced || saving} onClick={submit}>
              {saving ? 'Posting…' : 'Post entry'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
