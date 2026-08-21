# Word rendering execution and presentation spine (2026)

## Decision

WordToolkit needs one render-execution contract and one immutable presentation
snapshot before it adds another output format.  The current semantic HTML and SVG
paths independently rediscover document meaning, while the live Word PDF exporter
is an unrelated convenience command.  That split is already producing semantic
drift: heading classification in the renderers is weaker than the source-linked
`WordOutlineGraph`, and no result describes the complete artifact lineage.

The first implementation therefore separates three claims which must never be
collapsed:

1. **Semantic preview** -- deterministic, inert HTML or SVG generated from typed
   package evidence.  It is not paginated and does not claim Word layout fidelity.
2. **Word-authoritative fixed layout** -- PDF exported by a named Microsoft Word
   version/build from an exact live version or immutable package fingerprint.
3. **Derived page raster** -- PNG pages produced from an exact PDF artifact by a
   named rasterizer/version/DPI.  A PNG is not described as a direct Word render.

There is no silent backend fallback.  A missing Word or rasterizer capability is a
failed precondition, not permission to substitute LibreOffice, a browser, or an
estimated SVG.

## Primary-source findings

- Microsoft describes Word's `Document.ExportAsFixedFormat` as the document PDF/XPS
  fixed-format path.  Its range argument distinguishes all-document, selection,
  current-page and explicit `From`/`To`; the selected `Item` determines whether
  tracked markup is exported.  Source:
  [Document.ExportAsFixedFormat](https://learn.microsoft.com/en-us/office/vba/api/word.document.exportasfixedformat).
- `WdExportRange` assigns `0` to the whole document and `3` to an explicit page
  interval.  The implementation must not pass `From`/`To` while claiming an
  all-document export.  Source:
  [WdExportRange](https://learn.microsoft.com/en-gb/office/vba/api/word.wdexportrange).
- `WdExportItem` distinguishes document content (`0`) from content with tracked
  markup (`7`).  This is an explicit render intent, not a renderer default that may
  be omitted from provenance.  Source:
  [WdExportItem](https://learn.microsoft.com/en-gb/office/vba/api/word.wdexportitem).
- Word's range exporter can emit only a portion of a document, but a Range is an
  ephemeral pair of character positions and not a durable semantic identity.
  WordToolkit therefore does not claim semantic-object cropping until a durable
  package-node-to-live-range join exists.  Sources:
  [Range.ExportAsFixedFormat](https://learn.microsoft.com/en-us/office/vba/api/word.range.exportasfixedformat),
  [Range object](https://learn.microsoft.com/en-us/office/vba/api/word.range).
- Office's fixed-format exporters produce a paginated, application-independent
  artifact, while newer exporter extension points can improve structure tagging.
  The exporting application and build remain part of the evidence because the PDF
  is still an execution result of that Word environment.  Source:
  [Extending Office PDF Export](https://learn.microsoft.com/en-us/office/pdf/extendingofficepdfexport).

## Required model

Every backend advertises a closed capability profile before execution:

- source kinds: immutable saved package and/or connected live document;
- target kinds: whole document, explicit page interval, semantic subtree;
- output formats and media types;
- pagination and text-metric authority;
- external-resource and active-content behavior;
- supported review view, bookmarks, PDF/A and accessibility tagging;
- platform/application/rasterizer requirements.

Every request is an immutable intent.  Capability negotiation either returns one
exact backend or rejects the request.  Ranking and fallback are deliberately not
part of v1.

Every artifact manifest records:

- exact source fingerprint or `live_document_id` plus `expected_version`;
- backend identity/version and environment evidence;
- primary/derived relationship between artifacts;
- media type, byte length and SHA-256;
- page interval, count and declared page boxes where available;
- raster DPI, width and height for every PNG page;
- source-mutated, external-resources-loaded and active-content-executed verdicts;
- fidelity state: `resolved`, `approximated`, `unsupported`, or `ambiguous`.

Artifact publication is a transaction.  All outputs are staged and validated
before any public path appears.  A partial publication must remove every path
created by that transaction.  If that cleanup cannot be proved, the operation
returns `ROLLBACK_FAILED` with the surviving paths; it must not report success.

## Presentation snapshot

HTML and SVG consume the same immutable `WordPresentationSnapshot`.  The snapshot
composes existing typed evidence rather than reparsing XML:

- semantic story tree and stable node identity;
- effective styles and the source-linked outline graph;
- review and canonical OfficeMath objects;
- section/page declarations;
- executed numbering when its label is deterministic;
- table, figure, font and settings evidence as each renderer starts using it.

An unresolved or view-ambiguous heading remains unresolved.  A renderer may style
it as ordinary text with a warning; it may not guess from a localized style name.

## Security and state invariants

- Saved packages are opened by Word read-only, invisible, not added to recent files,
  with macro automation forced off and link updates disabled for the bounded call.
  Those application-global settings are restored in `finally`.
- A live render requires the exact `expected_version`, checks it both before and on
  the COM thread, and compares a full read-only structural snapshot before/after.
- A saved-package render hashes the input before opening and after close.  Any byte
  drift is a hard failure.
- No exporter opens the output, follows external links, executes macros, overwrites
  an existing artifact, or returns document text/raw XML.
- A raster backend receives an explicit executable path and reports its version.
  It runs without a shell, under timeout/cancellation and byte/page/DPI bounds.

## Deliberate v1 boundary

This tranche does not build a clone of Word's layout engine.  It does not claim
pixel equivalence across Word builds, font inventories, printers, operating systems
or PDF rasterizers.  It does not crop one table/equation by semantic ID from a Word
PDF.  Those claims remain false until versioned corpus evidence and durable live
range joins exist.
