# Changelog

## Unreleased

- Added the cross-platform `WordToolkit.Engine` .NET library with a bounded,
  immutable OPC package graph, content-type and relationship resolution,
  reachability diagnostics, opaque-part preservation and deterministic
  package fingerprints.
- Strengthened OPC relationship validation with XML-ID and RFC 3986 checks,
  fragment-preserving target resolution, reserved relationship-part rules,
  required relationship content types, and explicit rejection of targets that
  point at package infrastructure.
- Added hash-preconditioned package mutations, deterministic serialization and
  version-checked atomic file replacement with validation and optional backup.
- Added the token-bounded `inspect_ooxml_package` native MCP action. It inspects
  DOCX/DOCM/DOTX/DOTM files without opening Word or dereferencing external
  relationships.
- Added a source-linked semantic projection for paragraphs, runs, tables,
  fields, equations and every nested OfficeMath element, revisions, drawings,
  content controls, and unknown extension islands. Stable node IDs prefer
  durable Word anchors and do not depend on raw paragraph indices.
- Added the lazy, token-bounded `inspect_ooxml_semantics` MCP action.
- Added 34 document-engine tests, including deterministic malformed-input,
  randomized relationship metadata and opaque-part round-trip fuzz smoke, plus
  two native end-to-end package/semantic inspection tests while retaining all
  existing native runtime tests.
- Added a source-backed 2026 research matrix and the target lossless semantic
  document-engine architecture.

## 0.19.0 — 2026-07-20

- Replaced the eager 48-tool model surface with 10 common tools plus three
  lazy gateways for search, schema inspection and execution. All 48 native
  actions remain available without loading every schema into the AI context.
- Removed presentation-only JSON Schema titles from the exposed catalog.
- Added compact MCP responses that omit performance diagnostics, runtime
  boilerplate, repeated document metadata and echoed batch content. Full
  responses remain available explicitly through the execution gateway.
- Reduced the always-loaded tool catalog plus skill instructions from about
  15,065 to 2,489 estimated tokens, an 83.5% reduction.
- Added regression budgets for catalog size, lazy action coverage and compact
  mutation responses.

## 0.18.4 — 2026-07-20

- Fixed native Word Find and replace inside built-up OMath runs. Word may
  expand the next forward Find range back to the equation that contains the
  cursor; the bridge now skips that duplicate range and advances monotonically
  instead of rejecting the whole transaction as a backward range.
- Rebuilt the live equation atlas with proper function powers, preserved
  spacing between named operators and readable spacing in native cases.

## 0.18.3 — 2026-07-20

- Replaced Math AutoCorrect control words with their actual UnicodeMath
  structural characters before calling `OMath.BuildUp()`. Programmatic COM
  build-up does not expand keyboard-only commands such as `\matrix`, so the
  previous release could still display those commands as literal text.
- Matrices now use the native `■`, `⒨`, `ⓢ`, `⒱` and `⒩` enclosure markers;
  equation arrays use `█`, cases use `Ⓒ`, and accents use combining Unicode
  marks. These forms were verified visually in the real Word window.
- Limits now use the native below-script marker where Word requires it.
- MathML and OMML conversion now emit the same structural characters,
  including native function application, matrices, arrays, accents and boxes.

## 0.18.2 — 2026-07-20

- Fixed professional Word build-up for matrices, equation arrays, cases,
  accents, named functions and equation text. The converters now emit actual
  UnicodeMath control words such as `\matrix` and `\eqarray` instead of
  leaking internal strings such as `matrix(...)` into the document.
- Added correct delimiters for `pmatrix`, `bmatrix`, `vmatrix` and `Vmatrix`.
- Changed accent conversion to Word's postfix UnicodeMath form and mapped
  overbars, equation text and boxed expressions to native Word syntax.
- Applied the same structural rules to LaTeX, Presentation MathML and OMML
  input, with regression coverage for every repaired class.

## 0.18.1 — 2026-07-20

- Fixed native Word construction of integrals, sums, products and other
  n-ary equations. The LaTeX converter now emits Word's required `▒`
  boundary between operator limits and the equation body, so `OMath.BuildUp()`
  no longer absorbs the integrand into the upper limit or leaves an empty
  dotted operand box.
- Preserved LaTeX spacing commands such as `\,` as real Word linear-math
  boundaries, preventing a following differential such as `d x` from being
  absorbed into the preceding exponent.
- Applied the same n-ary boundary rule to Presentation MathML and OMML input.
- Added regression coverage for bounded and unbounded integrals, nested
  integrals, sums, MathML n-ary expressions and native OMML n-ary objects.
