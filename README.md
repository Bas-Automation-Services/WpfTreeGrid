# WpfTreeGrid

A tree grid for WPF: hierarchical rows with expand/collapse, sorting, Excel-style column
filters and cell editing, built on the standard `DataGrid`. Column resizing and reordering,
virtualization and selection come from WPF itself.

## Usage

```xml
<tg:TreeDataGrid xmlns:tg="clr-namespace:WpfTreeGrid;assembly=WpfTreeGrid"
                 ItemsSource="{Binding Roots}"
                 ChildPropertyName="Children"
                 ExpandStateMappingName="IsExpanded"
                 AutoExpandMode="RootNodesExpanded"
                 AllowFiltering="True" AllowEditing="True" SelectionMode="Extended">
    <tg:TreeDataGrid.Columns>
        <tg:TreeGridTextColumn MappingName="Name" ColumnSizer="Star" />
        <tg:TreeGridCheckBoxColumn MappingName="IsSelected" HeaderText="Use" AllowFiltering="False" />
        <tg:TreeGridNumericColumn MappingName="Mass" HeaderText="Mass (kg)" NumberDecimalDigits="3" />
    </tg:TreeDataGrid.Columns>
</tg:TreeDataGrid>
```

The expander goes in the first column, or in the column named by `ExpanderColumn`. Rows are
the bound items themselves, so columns and styles bind as in any `DataGrid`.

## API at a glance

| Area | Members |
|---|---|
| Grid | `ChildPropertyName`, `ParentPropertyName` + `SelfRelationRootValue` (flat lists), `ExpandStateMappingName`, `AutoExpandMode`, `ExpanderColumn`, `FilterLevel`, `AllowFiltering`, `AllowEditing`, `AllowSorting`, `AllowResizingColumns`, `AllowDraggingColumns`, `ShowRowHeader`, `HeaderRowHeight`, `ColumnSizer`, `RecordContextMenu`, `BindableSelectedItems` |
| Columns | `TreeGridTextColumn`, `TreeGridNumericColumn`, `TreeGridDateTimeColumn`, `TreeGridCheckBoxColumn`, `TreeGridTemplateColumn`, all with `MappingName`, `HeaderText`, `AllowEditing`, `AllowSorting`, `AllowFiltering`, `AllowResizing`, `IsHidden`, `MinimumWidth`, `MaximumWidth`, `ColumnSizer`; `ITreeGridColumn` to treat them alike |
| Tree | `View` (`Nodes`, `Filter`, `RefreshFilter()`), `GetNode`, `ExpandNode`, `CollapseNode`, `ExpandAllNodes`, `CollapseAllNodes`, `ExpandToLevel`, `TreeNode` (`Item`, `ChildNodes`, `ParentNode`, `Level`, `IsExpanded`), `NodeExpanding`/`NodeExpanded`/`NodeCollapsing`/`NodeCollapsed` |
| Sorting and filters | `SortColumnDescriptions`, `SortBy`, `ClearSort`; per column `FilterPredicates` (`FilterPredicate` with `FilterType`, `FilterValue`, `PredicateType`, `FilterBehavior`), `ClearFilter`, `ClearFilters`, `FilterChanged` |
| Cell selection and editing | With `SelectionUnit="Cell"`: `SelectCell(s)`, `UnselectCell(s)`, `ClearSelections`, `MoveCurrentCell`, `GetSelectedCells`, `CurrentCellInfo`, `CurrentCellBeginEdit` (cancelable), `CurrentCellEndEdit`, `EditTrigger`, `SelectionController.CurrentCellManager` (`BeginEdit`, `EndEdit`, `CurrentCell.IsEditing`), `TreeGridIndexResolver` + `RowColumnIndex` (header row 0, visible rows from 1) |

Column properties are dependency properties, so they can be bound, and columns see the grid's
DataContext, so `IsHidden="{Binding ShowSomething}"` works. A column's `Width` reads as pixels
(`double`, NaN when sized by `ColumnSizer`), and `ItemsSource` reads back as the bound
collection. The grid-level `Allow*` properties are plain CLR properties over the `DataGrid`
ones. Set them as attributes; to bind, use the underlying property (`IsReadOnly`,
`CanUserSortColumns` and so on).

### Behaviour notes

- **Sorting:** strings sort naturally ("Part2" before "Part10"), children stay under their
  parent, and a child added under a sorted column lands in sorted position.
- **Filter levels:** `FilterLevel.Extended` also sees descendants that were never expanded.
- **Auto-generated columns:** `AutoGenerateColumns` defaults to false, because generated
  `DataGrid` columns can't host the expander.
- **Filter events:** column filters apply immediately when predicates are added;
  `FilterChanged` is raised only for changes made in the filter popup.
- **Editing:** the value is written when the cell commits (Enter, Tab, another cell), and each
  committed cell is written straight away, with no row-level edit. Escape or `EndEdit(false)`
  puts the old value back. `CurrentCellEndEdit` comes after the value is in the row. A grid
  with `AllowEditing="False"` locks every column, and a cell bound to a property without a
  setter doesn't open an editor.
- **Selection:** the selected cells and the current cell stay put when rows expand or sort.

## Styling

The default look is a plain, light grid. Each part is a resource under a key on
`TreeDataGrid`:

| Key | What |
|---|---|
| `ColumnHeaderStyleKey` | Header with sort chevron and filter button |
| `CellStyleKey` | Cell with grey selection and current-cell border |
| `ExpanderStyleKey` | The ⊞/⊟ toggle |
| `LineBrushKey`, `SelectionBrushKey`, `FilteredBrushKey` | Colours |

To replace a part, define a resource under the same key in app resources. To adjust one
instead, base a style on it:

```xml
<Style TargetType="DataGridColumnHeader" BasedOn="{StaticResource {x:Static tg:TreeDataGrid.ColumnHeaderStyleKey}}">
    <Setter Property="FontWeight" Value="SemiBold" />
</Style>
```

The filter button lives in the header style. A header style that is not based on
`ColumnHeaderStyleKey` loses it.

## How it works

`TreeModel` (in `WpfTreeGrid.Core`, no WPF dependency) walks the items through a
`TreeSchema` and exposes the visible rows as `TreeRows`. Every change recomputes the visible
rows in one pass and diffs them against the current list. Expand/collapse, observable child
collections, sorting and filtering all go through that path. Small changes raise single-item
Add/Remove; large ones raise a single Reset (`TreeRows.MaxIncrementalChanges`). Items are
tracked by reference, so the same instance must not appear twice in a tree.

## Layout

| Project | Target | Contents |
|---|---|---|
| `src/WpfTreeGrid.Core` | `net8.0` | Tree model, sorting and filter predicates; no WPF dependency |
| `src/WpfTreeGrid` | `net8.0-windows` | `TreeDataGrid`, the columns, the filter popup and the theme |
| `tests/*` | | xUnit tests: pure logic, plus STA tests of the control |
| `samples/WpfTreeGrid.Demo` | | A 10,000-row demo; command-line switches script screenshots |

## Licence

MIT, see [LICENSE](LICENSE).
