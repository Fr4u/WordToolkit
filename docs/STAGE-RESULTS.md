# Stage results

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