- Passed all 29 native runtime tests.

## 0.18.0 — 2026-07-20

- Expanded the self-contained native runtime from 29 to 48 Word Live tools.
  The restored native modules cover 17 story types and 23 structure
  collections, bounded item inspection, layout diagnosis, privacy-preserving
  aggregate learning, tokenized comments/revisions, typed table formulas,
  table-field recalculation, native bookmarks and allowlisted fields.
- Added a real installed-Word COM type-library scanner. On the release machine
  it cataloged 767 types and 12,167 members with zero scan errors and no
  truncation. The catalog is kept only in process memory and never stores
  document content, paths, help files or owner identifiers.
- Added one deterministic capability profile per installed member plus typed
  preflight and execution graphs. Execution accepts stable capability IDs,
  never raw names or dotted COM paths. Macros, DDE, print/mail/web effects,
  lifecycle actions, sensitive metadata, application-global mutations,
  events, restricted members, unknown writes and unsafe setters fail closed.
- Replaced the full-test `SendKeys` dependency with catalog-backed
  `Document.Range` plus `Range.Select`, two narrow non-content-changing
  operations. Live selection tests no longer depend on whichever window
  happened to own the keyboard focus.
- Fixed named C# tuple values crossing a `dynamic` COM boundary in locale-aware
  table formulas, bookmarks and field batches.
- Added native equation outcome counters and preflight regression tests for
  formulas, bookmarks and safe fields.
- Passed 25 native unit tests with zero build warnings.
- Passed a 48/48 real Microsoft Word acceptance: 71 MCP requests in 24.691 s,
  47 positive tools plus the guarded quit safety gate, valid Open XML,
  132,802-byte PDF, close/open/reconnect, and protection of one unrelated dirty
  document. The test document remained open and visible in Word.

## 0.17.0 — 2026-07-20

- Replaced the local Codex plugin runtime with a self-contained .NET 8
  Windows x64 MCP executable. The installed plugin launches
  `wordtoolkit-native.exe` directly and packages no Python, uv, pywin32,
  virtual environment, interpreter bootstrap or per-call helper process.
- Added a persistent COM STA host with Running Object Table attachment,
  bounded Word-busy retries through `IOleMessageFilter`, optimistic live
  versions, exact selection tokens, guarded top-entry Undo and transactional
  rollback.
- Added 29 native tools for the full explicit Word lifecycle, live document
  creation and connection, inspection,
  fast text and mixed batches, formatting, native Find and replacement,
  tables, lists, images, comments, footnotes/endnotes, headers/footers,
  LaTeX/UnicodeMath/MathML/OMML OMath, PDF export, save, close, quit,
  validation and disconnect.
  Removed unported local declarations instead of silently falling back to the
  Python runtime.
- Added direct COM Word activation and bounded existing-file opening for
  explicit absolute DOC, DOCX, DOCM, DOT, DOTX, DOTM, ODT, RTF, TXT, PDF,
  HTML/MHTML and XML paths. Macro execution is force-disabled and external
  links are not updated during open. Close and quit require explicit
  save/discard policy; untitled dirty documents fail before a blocking Save As
  prompt.
- Added content-bound native-Find range tokens so comments can be placed fully
  automatically without trusting stale raw coordinates or requiring the user
  to select text manually.
- Added secure Presentation MathML and OMML parsers with DTD/external-entity
  rejection, strict namespace/root checks, bounded depth and element counts,
  and conversion to editable native Word OMath.
- Added transactional native inline images, comments, notes, headers and
  footers plus native PDF export through a verified sibling temporary file.
- Added an in-process fail-closed LaTeX-to-UnicodeMath converter covering
  fractions, indexed radicals, scripts, n-ary operators, common symbols,
  functions, accents, text, matrices, aligned arrays and cases.
- Added a native self-contained build that rejects Python runtime files and
  verifies the direct MCP executable command before creating the ZIP.
- Measured a real 48,800-character, 100-operation Word mutation at
  259–268 ms versus 751.658 ms through the old Python bridge, about a 2.9×
  improvement. The packaged MCP initialized in 106.767 ms and spawned no
  Python or uv child process.
- Added real installed-cache acceptance tests covering the original insert,
  Find, replace, guarded Undo, table, list and LaTeX path with automatic
  restoration plus the expanded lifecycle and document-structure surface,
  saved-DOCX validation, PDF export, close and reopen.
