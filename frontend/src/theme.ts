import { makeStyles, tokens } from '@fluentui/react-components'

/**
 * Cross-cutting visual conventions. Anything that should look the same on every page
 * belongs here rather than in a page-local override, so fixing it once fixes it everywhere.
 */
export const NAV_WIDTH = '248px'
export const HEADER_HEIGHT = '48px'

export const useLayoutStyles = makeStyles({
  page: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXXL,
    paddingLeft: tokens.spacingHorizontalXXL,
    paddingRight: tokens.spacingHorizontalXXL,
    maxWidth: '1200px',
  },
  pageHeader: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    marginBottom: tokens.spacingVerticalM,
  },
  subtle: { color: tokens.colorNeutralForeground3 },
  spacer: { flexGrow: 1 },
})

/** Colour coding for the five account classifications, used wherever a type is shown. */
export const accountTypeColor = {
  Asset: 'brand',
  Liability: 'danger',
  Equity: 'important',
  Income: 'success',
  Expense: 'warning',
} as const

export type AccountTypeName = keyof typeof accountTypeColor
