import {
  Badge,
  Body1Strong,
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
  MessageBarTitle,
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
  ToolbarDivider,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import {
  AddRegular,
  LockClosedRegular,
  LockOpenRegular,
  ReceiptMoneyRegular,
} from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { useEffect, useState } from 'react'
import { formatMoney } from '../api/journalEntries'
import {
  closePeriod,
  createFiscalYear,
  finaliseFiscalYear,
  getClosingEntryPreview,
  getFiscalYears,
  getPeriodEvents,
  getPeriodReadiness,
  getPeriods,
  periodStateColor,
  periodStateLabel,
  postClosingEntry,
  reopenPeriod,
} from '../api/periods'
import type {
  ClosingEntryPreview,
  FiscalYearSummary,
  PeriodEventSummary,
  PeriodReadiness,
  PeriodSummary,
} from '../api/periods'
import { useLayoutStyles } from '../theme'

const useStyles = makeStyles({
  mono: { fontFamily: tokens.fontFamilyMonospace },
  right: { textAlign: 'right' },
  actions: { display: 'flex', gap: tokens.spacingHorizontalXS, justifyContent: 'flex-end' },
  yearPicker: { minWidth: '220px' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  row: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'end' },
  summary: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXXL,
    padding: tokens.spacingVerticalM,
  },
  stat: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
})