- Created and validated
  `WordToolkit-Native-Mechanika-Kwantowa-2026-07-20.docx` through the packaged
  runtime: 16 paragraphs, four native equations, one native list and zero
  Microsoft Open XML SDK errors.
- Rejected and removed the NativeAOT experiment after real COM startup proved
  that dynamic COM requires a registered `ComWrappers` implementation.

## 0.16.2 — 2026-07-20

- Removed more than 1.2 GiB of historical release directories, smoke
  environments, old archives, test artifacts, caches, temporary storage and
  generated validator build output from the development workspace.
- Removed the obsolete embedded `src/docx_mcp/skill` copy and the unused
  `docx_mcp.cli` / `python -m docx_mcp` launcher that tried to auto-install a
  Claude-specific skill. WordToolkit now has one authoritative Codex skill and
  only its declared `wordtoolkit` and `wordtoolkit-stdio` entry points.
- Added `scripts/clean_workspace.py`. It is dry-run by default, constrains all
  targets to the repository root, collapses nested targets, handles Windows
  read-only files and requires `--apply` before deleting anything.
- Expanded ignore rules so build history, test artifacts, local schemas,
  caches, temporary Word Live storage and .NET build products do not grow back
  into the source tree.
- Added regression tests for the cleaner and a packaged-module import sweep.
  The tested `docx_mcp` compatibility package remains because it is the active
  OOXML engine, not dead code.

## 0.16.1 — 2026-07-20

- Fixed native numbered lists created by `manage_lists(action="apply")`.
  WordToolkit now writes an explicit `w:start` element and passes the bounded
  `start` argument through the local MCP layer. LibreOffice and Microsoft Word
  no longer get room to disagree about whether a fresh numbered list begins
  at zero or one.
- Added regression coverage for both the normal one-based start and an
  explicitly requested zero-based list.

## 0.16.0 — 2026-07-20

- Added `find_live_word_text` and `replace_live_word_text`. Native Word Find
  returns bounded ranges and context. Replacement discovers the complete
  bounded match set before mutation, edits ranges in reverse, restores Track
  Changes and groups the operation in one custom Undo record with rollback.
- Added token-safe live review. Comments can be added to a fresh selection,
  replied to, resolved or deleted; one inspected tracked revision can be
  accepted or rejected. Every existing-item mutation requires an HMAC token
  bound to its current range, metadata, content fingerprint, reply count and
  live version.
- Added explicit Track Changes control with read-back verification and manual
  rollback. It does not falsely claim that the property participates in Word
  custom Undo.
- Added bounded live layout diagnosis for keep-with-next chains, long headings,
  body page breaks, oversized keep-together paragraphs, disabled widow
  control, empty-paragraph runs, manual page breaks and heading-style overuse.
  It returns no paragraph text.
- Added guarded Undo. WordToolkit can undo only one current top entry labeled
  `WordToolkit:`, and only with a fresh HMAC token plus exact live version.
  Raw counts, unavailable history, stale stacks and intervening user actions
  fail closed.
- Added a disposable real-Word acceptance harness. Microsoft Word 16.0 passed
  Find, replacement, comment add/reply/resolve, Track Changes, tokenized
  revision acceptance, guarded Undo, same-path save and structural plus
  Microsoft Open XML SDK validation with zero errors.
- Audited `ykarapazar/word-mcp-live` at commit `c6c76179`. The comparison
  records both projects' strengths, the competitor's inconsistent tool counts
  and broken test bootstrap, and WordToolkit's remaining macOS and dedicated
  wrapper gaps without hiding them.

## 0.15.0 — 2026-07-20

- Expanded the installed-member registry from one policy profile per entry to
  one individually named virtual tool definition per entry. The refreshed Word
  8.7 catalog produces exactly 12,167 profiles, 12,167 unique virtual-tool
  names and 24,334 Draft 2020-12 input/output schemas.
- Added parameter-specific schemas for typed targets, positional arguments,
  result chaining, enum constants, optional COM omissions and member return
  values. A member-by-member audit validates every schema and hashes the full
  coverage surface so a missing or duplicated tool fails the release.
- Added typed `constant_id` arguments for all 3,756 installed Word enum values.
  Preflight resolves a constant only when its enum type matches the COM
  parameter; unchecked strings no longer pass as interface or enum arguments.
- Added an indexed-property adapter using the catalog DISPID and invocation
  kind. A real Word test proved that `Document.Compatibility(...)` accepts the
  adapter call but does not reliably participate in Word Undo. Therefore every
  indexed setter keeps its individual schema and adapter metadata but generic
  execution fails closed until that exact member has a proven reversible
  workflow.
