# Stage results

## Typed document properties and field dependencies — 2026-07-24

- Added a bounded read-only graph for OPC core, Office extended and custom typed
  properties. Exact relationship/content-type families, Strict/Transitional namespaces,
  standard custom `fmtid`, numeric `pid`, duplicate case-insensitive names/IDs, one typed
  value child and scalar lexical forms are validated. Core created/modified timestamps
  additionally require an `xsi:type` QName resolving to `dcterms:W3CDTF`. Invalid values
  remain diagnosed and cannot resolve a field; complex/binary values are classified
  without decoding.
- Added lazy `inspect_ooxml_properties` as native action 93. Summary-first paging, exact
  `wdp_`/family/type filters and a 32 KiB projected-item ceiling keep the response
  bounded. Custom names, scalar values, HMAC equality fingerprints and source provenance
  have four independent opt-ins. Raw XML, complex values and field results are absent;
  the action never opens Word or mutates the package.
- Integrated document-property and persistent-document-variable definition nodes with
  `DOCPROPERTY` and `DOCVARIABLE` reads in the unified dependency graph. `SET`/`ASK` are
  not misrepresented as persistent definitions. The nine-producer golden oracle now
  records the added definition edges; its existing non-property counts remain unchanged.
- Focused verification passes all 14 property/dependency and complete-golden-oracle
  cases. The full gates pass **471 Engine tests**, **318 Native tests** and
  **1,309 Python tests** with 16 intentional environment/model skips. Ruff is clean;
  mypy is clean across the maintained 29-file `src/wordtoolkit` layer. The broader
  historical `scripts` directory is not part of that mypy claim and still has known
  typing debt.
- Release packaging now embeds the complete manifest SemVer, including build metadata,
  into the native assembly. `--version`, MCP `serverInfo.version` and
  `toolkit_version` therefore identify the exact installed build instead of collapsing
  every package to the same base version.
- Two pinned .NET SDK 8.0.423 builds from commit `f378726` produced byte-identical
  196-file, 86,133,096-byte expanded trees and 36,490,920-byte ZIPs at SHA-256
  `6505f683e21486c257f55ad6cebd102cf661ea5b57b3b2701ad0fe4d51c644d8`.
  The personal marketplace and enabled cache at
  `0.39.0+codex.20260724033816` have zero path/length/hash differences and contain zero
  Python files. The installed executable reports that exact version and 93 actions.
- Fresh installed-runtime calls over `lo_chart.docx` report 27 valid properties with
  zero issues, then 75 dependency nodes and 83/83 resolved edges with exact property/
  variable coverage. The complete mirrored responses are 3,278 and 7,320 characters;
  Word remains unopened and the source SHA-256 remains
  `222628bcdb587c232e968d6aa1ba0a70dfd80845a4a2b8050316ec9d142ad33f`.
  The licensed real-Word acceptance gate also passes the complete eight-row complex
  derivation of `\int x^3e^{2x}\sin(3x)\,dx`: six integrals and six owned differentials
  survive native build-up/readback with equal canonical contract hashes, and the
  disposable document is discarded.

## Typed active-content metadata graph — 2026-07-24

- Added a bounded read-only graph for legacy/ISO Word OLE declarations, embedded and
  linked targets, ActiveX XML/binary bindings, embedded-package payloads, VBA
  project/data/support/customization parts, VBA project-signature parts and OPC package
  signature topology. Exact standardized relationship URIs are required; suffix
  lookalikes do not enter the model. Orphan declarations, duplicate relationship IDs,
  missing targets, macro-container contradictions and invalid signature topology remain
  typed diagnostics.
- Added lazy `inspect_ooxml_active_content` as native action 92. Summary-first paging and
  exact IDs/kinds keep requests small. Names, targets, hashes and source provenance have
  separate opt-ins. Raw XML, field-code text, binary values, ActiveX license strings and
  property values are unavailable. The action never opens Word, decodes a binary, opens
  an embedded package, executes code, follows an external target or presents signature
  presence as cryptographic validation.
- Integrated typed payload, declaration and ActiveX nodes plus six edge families into
  the unified dependency graph and shared `wop1` lease. The graph now exposes an exact
  active-content coverage flag and source issue count while retaining explicit gaps for
  binary internals/execution, cryptographic validation/resigning and encrypted packages.
  The cross-producer golden oracle changed only for the LibreOffice chart fixture whose
  embedded workbook now receives one payload node and two typed edges.
- Current local verification passes 457/457 Engine and 313/313 Native tests with no
  skips, plus 1309 Python/OOXML tests with 16 intentional environment/model skips.
  Release builds have zero warnings, Ruff is clean and mypy passes all 29 maintained
  Python source files. Four native inspector regressions enforce default redaction, a
  sub-5,000-character direct result, a sub-8,000-character complete gateway envelope, zero COM
  calls, independent disclosure opt-ins, exact selector failure and the shared operation
  budget boundary down to one byte. Two independent .NET SDK 8.0.423 package builds from
  commit `41e85a1` produced byte-identical 196-file, 86,038,586-byte trees and
  36,461,821-byte ZIPs at SHA-256
  `5ef40ebf6a112bc2b791b5e862cafc78a0cd275ce88c46721f39a65ce61c677e`.
  The personal marketplace and enabled cache at `0.39.0+codex.20260724023427` have zero
  path/length/hash differences and contain zero Python files. Fresh installed-runtime
  calls over `lo_chart.docx` report one `embedded_package` payload, 48 dependency nodes,
  56 edges and active-content coverage; both actions keep Word, macros, binary decoding,
  embedded-package opening, signature verification and external traversal disabled. The
  complete dependency response is 7,040 characters, below its 8,000-character gate.

