import { getJson, postJson } from './client'

export interface SignInResponse {
  accessToken: string
  expiresAtUtc: string
  userId: string
  tenantId: string
  email: string
  displayName: string
}

export interface Session {
  token: string
  expiresAtUtc: string
  userId: string
  tenantId: string
  email: string
  displayName: string
}

const STORAGE_KEY = 'accounting.session'

/**
 * The signed-in session.
 *
 * Held in localStorage, which is a deliberate trade rather than an oversight: it survives a
 * reload, but it is readable by any script on the origin, so an XSS becomes a stolen token.
 * The mitigations are the short token lifetime and the fact that nothing else is kept here.
 * A production deployment should prefer an httpOnly refresh cookie.
 */
export function loadSession(): Session | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null

    const session = JSON.parse(raw) as Session
    // A token past its expiry is worse than none: every call fails and the cause is opaque.
    if (new Date(session.expiresAtUtc) <= new Date()) {
      localStorage.removeItem(STORAGE_KEY)
      return null
    }
    return session
  } catch {
    return null
  }
}

function storeSession(session: Session | null): void {
  try {
    if (session) localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
    else localStorage.removeItem(STORAGE_KEY)
  } catch {
    // Storage can be unavailable in a private window. The session still works for this
    // page; it simply will not survive a reload.
  }
}

let current: Session | null = loadSession()

export function currentSession(): Session | null {
  return current
}

/** Read by the HTTP client to attach the bearer token. */
export function currentToken(): string | null {
  return current?.token ?? null
}

export async function signIn(email: string, password: string): Promise<Session> {
  const response = await postJson<SignInResponse>('/api/auth/sign-in', { email, password })

  const session: Session = {
    token: response.accessToken,
    expiresAtUtc: response.expiresAtUtc,
    userId: response.userId,
    tenantId: response.tenantId,
    email: response.email,
    displayName: response.displayName,
  }

  current = session
  storeSession(session)
  return session
}

export function signOut(): void {
  current = null
  storeSession(null)
}

export function whoAmI(): Promise<{ userId: string; tenantId: string; email: string; displayName: string }> {
  return getJson('/api/auth/me')
}