- Corrected positional optional-argument handling. An omitted parameter before
  a later required value must use `{"missing": true}` and resolves to the
  native PyWin32 missing-argument sentinel only during local execution.

## 0.14.0 — 2026-07-20

- Added a deterministic capability registry with exactly one profile for every
  installed Word COM catalog member. On the release workstation the refreshed
  Word 8.7 catalog produced 12,167 profiles and 12,167 unique stable IDs with
  zero scan errors and no truncation.
- Added `inspect_live_word_member_capabilities`,
  `preflight_live_word_member_operations` and
  `execute_live_word_member_operations`. Together they provide a bounded
  browser, typed planning and one transactional executor instead of dumping
  12,167 near-duplicate MCP schemas into model context.
- Every profile records its target type, signature, accessor group, effect
  class and execution policy. Enum values remain constants, event callbacks
  remain non-invocable, and lifecycle, external, password, path, macro, DDE,
  print, mail, web and application-global actions fail closed.
- Catalog-backed execution accepts only stable capability IDs, document-rooted
  targets and typed results from earlier operations. It rejects arbitrary COM
  paths and unchecked member names, caps batches at 50 operations and 512 KiB,
  validates argument counts/types, and requires `expected_version` for writes.
- A mutating batch resolves the connected document inside one COM attachment,
  runs in one custom Word Undo record, rolls back on any native failure and
  advances the live version once only after the whole sequence succeeds.
- Corrected PyWin32 `FUNCDESC` decoding. Parameter count now comes from the
  parameter descriptor array, `cParamsOpt` remains the optional count, and the
  scanner retains invocation kind, call convention, vtable offset, variadic
  state, function flags, defaults and implemented-interface relationships.
- Real Word verification appended a marker through the catalog-backed
  `Range.InsertAfter` profile, observed it in the visible document, then
  removed it with the same custom Undo record. The document content and saved
  state were restored after the test.
- Reorganized the plugin skill into a short lifecycle router plus focused
  references for equations, structures, formatting, fields, tables, lists,
  learning and installed-member operations.

## 0.13.0 — 2026-07-20

- Added `inspect_live_word_object_model_types` and
  `inspect_live_word_object_model_members`, a paged read-only catalog built
  from the actual Microsoft Word COM type library installed on the local PC.
- The first explicit scan reads type, method, property, parameter, variable
  and enum metadata from already-running Word. The bounded atomic cache serves
  later queries without another COM attachment and can be refreshed after an
  Office update.
- The catalog stores no document content, document counts, paths, handles,
  owner identifiers, documentation strings or help-file paths. It is API
  discovery, not autonomous code mutation or fabricated edit support.
- Added `update_live_word_table_fields`, which refreshes up to 5,000 existing
  fields in one native table through one `Fields.Update()` call, one COM
  attachment and one Undo transaction.
- Field counts and numeric types are checked before and after refresh. A Word
  error or structural change rolls back the mutation; responses omit field
  codes, displayed results and document content.
- On the release workstation the installed Word 8.7 type library contained
  767 types and 12,167 callable/constant members. The 2.36 MB cache had zero
  scan errors; a cached query took 0.24 ms. A real two-formula table refresh
  completed in 233 ms with native return value zero.
- Plugin smoke and real-world tests now run `uv` in an isolated environment
  with bytecode writes disabled. Testing a release directory therefore no
  longer leaves a machine-bound `.venv`, editable-path references,
  `__pycache__` directories or `.pyc` files inside the package that is later
  installed.
- The final installed-cache test exposed 93 local tools, rebuilt its runtime
  against the installed cache path, indexed 767 Word types and 12,167 members,
  and refreshed two existing type-34 fields in 400.782 ms. Word returned zero,
  used one Undo transaction and left the open document saved.

## 0.12.0 — 2026-07-20

- Added `preflight_live_word_table_formulas` and
  `insert_live_word_table_formulas` for up to 200 typed calculations in one
  existing rectangular native Word table.
- Supports `SUM`, `AVERAGE`, `COUNT`, `MAX`, `MIN` and `PRODUCT` over one or
  two positional directions or a bounded source range generated from numeric
  row and column coordinates. Raw formulas and field codes remain unreachable.
- Destination cells must be empty unless `replace_existing=true` is explicit.
  The bridge validates all cells before mutation, uses one COM attachment and
  one Undo transaction, localizes separators once per batch, inserts native
  field type 34 directly at each cell range and checks calculated result
  ranges plus the final field count.
