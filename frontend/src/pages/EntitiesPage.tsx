import {
  Badge,
  Caption1,
  DataGrid,
  DataGridBody,
  DataGridCell,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridRow,
  Title3,
  createTableColumn,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import type { TableColumnDefinition } from '@fluentui/react-components'
import type { LegalEntitySummary } from '../api/entities'
import { useLayoutStyles } from '../theme'

const useStyles = makeStyles({
  code: { fontFamily: tokens.fontFamilyMonospace },
  grid: { padding: tokens.spacingVerticalS },
})

const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

export function EntitiesPage({ entities }: { entities: LegalEntitySummary[] }) {
  const layout = useLayoutStyles()
  const styles = useStyles()

  const columns: TableColumnDefinition<LegalEntitySummary>[] = [
    createTableColumn<LegalEntitySummary>({
      columnId: 'code',
      compare: (a, b) => a.code.localeCompare(b.code),
      renderHeaderCell: () => 'Code',
      renderCell: (item) => <span className={styles.code}>{item.code}</span>,
    }),
    createTableColumn<LegalEntitySummary>({
      columnId: 'name',
      compare: (a, b) => a.name.localeCompare(b.name),
      renderHeaderCell: () => 'Name',
      renderCell: (item) => item.name,
    }),
    createTableColumn<LegalEntitySummary>({
      columnId: 'currency',
      compare: (a, b) => a.functionalCurrency.localeCompare(b.functionalCurrency),
      renderHeaderCell: () => 'Functional currency',
      renderCell: (item) => item.functionalCurrency,
    }),
    createTableColumn<LegalEntitySummary>({
      columnId: 'fy',
      compare: (a, b) => a.financialYearStartMonth - b.financialYearStartMonth,
      renderHeaderCell: () => 'Financial year starts',
      renderCell: (item) => MONTHS[item.financialYearStartMonth - 1],
    }),
    createTableColumn<LegalEntitySummary>({
      columnId: 'taxId',
      compare: (a, b) => (a.taxId ?? '').localeCompare(b.taxId ?? ''),
      renderHeaderCell: () => 'Tax ID',
      renderCell: (item) =>
        item.taxId ?? <Caption1 style={{ color: tokens.colorNeutralForeground4 }}>not set</Caption1>,
    }),
    createTableColumn<LegalEntitySummary>({
      columnId: 'status',
      compare: (a, b) => Number(a.isActive) - Number(b.isActive),
      renderHeaderCell: () => 'Status',
      renderCell: (item) => (
        <Badge appearance="tint" color={item.isActive ? 'success' : 'subtle'}>
          {item.isActive ? 'Active' : 'Inactive'}
        </Badge>
      ),
    }),
  ]

  return (
    <div className={layout.page}>
      <div className={layout.pageHeader}>
        <Title3>Entities</Title3>
        <Caption1 className={layout.subtle}>
          Separate books within one tenant. Each keeps its own financial year and tax
          identity — e-Invoice is issued per TIN, so that cannot be shared — while the chart
          of accounts and master data are common, which is what makes them consolidatable.
        </Caption1>
      </div>

      <div className={layout.surface}>
        <DataGrid
          items={entities}
          columns={columns}
          sortable
          getRowId={(item) => item.id}
          className={styles.grid}
        >
          <DataGridHeader>
            <DataGridRow>
              {({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}
            </DataGridRow>
          </DataGridHeader>
          <DataGridBody<LegalEntitySummary>>
            {({ item, rowId }) => (
              <DataGridRow<LegalEntitySummary> key={rowId}>
                {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
              </DataGridRow>
            )}
          </DataGridBody>
        </DataGrid>
      </div>
    </div>
  )
}
