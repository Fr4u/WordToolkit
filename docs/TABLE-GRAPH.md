# Word table graph

`WordTableGraphBuilder` turns every projected Transitional or Strict `w:tbl` into a
bounded, source-linked table object. It does not treat `Table`, `TableRow`, and
`TableCell` as decorative semantic labels. The graph records the shared grid, maps each
physical cell onto a half-open logical column range, resolves nested tables, constructs
vertical-merge chains, evaluates the contiguous repeating-header prefix, and retains the
table, row, and cell properties needed to explain Word's layout decisions.

The implementation follows Microsoft's published WordprocessingML model: `w:tbl`
contains table properties, a grid, and rows; `w:tblPr` carries width, layout, positioning,
borders, cell margins, caption and description; `w:tblPrEx` supplies row-scoped table
property exceptions. See Microsoft's [table overview](https://learn.microsoft.com/en-us/office/open-xml/word/working-with-wordprocessingml-tables),
[`TableProperties`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.tableproperties?view=openxml-3.0.1),
and [`TablePropertyExceptions`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.tablepropertyexceptions?view=openxml-3.0.1).

## Logical grid and merges

For each row, the logical cursor starts at `gridBefore`. Every direct `w:tc` occupies
`max(1, gridSpan)` columns, and `gridAfter` completes the row extent. Invalid spans and
negative or out-of-range skips produce stable diagnostics and a bounded fallback; they
never disappear into guessed geometry. A row that overflows or underfills a declared
`w:tblGrid` remains explicit evidence. Microsoft's `GridBefore` contract describes the
skipped-grid semantics and Word's range restrictions in the
[`GridBefore` API](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.gridbefore?view=openxml-3.0.1)
and [Word interoperability note](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/c0d7a131-66d2-4bee-8df8-e5bae148119f).

`w:vMerge w:val="restart"` creates a merge root. An omitted value or `continue`
continues only an active merge with the exact same logical start and end columns in the
preceding row. Orphans and span mismatches are errors. Legacy `w:hMerge` is inventoried
as a separate state and diagnostic; the graph does not silently pretend it was
`gridSpan`. Structural cell properties are read from document cells, not table-style
cell properties, consistent with Microsoft's restriction on structural properties in
[table-style `tcPr`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/9e742550-b438-4b9d-9606-4ae34d83830f).

Repeating header rows are effective only while they form an unbroken prefix from the
top. A later `tblHeader` declaration remains visible but is marked ineffective and
diagnosed, matching Microsoft's [`TableHeader`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.tableheader?view=openxml-3.0.1)
contract. The accessibility linter consumes this typed result instead of reparsing row
XML independently.

## Width and floating-position semantics

Widths retain `auto`, twips (`dxa`), fiftieths-of-a-percent (`pct`), `nil`, unknown and
unspecified states. A preferred width is not reported as a guaranteed rendered width;
Word's table layout algorithm can override conflicting preferences, as the
[`TableWidth` documentation](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.tablewidth?view=openxml-3.0.1)
states. Fixed/autofit layout, justification, indentation, cell spacing, bidirectional
visual order, look mask, row height/rule, no-wrap, fit-text, vertical alignment and text
direction stay typed and bounded.

`w:tblpPr` exposes both declared and Word-effective state. Word defaults a missing
horizontal anchor to text and a missing vertical anchor to margin, ignores positioning
in text boxes, footnotes, endnotes and comments, and accepts position/distance integers
only in the range 0..32767. Empty and Word's all-zero/default case are marked ignored.
Those differences come from Microsoft's [`tblpPr` interoperability note](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/4175e818-958f-4b4f-b6f7-17003f1721b5).

Adjacent direct sibling tables with the same explicit style state receive one shared
visual-continuation group because Word can display such tables as one. The graph does
not physically merge their identities or rows.

## AI boundary

The lazy `inspect_ooxml_tables` action exposes six paged views: `summary`, `tables`,
`rows`, `cells`, `merges`, and `issues`. Exact table, row, and cell IDs can narrow the
result. The default response contains topology and counts only.

- Cell text and raw XML have no response field and cannot be requested.
- Style IDs, captions, and descriptions require `include_names`.
- Widths, grid-column widths, row/cell presentation, and floating coordinates require
  `include_layout`.
- Part URIs, semantic IDs, and source ordinals require `include_source`.
- Pages are capped at 100 objects; nested IDs and grid widths are independently capped
  at 100 with explicit truncation flags.

This is the token boundary: the model receives a small semantic object and stable
handles, never an unbounded table dump.

## Limits and measured scale

Production defaults cap the graph at 256 story parts, 100,000 tables, 1,000,000 rows,
5,000,000 cells, 65,536 grid columns per table, 10,000 diagnostics, 128 MiB per story
part, 512 MiB aggregate story XML and 5,000,000 parsed elements. Cancellation is checked
through package read, semantic projection, story parsing, rows, cells, dependencies and
response selection.

The checked-in Windows x64 fixtures contain one 20-column table with a five-row vertical
merge cycle. The 10,000-cell point completed package read, semantic projection and table
graph construction in 0.89 seconds with 110.1 MiB peak working set. The 100,000-cell
point completed in 5.23 seconds with 579.3 MiB peak working set and about 1.84 GiB of
managed allocations. These are boundary measurements on one host, not throughput
promises. The exact reports are `tables-10k.json` and `tables-100k.json` under the dated
benchmark directory.

## Honest exclusions

The graph is read-only. It does not yet mutate grids or merges, calculate final page
breaks or row splits, reproduce Word's full autofit algorithm, resolve conditional table
style formatting, infer semantic header associations across complex merged cells, or
render a table. Legacy `hMerge` is recognized but not normalized. Floating-position
state describes published Word behavior; licensed multi-version visual round trips are
still required before claiming rendering equivalence.
