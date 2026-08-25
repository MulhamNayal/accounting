import {
  Body1,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Title3,
} from '@fluentui/react-components'
import type { ReactNode } from 'react'
import { useLayoutStyles } from '../theme'

/**
 * A navigation target that exists but isn't built yet. Shown rather than hidden so the
 * shape of the product is visible, and honest about what does not work.
 */
export function PlaceholderPage({ title, layer, children }: {
  title: string
  layer: string
  children: ReactNode
}) {
  const layout = useLayoutStyles()

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>{title}</Title3>
      </div>

      <MessageBar intent="warning">
        <MessageBarBody>
          <MessageBarTitle>Not built yet</MessageBarTitle>
          Arrives in {layer}.
        </MessageBarBody>
      </MessageBar>

      <Body1 className={layout.subtle}>{children}</Body1>
    </div>
  )
}
