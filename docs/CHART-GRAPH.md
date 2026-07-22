# Classic DrawingML chart graph

`WordChartGraph` is the first typed read-only chart layer in the saved-package engine.
It replaces the old binary choice between opaque retention and hand-reading raw chart
XML. It does not claim that chart editing is solved.

## Modeled surface

The builder discovers classic chart parts through both package content types and
standard or Strict chart relationships. It models:

- all 16 classic DrawingML plot families;
- chart references, reachability, titles and top-level display settings;
- series identity, index/order and source roles (`tx`, `cat`, `val`, `xVal`, `yVal`,
  and `bubbleSize`);
- string, numeric, multi-level, literal, rich-text and unknown data-source forms;
- formulas, cache presence, declared point counts, actual point/index counts and format
  codes;
- category, value, date and series axes with cross-axis validation;
- `externalData`, embedded packages, images, styles, color styles, chart drawings,
  theme overrides, hyperlinks and unknown related parts;
- source element ordinals, relationship provenance and deterministic `wdch_`/`wdcs_`
  identities.

The graph understands the classic Transitional and Strict namespaces and relationship
types. Office 2016 extended `cx:chartSpace` parts are retained by the package layer and
reported as explicitly unmodeled. SmartArt, chart rendering and chart mutation remain
outside this tranche.

## Privacy and execution boundary

Cache point values are counted during validation and immediately discarded. They are
not properties of `WordChartDataSourceDefinition`, so no MCP flag can accidentally
serialize them. The native inspector redacts chart-title text and formulas by default;
bounded text appears only with `include_sensitive=true`. Part URIs, relationship IDs,
types and targets require the independent `include_source=true` switch.

The inspector parses only the supplied OPC package. It never starts Word, opens an
embedded workbook, evaluates a formula, follows an external target or fetches network
content. `externalData` and unresolved relationships remain evidence, not instructions.

## Bounded behavior

`WordChartGraphOptions` independently caps chart count, bytes and XML elements per
chart, series per chart, sources per series, cached points, formula/title characters
and diagnostics. Exceeding a cap fails with `WordChartLimitException`; malformed graph
structure fails with `WordChartProjectionException`. No partial success is disguised as
complete analysis.

`inspect_ooxml_charts` adds exact chart/type filters, offset paging and six views:
`summary`, `charts`, `series`, `axes`, `relationships` and `issues`. The default summary
returns aggregate counts plus plot-family counts. Regression tests cap the default data
and text payloads below 5,000 serialized characters and the mirrored JSON-RPC envelope
below 8,000 characters.

## Dependency integration

`WordDependencyGraph` now adds chart, series and axis nodes plus `defines_chart`,
`chart_contains_series`, `chart_contains_axis` and `chart_uses_part` edges. Chart
relationships to embedded packages, styles, colors, images and other related parts are
therefore visible to impact analysis. The remaining `smartart_diagrams` coverage label
is literal: it no longer hides charts behind a combined omission.

## Evidence

The tests cover a real LibreOffice chart with three series, two axes, nine caches and an
embedded XLSX; synthetic Strict OOXML; corrupt cache counts/indexes; unresolved cross
axes; external workbook targets; unreferenced and extended chart parts; resource-limit
failures; privacy redaction; paging and a complete MCP envelope. The versioned semantic
golden corpus fixes exact chart dependency counts for the LibreOffice fixture.

This is a credible inspection foundation. It is not chart editing, workbook
synchronization, rendering fidelity, modern extended-chart support or a promise that
Word will display a damaged chart exactly as the graph describes it.

## Standards anchors

- Microsoft documents the classic package relationships exposed by
  [`ChartPart`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.chartpart?view=openxml-3.0.1),
  including embedded packages, images, styles, colors, drawings and theme overrides.
- The classic
  [`c:externalData/@r:id`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.charts.externaldata.id?view=openxml-3.0.1)
  contract is a relationship reference, not permission to open the target.
- Microsoft exposes Office 2016 extended
  [`cx:chartSpace`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.office2016.drawing.chartdrawing.chartspace?view=openxml-3.0.1)
  as a distinct schema family; this tranche reports it instead of coercing it into the
  classic model.
