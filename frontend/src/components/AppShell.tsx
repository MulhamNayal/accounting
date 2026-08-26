import {
  AppItem,
  Body1Strong,
  Caption1,
  Dropdown,
  Hamburger,
  Menu,
  MenuButton,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  NavDrawer,
  NavDrawerBody,
  NavDrawerHeader,
  NavItem,
  NavSectionHeader,
  Option,
  Switch,
  Toolbar,
  ToolbarDivider,
  Tooltip,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components'
import {
  BookOpenFilled,
  BookOpenRegular,
  BuildingBankFilled,
  BuildingBankRegular,
  ClockFilled,
  ClockRegular,
  DocumentBulletListFilled,
  DocumentBulletListRegular,
  DocumentTableFilled,
  DocumentTableRegular,
  BoxFilled,
  BoxRegular,
  DataTrendingFilled,
  DataTrendingRegular,
  LibraryFilled,
  LibraryRegular,
  ArrowUndoFilled,
  ArrowUndoRegular,
  ReceiptFilled,
  ReceiptRegular,
  WalletCreditCardFilled,
  WalletCreditCardRegular,
  MoneyFilled,
  MoneyRegular,
  PersonRegular,
  ScalesFilled,
  SignOutRegular,
  ScalesRegular,
  SettingsFilled,
  SettingsRegular,
  bundleIcon,
} from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import type { LegalEntitySummary } from '../api/entities'
import { HEADER_HEIGHT } from '../theme'

// Fluent's convention: the outline icon at rest, the filled one when selected.
const EntitiesIcon = bundleIcon(BuildingBankFilled, BuildingBankRegular)
const AccountsIcon = bundleIcon(BookOpenFilled, BookOpenRegular)
const JournalsIcon = bundleIcon(DocumentTableFilled, DocumentTableRegular)
const ScalesIcon = bundleIcon(ScalesFilled, ScalesRegular)
const InvoicesIcon = bundleIcon(DocumentBulletListFilled, DocumentBulletListRegular)
const ReceiptsIcon = bundleIcon(MoneyFilled, MoneyRegular)
const AgeingIcon = bundleIcon(ClockFilled, ClockRegular)
const StockIcon = bundleIcon(BoxFilled, BoxRegular)
const BillsIcon = bundleIcon(ReceiptFilled, ReceiptRegular)
const CreditNotesIcon = bundleIcon(ArrowUndoFilled, ArrowUndoRegular)
const PaymentsIcon = bundleIcon(WalletCreditCardFilled, WalletCreditCardRegular)
const ProfitIcon = bundleIcon(DataTrendingFilled, DataTrendingRegular)
const BalanceSheetIcon = bundleIcon(LibraryFilled, LibraryRegular)
const SettingsIcon = bundleIcon(SettingsFilled, SettingsRegular)

const useStyles = makeStyles({
  root: {
    display: 'flex',
    height: '100vh',
    backgroundColor: tokens.colorNeutralBackground2,
  },
  content: {
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    minWidth: 0,
  },
  header: {
    height: HEADER_HEIGHT,
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
  },
  main: { flexGrow: 1, overflowY: 'auto' },
  spacer: { flexGrow: 1 },
  entityPicker: { minWidth: '280px' },
})

const SALES_ITEMS = [
  { path: '/invoices', label: 'Invoices', icon: <InvoicesIcon /> },
  { path: '/receipts', label: 'Receipts', icon: <ReceiptsIcon /> },
  { path: '/ageing', label: 'Ageing', icon: <AgeingIcon /> },
]

const PURCHASE_ITEMS = [
  { path: '/bills', label: 'Bills', icon: <BillsIcon /> },
  { path: '/payments', label: 'Payments', icon: <PaymentsIcon /> },
  { path: '/payables-ageing', label: 'Ageing', icon: <AgeingIcon /> },
]

const STOCK_ITEMS = [
  { path: '/stock', label: 'Stock', icon: <StockIcon /> },
]

const LEDGER_ITEMS = [
  { path: '/journals', label: 'Journals', icon: <JournalsIcon /> },
]

// Separated from the ledger: these are read-only views of the same postings, and grouping
// them together is how an accountant expects to find them.
// Its own group rather than sitting under Sales or Purchases: one page covers both sides,
// and filing it under either would make the other half invisible.
const ADJUSTMENT_ITEMS = [
  { path: '/credit-notes', label: 'Credit notes', icon: <CreditNotesIcon /> },
]

const REPORT_ITEMS = [
  { path: '/trial-balance', label: 'Trial balance', icon: <ScalesIcon /> },
  { path: '/profit-and-loss', label: 'Profit and loss', icon: <ProfitIcon /> },
  { path: '/balance-sheet', label: 'Balance sheet', icon: <BalanceSheetIcon /> },
]

const SETUP_ITEMS = [
  { path: '/entities', label: 'Entities', icon: <EntitiesIcon /> },
  { path: '/accounts', label: 'Chart of accounts', icon: <AccountsIcon /> },
]

export interface AppShellProps {
  children: ReactNode
  entities: LegalEntitySummary[]
  selectedEntityId: string | null
  onSelectEntity: (id: string) => void
  isDark: boolean
  onToggleTheme: (dark: boolean) => void
  signedInAs: string | null
  onSignOut: () => void
}

export function AppShell({
  children,
  entities,
  selectedEntityId,
  onSelectEntity,
  isDark,
  onToggleTheme,
  signedInAs,
  onSignOut,
}: AppShellProps) {
  const styles = useStyles()
  const navigate = useNavigate()
  const location = useLocation()
  const [navOpen, setNavOpen] = useState(true)

  const selected = entities.find((e) => e.id === selectedEntityId) ?? null

  return (
    <div className={styles.root}>
      <NavDrawer
        open={navOpen}
        type="inline"
        selectedValue={location.pathname}
        onNavItemSelect={(_, data) => navigate(String(data.value))}
        size="small"
      >
        <NavDrawerHeader>
          <Tooltip content="Hide navigation" relationship="label">
            <Hamburger onClick={() => setNavOpen(false)} />
          </Tooltip>
        </NavDrawerHeader>

        <NavDrawerBody>
          <AppItem
            icon={<Body1Strong>Ac</Body1Strong>}
            as="button"
            onClick={() => navigate('/entities')}
          >
            Accounting
          </AppItem>

          <NavSectionHeader>Sales</NavSectionHeader>
          {SALES_ITEMS.map((item) => (
            <NavItem key={item.path} icon={item.icon} value={item.path}>
              {item.label}
            </NavItem>
          ))}

          <NavSectionHeader>Purchases</NavSectionHeader>
          {PURCHASE_ITEMS.map((item) => (
            <NavItem key={item.path} icon={item.icon} value={item.path}>
              {item.label}
            </NavItem>
          ))}

          <NavSectionHeader>Stock</NavSectionHeader>
          {STOCK_ITEMS.map((item) => (
            <NavItem key={item.path} icon={item.icon} value={item.path}>
              {item.label}
            </NavItem>
          ))}

          <NavSectionHeader>Ledger</NavSectionHeader>
          {LEDGER_ITEMS.map((item) => (
            <NavItem key={item.path} icon={item.icon} value={item.path}>
              {item.label}
            </NavItem>
          ))}

          <NavSectionHeader>Adjustments</NavSectionHeader>
          {ADJUSTMENT_ITEMS.map((item) => (
            <NavItem key={item.path} icon={item.icon} value={item.path}>
              {item.label}
            </NavItem>
          ))}

          <NavSectionHeader>Reports</NavSectionHeader>
          {REPORT_ITEMS.map((item) => (
            <NavItem key={item.path} icon={item.icon} value={item.path}>
              {item.label}
            </NavItem>
          ))}

          <NavSectionHeader>Setup</NavSectionHeader>
          {SETUP_ITEMS.map((item) => (
            <NavItem key={item.path} icon={item.icon} value={item.path}>
              {item.label}
            </NavItem>
          ))}
          <NavItem icon={<SettingsIcon />} value="/settings">
            Settings
          </NavItem>
        </NavDrawerBody>
      </NavDrawer>

      <div className={styles.content}>
        <header className={styles.header}>
          {!navOpen && (
            <Tooltip content="Show navigation" relationship="label">
              <Hamburger onClick={() => setNavOpen(true)} />
            </Tooltip>
          )}

          <Toolbar aria-label="Workspace">
            <Dropdown
              className={styles.entityPicker}
              aria-label="Active entity"
              placeholder="Select an entity"
              value={selected ? `${selected.code} — ${selected.name}` : ''}
              selectedOptions={selected ? [selected.id] : []}
              onOptionSelect={(_, data) => data.optionValue && onSelectEntity(data.optionValue)}
            >
              {entities.map((entity) => (
                <Option key={entity.id} value={entity.id} text={`${entity.code} — ${entity.name}`}>
                  {entity.code} — {entity.name}
                </Option>
              ))}
            </Dropdown>

            {selected && (
              <>
                <ToolbarDivider />
                <Caption1 style={{ color: tokens.colorNeutralForeground3 }}>
                  {selected.functionalCurrency}
                </Caption1>
              </>
            )}
          </Toolbar>

          <div className={styles.spacer} />

          <Switch
            checked={isDark}
            onChange={(_, data) => onToggleTheme(data.checked)}
            label="Dark"
          />

          {signedInAs && (
            <Menu>
              <MenuTrigger disableButtonEnhancement>
                <Tooltip content={`Signed in as ${signedInAs}`} relationship="label">
                  <MenuButton appearance="subtle" icon={<PersonRegular />}>
                    {signedInAs}
                  </MenuButton>
                </Tooltip>
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  <MenuItem icon={<SignOutRegular />} onClick={onSignOut}>
                    Sign out
                  </MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
          )}
        </header>

        <main className={styles.main}>{children}</main>
      </div>
    </div>
  )
}
