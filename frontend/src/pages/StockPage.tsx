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
  MessageBarTitle,
  Option,
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
  ArrowDownloadRegular,
  ArrowUploadRegular,
  ChevronDownRegular,
  ChevronRightRegular,
  EditRegular,
} from '@fluentui/react-icons'
import { Fragment, useCallback, useEffect, useMemo, useState } from 'react'
import { getAccounts } from '../api/accounts'
import type { AccountSummary } from '../api/accounts'
import { todayLocal } from '../api/dates'
import { formatMoney } from '../api/journalEntries'
import {
  adjustCost,
  getItems,
  getLayers,
  getOnHand,
  issueStock,
  receiveStock,
} from '../api/stock'
import type {
  CostAdjustmentResult,
  CostLayerDetail,
  ItemSummary,
  StockIssueResult,
  StockOnHand,
} from '../api/stock'
import { useLayoutStyles } from '../theme'

const useStyles = makeStyles({
  mono: { fontFamily: tokens.fontFamilyMonospace },
  right: { textAlign: 'right' },
  layerRow: { backgroundColor: tokens.colorNeutralBackground2 },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  row: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'end' },
  narrow: { width: '130px' },
  grow: { flexGrow: 1, minWidth: '200px' },
})

const today = todayLocal

export function StockPage({ entityId }: { entityId: string | null }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [onHand, setOnHand] = useState<StockOnHand[]>([])
  const [items, setItems] = useState<ItemSummary[]>([])
  const [accounts, setAccounts] = useState<AccountSummary[]>([])
  const [layers, setLayers] = useState<Record<string, CostLayerDetail[]>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [note, setNote] = useState<string | null>(null)
  const [dialog, setDialog] = useState<'receive' | 'issue' | null>(null)
  const [adjusting, setAdjusting] = useState<{ layer: CostLayerDetail; itemCode: string } | null>(null)

  const load = useCallback(() => {
    if (!entityId) return
    setLoading(true)
    Promise.all([getOnHand(entityId), getItems(), getAccounts()])
      .then(([h, i, a]) => {
        setOnHand(h)
        setItems(i)
        setAccounts(a)
        setLayers({})
        setError(null)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [entityId])

  useEffect(load, [load])

  const expand = async (itemId: string) => {
    const detail = await getLayers(entityId!, itemId)
    setLayers((current) => ({ ...current, [itemId]: detail }))
  }

  if (!entityId) {
    return (
      <div className={layout.page}>
        <Title3>Stock</Title3>
        <MessageBar intent="warning">
          <MessageBarBody>Select an entity first.</MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Stock</Title3>
        <Caption1 className={layout.subtle}>
          Quantity comes from the movements and value from the cost layers not yet consumed.
          Nothing is stored, so stock on hand and the inventory account cannot disagree.
        </Caption1>
      </div>

      <Toolbar aria-label="Stock actions">
        <Button
          icon={<ArrowDownloadRegular />}
          appearance="primary"
          disabled={items.length === 0}
          onClick={() => setDialog('receive')}
        >
          Receive
        </Button>
        <Button
          icon={<ArrowUploadRegular />}
          disabled={onHand.every((h) => h.quantityOnHand <= 0)}
          onClick={() => setDialog('issue')}
        >
          Issue
        </Button>
        <div className={layout.spacer} />
        <Caption1 className={layout.subtle}>
          {items.length} items, {formatMoney(onHand.reduce((s, h) => s + h.valueOnHand, 0))} on hand
        </Caption1>
      </Toolbar>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      {note && (
        <MessageBar intent="success">
          <MessageBarBody>{note}</MessageBarBody>
        </MessageBar>
      )}

      {items.length === 0 && !loading && (
        <MessageBar intent="info">
          <MessageBarBody>
            <MessageBarTitle>No items yet</MessageBarTitle>
            An item needs a stock control account for its value and an expense account for
            cost of sales. Create one through <code>POST /api/items</code> — there is no UI
            for item setup yet.
          </MessageBarBody>
        </MessageBar>
      )}

      {loading ? (
        <Spinner label="Loading stock…" />
      ) : (
        <Card>
          <Table size="small" aria-label="Stock on hand">
            <TableHeader>
              <TableRow>
                <TableHeaderCell style={{ width: '40px' }} />
                <TableHeaderCell>Item</TableHeaderCell>
                <TableHeaderCell className={styles.right}>On hand</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Value</TableHeaderCell>
                <TableHeaderCell className={styles.right}>Average cost</TableHeaderCell>
                <TableHeaderCell />
              </TableRow>
            </TableHeader>
            <TableBody>
              {onHand.map((item) => (
                <Fragment key={item.itemId}>
                  <TableRow>
                    <TableCell>
                      <Button
                        appearance="subtle"
                        size="small"
                        aria-label="Show cost layers"
                        icon={layers[item.itemId] ? <ChevronDownRegular /> : <ChevronRightRegular />}
                        onClick={() =>
                          layers[item.itemId]
                            ? setLayers((c) => {
                                const next = { ...c }
                                delete next[item.itemId]
                                return next
                              })
                            : void expand(item.itemId)}
                      />
                    </TableCell>
                    <TableCell>
                      <span className={styles.mono}>{item.itemCode}</span> {item.itemName}
                    </TableCell>
                    <TableCell className={`${styles.right} ${styles.mono}`}>
                      {item.quantityOnHand} {item.baseUom}
                    </TableCell>
                    <TableCell className={`${styles.right} ${styles.mono}`}>
                      {formatMoney(item.valueOnHand)}
                    </TableCell>
                    <TableCell className={`${styles.right} ${styles.mono}`}>
                      {item.averageUnitCost !== null ? formatMoney(item.averageUnitCost) : ''}
                    </TableCell>
                    <TableCell />
                  </TableRow>

                  {layers[item.itemId]?.length === 0 && (
                    <TableRow className={styles.layerRow}>
                      <TableCell />
                      <TableCell colSpan={5}>
                        <Caption1 className={layout.subtle}>
                          No unconsumed layers — everything received has been issued.
                        </Caption1>
                      </TableCell>
                    </TableRow>
                  )}

                  {layers[item.itemId]?.map((layer) => (
                    <TableRow key={layer.id} className={styles.layerRow}>
                      <TableCell />
                      <TableCell>
                        <Caption1 className={layout.subtle}>
                          #{layer.sequence} received {layer.receivedOn}
                          {layer.adjustsLayerId && (
                            <Badge appearance="tint" color="warning" style={{ marginLeft: 8 }}>
                              cost correction
                            </Badge>
                          )}
                        </Caption1>
                      </TableCell>
                      <TableCell className={`${styles.right} ${styles.mono}`}>
                        {layer.quantityRemaining} of {layer.quantityReceived}
                      </TableCell>
                      <TableCell className={`${styles.right} ${styles.mono}`}>
                        {formatMoney(layer.valueRemaining)}
                      </TableCell>
                      <TableCell className={`${styles.right} ${styles.mono}`}>
                        {formatMoney(layer.unitCost)}
                      </TableCell>
                      <TableCell className={styles.right}>
                        {!layer.adjustsLayerId && (
                          <Tooltip
                            content="Correct this cost — posts the difference, never edits the layer"
                            relationship="label"
                          >
                            <Button
                              appearance="subtle"
                              size="small"
                              icon={<EditRegular />}
                              onClick={() => setAdjusting({ layer, itemCode: item.itemCode })}
                            />
                          </Tooltip>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </Fragment>
              ))}

              {onHand.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6}>
                    <Caption1 className={layout.subtle}>Nothing in stock.</Caption1>
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </Card>
      )}

      {dialog && (
        <MovementDialog
          kind={dialog}
          entityId={entityId}
          items={items}
          accounts={accounts}
          onHand={onHand}
          onClose={() => setDialog(null)}
          onDone={(message) => {
            setNote(message)
            load()
          }}
        />
      )}

      {adjusting && (
        <AdjustCostDialog
          entityId={entityId}
          layer={adjusting.layer}
          itemCode={adjusting.itemCode}
          accounts={accounts}
          onClose={() => setAdjusting(null)}
          onDone={(message) => {
            setNote(message)
            load()
          }}
        />
      )}
    </div>
  )
}

function MovementDialog({ kind, entityId, items, accounts, onHand, onClose, onDone }: {
  kind: 'receive' | 'issue'
  entityId: string
  items: ItemSummary[]
  accounts: AccountSummary[]
  onHand: StockOnHand[]
  onClose: () => void
  onDone: (message: string) => void
}) {
  const styles = useStyles()
  const layout = useLayoutStyles()

  const [itemId, setItemId] = useState('')
  const [quantity, setQuantity] = useState('')
  const [unitCost, setUnitCost] = useState('')
  const [creditAccountId, setCreditAccountId] = useState('')
  const [movedOn, setMovedOn] = useState(today)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [result, setResult] = useState<StockIssueResult | null>(null)

  // Control accounts are excluded: a posting to one must carry its dimension, and there is
  // no supplier master yet to name. Trade payables becomes selectable when AP arrives; until
  // then a receipt credits an accrual, which is what goods-received-not-invoiced means anyway.
  const creditAccounts = useMemo(
    () => accounts.filter(
      (a) => a.isPostable && a.accountType === 'Liability' && a.controlType === 'None'),
    [accounts],
  )

  const item = items.find((i) => i.id === itemId)
  const available = onHand.find((h) => h.itemId === itemId)?.quantityOnHand ?? 0

  const valid =
    itemId !== '' &&
    Number(quantity) > 0 &&
    (kind === 'issue' ? Number(quantity) <= available : Number(unitCost) > 0 && creditAccountId !== '')

  const submit = async () => {
    setError(null)
    setSaving(true)
    try {
      if (kind === 'receive') {
        await receiveStock({
          legalEntityId: entityId,
          itemId,
          quantity: Number(quantity),
          unitCost: Number(unitCost),
          movedOn,
          creditAccountId,
        })
        onClose()
        onDone(`Received ${quantity} ${item?.baseUom} of ${item?.code} at ${unitCost} each.`)
      } else {
        const issued = await issueStock({
          legalEntityId: entityId,
          itemId,
          quantity: Number(quantity),
          movedOn,
        })
        // Held open so the layer breakdown can be read — that is the point of FIFO.
        setResult(issued)
        onDone(`Issued ${quantity} of ${item?.code} at a cost of ${formatMoney(issued.totalCost)}.`)
      }
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>{kind === 'receive' ? 'Receive stock' : 'Issue stock'}</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {error && (
                <MessageBar intent="error">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              )}

              {result ? (
                <>
                  <MessageBar intent="success">
                    <MessageBarBody>
                      <MessageBarTitle>
                        Cost of sales {formatMoney(result.totalCost)}
                      </MessageBarTitle>
                      Taken from {result.consumed.length} layer
                      {result.consumed.length === 1 ? '' : 's'}, oldest first.
                    </MessageBarBody>
                  </MessageBar>
                  <Table size="small" aria-label="Layers consumed">
                    <TableHeader>
                      <TableRow>
                        <TableHeaderCell>Layer</TableHeaderCell>
                        <TableHeaderCell className={styles.right}>Quantity</TableHeaderCell>
                        <TableHeaderCell className={styles.right}>Unit cost</TableHeaderCell>
                        <TableHeaderCell className={styles.right}>Amount</TableHeaderCell>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {result.consumed.map((c) => (
                        <TableRow key={c.costLayerId}>
                          <TableCell className={styles.mono}>#{c.layerSequence}</TableCell>
                          <TableCell className={`${styles.right} ${styles.mono}`}>{c.quantity}</TableCell>
                          <TableCell className={`${styles.right} ${styles.mono}`}>
                            {formatMoney(c.unitCost)}
                          </TableCell>
                          <TableCell className={`${styles.right} ${styles.mono}`}>
                            {formatMoney(c.amount)}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                  <Caption1 className={layout.subtle}>
                    This is what makes the figure explainable rather than merely calculated —
                    the exact receipts the cost came from.
                  </Caption1>
                </>
              ) : (
                <>
                  <Field label="Item" required>
                    <Dropdown
                      placeholder="Select item"
                      value={item ? `${item.code} — ${item.name}` : ''}
                      selectedOptions={itemId ? [itemId] : []}
                      onOptionSelect={(_, d) => setItemId(d.optionValue ?? '')}
                    >
                      {items.map((i) => (
                        <Option key={i.id} value={i.id} text={`${i.code} — ${i.name}`}>
                          {i.code} — {i.name}
                        </Option>
                      ))}
                    </Dropdown>
                  </Field>

                  {kind === 'issue' && itemId && (
                    <Caption1 className={layout.subtle}>
                      {available} {item?.baseUom} on hand.
                    </Caption1>
                  )}

                  <div className={styles.row}>
                    <Field label="Quantity" required className={styles.narrow}>
                      <Input
                        type="number"
                        value={quantity}
                        onChange={(_, d) => setQuantity(d.value)}
                      />
                    </Field>
                    {kind === 'receive' && (
                      <Field label="Unit cost" required className={styles.narrow}>
                        <Input
                          type="number"
                          value={unitCost}
                          onChange={(_, d) => setUnitCost(d.value)}
                          placeholder="0.00"
                        />
                      </Field>
                    )}
                    <Field label="Date">
                      <Input type="date" value={movedOn} onChange={(_, d) => setMovedOn(d.value)} />
                    </Field>
                  </div>

                  {kind === 'receive' && (
                    <Field label="Credit to" required>
                      <Dropdown
                        placeholder="Trade payables or an accrual"
                        value={
                          creditAccounts.find((a) => a.id === creditAccountId)
                            ? `${creditAccounts.find((a) => a.id === creditAccountId)!.code} — ${creditAccounts.find((a) => a.id === creditAccountId)!.name}`
                            : ''
                        }
                        selectedOptions={creditAccountId ? [creditAccountId] : []}
                        onOptionSelect={(_, d) => setCreditAccountId(d.optionValue ?? '')}
                      >
                        {creditAccounts.map((a) => (
                          <Option key={a.id} value={a.id} text={`${a.code} — ${a.name}`}>
                            {a.code} — {a.name}
                          </Option>
                        ))}
                      </Dropdown>
                    </Field>
                  )}

                  {kind === 'issue' && (
                    <Caption1 className={layout.subtle}>
                      Cost comes from the oldest layers with quantity remaining. You will see
                      which ones after issuing.
                    </Caption1>
                  )}
                </>
              )}
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onClose}>
              {result ? 'Close' : 'Cancel'}
            </Button>
            {!result && (
              <Button appearance="primary" disabled={!valid || saving} onClick={submit}>
                {saving ? 'Posting…' : kind === 'receive' ? 'Receive' : 'Issue'}
              </Button>
            )}
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

function AdjustCostDialog({ entityId, layer, itemCode, accounts, onClose, onDone }: {
  entityId: string
  layer: CostLayerDetail
  itemCode: string
  accounts: AccountSummary[]
  onClose: () => void
  onDone: (message: string) => void
}) {
  const styles = useStyles()
  const layout = useLayoutStyles()

  const [correctedUnitCost, setCorrectedUnitCost] = useState(String(layer.unitCost))
  const [counterAccountId, setCounterAccountId] = useState('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [result, setResult] = useState<CostAdjustmentResult | null>(null)

  const counterAccounts = useMemo(
    () => accounts.filter((a) => a.isPostable && a.accountType === 'Liability'),
    [accounts],
  )

  const counter = counterAccounts.find((a) => a.id === counterAccountId)
  const valid = Number(correctedUnitCost) > 0 && Number(correctedUnitCost) !== layer.unitCost && counterAccountId !== ''

  const submit = async () => {
    setError(null)
    setSaving(true)
    try {
      const adjusted = await adjustCost({
        legalEntityId: entityId,
        costLayerId: layer.id,
        correctedUnitCost: Number(correctedUnitCost),
        adjustedOn: today(),
        counterAccountId,
        reason: reason || undefined,
      })
      setResult(adjusted)
      onDone(`Cost of ${itemCode} corrected to ${correctedUnitCost}.`)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Correct the cost of layer #{layer.sequence}</DialogTitle>
          <DialogContent>
            <div className={styles.form}>
              {error && (
                <MessageBar intent="error">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              )}

              {result ? (
                <MessageBar intent="success">
                  <MessageBarBody>
                    <MessageBarTitle>
                      {result.difference > 0 ? 'Cost increased' : 'Cost decreased'} by{' '}
                      {formatMoney(Math.abs(result.difference))} per unit
                    </MessageBarTitle>
                    {formatMoney(Math.abs(result.inventoryAdjustment))} on the{' '}
                    {result.quantityStillOnHand} still held went to inventory;{' '}
                    {formatMoney(Math.abs(result.costOfSalesAdjustment))} on the{' '}
                    {result.quantityAlreadySold} already sold went to cost of sales, in the
                    current period. The original layer is unchanged.
                  </MessageBarBody>
                </MessageBar>
              ) : (
                <>
                  <MessageBar intent="info">
                    <MessageBarBody>
                      This posts the difference; it does not edit the layer. Whatever is still
                      held adjusts inventory, whatever has been sold adjusts cost of sales —
                      prior periods stand as reported.
                    </MessageBarBody>
                  </MessageBar>

                  <Caption1 className={layout.subtle}>
                    Currently {formatMoney(layer.unitCost)} per unit ·{' '}
                    {layer.quantityRemaining} of {layer.quantityReceived} still on hand
                  </Caption1>

                  <Field label="Corrected unit cost" required>
                    <Input
                      type="number"
                      value={correctedUnitCost}
                      onChange={(_, d) => setCorrectedUnitCost(d.value)}
                    />
                  </Field>

                  <Field label="Charge the difference to" required>
                    <Dropdown
                      placeholder="Usually trade payables"
                      value={counter ? `${counter.code} — ${counter.name}` : ''}
                      selectedOptions={counterAccountId ? [counterAccountId] : []}
                      onOptionSelect={(_, d) => setCounterAccountId(d.optionValue ?? '')}
                    >
                      {counterAccounts.map((a) => (
                        <Option key={a.id} value={a.id} text={`${a.code} — ${a.name}`}>
                          {a.code} — {a.name}
                        </Option>
                      ))}
                    </Dropdown>
                  </Field>

                  <Field label="Reason">
                    <Input
                      value={reason}
                      onChange={(_, d) => setReason(d.value)}
                      placeholder="Supplier debit note, freight not originally included…"
                    />
                  </Field>
                </>
              )}
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onClose}>
              {result ? 'Close' : 'Cancel'}
            </Button>
            {!result && (
              <Button appearance="primary" disabled={!valid || saving} onClick={submit}>
                {saving ? 'Posting…' : 'Post correction'}
              </Button>
            )}
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
