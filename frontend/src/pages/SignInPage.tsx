import {
  Button,
  Caption1,
  Card,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { useState } from 'react'
import { signIn } from '../api/auth'
import type { Session } from '../api/auth'

const useStyles = makeStyles({
  screen: {
    minHeight: '100vh',
    display: 'grid',
    placeItems: 'center',
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  card: {
    width: '380px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingVerticalXXL,
  },
  header: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  subtle: { color: tokens.colorNeutralForeground3 },
})

export function SignInPage({ onSignedIn }: { onSignedIn: (session: Session) => void }) {
  const styles = useStyles()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async () => {
    setError(null)
    setBusy(true)
    try {
      onSignedIn(await signIn(email, password))
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={styles.screen}>
      <Card className={styles.card}>
        <div className={styles.header}>
          <Title2>Accounting</Title2>
          <Caption1 className={styles.subtle}>Sign in to continue</Caption1>
        </div>

        {error && (
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        )}

        <Field label="Email">
          <Input
            type="email"
            value={email}
            onChange={(_, d) => setEmail(d.value)}
            onKeyDown={(e) => e.key === 'Enter' && email && password && void submit()}
            autoComplete="username"
          />
        </Field>

        <Field label="Password">
          <Input
            type="password"
            value={password}
            onChange={(_, d) => setPassword(d.value)}
            onKeyDown={(e) => e.key === 'Enter' && email && password && void submit()}
            autoComplete="current-password"
          />
        </Field>

        <Button
          appearance="primary"
          disabled={!email || !password || busy}
          onClick={() => void submit()}
        >
          {busy ? 'Signing in…' : 'Sign in'}
        </Button>

        <Caption1 className={styles.subtle}>
          The token determines which tenant's books you see. It is signed by the server, so it
          cannot be altered to reach another one.
        </Caption1>
      </Card>
    </div>
  )
}