## Typed bibliography source graph — 2026-07-23

- Added a provider-neutral, read-only bibliography graph for both supported Word source
  namespaces. It retains source provenance, stable identity, typed Tag/SourceType/GUID/
  LCID fields, bounded scalar metadata, contributor roles, people and corporate names.
  Unique case-insensitive tags resolve `CITATION` fields to concrete source nodes in the
  unified dependency graph; missing or duplicate tags remain unresolved.
- Added lazy `inspect_ooxml_bibliography` as native action 91. Default output is paged and
  redacts tags, titles, GUIDs, names, field values, style paths and URIs. The fixed policy
  parses the package only: Word is not opened, fields are not evaluated, bibliography
  XSLT is not executed and external targets are not followed. One 640 MiB `wop1` lease
  now spans OPC, semantic, reference and bibliography projection.
- Closed the independent review's three P1 defects before publication. People and
  corporate names are capped across the whole source; response projection has a shared
  64 KiB payload budget; low-entropy values use process-keyed HMAC fingerprints; and
  duplicate singleton identity fields fail closed rather than selecting the first XML
  value. Manual final diff review additionally capped unique unmodeled element names at
  256 per source and charged their characters before display-string materialization.
  Empty citation lookup fails unresolved instead of throwing.
- Final local verification passed 448/448 Engine and 291/291 Native tests with no skips,
  plus 1309 Python tests with 16 intentional environment/model skips. Release builds of
  the native runtime, Open XML SDK validator adapter and benchmark project have zero
  warnings/errors. Ruff is clean and mypy passes all 29 maintained Python source files.
- The reproducible Release benchmark generated 10,000 unique Book sources, 10,000 people
  and 10,000 matching `CITATION` fields. Seven bibliography builds resolve 10,000/10,000
  citations with zero issues, retain the package fingerprint and measure 642.5681 ms
  median, 811.9211 ms p95/max, 245,631,072 median thread-allocated bytes and
  81,230,184/671,088,640 accounted operation bytes on Windows 10 x64, .NET 8.0.29,
  12 logical processors, workstation GC. The raw JSON is checked in under
  `docs/benchmarks/bibliography-10k-2026-07-23.json`. No prior-feature baseline exists,
  so no speedup is claimed.

## Remote heterogeneous draft batch — 2026-07-23

- Added the provider-neutral `wordtoolkit.apply_document_operations/1.0` contract and
  the remote MCP adapter over all 33 ordinary draft-mutator types. The generated closed
  `oneOf` schema rejects read-only hybrid actions, unknown/nested transaction fields,
  empty or 17-operation lists and invalid types. Runtime repeats validation through each
  standalone Pydantic contract and enforces a 1 MiB aggregate argument limit. The same
  exporter produces `schemas/draft-operations.v1.json` with input/success/error schemas,
  permissions, side effects, limits and examples that pass Draft 2020-12 validation.
- One locked clone receives the ordered operations; the final candidate is snapshotted
  and structurally validated once, then the active engine swaps and `draft_version`
  advances once. Injected middle-operation and final-validation failures preserve the
  original engine, package hashes and version. Cancellation drains both staging and
  transaction success before lock release. Image binding uses one Apps-compatible
  top-level `files` array plus per-operation `file_index`; missing, nested and unused
  references fail before a fork. Partial downloads are removed, while a complete staged
  upload remains explicitly outside document atomicity and is covered by regression.
- Final local verification passed 438/438 Engine and 283/283 Native tests with no skips,
  the Open XML validator Release build with zero warnings/errors, and 1309 Python/OOXML
  tests with 16 intentional environment/model skips. Ruff and targeted mypy over the
  changed contract, adapter and test modules are clean.
- Fifteen Windows x64/Python 3.13 in-process FastMCP samples measured
  `format_paragraph -> enable_track_changes -> insert_paragraph` at 189.479 ms median
  across three standalone COW calls and 70.901 ms in one batch (-62.58%, -118.578 ms).
  The measured compact request JSON fell from 480 to 427 characters (-11.04%), with one
  instead of three COW commits/version increments. Creation/close and the optional SDK
  validator were excluded. A representative one-step compact success envelope fell from
  290 to 102 characters by removing repeated protocol/backend/atomicity fields and
  operation-name echoes. The generated 33-variant input schema is regression-capped
  below 20,000 compact characters and measured 19,088; the complete compact remote
  catalog rose from 57,566 to 77,439 characters (+34.52%), so the request saving is not
  misreported as a whole-catalog token saving.

## WordToolkit 0.39.0 operation-wide dependency resource lease — 2026-07-23

- Added one cumulative `word_operation_accounted_v1` lease across ZIP/OPC admission,
  metadata, lossless XML, semantic projection, typed styles/numbering/references/
  sections/charts/figures/content-controls/tables and final dependency-graph retention.
  The calibrated default is 640 MiB. The existing 128 MiB graph-local `wdg1` boundary
  remains independent; MCP reports the new lease only as compact `operation_budget`
  model `wop1`.
