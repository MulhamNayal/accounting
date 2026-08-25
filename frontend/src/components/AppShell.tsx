import {
  Avatar,
  Body1Strong,
  Caption1,
  Divider,
  Dropdown,
  Option,
  Switch,
  Tab,
  TabList,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components'
import {
  BookOpenRegular,
  BuildingBankRegular,
  DocumentTableRegular,
  SettingsRegular,
} from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import type { LegalEntitySummary } from '../api/entities'
import { HEADER_HEIGHT, NAV_WIDTH } from '../theme'

const useStyles = makeStyles({
  root: {
    display: 'grid',
    gridTemplateColumns: `${NAV_WIDTH} 1fr`,
    gridTemplateRows: `${HEADER_HEIGHT} 1fr`,
    gridTemplateAreas: `"brand header" "nav main"`,
    height: '100vh',
    backgroundColor: tokens.colorNeutralBackground2,
  },
  brand: {
    gridArea: 'brand',
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingLeft: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRight('1px', 'solid', tokens.colorNeutralStroke2),
  },
  header: {
    gridArea: 'header',
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
  },
  nav: {
    gridArea: 'nav',
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRight('1px', 'solid', tokens.colorNeutralStroke2),
    paddingTop: tokens.spacingVerticalM,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    overflowY: 'auto',
  },
  navSection: {
    paddingLeft: tokens.spacingHorizontalL,
    paddingTop: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground4,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
  },
  main: { gridArea: 'main', overflowY: 'auto' },
  spacer: { flexGrow: 1 },
  entityPicker: { minWidth: '260px' },
  footer: {
    marginTop: 'auto',
    padding: tokens.spacingHorizontalL,
    color: tokens.colorNeutralForeground4,
  },
})

export interface AppShellProps {
  children: ReactNode
  entities: LegalEntitySummary[]
  selectedEntityId: string | null
  onSelectEntity: (id: string) => void
  isDark: boolean
  onToggleTheme: (dark: boolean) => void
}

const NAV_ITEMS = [
  { path: '/entities', label: 'Entities', icon: <BuildingBankRegular /> },
  { path: '/accounts', label: 'Chart of accounts', icon: <BookOpenRegular /> },
  { path: '/journals', label: 'Journals', icon: <DocumentTableRegular /> },
  { path: '/settings', label: 'Settings', icon: <SettingsRegular /> },
]

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

  const selected = entities.find((e) => e.id === selectedEntityId) ?? null

  return (
    <div className={styles.root}>
      <div className={styles.brand}>
        <Avatar shape="square" color="brand" name="CW" size={24} />
        <Body1Strong>ClearWise</Body1Strong>
      </div>

      <div className={styles.header}>
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
          <Caption1 style={{ color: tokens.colorNeutralForeground3 }}>
            {selected.functionalCurrency}
          </Caption1>
        )}

        <div className={styles.spacer} />

        <Switch
          checked={isDark}
          onChange={(_, data) => onToggleTheme(data.checked)}
          label="Dark"
        />
      </div>

      <nav className={styles.nav}>
        <Caption1 className={styles.navSection}>Accounting</Caption1>
        <TabList
          vertical
          appearance="subtle"
          selectedValue={location.pathname}
          onTabSelect={(_, data) => navigate(String(data.value))}
        >
          {NAV_ITEMS.map((item) => (
            <Tab key={item.path} value={item.path} icon={item.icon}>
              {item.label}
            </Tab>
          ))}
        </TabList>

        <div className={styles.footer}>
          <Divider />
          <Caption1 style={{ display: 'block', marginTop: tokens.spacingVerticalS }}>
            Layer 0 — nothing posted yet
          </Caption1>
        </div>
      </nav>

      <main className={styles.main}>{children}</main>
    </div>
  )
}
