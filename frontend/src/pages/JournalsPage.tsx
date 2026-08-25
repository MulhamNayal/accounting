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
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { AddRegular, ArrowUndoRegular, ChevronDownRegular, ChevronRightRegular } from '@fluentui/react-icons'
import { Fragment, useCallback, useEffect, useState } from 'react'
import { getAccounts } from '../api/accounts'
import type { AccountSummary } from '../api/accounts'
import {
  formatMoney,
  getJournalEntries,
  getJournalEntry,
  reverseJournalEntry,
} from '../api/journalEntries'
import type { JournalEntryDetail, JournalEntrySummary } from '../api/journalEntries'
import { NewEntryDialog } from '../components/NewEntryDialog'
import { useLayoutStyles } from '../theme'

const useStyles = makeStyles({
  mono: { fontFamily: tokens.fontFamilyMonospace },
  right: { textAlign: 'right' },
  lineRow: { backgroundColor: tokens.colorNeutralBackground2 },
  reversed: { color: tokens.colorNeutralForeground4, textDecoration: 'line-through' },
  actions: { display: 'flex', gap: tokens.spacingHorizontalXS, justifyContent: 'flex-end' },
})

export function JournalsPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [entries, setEntries] = useState<JournalEntrySummary[]>([])
  const [accounts, setAccounts] = useState<AccountSummary[]>([])
  const [expanded, setExpanded] = useState<Record<string, JournalEntryDetail>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showNew, setShowNew] = useState(false)
  const [reversing, setReversing] = useState<JournalEntrySummary | null>(null)
  const [reason, setReason] = useState('')
  const [reverseError, setReverseError] = useState<string | null>(null)

  const load = useCallback(() => {
    if (!entityId) return
    setLoading(true)
    Promise.all([getJournalEntries(entityId), getAccounts()])
      .then(([loadedEntries, loadedAccounts]) => {
        setEntries(loadedEntries)
        setAccounts(loadedAccounts)
        setExpanded({})
        setError(null)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  useEffect(load, [load])

  const toggle = async (entry: JournalEntrySummary) => {
    if (expanded[entry.id]) {
      setExpanded((current) => {
        const next = { ...current }
        delete next[entry.id]
        return next
      })
      return
    }
    const detail = await getJournalEntry(entry.id)
    setExpanded((current) => ({ ...current, [entry.id]: detail }))
  }

  const confirmReverse = async () => {
    if (!reversing) return
    try {
      await reverseJournalEntry(reversing.id, reason)
      setReversing(null)
      setReason('')
      setReverseError(null)
      load()
    } catch (e: unknown) {
      setReverseError(e instanceof Error ? e.message : String(e))
    }
  }

  if (!entityId) {
    return (
      <div className={layout.page}>
        <Title3>Journals</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Journals</Title3>
        <Caption1 className={layout.subtle}>
          Every entry ever posted. Nothing here can be edited or deleted — a correction is a
          reversal, and both stay visible.
        </Caption1>
      </div>

      <Toolbar aria-label="Journal actions">
        <Button icon={<AddRegular />} appearance="primary" onClick={() => setShowNew(true)}>
          New entry
        </Button>
        <div className={layout.spacer} />
        <Caption1 className={layout.subtle}>{entries.length} entries</Caption1>
      </Toolbar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {loading ? (
        <Spinner label="Loading journals…" />
      ) : (
        <Card>
          <Table size="small" aria-label="Journal entries">
            <TableHeader>
              <TableRow>
                <TableHeaderCell style={{ width: '40px' }} />
                <TableHeaderCell>Entry</TableHeaderCell>
                <TableHeaderCell>Date</TableHeaderCell>
                <TableHeaderCell>Memo</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Amount</TableHeaderCell>
                <TableHeaderCell>Status</TableHeaderCell>
                <TableHeaderCell />
              </TableRow>
            </TableHeader>
            <TableBody>
              {entries.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7}>
                    <Caption1 className={layout.subtle}>
                      No entries yet. Post one with “New entry”.
                    </Caption1>
                  </TableCell>
                </TableRow>
              )}

              {entries.map((entry) => (
                // The key belongs on the fragment: each entry renders its summary row plus
                // any expanded line rows, and keying an inner row breaks reconciliation.
                <Fragment key={entry.id}>
                  <TableRow>
                    <TableCell>
                      <Button
                        appearance="subtle"
                        size="small"
                        aria-label="Show lines"
                        icon={expanded[entry.id] ? <ChevronDownRegular /> : <ChevronRightRegular />}
                        onClick={() => void toggle(entry)}
                      />
                    </TableCell>
                    <TableCell className={styles.mono}>{entry.entryNo}</TableCell>
                    <TableCell>{entry.entryDate}</TableCell>
                    <TableCell className={entry.isReversed ? styles.reversed : undefined}>
                      {entry.memo ?? <span className={layout.subtle}>—</span>}
                    </TableCell>
                    <TableCell className={`${styles.right} ${styles.mono}`}>
                      {formatMoney(entry.totalDebit)}
                    </TableCell>
                    <TableCell>
                      {entry.isReversal && (
                        <Badge appearance="tint" color="warning">Reversal</Badge>
                      )}
                      {entry.isReversed && (
                        <Badge appearance="tint" color="danger">Reversed</Badge>
                      )}
                      {!entry.isReversal && !entry.isReversed && (
                        <Badge appearance="tint" color="success">Posted</Badge>
                      )}
                    </TableCell>
                    <TableCell>
                      <div className={styles.actions}>
                        <Tooltip
                          content={
                            entry.isReversed
                              ? 'Already reversed'
                              : 'Reverse — posts the mirror image, leaves this entry untouched'
                          }
                          relationship="label"
                        >
                          <Button
                            appearance="subtle"
                            size="small"
                            icon={<ArrowUndoRegular />}
                            disabled={entry.isReversed}
                            onClick={() => { setReversing(entry); setReverseError(null) }}
                          />
                        </Tooltip>
                      </div>
                    </TableCell>
                  </TableRow>

                  {expanded[entry.id]?.lines.map((line) => (
                    <TableRow key={line.id} className={styles.lineRow}>
                      <TableCell />
                      <TableCell className={styles.mono}>{line.accountCode}</TableCell>
                      <TableCell colSpan={2}>{line.accountName}</TableCell>
                      <TableCell className={`${styles.right} ${styles.mono}`}>
                        {line.direction === 'Debit' ? formatMoney(line.functionalAmount) : ''}
                      </TableCell>
                      <TableCell className={styles.mono}>
                        {line.direction === 'Credit' ? formatMoney(line.functionalAmount) : ''}
                      </TableCell>
                      <TableCell>
                        <Caption1 className={layout.subtle}>{line.direction}</Caption1>
                      </TableCell>
                    </TableRow>
                  ))}
                </Fragment>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}

      <NewEntryDialog
        open={showNew}
        onOpenChange={setShowNew}
        entityId={entityId}
        accounts={accounts}
        onPosted={load}
      />

      <Dialog open={reversing !== null} onOpenChange={(_, d) => !d.open && setReversing(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Reverse {reversing?.entryNo}</DialogTitle>
            <DialogContent>
              <Caption1 className={layout.subtle}>
                This posts the mirror image as a new entry, dated today. {reversing?.entryNo} is
                left exactly as it was — it cannot be altered.
              </Caption1>
              {reverseError && (
                <MessageBar intent="error" style={{ marginTop: tokens.spacingVerticalM }}>
                  <MessageBarBody>{reverseError}</MessageBarBody>
                </MessageBar>
              )}
              <Field label="Reason" required style={{ marginTop: tokens.spacingVerticalM }}>
                <Input
                  value={reason}
                  onChange={(_, d) => setReason(d.value)}
                  placeholder="Why is this being reversed?"
                />
              </Field>
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setReversing(null)}>Cancel</Button>
              <Button appearance="primary" disabled={!reason.trim()} onClick={confirmReverse}>
                Reverse
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  )
}