- Moved hostile-archive rejection ahead of `ZipArchive.Entries`: bounded EOCD/ZIP64
  preflight now rejects excessive entry counts, central-directory bytes, multi-disk
  archives and invalid subtraction-based offsets. Public non-seekable streams pass
  through a 576 MiB bounded random-name `CreateNew` spool with `FileShare.None` and
  `DeleteOnClose`; its buffer is charged and zeroed. OPC metadata counts, diagnostics,
  XML retention and typed aggregate limits all reject before the next guarded retention.
- Cancellation now reaches metadata XML loading and semantic fingerprint recursion.
  Resource exhaustion returns bounded `PACKAGE_LIMIT` stage/attempted-charge data; it
  does not return paths, XML or document content. Case-insensitive ZIP collision
  diagnostics disclose only a spelling count instead of joining attacker-controlled
  names. The exact dependency input schema remains byte-for-byte unchanged at SHA-256
  `e371a9c3800f58dcd685c80a9d5a63cee967aa2ba563a8bb01965c373f06b7a2`.
- Final local verification passed 438/438 Engine and 283/283 Native tests with no skips.
  The full Python/OOXML lane passed 1,279 tests with 16 intentional environment/model
  skips; Ruff is clean and mypy succeeds across 28 maintained source files. Independent
  correctness/resource and final contract red teams found no remaining P0/P1 issue.
- Five cold 99,997-node samples deterministically charge 539,282,576/671,088,640
  operation bytes and 130,132,744/134,217,728 graph bytes. Medians are 2,956.082 ms
  dependency build, 5,491.3294 ms measured total, 175,334,544 retained managed bytes,
  1,610,477,144 allocated bytes and 615,305,216 bytes peak working set. Against the
  same-host 0.38 series, peak working set rose 6.75%; no peak-memory win is claimed.
- Two pinned .NET SDK 8.0.423 builds produced byte-identical 196-file, 85,759,136-byte
  trees and 36,380,971-byte ZIPs at SHA-256
  `2875209550cca57b36d0e99873d443c337f89950e5230d3083110398da0d7468`.
  The package contains no Python files. The enabled personal-plugin cache at
  `0.39.0+codex.20260723142922` has zero path/length/hash differences from that tree;
  installed initialization reports runtime 0.39.0 and capability discovery reports 90
  actions. A packaged read-only dependency smoke call returned 209 nodes, 259 edges,
  both `wop1` and `wdg1`, and left the source DOCX hash unchanged.

## WordToolkit 0.38.0 dependency-graph byte boundary — 2026-07-23

- Replaced two eager dictionaries of per-node edge arrays with compact incoming and
  outgoing compressed-row offset/index arrays. The public typed adjacency view preserves
  deterministic kind/ID order without allocating a list for every node; endpoint checks
  and cancellation remain fail-closed.
- Added the deterministic `dependency_graph_accounted_v1` model. Production rejects
  before retaining an item that would cross 128 MiB and independently caps keys and
  metadata at 65,536 characters. The MCP response exposes only
  `byte_budget: {model, used, maximum}` and stays inside the existing sub-8,000-character
  full-envelope regression gate.
- The exact-budget regression rebuilds one graph with a ceiling one byte below its
  measured usage and requires `WordDependencyLimitException`. Compact incoming/outgoing
  ordering, missing-node empties, option validation and Native summary disclosure are
  covered. Full local results are 429/429 Engine, 281/281 Native and 1279/1279 Python
  passed with 16 intentional Python skips; Ruff and mypy are clean.
- Five cold-process 99,997-node/99,996-edge Windows x64 samples per version account
  130,132,744 of 134,217,728 bytes and use exactly 1,599,952 adjacency-index bytes.
  Against 0.37.0, median retained managed memory fell 16.8%, managed allocations 4.2%,
  dependency-build time 9.0% and total measured time 5.4%. Median peak working set was
  effectively flat (+0.1%), so no peak-memory reduction is claimed.
- This closes only the dependency graph's own missing byte boundary. The semantic and
  typed source graphs are still constructed first under independent limits; a shared
  operation-wide resource lease and immutable parsed-story storage remain open work.
- Two pinned .NET SDK 8.0.423 builds produced byte-identical 196-file, 85,730,528-byte
  trees and 36,372,963-byte ZIPs at SHA-256
  `69c3406b238590dae096370a550ff6902352972cbe942b2344e2d80dca1e0541`.
  The personal marketplace and enabled 0.38.0 cache have zero path/length/hash
  differences. Installed capability discovery reports runtime 0.38.0 and 90 actions.
  A packaged MCP smoke test over `pandoc_image_vml.docx` returns 119 nodes, 175 edges,
  `wdg1` usage 194,760/134,217,728 bytes and a 7,526-character call response while
  omitting six diagnostic items by default, keeping Word closed and leaving external
  targets unfollowed.

## WordToolkit 0.37.0 Figure/Caption graph — 2026-07-23

- Added a fingerprint-bound logical Figure/Caption graph, conservative
  `mc:AlternateContent` handling, strict Transitional/Strict QName parsing, inert
  internal/external resources and ambiguity-preserving caption association.
- Default lazy output is redacted and issue-free unless requested; closed runtime input
  validation matches the schema. Word, binary payloads, external targets and active
  content are never opened or executed by this action.
- Full Engine suite: **427 passed**. Full Native suite: **280 passed**. Python/OOXML:
  **1,279 passed, 16 intentional skips**. Ruff and maintained 28-module mypy are clean.