export function PeriodsPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [years, setYears] = useState<FiscalYearSummary[]>([])
  const [selectedYearId, setSelectedYearId] = useState<string | null>(null)
  const [periods, setPeriods] = useState<PeriodSummary[]>([])
  const [events, setEvents] = useState<PeriodEventSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [showNewYear, setShowNewYear] = useState(false)
  const [closing, setClosing] = useState<PeriodSummary | null>(null)
  const [reopening, setReopening] = useState<PeriodSummary | null>(null)
  const [finalising, setFinalising] = useState(false)
  const [preview, setPreview] = useState<ClosingEntryPreview | null>(null)

  // Bumped after any action, to refetch both of the loads below. The two are separate
  // because changing the selected year should reload its periods without refetching the
  // year list — and because a single effect that both reads and sets the selection would
  // retrigger itself.
  const [refresh, setRefresh] = useState(0)
  const load = () => setRefresh((n) => n + 1)

  useEffect(() => {
    if (!entityId) return
    let cancelled = false

    getFiscalYears(entityId)
      .then((loaded) => {
        if (cancelled) return
        setYears(loaded)
        setSelectedYearId((current) =>
          loaded.some((y) => y.id === current) ? current : (loaded[0]?.id ?? null),
        )
        setError(null)
      })
      .catch((e: unknown) => {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      })

    return () => {
      cancelled = true
    }
  }, [entityId, refresh])

  useEffect(() => {
    if (!entityId || !selectedYearId) {
      setPeriods([])
      setEvents([])
      setLoading(false)
      return
    }

    let cancelled = false
    setLoading(true)

    Promise.all([getPeriods(entityId, selectedYearId), getPeriodEvents(entityId, selectedYearId)])
      .then(([loadedPeriods, loadedEvents]) => {
        if (cancelled) return
        setPeriods(loadedPeriods)
        setEvents(loadedEvents)
        setError(null)
      })
      .catch((e: unknown) => {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [entityId, selectedYearId, refresh])

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true)
    setError(null)
    try {
      await action()
      load()
      return true
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
      return false
    } finally {
      setBusy(false)
    }
  }

  const openPreview = async () => {
    if (!selectedYearId) return
    setError(null)
    try {
      setPreview(await getClosingEntryPreview(selectedYearId))
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }

  if (!entityId) {
    return (
      <div className={layout.page}>
        <Title3>Periods</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  const year = years.find((y) => y.id === selectedYearId) ?? null

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Periods</Title3>
        <Caption1 className={layout.subtle}>
          Posting is permitted only into an open period, and the period is taken from the
          entry's date rather than from today. Every close and reopen is recorded with who did
          it and why — the database refuses a state change that is not.
        </Caption1>
      </div>

      <Toolbar aria-label="Period actions">
        <Dropdown
          className={styles.yearPicker}
          aria-label="Fiscal year"
          placeholder="No fiscal year yet"
          value={year ? `${year.code} (${year.startDate} to ${year.endDate})` : ''}
          selectedOptions={year ? [year.id] : []}
          onOptionSelect={(_, data) => data.optionValue && setSelectedYearId(data.optionValue)}
        >
          {years.map((y) => (
            <Option key={y.id} value={y.id} text={`${y.code} (${y.startDate} to ${y.endDate})`}>
              {y.code} — {y.startDate} to {y.endDate}
            </Option>
          ))}
        </Dropdown>

        <ToolbarDivider />

        <Button icon={<AddRegular />} onClick={() => setShowNewYear(true)}>
          New fiscal year
        </Button>

        {year && year.state !== 'HardClosed' && (
          <Button icon={<ReceiptMoneyRegular />} onClick={() => void openPreview()}>
            Year-end close
          </Button>
        )}

        <div className={layout.spacer} />

        {year && (
          <Badge appearance="tint" color={periodStateColor[year.state]}>
            {periodStateLabel[year.state]}
          </Badge>
        )}
      </Toolbar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {loading ? (
        <Spinner label="Loading periods…" />
      ) : !year ? (
        <MessageBar intent="info">
          <MessageBarBody>
            <MessageBarTitle>No fiscal year yet</MessageBarTitle>
            Nothing can be posted until a year exists, because a posting has to land in a
            period. Create one with “New fiscal year”.
          </MessageBarBody>
        </MessageBar>
      ) : (
        <>
          <Card>
            <div className={styles.summary}>
              <div className={styles.stat}>
                <Caption1 className={layout.subtle}>Periods</Caption1>
                <Body1Strong>
                  {year.periodCount} — {year.openPeriodCount} open
                </Body1Strong>
              </div>
              <div className={styles.stat}>
                <Caption1 className={layout.subtle}>Closing entry</Caption1>
                <Body1Strong className={styles.mono}>
                  {year.closingEntryNo ?? '—'}
                </Body1Strong>
              </div>
              <div className={styles.stat}>
                <Caption1 className={layout.subtle}>Year</Caption1>
                <Body1Strong>{periodStateLabel[year.state]}</Body1Strong>
              </div>
              <div className={layout.spacer} />
              {year.canFinalise && (
                <Button
                  appearance="primary"
                  icon={<LockClosedRegular />}
                  disabled={busy}
                  onClick={() => setFinalising(true)}
                >
                  Finalise {year.code}
                </Button>
              )}
            </div>
          </Card>

          {year.state === 'HardClosed' && (
            <MessageBar intent="info">
              <MessageBarBody>
                {year.code} is filed. There is no transition out of hard closed anywhere in
                this system — not a permission that could be granted, and nothing the database
                will accept.
              </MessageBarBody>
            </MessageBar>
          )}

          <Card>
            <Table size="small" aria-label="Periods">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell style={{ width: '60px' }}>#</TableHeaderCell>
                  <TableHeaderCell>From</TableHeaderCell>
                  <TableHeaderCell>To</TableHeaderCell>
                  <TableHeaderCell className={styles.right}>Entries</TableHeaderCell>
                  <TableHeaderCell>State</TableHeaderCell>
                  <TableHeaderCell />
                </TableRow>
              </TableHeader>
              <TableBody>
                {periods.map((period) => (
                  <TableRow key={period.id}>
                    <TableCell className={styles.mono}>{period.sequence}</TableCell>
                    <TableCell>{period.startDate}</TableCell>
                    <TableCell>{period.endDate}</TableCell>
                    <TableCell className={`${styles.right} ${styles.mono}`}>
                      {period.entryCount || ''}
                    </TableCell>
                    <TableCell>
                      <Badge appearance="tint" color={periodStateColor[period.state]}>
                        {periodStateLabel[period.state]}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <div className={styles.actions}>
                        {period.state === 'Open' && (
                          <Tooltip
                            content="Close — stops anything being posted into this period"
                            relationship="label"
                          >
                            <Button
                              appearance="subtle"
                              size="small"
                              icon={<LockClosedRegular />}
                              disabled={busy}
                              onClick={() => setClosing(period)}
                            >
                              Close
                            </Button>
                          </Tooltip>
                        )}
                        {period.state === 'SoftClosed' && (
                          <Tooltip
                            content="Reopen — recorded with a reason, permanently"
                            relationship="label"
                          >
                            <Button
                              appearance="subtle"
                              size="small"
                              icon={<LockOpenRegular />}
                              disabled={busy}
                              onClick={() => setReopening(period)}
                            >
                              Reopen
                            </Button>
                          </Tooltip>
                        )}
                        {period.state === 'HardClosed' && (
                          <Caption1 className={layout.subtle}>filed</Caption1>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Card>

          <div className={layout.pageHeader}>
            <Body1Strong>History</Body1Strong>
            <Caption1 className={layout.subtle}>
              Append-only. The application has no privilege to change or remove a row here,
              which is the whole point of it.
            </Caption1>
          </div>

          <Card>
            <Table size="small" aria-label="Period history">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>When</TableHeaderCell>
                  <TableHeaderCell style={{ width: '60px' }}>#</TableHeaderCell>
                  <TableHeaderCell>Change</TableHeaderCell>
                  <TableHeaderCell>By</TableHeaderCell>
                  <TableHeaderCell>Reason</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {events.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={5}>
                      <Caption1 className={layout.subtle}>
                        Nothing has been closed or reopened yet.
                      </Caption1>
                    </TableCell>
                  </TableRow>
                )}

                {events.map((event) => (
                  <TableRow key={event.id}>
                    <TableCell>{new Date(event.atUtc).toLocaleString()}</TableCell>
                    <TableCell className={styles.mono}>{event.periodSequence}</TableCell>
                    <TableCell>
                      {periodStateLabel[event.fromState]} → {periodStateLabel[event.toState]}
                    </TableCell>
                    <TableCell>{event.byUser}</TableCell>
                    <TableCell>{event.reason}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Card>
        </>
      )}

      <NewYearDialog
        open={showNewYear}
        onOpenChange={setShowNewYear}
        entityId={entityId}
        onCreated={(created) => {
          setSelectedYearId(created.id)
          load()
        }}
      />

      {closing && (
        <ClosePeriodDialog
          period={closing}
          onCancel={() => setClosing(null)}
          onConfirm={async (reason) => {
            if (await run(() => closePeriod(closing.id, reason))) setClosing(null)
          }}
          busy={busy}
        />
      )}

      {reopening && (
        <ReasonDialog
          title={`Reopen period ${reopening.sequence}`}
          confirmLabel="Reopen"
          busy={busy}
          onCancel={() => setReopening(null)}
          onConfirm={async (reason) => {
            if (await run(() => reopenPeriod(reopening.id, reason))) setReopening(null)
          }}
        >
          <MessageBar intent="warning">
            <MessageBarBody>
              Reopening is recorded permanently, with your name and this reason. That is what
              an auditor looks for, so say something they would find useful.
            </MessageBarBody>
          </MessageBar>
        </ReasonDialog>
      )}

      {finalising && year && (
        <ReasonDialog
          title={`Finalise ${year.code}`}
          confirmLabel="Finalise the year"
          busy={busy}
          onCancel={() => setFinalising(false)}
          onConfirm={async (reason) => {
            if (await run(() => finaliseFiscalYear(year.id, reason))) setFinalising(false)
          }}
        >
          <MessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>This cannot be undone</MessageBarTitle>
              Every period in {year.code} becomes hard closed, and nothing in this system —
              application or database — will move it back. Do this when the year is filed.
            </MessageBarBody>
          </MessageBar>
        </ReasonDialog>
      )}

      {preview && (
        <ClosingEntryDialog
          preview={preview}
          busy={busy}
          onCancel={() => setPreview(null)}
          onConfirm={async (memo) => {
            if (await run(() => postClosingEntry(preview.fiscalYearId, memo))) setPreview(null)
          }}
        />
      )}
    </div>
  )
}

// ---------------------------------------------------------------- dialogs

/**
 * A confirmation that will not proceed without a reason.
 *
 * Shared by reopen and finalise rather than generalised into `components/`, because the
 * mandatory-reason shape is specific to how this page reads the period trail.
 */
function ReasonDialog({
  title,
  confirmLabel,
  busy,
  children,
  onCancel,
  onConfirm,
}: {
  title: string
  confirmLabel: string
  busy: boolean
  children?: ReactNode
  onCancel: () => void
  onConfirm: (reason: string) => void
}) {
  const styles = useStyles()
  const [reason, setReason] = useState('')

  return (
    <Dialog open onOpenChange={(_, data) => !data.open && onCancel()}>
      <DialogSurface style={{ maxWidth: '560px' }}>
        <DialogBody>
          <DialogTitle>{title}</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {children}
              <Field label="Reason" required>
                <Input
                  value={reason}
                  onChange={(_, data) => setReason(data.value)}
                  placeholder="Why is this happening?"
                />
              </Field>
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onCancel}>
              Cancel
            </Button>
            <Button
              appearance="primary"
              disabled={!reason.trim() || busy}
              onClick={() => onConfirm(reason.trim())}
            >
              {busy ? 'Working…' : confirmLabel}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

/** Close a period, showing what the close would leave behind. */
function ClosePeriodDialog({
  period,
  busy,
  onCancel,
  onConfirm,
}: {
  period: PeriodSummary
  busy: boolean
  onCancel: () => void
  onConfirm: (reason: string) => void
}) {
  const layout = useLayoutStyles()
  const [readiness, setReadiness] = useState<PeriodReadiness | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    getPeriodReadiness(period.id)
      .then(setReadiness)
      .catch((e: unknown) => setLoadError(e instanceof Error ? e.message : String(e)))
  }, [period.id])

  return (
    <ReasonDialog
      title={`Close period ${period.sequence} — ${period.startDate} to ${period.endDate}`}
      confirmLabel="Close the period"
      busy={busy}
      onCancel={onCancel}
      onConfirm={onConfirm}
    >
      {loadError && (
        <MessageBar intent="error">
          <MessageBarBody>{loadError}</MessageBarBody>
        </MessageBar>
      )}

      {!readiness && !loadError && <Spinner size="tiny" label="Checking…" />}

      {readiness?.blockers.map((blocker) => (
        <MessageBar key={blocker} intent="error">
          <MessageBarBody>{blocker}</MessageBarBody>
        </MessageBar>
      ))}

      {readiness && readiness.draftCount > 0 && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>
              {readiness.draftCount} draft
              {readiness.draftCount === 1 ? '' : 's'} dated in this period
            </MessageBarTitle>
            {readiness.drafts.map((d) => `${d.count} ${d.documentType.toLowerCase()}`).join(', ')}.
            A draft is not in the books, but once the period is closed there is no posting one —
            so deal with them first if they matter.
          </MessageBarBody>
        </MessageBar>
      )}

      {readiness && (
        <Caption1 className={layout.subtle}>
          {readiness.postedEntryCount} posted{' '}
          {readiness.postedEntryCount === 1 ? 'entry' : 'entries'} in this period.
        </Caption1>
      )}
    </ReasonDialog>
  )
}

/** What the year-end entry would post, and the confirmation to post it. */
function ClosingEntryDialog({
  preview,
  busy,
  onCancel,
  onConfirm,
}: {
  preview: ClosingEntryPreview
  busy: boolean
  onCancel: () => void
  onConfirm: (memo?: string) => void
}) {
  const layout = useLayoutStyles()
  const styles = useStyles()
  const [memo, setMemo] = useState('')

  return (
    <Dialog open onOpenChange={(_, data) => !data.open && onCancel()}>
      <DialogSurface style={{ maxWidth: '760px' }}>
        <DialogBody>
          <DialogTitle>Close off {preview.fiscalYearCode}</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {preview.blockers.map((blocker) => (
                <MessageBar key={blocker} intent="error">
                  <MessageBarBody>{blocker}</MessageBarBody>
                </MessageBar>
              ))}

              {preview.canPost && (
                <MessageBar intent="info">
                  <MessageBarBody>
                    An ordinary journal entry, dated {preview.entryDate} and reversible like
                    any other. The year stays open until you finalise it separately.
                  </MessageBarBody>
                </MessageBar>
              )}

              {preview.lines.length > 0 && (
                <>
                  <Table size="small" aria-label="Closing entry">
                    <TableHeader>
                      <TableRow>
                        <TableHeaderCell>Code</TableHeaderCell>
                        <TableHeaderCell>Account</TableHeaderCell>
                        <TableHeaderCell className={styles.right}>Debit</TableHeaderCell>
                        <TableHeaderCell className={styles.right}>Credit</TableHeaderCell>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {preview.lines.map((line) => (
                        <TableRow key={line.accountId}>
                          <TableCell className={styles.mono}>{line.accountCode}</TableCell>
                          <TableCell>{line.accountName}</TableCell>
                          <TableCell className={`${styles.right} ${styles.mono}`}>
                            {line.direction === 'Debit' ? formatMoney(line.amount) : ''}
                          </TableCell>
                          <TableCell className={`${styles.right} ${styles.mono}`}>
                            {line.direction === 'Credit' ? formatMoney(line.amount) : ''}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>

                  <Text className={layout.subtle}>
                    Income {formatMoney(preview.totalIncome)}, expenses{' '}
                    {formatMoney(preview.totalExpense)} — a{' '}
                    {preview.netResult >= 0 ? 'profit' : 'loss'} of{' '}
                    {formatMoney(Math.abs(preview.netResult))} {preview.currencyCode}, to
                    account {preview.retainedEarningsAccountCode}.
                  </Text>
                </>
              )}

              <Field label="Memo">
                <Input
                  value={memo}
                  onChange={(_, data) => setMemo(data.value)}
                  placeholder={`Transfer of the ${preview.fiscalYearCode} result to retained earnings`}
                />
              </Field>
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onCancel}>
              Cancel
            </Button>
            <Button
              appearance="primary"
              disabled={!preview.canPost || busy}
              onClick={() => onConfirm(memo.trim() || undefined)}
            >
              {busy ? 'Posting…' : 'Post the closing entry'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

/** Create a fiscal year and generate its periods. */
function NewYearDialog({
  open,
  onOpenChange,
  entityId,
  onCreated,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  entityId: string
  onCreated: (year: FiscalYearSummary) => void
}) {
  const styles = useStyles()
  const layout = useLayoutStyles()

  const [code, setCode] = useState('')
  const [startDate, setStartDate] = useState('')
  const [endDate, setEndDate] = useState('')
  const [periodCount, setPeriodCount] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    setError(null)
    setSaving(true)
    try {
      const created = await createFiscalYear({
        legalEntityId: entityId,
        code: code.trim(),
        startDate,
        endDate,
        periodCount: periodCount ? Number(periodCount) : undefined,
      })
      setCode('')
      setStartDate('')
      setEndDate('')
      setPeriodCount('')
      onOpenChange(false)
      onCreated(created)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  const complete = code.trim() && startDate && endDate

  return (
    <Dialog open={open} onOpenChange={(_, data) => onOpenChange(data.open)}>
      <DialogSurface style={{ maxWidth: '560px' }}>
        <DialogBody>
          <DialogTitle>New fiscal year</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {error && (
                <MessageBar intent="error">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              )}

              <Field label="Code" required>
                <Input
                  value={code}
                  onChange={(_, data) => setCode(data.value)}
                  placeholder="FY2027"
                />
              </Field>

              <div className={styles.row}>
                <Field label="Starts" required>
                  <Input
                    type="date"
                    value={startDate}
                    onChange={(_, data) => setStartDate(data.value)}
                  />
                </Field>
                <Field label="Ends" required>
                  <Input
                    type="date"
                    value={endDate}
                    onChange={(_, data) => setEndDate(data.value)}
                  />
                </Field>
                <Field label="Periods">
                  <Input
                    type="number"
                    value={periodCount}
                    onChange={(_, data) => setPeriodCount(data.value)}
                    placeholder="12"
                  />
                </Field>
              </div>

              <Caption1 className={layout.subtle}>
                Leave the period count empty for calendar months, which is what a normal year
                wants — a year starting mid-month simply gets a short first period. Set it to
                divide the year into equal spans instead, for example 13 for a 52/53-week year.
              </Caption1>
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button appearance="primary" disabled={!complete || saving} onClick={submit}>
              {saving ? 'Creating…' : 'Create the year'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
