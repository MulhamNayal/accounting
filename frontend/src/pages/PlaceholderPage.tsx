import { Body1, Caption1, Title3, tokens } from '@fluentui/react-components'
import { useLayoutStyles } from '../theme'

/**
 * A navigation target that exists but isn't built yet. Shown rather than hidden so the
 * shape of the product is visible, and honest about what does not work.
 */
export function PlaceholderPage({ title, layer, children }: {
  title: string
  layer: string
  children: React.ReactNode
}) {
  const layout = useLayoutStyles()

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>{title}</Title3>
        <Caption1 style={{ color: tokens.colorPaletteMarigoldForeground1 }}>
          Not built yet — arrives in {layer}.
        </Caption1>
      </div>
      <Body1 className={layout.subtle}>{children}</Body1>
    </div>
  )
}
