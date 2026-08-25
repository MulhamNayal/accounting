import {
  AppItem,
  Body1Strong,
  Caption1,
  Dropdown,
  Hamburger,
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
  DocumentTableFilled,
  DocumentTableRegular,
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

const NAV_ITEMS = [
  { path: '/entities', label: 'Entities', icon: <EntitiesIcon /> },
  { path: '/accounts', label: 'Chart of accounts', icon: <AccountsIcon /> },
  { path: '/journals', label: 'Journals', icon: <JournalsIcon /> },
]

export interface AppShellProps {
  children: ReactNode
  entities: LegalEntitySummary[]
  selectedEntityId: string | null
  onSelectEntity: (id: string) => void
  isDark: boolean
  onToggleTheme: (dark: boolean) => void
}

export function AppShell({
  children,
  entities,
  selectedEntityId,
  onSelectEntity,
  isDark,
  onToggleTheme,
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
            icon={<Body1Strong>CW</Body1Strong>}
            as="button"
            onClick={() => navigate('/entities')}
          >
            ClearWise
          </AppItem>

          <NavSectionHeader>Accounting</NavSectionHeader>
          {NAV_ITEMS.map((item) => (
            <NavItem key={item.path} icon={item.icon} value={item.path}>
              {item.label}
            </NavItem>
          ))}

          <NavSectionHeader>Configuration</NavSectionHeader>
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
        </header>

        <main className={styles.main}>{children}</main>
      </div>
    </div>
  )
}