- Word calculates formula fields on insertion, so the default fast path avoids
  immediately recalculating every field a second time. `force_update=true`
  keeps an explicit verified recalculation path when it is genuinely needed.
- Responses return only coordinates, function classes, ranges, verification
  and performance metadata—never formula codes, source values or results.

## 0.11.0 — 2026-07-20

- Added `inspect_live_word_structure_items`, a read-only paged inspector for
  all 23 native collections already covered by the Word Live structure map.
- The inspector reads at most 200 items through one COM attachment and returns
  bounded semantic metadata such as native type, range, dimensions, style,
  lock state and structural identifiers. Optional text previews are capped at
  2,000 characters per item.
- Field codes and external hyperlink addresses are never returned. Property
  failures are isolated per item instead of aborting the entire collection.
- Structure learning now records only fixed property names, aggregate read
  successes/failures and timing. It never receives property values, text,
  document counts, paths, owners, handles or document-derived identifiers.
- Properties unavailable in the installed Word object model are retried at
  exponentially spaced inspection observations, while properties that have
  succeeded remain enabled for current-value reads.

## 0.10.0 — 2026-07-20

- Added `preflight_live_word_bookmarks` and
  `insert_live_word_bookmarks` for up to 200 native named ranges in one text
  assignment, one COM attachment and one Undo transaction.
- Bookmark names are bounded and case-insensitively unique. Existing-name
  collisions fail before mutation; every native name and exact range is
  verified before the live version advances.
- Added privacy-preserving structure learning and
  `inspect_live_word_structure_learning`. The store retains fixed collection
  names, native enum values, scan outcomes and timing, but never content,
  document counts, paths, handles or document-derived identifiers.
- `map_live_word_structures` now performs adaptive type scans at exponentially
  spaced presence observations. Dirty or truncated scans retry on the next
  presence, while an explicit histogram request still forces every scan.
- The live `reference` field workflow is now complete: create a verified native
  bookmark, then insert and update a native `REF` field targeting it.

## 0.9.0 — 2026-07-20

- Added `preflight_live_word_fields`, a read-only validation pass for up to 200
  allowlisted native Word fields without attaching to Word.
- Added `insert_live_word_fields`, which writes one marker payload and creates
  page, document-property, date/time, sequence, bookmark-reference and
  restricted numeric formula fields inside one COM attachment and one Undo
  transaction.
- Raw field codes, external-data field types, unbounded field switches,
  unknown formula names and bookmark references that do not exist are rejected
  before the document is mutated.
- Every inserted field is type-checked and updated by Word. A failed update,
  count mismatch or COM error rolls back the entire batch and leaves the live
  version unchanged.
- Formula expressions use a locale-neutral public syntax and are translated
  to Word's active list, decimal and thousands separators immediately before
  `Fields.Add`, so the same request works on English and Polish installations.
- Structured live equations now force native OMML readback. Normalized
  mathematical text and recursive child scope are compared with the prepared
  AST; Word build-up drift rolls the full transaction back instead of being
  reported as a native success.
- Equation learning classifies those failures as
  `NATIVE_FIDELITY_MISMATCH` without retaining formula text, document content,
  names or paths.

## 0.8.0 — 2026-07-19

- Added `insert_live_word_list` for up to 1,000 native bulleted or numbered
  paragraphs in an already-open Word document.
- The fast path assigns one validated paragraph payload and applies one
  `Range.ListFormat` operation, avoiding one COM write per list item.
- Added token-safe cursor/selection targets, optional style and formatting,
  optimistic versioning, screen-update suspension and full Undo rollback.
- Structure mapping now inventories Word `Lists` separately from
  `ListParagraphs` and optionally reports bounded `WdListType` histograms.

## 0.7.0 — 2026-07-19

- Added `insert_live_word_table` for native rectangular Word tables with up to
  200 rows, 50 columns and 5,000 cells.
- The fast path writes one validated TSV/paragraph payload and calls
  `Range.ConvertToTable`, avoiding one COM write per cell.
- Added style, repeating header row, fixed/content/window AutoFit and
  left/center/right table alignment controls.
- Added transactional rollback, optimistic versioning, token-safe cursor or
  selection targets, count verification and content-free results.

## 0.6.0 — 2026-07-19

- Added `format_live_word_selection`, which applies a style plus validated font
  and paragraph formatting to an exact non-empty Word selection protected by a
  fresh selection token, one COM attachment and one Undo record.
