import {
  Caption1,
  FluentProvider,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  makeStyles,
  tokens,
  webDarkTheme,
  webLightTheme,
} from '@fluentui/react-components'
import { useEffect, useState } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { getEntities } from './api/entities'
import type { LegalEntitySummary } from './api/entities'
import { AppShell } from './components/AppShell'
import { ChartOfAccountsPage } from './pages/ChartOfAccountsPage'
import { AgeingPage } from './pages/AgeingPage'
import { EntitiesPage } from './pages/EntitiesPage'
import { InvoicesPage } from './pages/InvoicesPage'
import { JournalsPage } from './pages/JournalsPage'
import { PlaceholderPage } from './pages/PlaceholderPage'
import { ReceiptsPage } from './pages/ReceiptsPage'
import { TrialBalancePage } from './pages/TrialBalancePage'

const useStyles = makeStyles({
  centre: {
    height: '100vh',
    display: 'grid',
    placeItems: 'center',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalXXL,
    textAlign: 'center',
  },
})

const THEME_KEY = 'clearwise.theme'

function App() {
  const styles = useStyles()
  const [entities, setEntities] = useState<LegalEntitySummary[]>([])
  const [selectedEntityId, setSelectedEntityId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [isDark, setIsDark] = useState(() => {
    try {
      return localStorage.getItem(THEME_KEY) === 'dark'
    } catch {
      return false
    }
  })

  useEffect(() => {
    getEntities()
      .then((loaded) => {
        setEntities(loaded)
        setSelectedEntityId((current) => current ?? loaded[0]?.id ?? null)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [])

  const handleToggleTheme = (dark: boolean) => {
    setIsDark(dark)
    try {
      localStorage.setItem(THEME_KEY, dark ? 'dark' : 'light')
    } catch {
      // Storage can be unavailable (private windows, blocked site data). The toggle still
      // works for this session; it simply will not be remembered.
    }
  }

  const theme = isDark ? webDarkTheme : webLightTheme

  if (loading) {
    return (
      <FluentProvider theme={theme}>
        <div className={styles.centre}>
          <Spinner label="Starting ClearWise…" />
        </div>
      </FluentProvider>
    )
  }

  if (error) {
    return (
      <FluentProvider theme={theme}>
        <div className={styles.centre}>
          <MessageBar intent="error" style={{ maxWidth: '560px' }}>
            <MessageBarBody>
              <MessageBarTitle>Cannot reach the API</MessageBarTitle>
              {error}
              <div style={{ marginTop: tokens.spacingVerticalS }}>
                <Caption1>Start the backend: dotnet run --urls http://localhost:5100</Caption1>
              </div>
            </MessageBarBody>
          </MessageBar>
        </div>
      </FluentProvider>
    )
  }

  return (
    <FluentProvider theme={theme}>
      <AppShell
        entities={entities}
        selectedEntityId={selectedEntityId}
        onSelectEntity={setSelectedEntityId}
        isDark={isDark}
        onToggleTheme={handleToggleTheme}
      >
        <Routes>
          <Route path="/" element={<Navigate to="/invoices" replace />} />
          <Route path="/entities" element={<EntitiesPage entities={entities} />} />
          <Route path="/accounts" element={<ChartOfAccountsPage />} />
          <Route path="/invoices" element={<InvoicesPage entityId={selectedEntityId} />} />
          <Route path="/receipts" element={<ReceiptsPage entityId={selectedEntityId} />} />
          <Route path="/ageing" element={<AgeingPage entityId={selectedEntityId} />} />
          <Route path="/journals" element={<JournalsPage entityId={selectedEntityId} />} />
          <Route path="/trial-balance" element={<TrialBalancePage entityId={selectedEntityId} />} />
          <Route
            path="/settings"
            element={
              <PlaceholderPage title="Settings" layer="a later layer">
                Number series, fiscal years and period closing, tax codes and user access.
              </PlaceholderPage>
            }
          />
        </Routes>
      </AppShell>
    </FluentProvider>
  )
}

export default App