- The current 10,000-figure/10,000-distinct-relationship benchmark reports 1,897.6 ms
  median and 2,084.6 ms p95 across seven graph builds. It retained 317,194,760 managed
  bytes from the pre-read baseline and peaked at 1,251,549,184 bytes working set.
- Two pinned .NET SDK 8.0.423 builds produced identical **196-file**,
  **85,719,250-byte** trees and identical **36,369,889-byte** ZIPs at SHA-256
  `addb0ac796b9c5e41e6174f2bd1937d9fe996ae88015a81af5b000968caa63c7`.
  The enabled `wordtoolkit@personal` 0.37.0 cache has zero file/hash differences from
  the built package and its installed CLI reports 90 actions.
- Independent red teams found and forced fixes for quadratic relationship lookup,
  hidden VML direct targets, mixed revisions, false MCE primacy, foreign namespaces,
  unbounded QName collection, input-schema drift, repeated issue previews and error
  provenance leaks. At this 0.37.0 checkpoint the system-level dependency graph still
  lacked a byte budget; 0.38.0 closes that graph-local gap without claiming a
  whole-pipeline memory ceiling.

## WordToolkit 0.36.0 exact-target semantic SVG — 2026-07-23

- Added `wordtoolkit.render_ooxml_semantic_svg/1.0` as the seventh transport-neutral
  Engine/CLI/MCP operation. It requires the exact package fingerprint and semantic target
  ID, then creates one deterministic, self-contained SVG with selectable text,
  accessibility metadata and estimated flow/table geometry. The closed contract states
  `paginated=false`, `exact_text_metrics=false` and `pixel_equivalence_claimed=false`.
- HTML and SVG now share package preparation, target authorization, backend metadata and
  atomic create-new publication while retaining their public 1.0 contracts. The SVG path
  is bounded before final XML materialization at 40,000 text lines, 100,000 generated
  elements, a 1,000,000-pixel canvas dimension and a 256 MiB artifact. UNC and Windows
  device namespaces are rejected before any filesystem probe, preserving `network=none`.
- Security regressions target the hostile text, external hyperlink and complex field
  themselves: package text resembling `<script>` remains inert text, no active link or
  external URI is emitted, cached field results survive and instructions remain absent.
  Review annotations are backed by real review-graph evidence. Resource exhaustion,
  stale/missing/out-of-scope targets and structurally non-renderable roots fail closed
  without an output or temporary-file residue. Independent red-team review found no
  remaining P0, P1 or P2 issue in the final diff.
- Full Engine suite: **408 passed, 0 skipped**. Full Native suite: **272 passed, 0
  skipped**. Full Python/OOXML suite: **1,279 passed, 16 intentional skips**. Ruff, the
  maintained 28-module mypy lane, six HTML/SVG schema regressions and the modified-file
  C# whitespace lane are clean.
- Native package version: `0.36.0+codex.20260723100732`, with 15 exposed core/gateway
  tools and **89** native actions. Seven actions publish complete operation-version,
  permission, reversibility and output-schema metadata.
- Two repository-pinned .NET SDK 8.0.423 Windows builds produced byte-identical
  **196-file**, **85,514,480-byte** expanded trees and byte-identical
  **36,306,172-byte** ZIPs. ZIP SHA-256:
  `5076fc4b41670ae05752ab36c0d23ff66fac7a2b8d753706b15473a478e4682a`;
  expanded manifest SHA-256:
  `f9bfd22a28d04e509968f3fb799e2e283e9ff8e3ccabdb14fdbc7503c1bc7207`.
  The installed and enabled `wordtoolkit@personal` cache has the same version, file count,
  byte count and manifest with zero file differences from the built package.
