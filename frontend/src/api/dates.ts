/**
 * Today, in the user's own timezone, as YYYY-MM-DD.
 *
 * Deliberately not `new Date().toISOString().slice(0, 10)` — that returns the **UTC** date.
 * East of Greenwich the two disagree for part of every day: at 07:00 in Kuala Lumpur
 * (UTC+8) the UTC date is still yesterday, so a document would default to the wrong day and
 * a report would hide entries that were posted today.
 *
 * Accounting dates are business dates. They belong to wherever the business is, never to UTC.
 */
export function todayLocal(): string {
  const now = new Date()
  const month = `${now.getMonth() + 1}`.padStart(2, '0')
  const day = `${now.getDate()}`.padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}
