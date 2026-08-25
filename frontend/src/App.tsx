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
import { EntitiesPage } from './pages/EntitiesPage'
import { PlaceholderPage } from './pages/PlaceholderPage'

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
          <Route path="/" element={<Navigate to="/entities" replace />} />
          <Route path="/entities" element={<EntitiesPage entities={entities} />} />
          <Route path="/accounts" element={<ChartOfAccountsPage />} />
          <Route
            path="/journals"
            element={
              <PlaceholderPage title="Journals" layer="Layer 1">
                The posting core: immutable journal entries and postings, with debits and
                credits proven equal by the database at commit time. Corrections will appear
                here as reversal and replacement pairs rather than edits.
              </PlaceholderPage>
            }
          />
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
