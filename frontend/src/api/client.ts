/**
 * Minimal fetch wrapper. Relative URLs only — Vite proxies /api to the backend in
 * development, and in production both are served from one origin.
 */
export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    headers: { Accept: 'application/json' },
  })

  if (!response.ok) {
    throw new Error(await readError(response))
  }

  return (await response.json()) as T
}

export async function postJson<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(body),
  })

  const text = await response.text()

  if (!response.ok) {
    throw new Error(parseError(text) || `${response.status} ${response.statusText}`)
  }

  return JSON.parse(text) as T
}

async function readError(response: Response): Promise<string> {
  const text = await response.text()
  return parseError(text) || `${response.status} ${response.statusText}`
}

/**
 * The API returns a raw JSON string for 400/404/409 and a ProblemDetails object for 502,
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