- Added the same formatting object to single live text insertion and mixed
  live batches, so generated text is inserted and formatted without a second
  connection or text round-trip.
- Added bounded support for fonts, point size, RGB color, emphasis, underline,
  highlighting, paragraph alignment, spacing, indentation, pagination and
  keep controls.
- Added numeric type histograms for OMath, fields, form fields, content
  controls, revisions, inline/floating shapes and styles. Inventories remain
  content-free; histograms are opt-in, default to 2,000 objects and hard-cap at
  10,000 objects per collection.

## 0.5.0 — 2026-07-19

- Added `map_live_word_structures`, a content-free inventory of all 17 Word
  story types plus more than 20 document collections including sections,
  styles, fields, bookmarks, revisions, content controls, shapes and notes.
- Added a privacy-preserving local equation outcome store. It records only
  structural feature classes, input formats, success/failure counts, readback
  outcomes and timings; formula text, document text, paths and owners never
  enter the store.
- Added adaptive equation policy: a structural class with real Word failures
  automatically forces native OMML readback on subsequent matching formulas.
- Added `inspect_live_word_equation_learning` so the learned aggregate policy
  is auditable instead of hidden.
- Added bounded, atomic learning persistence and corruption-safe fallback.

## 0.4.0 — 2026-07-19

- Added `apply_live_word_operations`, which preflights and appends up to 200
  interleaved text/equation operations through one COM attachment, one bulk
  text assignment and one Word Undo transaction.
- Added optional temporary `ScreenUpdating` suspension with exact restoration
  after success or failure.
- Added `preflight_live_word_equations`, a non-mutating syntax pass that
  returns canonical AST, Word linear math, rule hits, warnings and native
  readback requirements for up to 200 formulas.
- Added mandatory live symbol-preservation checks for hbar and dagger notation;
  Word now rolls back the mutation if its OMath parser drops either symbol.
- Added a durable equation-authoring rule reference and a live/isolated Word
  structure routing catalog to the plugin skill.

## 0.3.1 — 2026-07-19

- Added a local Word Live equation batch tool that inserts up to 100 native equations in one COM operation.
- Added an optional fast path that verifies native OMath counts without per-equation OMML AST readback; full readback remains opt-in.

## 0.3.0 — 2026-07-19

- Added nine Windows-only Word Live MCP tools to list, connect, inspect, edit,
  validate, save and disconnect documents already open in Microsoft Word.
- Added direct native `OMath` insertion through Word COM with explicit display
  or inline type, build-up, OOXML read-back verification and transactional Undo
  rollback on failure.
- Added cursor and selection tokens, explicit selection-replacement consent,
  optimistic live versions and read-only/protection/final-document checks.
- Added same-path `Document.Save()` plus temporary validation copies made only
  from an already-saved DOCX, without exporting a new user file.
- Kept Word Live out of the remote HTTP server. It is exposed only by the local
  STDIO plugin and never launches, closes or quits Microsoft Word.

## 0.2.0 — 2026-07-18

- Added a local MCP STDIO server for a one-install Codex workflow without a
  public domain, Docker daemon, OAuth tenant or background HTTP process.
- Added explicit local-path and `file://` inputs plus durable local artifact
  links for exported DOCX, PDF and PNG files.
- Preserved the authenticated Streamable HTTP deployment mode and made
  `local_stdio` impossible to use through the HTTP application.
- Added Windows discovery for LibreOffice and Poppler, including safe process
  environment handling and correct file-URI profile paths.
- Added a self-contained plugin build that bundles the Python runtime source,
  lockfile and optional Microsoft Open XML SDK validator.
- Fixed Windows ZIP part names in metadata sanitization, PII redaction, raw-part
  listing and upstream repacking. The old backslash paths could leave original
  XML beside sanitized or redacted XML inside a DOCX.

## 0.1.1 — 2026-07-18

- Added the advanced OPC/OOXML/OMML acceptance and visual torture test.
- Fixed root relationship security inspection and orphan-part detection.
- Fixed semantic LaTeX, UnicodeMath and MathML export/reparse edge cases.
- Fixed table geometry, inline DrawingML metadata ordering and section-aware
  layout-risk analysis.
- Made Poppler page rendering deterministic and resistant to stale or truncated
  preview files.
- Added nine-page validated DOCX/PDF evidence and CI release gates.

## 0.1.0 — 2026-07-18

- Initial remote Streamable HTTP MCP, document engine, native Office Math,
  security boundary, rendering pipeline, plugin manifest and deployment assets.
