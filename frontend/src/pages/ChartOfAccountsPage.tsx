import {
  Badge,
  Caption1,
  Card,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  SearchBox,
  Spinner,
  Switch,
  Title3,
  Toolbar,
  ToolbarDivider,
  Tree,
  TreeItem,
  TreeItemLayout,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { useEffect, useMemo, useState } from 'react'
import { getAccounts } from '../api/accounts'
import type { AccountSummary } from '../api/accounts'
import { accountTypeColor, useLayoutStyles } from '../theme'
import type { AccountTypeName } from '../theme'

const useStyles = makeStyles({
  code: {
    fontFamily: tokens.fontFamilyMonospace,
    color: tokens.colorNeutralForeground3,
    minWidth: '52px',
    display: 'inline-block',
  },
  row: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    width: '100%',
  },
  heading: { fontWeight: tokens.fontWeightSemibold },
  grow: { flexGrow: 1 },
  legend: { display: 'flex', gap: tokens.spacingHorizontalXS, flexWrap: 'wrap' },
})

interface AccountNode extends AccountSummary {
  children: AccountNode[]
}

/** Rebuilds the parent/child hierarchy the API returns as a flat list. */
function buildTree(accounts: AccountSummary[]): AccountNode[] {
  const byId = new Map<string, AccountNode>(
    accounts.map((a) => [a.id, { ...a, children: [] as AccountNode[] }]),
  )
  const roots: AccountNode[] = []

  for (const node of byId.values()) {
    const parent = node.parentId ? byId.get(node.parentId) : undefined
    if (parent) parent.children.push(node)
    else roots.push(node)
  }

  const sortByCode = (nodes: AccountNode[]) => {
    nodes.sort((a, b) => a.code.localeCompare(b.code))
    nodes.forEach((n) => sortByCode(n.children))
  }
  sortByCode(roots)

  return roots
}

/** Keeps any account that matches, plus every ancestor needed to reach it. */
function filterTree(nodes: AccountNode[], term: string): AccountNode[] {
  if (!term) return nodes
  const lower = term.toLowerCase()

  const walk = (node: AccountNode): AccountNode | null => {
    const children = node.children.map(walk).filter((n): n is AccountNode => n !== null)
    const selfMatches =
      node.code.toLowerCase().includes(lower) || node.name.toLowerCase().includes(lower)
    if (!selfMatches && children.length === 0) return null
    return { ...node, children }
  }

  return nodes.map(walk).filter((n): n is AccountNode => n !== null)
}

function collectBranchIds(nodes: AccountNode[]): string[] {
  return nodes.flatMap((n) => (n.children.length ? [n.id, ...collectBranchIds(n.children)] : []))
}

export function ChartOfAccountsPage() {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const [accounts, setAccounts] = useState<AccountSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [controlsOnly, setControlsOnly] = useState(false)

  useEffect(() => {
    getAccounts()
      .then(setAccounts)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [])

  const visible = useMemo(() => {
    const source = controlsOnly
      ? accounts.filter((a) => a.controlType !== 'None' || !a.isPostable)
      : accounts
    return filterTree(buildTree(source), search)
  }, [accounts, search, controlsOnly])

  const openItems = useMemo(() => collectBranchIds(visible), [visible])

  if (loading) {
    return (
      <div className={layout.page}>
        <Spinner label="Loading accounts…" />
      </div>
    )
  }

  if (error) {
    return (
      <div className={layout.page}>
        <Title3>Chart of accounts</Title3>
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Could not load accounts</MessageBarTitle>
            {error}
          </MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  const postableCount = accounts.filter((a) => a.isPostable).length

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Chart of accounts</Title3>
        <Caption1 className={layout.subtle}>
          Shared across every entity in the tenant, which is what makes consolidation a sum
          rather than a mapping exercise. {postableCount} postable of {accounts.length}.
        </Caption1>
      </div>

      <Toolbar aria-label="Filter accounts">
        <SearchBox
          value={search}
          onChange={(_, data) => setSearch(data.value)}
          placeholder="Search code or name"
          style={{ minWidth: '280px' }}
        />
        <ToolbarDivider />
        <Switch
          checked={controlsOnly}
          onChange={(_, d) => setControlsOnly(d.checked)}
          label="Control accounts only"
        />
        <div className={styles.grow} />
        <div className={styles.legend}>
          {(Object.keys(accountTypeColor) as AccountTypeName[]).map((type) => (
            <Badge key={type} appearance="tint" color={accountTypeColor[type]}>
              {type}
            </Badge>
          ))}
        </div>
      </Toolbar>

      <Card>
        {visible.length === 0 ? (
          <Caption1 className={layout.subtle}>No accounts match “{search}”.</Caption1>
        ) : (
          <Tree aria-label="Chart of accounts" openItems={openItems}>
            {visible.map((node) => (
              <AccountTreeItem key={node.id} node={node} />
            ))}
          </Tree>
        )}
      </Card>
    </div>
  )
}

function AccountTreeItem({ node }: { node: AccountNode }) {
  const styles = useStyles()
  const hasChildren = node.children.length > 0

  const label = (
    <div className={styles.row}>
      <span className={styles.code}>{node.code}</span>
      <span className={hasChildren ? styles.heading : undefined}>{node.name}</span>
      <span className={styles.grow} />
      {node.controlType !== 'None' && (
        <Badge appearance="outline" color="informative">
          {node.controlType}
        </Badge>
      )}
      {node.isPostable ? (
        <Badge appearance="tint" color={accountTypeColor[node.accountType as AccountTypeName]}>
          {node.normalBalance}
        </Badge>
      ) : (
        <Caption1 style={{ color: tokens.colorNeutralForeground4 }}>heading</Caption1>
      )}
    </div>
  )

  if (!hasChildren) {
    return (
      <TreeItem itemType="leaf" value={node.id}>
        <TreeItemLayout>{label}</TreeItemLayout>
      </TreeItem>
    )
  }

  return (
    <TreeItem itemType="branch" value={node.id}>
      <TreeItemLayout>{label}</TreeItemLayout>
      <Tree>
        {node.children.map((child) => (
          <AccountTreeItem key={child.id} node={child} />
        ))}
      </Tree>
    </TreeItem>
  )
}
