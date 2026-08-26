/**
 * Minimal fetch wrapper. Relative URLs only — Vite proxies /api to the backend in
 * development, and in production both are served from one origin.
 */

/**
 * Set by the auth module. A function rather than a value so the client always reads the
 * live token; a captured one would go stale the moment the user signs in or out.
 */
let tokenReader: () => string | null = () => null

export function setTokenReader(reader: () => string | null): void {
  tokenReader = reader
}

/** Called when the server rejects the token, so the app can return to sign-in. */
let onUnauthorized: () => void = () => {}

export function setUnauthorizedHandler(handler: () => void): void {
  onUnauthorized = handler
}

function headers(extra?: Record<string, string>): Record<string, string> {
  const token = tokenReader()
  return {
    Accept: 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...extra,
  }
}

/**
 * Resolves an API path against the app's base path.
 *
 * In development the app is served from the origin root and this is a no-op. Deployed, it
 * lives under a sub-path (`/accounting/`), where a root-relative `/api/...` would resolve
 * to the wrong place entirely. Vite substitutes BASE_URL at build time.
 */
function resolve(path: string): string {
  if (!path.startsWith('/')) return path
  return import.meta.env.BASE_URL.replace(/\/$/, '') + path
}

export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(resolve(path), { headers: headers() })

  if (!response.ok) {
    throw new Error(await readError(response))
  }

  return (await response.json()) as T
}

export async function postJson<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(resolve(path), {
    method: 'POST',
    headers: headers({ 'Content-Type': 'application/json' }),
    body: JSON.stringify(body),
  })

  const text = await response.text()

  if (!response.ok) {
    if (response.status === 401) {
      // Sign-in itself returns 401 for bad credentials; that is a message to show, not a
      // session to end. Anything else means the token is gone or expired.
      if (!path.endsWith('/sign-in')) onUnauthorized()
    }
    throw new Error(parseError(text) || `${response.status} ${response.statusText}`)
  }

  return JSON.parse(text) as T
}

async function readError(response: Response): Promise<string> {
  const text = await response.text()

  if (response.status === 401) {
    onUnauthorized()
    return 'Your session has expired. Please sign in again.'
  }

  return parseError(text) || `${response.status} ${response.statusText}`
}

/**
 * The API returns a raw JSON string for 400/401/404/409 and a ProblemDetails object for 502,
 * so both shapes are unwrapped here. The server's message names which rule was broken,
 * which is more useful than anything the client could invent.
 */
function parseError(text: string): string {
  try {
    const parsed: unknown = JSON.parse(text)
    if (typeof parsed === 'string') return parsed
    if (parsed && typeof parsed === 'object' && 'detail' in parsed) {
      return String((parsed as { detail: unknown }).detail)
    }
  } catch {
    // Not JSON; the raw text is the best available message.
  }
  return text
}