- Hosted CI run [`29992366785`](https://github.com/Fr4u/WordToolkit/actions/runs/29992366785)
  passed all five jobs for commit `d6b663922a45f8e271872f61a63dffa86bf21256`.
  Downloaded Windows artifact `8557592140` matched both pinned-SDK local ZIPs byte for
  byte and its normalized 196-file tree had zero length/hash differences. The first local
  package built under SDK 10.0.300 was rejected as release evidence after its bundled
  self-contained runtime differed; the official SDK 8.0.423 archive was verified against
  Microsoft release metadata before rebuilding.
- The packaged executable selected the first semantic table in the checked-in Mammoth
  fixture under its exact fingerprint and created one **2,266-byte** SVG at SHA-256
  `b8e436cd184ab1b316321e305ad850f1f21a43e518428d6d7e9e54f6a48536d9`.
  XML inspection found four real text nodes, zero script/`foreignObject` nodes and zero
  active-link, event or external-URI attributes. The source hash and Word process count
  did not change; the response contained no source/output path and none of the four
  rendered text values.
- The checked-in 10,000-node benchmark projected 9,996 nodes, selected one six-node table
  and emitted a **1,305-byte** SVG seven times with one SHA-256
  (`a1c854f359ad28b50c2661576b0a348cdc0f4e0a1e7ac21c155c2ba414fe2ad3`).
  Median render time was **449.10 ms**, p95/max **844.87 ms**. The source stayed
  byte-identical, Word did not open and all external/active-content flags remained false.
  Whole-package projection still dominates; this is determinism and bounded-artifact
  evidence, not a Word-layout or proportional speedup claim.

## WordToolkit 0.35.0 semantic HTML renderer — 2026-07-23

- Added `wordtoolkit.render_ooxml_semantic_html/1.0` through one dependency-free Engine,
  strict JSON CLI and lazy MCP path. It renders the main body or all projected text
  stories to deterministic self-contained HTML without opening Word, following external
  relationships or executing active content.
- The artifact is explicitly `semantic_preview_non_paginated`: cached field results are
  retained while instructions are suppressed, hyperlinks are inert, tracked changes are
  annotated, equations use bounded linear-text fallbacks, and drawings/extensions become
  visible placeholders. CSP, HTML escaping, a 256 MiB limit, adaptive block containers,
  create-new writes and a two-writer race regression harden the boundary.
- Added an additive, fingerprint-bound `target_node_id` mode to the same contract. It
  renders one semantic subtree without siblings and rejects unbound, stale, missing,
  out-of-scope or structurally non-renderable targets before output creation. Selected
  rows, cells, nested tables, row-level revisions and recursively nested row/cell content
  controls receive valid synthetic table context; ambiguous mixed wrapper chains fail
  closed. Engine, CLI and MCP produce byte-identical selected artifacts and return no
  selected text in their JSON responses.
- On the checked-in 10,000-node synthetic benchmark path, the selected six-node table
  produced a 3,074-byte artifact versus 541,043 bytes for the full document (0.5682%).
  Two selected renders were byte-identical. Full-package projection still dominates
  latency, so this is recorded as an artifact/content-scope reduction, not a matching
  speedup claim.
- Full Engine suite: **396 passed, 0 skipped**. Full Native suite: **269 passed, 0
  skipped**. Full Python/OOXML suite: **1,276 passed, 16 intentional skips**; Ruff and
  the 28-module mypy lane are clean.
- Native package version: `0.35.0+codex.20260723091515`, with 15 exposed core/gateway
  tools and **88** native actions. Six actions now publish complete operation-version,
  permission, reversibility and output-schema metadata.
- Two supported Windows builds produced byte-identical **196-file**,
  **85,442,968-byte** expanded trees and byte-identical **36,285,575-byte** ZIPs. ZIP
  SHA-256: `a0737809d981cd739cade8f4036818408877afd903ff1049d538f29b4b99fb41`;
  expanded manifest SHA-256:
  `a45a778680bf0c9773089556e8a88104d155ebbf044bd852a24f20155d0596e4`.
  The manifest format is sorted relative path, byte length and lowercase file SHA-256,
  tab-separated and serialized as UTF-8/LF without a final newline.
- The packaged executable selected the real Mammoth table by semantic node ID under its
  exact package fingerprint and created one 3,742-byte HTML artifact at SHA-256
  `abb38860e9c0e2ad34bbec6131fe6438e64c8ee1ff7c2305fb49e3c1cded3c35`.
  An HTML5 parser found one table, one body, two rows and four cells with zero invalid
  table/body/row/cell parent relationships. The source hash and Word process count did
  not change, and the JSON response contained none of the four fixture cell values.
- Fifteen cold packaged processes rendered the checked-in Mammoth fixture at **254.66
  ms median** and **290.44 ms p95/max**. All 15 artifacts were the same 3,628 bytes with
  one SHA-256. Static HTML-tree, CSP and external-resource checks passed. The in-app
  browser blocked `file://`, so no visual screenshot claim is made and no bypass was used.
- Hosted CI run [`29988472344`](https://github.com/Fr4u/WordToolkit/actions/runs/29988472344)
  passed all five jobs for commit `dc72a8e811217bb9515b0ad30810c8925b3081cb`.
  Downloaded Windows artifact `8556049406` matched both local ZIPs byte for byte; after
  normalizing its single `wordtoolkit/` wrapper, the hosted 196-file tree had zero
  length/hash differences and the same expanded-manifest SHA-256.

## WordToolkit 0.35.0 logical table graph — 2026-07-22

- Added `WordTableGraphBuilder`, a bounded Transitional/Strict WordprocessingML read
  graph for declared grids, physical-to-logical cell placement, `gridBefore`/`gridAfter`,
  `gridSpan`, exact-span vertical merges, separately retained legacy `hMerge`, nested
  tables, contiguous repeating headers, row table-property exceptions, widths,
  fixed/autofit layout and Word-effective floating positioning.
- Integrated nested tables and vertical-merge continuation cells into the shared
  dependency graph. Table diagnostics now retain a source-linked dependency subject,
  and the accessibility linter consumes the same typed header result instead of parsing
  row XML independently.
- Added lazy `inspect_ooxml_tables` with summary/table/row/cell/merge/issue views, exact
  table/row/cell filters, paging and independent layout/name/source opt-ins. Cell text and
  raw XML have no response field. Nested ID and grid-width arrays are capped at 100 with
  explicit truncation flags.
- Full native engine suite: **340 passed, 0 skipped**.
- Full native MCP host suite: **228 passed, 0 skipped**.
- Full Python compatibility suite: **1,273 passed, 16 intentional skips**; Ruff clean.
  Open XML validator built with zero warnings. Generated basic and advanced artifacts
  passed structural and visual checks; the advanced report contained zero validation,
  warning or accessibility findings.
- Checked-in table scale points cover 10,000 and 100,000 physical cells. The smaller
  point completed package read + semantic projection + table build in 0.89 seconds with
  110.1 MiB peak working set. The larger completed in 5.23 seconds with 579.3 MiB peak
  working set and 1,885.1 MiB managed allocations. Both produced zero diagnostics; the
  production five-million-cell limit remains a rejection bound, not a throughput claim.
- Native package version: `0.35.0+codex.20260722231852`, with 14 public gateway tools
  and **84** lazy native actions.
- Two independent local builds produced byte-identical **195-file**, **85,056,480-byte**
  expanded trees and byte-identical **36,172,619-byte** ZIPs. ZIP SHA-256:
  `910a1cece37397f61568ea0a230fe663fdf54d834d8a8d367fa56b37ddfe1c13`.
  Executable SHA-256:
  `59fb8419dbe664d1b1ae7f0b5df35b7738cfd1c43b69719049d8e06dd64cd138`.
  Runtime assembly SHA-256:
  `f6e8923897b7578a0344347ec5c43f802f01f2de093553a819d036736ac698cd`.
  Document-engine assembly SHA-256:
  `1d7780bf01c5242b9e3d41e162d870a4bd7c7131653287e0c6737b97b18cc888`.
- The exact packaged MCP inspected the advanced torture DOCX as 2 tables, 34 rows and
  251 cells with zero table issues. Its complete compact JSON-RPC response was 2,308
  characters, did not start Word, left the Word process set unchanged, and contained no
  source part path, cell text or raw table XML.
- Mandatory hosted CI run [`29959678412`](https://github.com/Fr4u/WordToolkit/actions/runs/29959678412)
  passed all five jobs with zero annotations. Its downloaded Windows artifact matched
  both local ZIPs and the 195-file expanded tree byte for byte at the SHA-256 above.
  Human review and the licensed Word release gate remain required; the public release
  stays at 0.34.0.

## WordToolkit 0.35.0 content-control and Custom XML binding graph — 2026-07-22

- Added a typed, source-linked read graph for `w:sdt` controls, physical Custom XML
  stores, Word's built-in core/extended property stores, standard/Office 2013 bindings,
  restricted child-element XPath targets and repeating-section topology.
- Integrated control/store/target/repeating nodes and edges into the shared dependency
  graph. The nine-fixture semantic oracle was updated only after independent OPC part
  enumeration confirmed every newly counted physical or built-in store.
- Added lazy `inspect_ooxml_content_controls` with exact filters, paging and independent
  name/binding/source opt-ins. Custom XML values, visible bound values and raw XML never
  leave the action. Nested identifier arrays are capped at 100 and namespace/schema
  arrays at 20, with explicit truncation flags.
- Replaced quadratic positional XPath sibling rescans with one per-store parent/QName
  index and bounded intermediate expansion.
- Full native engine suite: **331 passed, 0 skipped**.
- Full native MCP host suite: **223 passed, 0 skipped**.
- The checked-in 10,000-binding point completed in 2.30 seconds with 201.9 MiB peak
  working set. The 100,000-control/binding/target point completed in 15.40 seconds with
  1,761.8 MiB peak working set and 5,960.7 MiB managed allocations. It required a
  benchmark-only 64 MiB metadata budget; production remains at 16 MiB. The result proves
  reachability on the measured 64 GiB host, not cheap operation.
- Native package version: `0.35.0+codex.20260722223601`, with 14 public gateway tools
  and **83** lazy native actions.
- Two independent local builds produced byte-identical **195-file**, **84,897,393-byte**
  expanded trees and byte-identical **36,123,774-byte** ZIPs. ZIP SHA-256:
  `dcaa12c58eed3b1b03f10c6772083a934c62e84f4615e738bee975f37fc7d471`.
  Executable SHA-256:
  `59fb8419dbe664d1b1ae7f0b5df35b7738cfd1c43b69719049d8e06dd64cd138`.
  Runtime assembly SHA-256:
  `5f3cee35fe78dd0f93392413b3990412b2e3d3da775b39abb216fd7d93ebecdc`.
- The exact packaged executable inspected the advanced torture DOCX as one control, one
  resolved binding, one target and two stores with zero issues and no Word process. The
  complete compact JSON-RPC response was 2,718 characters and contained no Word/Custom
  XML part path or raw XML element.
- Mandatory CI run `29956272868` passed all five jobs with zero check-run annotations.
  Its downloaded Windows artifact
  matched both local ZIPs byte for byte; the expanded hosted and local trees each had
  195 files and 84,897,393 bytes with zero file/hash/length differences. Human review
  and the licensed Word release gate remain required; the public release stays at
  0.34.0.

## WordToolkit 0.35.0 markup-compatibility graph — 2026-07-22

- Added a bounded, source-preserving ECMA-376 Part 3 fifth-edition graph for
  `Ignorable`, `ProcessContent`, `MustUnderstand` and `AlternateContent`, with explicit
  application/extension configuration and non-executed legacy preservation-hint
  inventory.
- Added the lazy, read-only `inspect_ooxml_markup_compatibility` action. Namespace and
  source details are independently opt-in; the action never preprocesses XML, follows
  external targets, opens Word or mutates the package.
- Full native engine suite: **316 passed, 0 skipped**.
- Full native MCP host suite: **218 passed, 0 skipped**.
- Full Python compatibility suite: **1,273 passed, 16 skipped**; Ruff clean. Open XML
  validator built with zero warnings, and the generated basic/advanced artifacts passed
  structural and visual checks.
- Corrected MCE scale points remain below their declared ceilings at 99,999, 499,999 and
  998,998 XML elements. The largest point took 4.78 seconds for package read plus graph
  build, retained about 1.03 GB managed memory and is documented as a hard boundary, not
  an ordinary workload.
- Native package version: `0.35.0+codex.20260722212907`, with 14 public gateway tools
  and **82** lazy native actions.
- Two independent local builds produced byte-identical **195-file**, **84,713,190-byte**
  expanded trees and byte-identical **36,077,499-byte** ZIPs. ZIP SHA-256:
  `5312432d0ff5e8ea2c0c4ca664011225be7ea7ad49a0b7b091aa9db4efba2ea3`.
  Runtime assembly SHA-256:
  `d8f3346b7f29b9bbf85fc4b1f2d38d903836c514a245629e85bb16694b58c78b`.
- The exact packaged MCP inspected the real LibreOffice chart document as 11 XML parts,
  five compatibility rules and one alternate-content block. Its data object was 1,218
  characters and complete response 3,054 characters; it returned no namespace URI,
  source-part name, formula or raw XML and left the Word process set unchanged.
- Mandatory CI run `29951495361` passed all five jobs with zero annotations. Its
  downloaded Windows artifact matched both local ZIPs and expanded trees byte for byte.
  Human review remains required. The public release stays at 0.34.0; no licensed Word
  release gate is claimed for this saved-package tranche.

## WordToolkit 0.35.0 classic chart graph — 2026-07-22

- Added a bounded typed graph for classic Transitional and Strict DrawingML charts:
  all 16 plot families, series/source roles, formulas, literal/reference/multi-level
  cache metadata, axes/cross-axis links, `externalData` and related package parts.
- Cached point values are discarded by the engine and have no public model property.
  Titles, formulas, format codes and source relationships are independently redacted;
  external targets and embedded workbooks are never opened.
- The shared dependency graph now includes chart, series and axis nodes plus chart
  containment and related-part edges. Office 2016 extended charts remain preserved and
  explicitly unmodeled; chart editing/rendering/workbook synchronization are not
  claimed.
- Full native engine suite: **309 passed, 0 skipped**.
- Full native MCP host suite: **213 passed, 0 skipped**.
- Full Python compatibility suite: **1,273 passed, 16 skipped**; Ruff clean.
- Native package version: `0.35.0+codex.20260722203415` with 14 public gateway tools
  and **81** lazy native actions.
- Native package: **195 files**, **84,535,811 expanded bytes**, **36,034,548 ZIP bytes**.
- ZIP SHA-256:
  `26b7c1ff41933a26a2cd812aec70abe2f74408005eba8a76d48918409f2f0f88`.
- Executable SHA-256:
  `59fb8419dbe664d1b1ae7f0b5df35b7738cfd1c43b69719049d8e06dd64cd138`.
- Runtime assembly SHA-256:
  `59ac4a47cfe5446cfcd4248f6b20a1398b740fae2967dcc98beaf7416efa3c09`.
- Two independent local builds produced byte-identical ZIPs and byte-identical
  195-file expanded trees. The packaged executable inspected the real LibreOffice
  chart fixture as one plot, three series, two axes, 27 cached point entries and one
  embedded workbook without opening Word or the workbook. Its compact content was 875
  characters and the complete tool-call response was 2,260 characters.
- Mandatory CI run `29947553481` passed all five jobs, and its downloaded Windows ZIP
  matched both local archives byte for byte at the same size and SHA-256. Human review
  remains required. The public release stays at 0.34.0, and the licensed 48-action Word
  gate is not re-claimed by this saved-package-only tranche.

## WordToolkit 0.35.0 semantic golden corpus — 2026-07-22

- Added a versioned semantic oracle for nine public DOCX fixtures from Apache POI,
  Pandoc, Mammoth, LibreOffice and a real-world Microsoft Word document.
- The manifest binds every fixture by file SHA-256 and package fingerprint, then checks
  exact semantic and dependency-kind counts plus style, numbering, field, review,
  section and effective-formatting facts. It contains no document text or raw XML.
- Independent source-part checks confirmed the selected style inheritance, numbering
  mappings, field types, comment anchors, tracked move pair, text boxes, six
  header/footer references and chart relationship.
- Focused semantic-oracle test: **1 passed** in 612 ms.
- Full native engine suite: **284 passed, 0 skipped**.
- Full native MCP host suite: **208 passed, 0 skipped**.
- Full Python compatibility suite: **1,273 passed, 16 skipped**; Ruff clean.
- Open XML SDK validator: built successfully with zero compiler warnings. Regenerated
  basic and advanced artifacts passed structural, SDK, accessibility and LibreOffice
  visual checks; the advanced document retained 17 native equations across 11 pages.
- Native package version:
  `0.35.0+codex.20260722195005`.
- Native package: **195 files**, **84,409,303 expanded bytes**, **35,998,031 ZIP bytes**.
- ZIP SHA-256:
  `bdbeaf9414e1b56311c6518c49787a76dc700da0fff184cbf62f8bb2c0079ed5`.
- Executable SHA-256:
  `59fb8419dbe664d1b1ae7f0b5df35b7738cfd1c43b69719049d8e06dd64cd138`.
- Runtime assembly SHA-256:
  `d08bb344be1d80e1836dd5bf5b44ae8d7b2f529414fd536e825c78f9931e9634`.
- Two independent local builds produced byte-identical ZIPs and byte-identical expanded
  trees. Packaged MCP initialization reported protocol `2025-06-18`; a compact semantic
  inspection returned the expected 17-node `poi_styles.docx` graph in a 1,448-character
  JSON-RPC response without opening Word or returning source XML.
- This is stronger semantic regression evidence, not release approval or a claim of
  Word-identical rendering. The public release remains 0.34.0 until the draft pull
  request receives human review and the self-hosted Microsoft Word gate runs.

## WordToolkit 0.16.0 verification — 2026-07-20

- Full Python suite: **1,284 passed, 16 skipped**.
- Focused live Word and local STDIO suite: **70 passed**.
- Ruff: clean.
- mypy: clean across 28 first-party source files.
- MCP schemas: 65 remote tools and 103 unique local tools, including all seven
  new live Find/review/layout/Undo contracts.
- WordToolkit skill validation: passed.
- Codex plugin validation: passed.
- Microsoft Word 16.0 acceptance: native Find 2/2, transactional replacement
  2/2, comment add/reply/resolve, tracked insertion, tokenized revision
  acceptance and guarded Undo all passed.
- Resulting live DOCX: valid OPC ZIP, zero WordToolkit structural errors and
  zero Microsoft Open XML SDK errors.
- Real Word evidence:
  `artifacts/wordtoolkit-live-competition-test/real-word-live-gap-test.json`.

The stage table and counts below are the earlier 0.1.1 baseline retained as
historical evidence. They are not the current release totals.

Results recorded on 2026-07-18 in the supplied Linux build environment.

| Stage | Result | Evidence |
|---|---|---|
| 1. Source and license audit | Complete | `docs/RESEARCH-AUDIT.md`, pinned upstream commits, MIT licenses and notices retained. |
| 2. Architecture and threat model | Complete | `docs/ARCHITECTURE.md`, `docs/SECURITY.md`, versioned tool schemas and migrations. |
| 3. Vertical DOCX flow | Complete locally | Authenticated MCP tests cover create/open, paragraph edit, native OMML insertion, validation, versioned save and signed download metadata. |
| 4. Document tools | Complete for the declared v1 contract | 65 small, typed MCP tools exported in `schemas/mcp-tools.v1.json`; unit and integration coverage includes structure, styles, formatting, lists, tables, stories, revisions, fields, notes, comments, images and math. |
| 5. Rendering and visual QA | Complete with stated renderer limits | The nine-page advanced acceptance DOCX was rendered with LibreOffice headless and Poppler, passed strict blank/sparse/edge/font checks, and every page was manually inspected after two correction iterations. |
| 6. Codex/ChatGPT plugin | Complete as an installable template | Plugin manifest, remote MCP configuration and WordToolkit skill validate with the Codex plugin validator. The deployment URL intentionally remains operator-configurable. |
| 7. Public HTTPS and phone test | Blocked on operator infrastructure | A public deployment cannot be created without an operator-owned hosting account, domain, OAuth issuer/client configuration and secrets. Cloud Run and Render examples plus phone setup instructions are included, but no public endpoint is claimed. |
| 8. Security and regression audit | Complete for this environment | Security tests, dependency packaging checks, structural OOXML validation, rendering, schema parsing and the full regression suite passed. Microsoft Word and the .NET SDK were unavailable; dedicated CI jobs and a Word COM validation script are supplied. |

## Exact automated results

- Full Python suite: **1194 passed, 15 skipped** in 7.03 seconds.
- Focused math/security/vertical-flow suite: **52 passed**; advanced equation module alone: **37 passed**.
- Skips: fourteen optional spaCy `en_core_web_lg` tests and one test that would download a model during a request. Production code deliberately forbids such downloads.
- Ruff: all checked WordToolkit source, scripts and first-party tests passed.
- Plugin validator: passed.
- JSON/YAML parsing: passed for schemas, plugin files and deployment descriptors.
- Advanced acceptance script: **passed** with 9 pages, 4 sections, 2 tables, 17 native equations, 0 structural errors, 0 structural warnings, 0 accessibility issues and 0 layout risks.
- Python package build: `wordtoolkit_mcp-0.1.1-py3-none-any.whl` and source distribution built successfully; vendored `docx-mcp` license and notice files are present in the wheel.
- Docker and local .NET build: not run because neither executable was present in this environment.

## Generated-document evidence

| Artifact | Structural result | Native math | Render result |
|---|---:|---:|---:|
| `WordToolkit-equations.docx` | valid; 0 errors; 0 warnings | 5 `m:oMath`, including 4 block equations | one-page PDF and PNG; visual heuristic passed |
| `WordToolkit-showcase.docx` | valid; 0 errors; 0 warnings | 1 native block equation | one-page PDF and PNG; visual heuristic passed |
| `WordToolkit-advanced-torture-test.docx` | valid; 0 errors; 0 warnings; protected custom/opaque parts byte-identical | 17 `m:oMath` values; 16 block plus 1 inline; 16 source round-trips and 48 export/reparse checks | nine-page mixed Letter/A4 portrait/landscape PDF; 9 PNGs; no blank/sparse/edge/font warning; all pages manually inspected |

The advanced document additionally contains comments and a reply, footnote/endnote, tracked insertions/deletions, fixed-layout and split/merged tables, inline/floating DrawingML with alt text, fields and references, multilevel numbering, four sections, first/default/even headers and footers, a bound content control, custom XML, a font table and an opaque linked binary. Those unmodified protected parts were compared byte-for-byte after save and reopen. The structural validator is not a substitute for the Microsoft Open XML SDK validator: the Docker image builds that official validator, while this local run correctly reports it as unavailable instead of claiming an SDK pass.
