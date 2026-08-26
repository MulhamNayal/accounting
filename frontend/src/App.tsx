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
import { useCallback, useEffect, useState } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { currentSession, currentToken, loadSession, signOut } from './api/auth'
import type { Session } from './api/auth'
import { setTokenReader, setUnauthorizedHandler } from './api/client'
import { getEntities } from './api/entities'
import type { LegalEntitySummary } from './api/entities'
import { AppShell } from './components/AppShell'
import { AgeingPage } from './pages/AgeingPage'
import { BalanceSheetPage } from './pages/BalanceSheetPage'
import { ChartOfAccountsPage } from './pages/ChartOfAccountsPage'
import { EntitiesPage } from './pages/EntitiesPage'
import { InvoicesPage } from './pages/InvoicesPage'
import { JournalsPage } from './pages/JournalsPage'
import { PlaceholderPage } from './pages/PlaceholderPage'
import { ProfitAndLossPage } from './pages/ProfitAndLossPage'
import { ReceiptsPage } from './pages/ReceiptsPage'
import { SignInPage } from './pages/SignInPage'
import { StockPage } from './pages/StockPage'
import { TrialBalancePage } from './pages/TrialBalancePage'

// Wired once at module load, before any component can issue a request.
setTokenReader(currentToken)

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

const THEME_KEY = 'accounting.theme'

function App() {
  const styles = useStyles()
  const [session, setSession] = useState<Session | null>(() => loadSession())
  const [entities, setEntities] = useState<LegalEntitySummary[]>([])
  const [selectedEntityId, setSelectedEntityId] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [isDark, setIsDark] = useState(() => {
    try {
      return localStorage.getItem(THEME_KEY) === 'dark'
    } catch {
      return false
    }
  })

  const endSession = useCallback(() => {
    signOut()
    setSession(null)
    setEntities([])
    setSelectedEntityId(null)
  }, [])

  // A rejected token means the session is over, wherever the request came from. Handled
  // centrally so no page has to think about it.
  useEffect(() => {
    setUnauthorizedHandler(endSession)
  }, [endSession])

  useEffect(() => {
    if (!session) return

    setLoading(true)
    getEntities()
      .then((loaded) => {
        setEntities(loaded)
        setSelectedEntityId((current) => current ?? loaded[0]?.id ?? null)
        setError(null)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [session])

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

  if (!session) {
    return (
      <FluentProvider theme={theme}>
        <SignInPage onSignedIn={setSession} />
      </FluentProvider>
    )
  }

  if (loading) {
    return (
      <FluentProvider theme={theme}>
        <div className={styles.centre}>
          <Spinner label="Loading your books…" />
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
        signedInAs={currentSession()?.displayName ?? null}
        onSignOut={endSession}
      >
        <Routes>
          <Route path="/" element={<Navigate to="/invoices" replace />} />
          <Route path="/entities" element={<EntitiesPage entities={entities} />} />
          <Route path="/accounts" element={<ChartOfAccountsPage />} />
          <Route path="/invoices" element={<InvoicesPage entityId={selectedEntityId} />} />
          <Route path="/receipts" element={<ReceiptsPage entityId={selectedEntityId} />} />
          <Route path="/ageing" element={<AgeingPage entityId={selectedEntityId} />} />
          <Route path="/stock" element={<StockPage entityId={selectedEntityId} />} />
          <Route path="/journals" element={<JournalsPage entityId={selectedEntityId} />} />
          <Route path="/trial-balance" element={<TrialBalancePage entityId={selectedEntityId} />} />
          <Route path="/profit-and-loss" element={<ProfitAndLossPage entityId={selectedEntityId} />} />
          <Route path="/balance-sheet" element={<BalanceSheetPage entityId={selectedEntityId} />} />
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
