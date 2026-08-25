/**
 * Minimal fetch wrapper. Relative URLs only — Vite proxies /api to the backend in
 * development, and in production both are served from one origin.
 */
export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    headers: { Accept: 'application/json' },
  })

  if (!response.ok) {
    const body = await response.text()
    throw new Error(body || `${response.status} ${response.statusText}`)
  }

  return (await response.json()) as T
}
