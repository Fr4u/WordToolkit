# Changelog

## Unreleased

- Removed automatic replay of mutating Word COM delegates after disconnect. COM work is
  now non-replayable by default: only explicitly proven read-only or idempotent calls
  may reconnect once. A disconnected non-replayable operation returns non-retryable
  `WORD_OPERATION_OUTCOME_UNKNOWN`, and further non-replayable work remains blocked
  until runtime restart, reconnect and inspection instead of risking a duplicate edit
  against a stale live version.
- A cancellation observed after a mutating COM call begins now reports the same unknown-
  outcome state rather than claiming that Word cancelled the mutation. Recovery gating
  remains active until every abandoned synchronous call returns. A queued request's
  cancellation can no longer clear recovery owned by an earlier executing call, and
  late COM completion cannot strand the host in recovery. Cancellation is registered
  at submission time, so an already queued mutation cannot slip through while the
  caller's cancellation continuation is still pending. The STA queue also waits for the
  client-side result/error/cancellation decision before starting its next item, closing
  callback scheduling races rather than merely making them less likely.
- Bounded `IOleMessageFilter` busy-call retry to 30 seconds at 100 ms intervals. Added
  twelve Windows host regressions covering no mutation replay, one replay-safe reconnect,
  cancellation/completion races, cancellation before replay, sticky unknown outcome,
  recovery ownership, `Document.Compare` policy and the hard OLE retry budget.
- Removed `compare*` from the generic object-model read-prefix policy. Word's mutating
  `Document.Compare` is now blocked instead of being mislabeled as a read. Generic
  object-model execution remains non-replayable even when its current batch contains
  only policy-approved reads.

- Added a bounded, read-only bibliography graph for the Open XML 2006 and legacy Word
  2004/10 `Sources` formats. Collections, stable sources, typed identity/type/locale
  fields, scalar metadata, people and corporate contributors retain package provenance;
  duplicate tags remain ambiguous. Unique case-insensitive `CITATION` tags now terminate
  at concrete bibliography source nodes in the unified dependency graph.
- Added lazy `inspect_ooxml_bibliography` as the 91st native action with paged
  summary/collection/source/field/contributor/citation/issue views. Sensitive values and
  source locations are opt-in; the operation never opens Word, evaluates fields,
  executes bibliography XSLT or follows external targets. Its OPC, semantic, reference
  and bibliography phases share the 640 MiB `wop1` budget. People and corporate-name
  ceilings are enforced across all roles in one source. A separate 64 KiB projected-
  payload budget bounds paged results and diagnostics; truncation returns a continuation
  offset. Redacted value fingerprints are process-keyed HMAC tokens rather than public
  unsalted hashes. Unique unmodeled element names are capped at 256 per source and
  charged to the aggregate metadata budget before display-string materialization.
- Duplicate singleton Tag, Guid and SourceType fields now fail closed instead of using
  the first value for identity or citation resolution. Runtime `source_id` validation
  now matches the published schema before OPC parsing.
- Added ten Engine and seven Native bibliography regressions, golden dependency-corpus
  updates and a reproducible 10,000-source Release benchmark. The benchmark resolves all
  10,000 citations with zero issues and preserves the package fingerprint; seven graph
  samples measure 642.5681 ms median, 811.9211 ms p95/max and 245,631,072 median
  thread-allocated bytes on the disclosed local Windows x64/.NET 8.0.29 host. The graph
  accounts 81,230,184/671,088,640 operation bytes; the raw result is checked in. No
  before/after speedup is claimed because the prior HEAD had no bibliography graph.

- Added remote `apply_document_operations`, a provider-neutral
  `wordtoolkit.apply_document_operations/1.0` command for 1-16 ordered operations from
  the complete 33-tool ordinary draft-mutation surface. The server validates every
  nested argument against the corresponding standalone Pydantic contract, rejects
  read-only hybrid actions, enforces a 1 MiB aggregate argument ceiling, runs the whole
  list on one isolated engine clone, validates the final candidate once and advances
  `draft_version` once. Its generated Draft 2020-12 schema is a closed 33-variant
  `oneOf`; arbitrary engine method dispatch and result-reference syntax are absent.
  The same exporter now emits `schemas/draft-operations.v1.json` with the input,
  success-data and error schemas, permissions, limits, side effects and validated
  examples so non-MCP clients do not need to reverse-engineer the adapter.
- Batch failures carry a bounded operation index/name/cause and discard every earlier
  clone mutation. Results preserve order but project only compact IDs/counts/status so
  inserted document content is not echoed back into the model context. Caller
  cancellation drains image staging and the transaction before releasing the document
  lock. Because Apps SDK file parameters support only top-level fields, image calls use
  the top-level `files` array declared through `openai/fileParams` and a bounded
  `file_index` inside `insert_image`; nested file objects are rejected. Downloads remove
  partial files on error or cancellation. An optional missing `file_name` is handled
  through an allowlisted extension from the final URL, declared MIME type or response
  content type rather than silently treating the payload as `.bin`. A fully staged
  upload can still remain when a later operation fails, so that non-document
  session-quota side effect is stated in the
  public tool description and tested. The compact success payload no longer repeats the
  operation contract, backend, atomicity, count or requested operation names; a
  representative one-step envelope fell from 290 to 102 compact JSON characters.
- Recorded 15 Windows x64 in-process FastMCP samples for
  `format_paragraph -> enable_track_changes -> insert_paragraph`: three standalone COW
  calls measured 189.479 ms median versus 70.901 ms for one batch (-62.58%, -118.578
  ms). The batch also reduced the measured compact request JSON from 480 to 427
  characters (-11.04%), removed two MCP round trips and used one commit/version advance
  instead of three. Document creation/close and the optional SDK validator were outside
  this measurement. The closed 33-variant schema is not free: after removing redundant
  nested titles and envelopes it occupies 19,088 compact JSON characters and raises the
  complete 66-tool catalog from 57,566 to 77,439 characters (+34.52%). A contract test
  caps that schema below 20,000; no whole-catalog token reduction is claimed.
- Made all 33 ordinary remote draft mutators copy-on-write transactions. Each request
  now snapshots the locked active engine, applies the complete operation to an isolated
  clone, serializes and structurally validates the candidate, and swaps the active
  engine plus `draft_version` only after every gate succeeds. An operation that mutates
  and then raises, or a candidate rejected by validation, leaves the active engine,
  package bytes and version unchanged. Validation failures expose only bounded counts
  and issue codes, never validator messages, part names or document content.
- Preserved cancellation truth at the stronger boundary: a cancelled caller cannot
  release the document lock while the clone worker is alive; a fully successful and
  validated post-cancellation mutation is committed and advances the version, while a
  failed post-cancellation attempt is discarded. Transaction snapshots and abandoned
  clones are drained and removed before the lock is released.
- Added partial-mutation rollback, candidate-validation rejection, redaction and engine
  identity regressions. Sequential mutation -> mutation -> save -> close coverage proves
  that the one active clone workspace survives publication and replaced workspaces are
  removed. Replaced-engine cleanup runs in a drained background worker, so a large
  workspace cannot block the event loop or escape cleanup when the caller cancels.
- Fixed a Windows snapshot defect that compared backslash filesystem paths with canonical
  POSIX OPC part names. The broken comparison could omit modified XML while still
  producing a structurally valid, stale package; snapshots now serialize every marked
  part with canonical ZIP names.
- Recorded a 15-sample local Windows x64 cost measurement for one paragraph insertion:
  direct in-memory mutation measured 0.160 ms median, while snapshot + clone open +
  mutation + candidate snapshot + structural validation measured 56.319 ms median. The
  absolute 56.159 ms / 351.99x median cost is an explicit correctness tradeoff. This
  candidate-preparation point excludes engine creation, commit swap, replaced-workspace
  cleanup and MCP dispatch; the optional Microsoft SDK validator was unavailable.

- Raised the legacy remote Python service to the unified 0.40.0 development line and
  published remote MCP schema v2.
  Every edit, save, repair, render, preview and close of an existing draft now requires a
  non-negative `expected_version`; stale calls fail under the same document lock before
  document-engine mutation or output/artifact publication. DOCX export enforces the
  version conditionally while
  read-only Markdown export remains version-neutral.
- Remote save, repair and render now execute on an isolated copy-on-write engine. The
  active engine, `draft_version`, current path and artifact inventory change only after
  validation and all-or-nothing artifact registration succeed. Failed saves/renders
  discard their clone and attempt outputs. Document/session close races recheck identity
  after lock acquisition, and session shutdown cannot close an engine in active use.
  Cancelled background engine calls are drained before the document lock is released; a
  mutation that completes after caller cancellation still advances the draft version.
- Added the v1-to-v2 migration, required-field schema tests, stale writer/save tests,
  failed save/render rollback tests, close-race coverage and atomic multi-artifact failure
  coverage, repeated-cancellation tests and version-stable concurrent snapshot tests.
  Remote example clients now carry the returned version through publication and close.
- Recorded a nine-sample copy-on-write DOCX publication benchmark: median direct save
  17.935 ms versus 63.224 ms for snapshot, clone open and validated save. The measured
  45.289 ms cost is reported as a correctness tradeoff, not hidden as a speed win.

- Added the vendor-neutral `word_operation_accounted_v1` lease and wired one instance
  through the complete saved-package dependency-inspection pipeline: OPC retention,
  lossless XML parsing, semantic nodes/fingerprint caches, styles, numbering, references,
  sections/settings, charts, figures/captions, content controls, tables and final graph.
  The calibrated production ceiling is 640 MiB; MCP exposes only
  `operation_budget: {model, used, maximum}` under compact alias `wop1`.
- ZIP central-directory count/size preflight now runs before `ZipArchive.Entries` is
  materialized. Non-seekable public streams are copied through a 576 MiB bounded,
  delete-on-close temporary spool so they cannot bypass that preflight. Package-entry
  and XML charges reject before guarded byte copies; OPC
  content-type, part, relationship and diagnostic records are count-bounded and charged.
  Semantic hash recursion and metadata XML loading observe cancellation, while
  table/figure/content-control aggregate byte ceilings reject the next part before
  parsing it. Operation exhaustion maps to a typed, bounded `PACKAGE_LIMIT`
  stage/attempted-charge response without paths or document data.
- Case-insensitive ZIP-name collision diagnostics now return only a bounded spelling
  count instead of joining every attacker-controlled name into one large string.
- Kept the existing `wdg1` graph budget and dependency input schema byte-for-byte
  compatible. Chart/table limit and projection failures now map to `PACKAGE_LIMIT` and
  `INVALID_WORD_PACKAGE` instead of falling through to `IO_ERROR`. Empty
  `issues_truncated=false` noise is omitted from compact responses; the complete default
  dependency JSON-RPC envelope remains below 8,000 characters.
- Calibrated the previous 99,997-node graph point at 539,282,576 operation-accounted
  bytes. A 512 MiB candidate failed at 536,870,624 bytes, while 640 MiB preserves the
  admitted boundary with 19.7% accounted headroom. The model remains a cumulative stable
  proxy, not an exact CLR heap, live-set or resident-memory claim.
- Raised the development runtime/plugin line to 0.39.0 without increasing the 90-action
  catalogue.
- Two pinned .NET SDK 8.0.423 builds produced byte-identical 196-file,
  85,759,136-byte trees and 36,380,971-byte ZIPs at SHA-256
  `2875209550cca57b36d0e99873d443c337f89950e5230d3083110398da0d7468`.
  The package contains no Python files. The enabled personal-plugin cache at
  `0.39.0+codex.20260723142922` is an exact path/length/hash copy; its runtime reports
  0.39.0 and 90 actions. A packaged dependency smoke call returned both `wop1` and
  `wdg1` while leaving the checked-in DOCX byte-identical.

- Replaced the unified dependency graph's two eager per-node adjacency dictionaries
  with compact incoming/outgoing offset and edge-index arrays. Direct adjacency views
  retain ordering without allocating one list per node, endpoint validation stays
  fail-closed and construction now observes cancellation through every large index pass.
- Added the deterministic `dependency_graph_accounted_v1` resource model with a 128 MiB
  production default, 65,536-character per-key/per-metadata ceilings and pre-retention
  rejection for nodes, edges and issues. Engine callers receive the complete resource
  record; the default MCP response pays only for `byte_budget: {model, used, maximum}`.
  The budget covers this graph, not upstream semantic and typed projections.
- Added exact one-byte-below-budget rejection, compact-adjacency ordering, option,
  cancellation-compatible and MCP token-envelope regression coverage. Five cold-process
  99,997-node Windows x64 samples per version reduced median retained managed memory from
  234,933,120 to 195,381,520 bytes (16.8%), median managed allocations by 4.2% and median
  dependency build time by 9.0%. Median peak working set was effectively flat (+0.1%),
  so no peak-memory win is claimed. Raw before/after series and primary .NET design
  evidence are checked in.
- Closed a typed-client contract gap by requiring the dependency action schema to cover
  every Engine node and edge kind. Dependency diagnostics are now an explicit
  `include_issues=true` opt-in outside `view=issues`; the default compact response no
  longer spends tokens on a diagnostic array.
- Raised the development runtime/plugin line to 0.38.0. Capability discovery remains at
  90 actions; this release hardens the existing dependency action rather than inflating
  the action count.
- Two pinned-SDK 8.0.423 package builds produced byte-identical 196-file,
  85,730,528-byte trees and 36,372,963-byte ZIPs at SHA-256
  `69c3406b238590dae096370a550ff6902352972cbe942b2344e2d80dca1e0541`.
  The personal marketplace and enabled cache match that final tree exactly. The installed
  runtime reports 0.38.0/90 actions and returns the dependency byte budget without
  opening Word or following external targets.

- Added the bounded, source-linked `WordFigureCaptionGraph` and lazy
  `inspect_ooxml_figures` action. Transitional/Strict inline and anchored DrawingML,
  VML and legacy-object representations now form stable logical figures with typed
  placement, accessibility metadata and inert image/chart/diagram/content-part/OLE
  resource relationships. `mc:AlternateContent` branches collapse into one logical
  object while the public selection basis states that Choice is present but not
  MCE-evaluated; no active or primary representation is invented. External targets and
  embedded resources are never opened.
- Added source-linked caption-style/`SEQ` candidates and a documented association
  policy that selects only mutual unique-best evidence within the same story/container.
  Ties stay ambiguous, weaker alternatives stay candidates and deleted/move-from
  evidence is never selected. Figure, representation, resource, caption and association
  objects now enter the shared dependency graph without upgrading non-selected
  candidates to resolved edges.
- Added independent text/source/relationship-target opt-ins, paging and default/gateway
  payload caps for figure inspection, plus hostile relationship, metadata, revision,
  ambiguity, Strict OOXML and fingerprint tests. A repeated 10,000-figure benchmark
  exposed and removed quadratic caption, ambiguity and field scans; the checked-in
  seven-sample 0.37.0 run with 10,000 distinct relationship IDs reports a 1,897.6 ms
  median and 2,084.6 ms p95 figure-graph build on its recorded host.
- Raised the development runtime/plugin line to 0.37.0 and capability discovery to 90
  lazy/core actions. Figure/caption behavior, research sources, competitor differentials,
  limits and honest exclusions are documented separately.
- Two pinned-SDK 8.0.423 package builds produced byte-identical 196-file,
  85,719,250-byte trees and 36,369,889-byte ZIPs at SHA-256
  `addb0ac796b9c5e41e6174f2bd1937d9fe996ae88015a81af5b000968caa63c7`.
  The enabled personal-plugin cache is an exact file/hash copy of that 0.37.0 package;
  its executable reports 90 actions and the Figure/Caption action through the CLI.
- Added `wordtoolkit.render_ooxml_semantic_svg/1.0` as the seventh public
  transport-neutral Engine/CLI/MCP operation and the second implementation of a shared
  native semantic-rendering backend contract. It requires both an exact semantic node
  ID and the inspected package fingerprint, creates only a new self-contained `.svg`,
  emits real selectable SVG text with `title`, `desc` and ARIA structure, estimates
  non-paginated flow/table geometry, and reports target subtree identity, backend,
  media type, layout basis, text mode, canvas, fidelity and degradation metadata. The
  static profile has no script, event handlers, `foreignObject`, active links, external
  resources or font loading; field instructions remain suppressed. Standalone drawing,
  marker and extension roots fail closed. Rendering is bounded before XML materialization
  by 40,000 text lines, 100,000 generated SVG elements, a 1,000,000-pixel canvas dimension
  and a 256 MiB artifact ceiling. The shared render path policy rejects UNC and Windows
  device namespaces before any filesystem probe, preserving the declared no-network
  boundary instead of risking implicit SMB access. The contract explicitly fixes `paginated`,
  `exact_text_metrics` and `pixel_equivalence_claimed` to false rather than laundering
  estimated geometry into a Word-fidelity claim. `render-package --backend semantic-svg`
  and the lazy MCP action call the same Engine implementation; omitting `--backend`
  preserves the historical HTML CLI path.
- Verified the final SVG slice with 408 Engine tests, 272 Native tests and the complete
  1,279-test Python/OOXML suite with 16 intentional skips; Ruff and the maintained
  28-module mypy lane are clean. Two supported Windows builds produced identical
  196-file, 85,514,480-byte trees and identical 36,306,172-byte ZIPs at SHA-256
  `5076fc4b41670ae05752ab36c0d23ff66fac7a2b8d753706b15473a478e4682a`; the expanded
  manifest and enabled personal-plugin cache matched at SHA-256
  `f9bfd22a28d04e509968f3fb799e2e283e9ff8e3ccabdb14fdbc7503c1bc7207`. Hosted run
  `29992366785` passed all five jobs for commit `d6b663922a45f8e271872f61a63dffa86bf21256`;
  artifact `8557592140` matched both pinned-SDK local ZIPs and expanded trees exactly. An
  initial .NET SDK 10 package was rejected as release evidence after its self-contained
  runtime differed from the repository-pinned .NET SDK 8.0.423 output.
- Added an explicit `workflow_dispatch` trigger to the existing five-job CI workflow so a
  named branch SHA can be verified when a pull-request webhook is not scheduled. Normal
  pull-request and release triggers remain unchanged.
- Added `wordtoolkit.render_ooxml_semantic_html/1.0` as the sixth public
  transport-neutral Engine/CLI/MCP operation. The dependency-free Engine creates a
  deterministic, self-contained HTML artifact from a saved DOCX/DOCM/DOTX/DOTM without
  Word, LibreOffice, Python, network access, external-resource loading or active-content
  execution. It renders the main body or all projected text stories, infers headings from
  modeled outline styles, preserves tables and cached field results, keeps hyperlinks
  inert, annotates tracked insertions/deletions/moves, emits bounded linear-text equation
  fallbacks and makes unsupported drawings/extensions visible as placeholders. The
  result contract calls the artifact `semantic_preview_non_paginated`; it makes no claim
  of Word layout, typography, pagination or print fidelity.
- Extended the same 1.0 renderer additively with fingerprint-bound `target_node_id`
  selection. One semantic table, row, cell, paragraph, equation, drawing, revision or
  other renderable subtree can now be emitted without its siblings. Missing, stale,
  out-of-scope and non-renderable targets fail closed before output creation. Selected
  rows, cells and semantic wrappers receive valid synthetic `table`/`tbody`/`tr`
  context, reported separately as `fragment_wrapper`. The selection-aware traversal
  applies the same normalization to nested tables, nested row/cell SDTs and row-level
  revisions anywhere below the selected target; pure wrapper chains are flattened with
  a warning and ambiguous mixed chains fail closed. Response metadata still returns no
  document text, raw XML, properties or source paths.
- Added the strict `render-package --request <json|->` CLI and lazy MCP adapter over the
  same renderer. The write is create-new and sibling-temporary, flushed before an atomic
  move, never overwrites an output, can bind an inspected package fingerprint, leaves the
  source byte-identical, and returns artifact hashes/counts rather than document text or
  XML in the response. The result explicitly marks that the local HTML artifact itself
  contains document content. Capability discovery now covers 89 lazy/core actions and reports seven actions with
  complete operation-version, permission, reversibility and output-schema metadata.
- Hardened the HTML boundary with output-name and 256 MiB artifact limits, a restrictive
  inline Content Security Policy, HTML escaping of every package-derived value, adaptive
  inline/block/table-row containers, inert hyperlinks, suppressed field instructions and
  an atomic create-new race test. Compact MCP responses and full gateway responses now
  both conform to the same closed output schema; telemetry is optional because the
  compact gateway intentionally removes it. Package-derived exception messages are never
  exposed as public reasons, so hostile ZIP entry names cannot escape through CLI or MCP
  errors; Engine and adapter regressions bind this privacy boundary.
- Verified the fingerprint-bound selection checkpoint with 396 Engine tests, 269 Native
  tests and the complete 1,276-test Python/OOXML suite with 16 intentional skips; Ruff and the
  maintained 28-module mypy lane are clean. Two supported Windows builds produced
  identical 196-file, 85,442,968-byte trees and identical 36,285,575-byte ZIPs at
  SHA-256 `a0737809d981cd739cade8f4036818408877afd903ff1049d538f29b4b99fb41`;
  expanded manifests matched at SHA-256
  `a45a778680bf0c9773089556e8a88104d155ebbf044bd852a24f20155d0596e4`.
  The manifest is the UTF-8/LF, no-final-newline serialization of relative path,
  byte length and lowercase SHA-256 fields separated by tabs and sorted by path.
  The packaged executable discovered the lazy action and rendered the checked-in Mammoth
  fixture to one deterministic 3,628-byte artifact across 15 cold processes at 254.66 ms
  median and 290.44 ms p95/max. Static HTML-tree, CSP and external-resource checks passed;
  the in-app browser blocked the local `file://` URL, so no visual-browser result is
  claimed and no policy bypass was attempted. The new packaged selection smoke rendered
  the fingerprint-bound Mammoth table to a 3,742-byte artifact with one `table`, one
  `tbody`, two `tr` and four cells under an HTML5 parser, zero invalid table ancestors,
  no source mutation, no Word process and no fixture cell text in the JSON response.
  Hosted CI run `29988472344` passed all five jobs for commit
  `dc72a8e811217bb9515b0ad30810c8925b3081cb`; downloaded Windows artifact
  `8556049406` matched both local ZIPs byte for byte and its normalized 196-file tree had
  zero length/hash differences at the recorded manifest SHA-256.

- Added the public comment-body operation pair
  `wordtoolkit.plan_ooxml_comment_body_edits/1.0` and
  `wordtoolkit.apply_ooxml_comment_body_edits/1.0`. The dependency-free Engine selects
  comments by stable semantic ID, matches exact bounded text across Word runs, binds match
  counts and optional body hashes into a deterministic reviewed plan, validates the exact
  candidate and atomically applies it through direct .NET, `comment-body-package` JSON CLI
  and lazy MCP without opening Word. Responses expose hashes and counts rather than comment
  text or XML, and candidate reprojection proves that anchors, authors, thread topology,
  durable IDs, reactions, revisions, permissions, unselected comments and unrelated parts
  remain invariant. Signatures, plan drift, unexpected matches and new schema errors fail
  closed; a recovery backup is retained by default.
- Comment matching is boundary-aware rather than a flattened descendant-text search.
  Ordinary `w:t` leaves may compose one match across adjacent runs in the same direct
  comment paragraph, including runs with formatting, but paragraph/table-cell boundaries,
  tabs, breaks, fields, content controls and other rich structures split or exclude the
  editable segment. Regression fixtures prove that none of those rendered or structural
  boundaries can be silently crossed.

- Verified the comment-body checkpoint with 388 Engine tests, 263 Native tests and the
  complete 1,273-test Python/OOXML suite with 16 intentional skips; Ruff and the
  maintained 28-module mypy lane are clean. Packaged plan/apply on the checked-in
  `mammoth_comments.docx` fixture reproduced the reviewed plan ID and predicted result
  fingerprint, changed only `word/comments.xml`, passed the bounded Microsoft schema
  comparison, retained a byte-exact backup and left the source fixture and Word-process
  set unchanged. Neither response returned comment text or raw XML.
- Built the checkpoint twice through the supported Windows PowerShell path. Both builds
  produced identical 196-file, 85,366,594-byte trees and identical 36,263,241-byte ZIPs
  at SHA-256 `267ace7127d82c8f54c32f56682830770d6209f1880b4f00d38a31dc45aa5503`;
  expanded manifests matched at SHA-256
  `25fd2c4afcfef292c95c5925d849cee55b5de1297dbbf0b05734e9edd9231373`.
  Fifteen cold packaged planner processes returned the same 1,027-character response at
  669.53 ms median and 688.29 ms p95/max without mutating the source or starting Word.
  No historical before-change comment-body CLI latency baseline exists. Hosted CI run
  `29980881560` passed all five jobs; its downloaded Windows ZIP matched both local
  archives byte for byte and its expanded 196-file tree had zero differences.

- Added the public semantic-style operation pair
  `wordtoolkit.plan_ooxml_semantic_edits/1.0` and
  `wordtoolkit.apply_ooxml_semantic_edits/1.0`. The dependency-free Engine now owns the
  seven-command typed contract, strict variant-aware JSON parser, bounded selector
  resolution, deterministic intent-bound plan ID, exact candidate projection and atomic
  apply used by direct .NET callers, `style-package` JSON CLI and the existing lazy MCP
  actions. Apply rebuilds the plan against the current package, rejects stale fingerprints,
  plan drift and signed packages, keeps a recovery backup by default and never opens Word.
  Requests are capped at 256 Ki characters, resolved edits and changed parts at 200 each,
  and schema issue details are returned only when explicitly requested.
- Added the neutral `IWordPackageCandidateValidator` boundary and the separate
  `WordToolkit.OpenXmlSdk` adapter. The Engine remains independent of Microsoft and every
  AI provider; plan reports an absent validator and apply fails closed with
  `VALIDATOR_REQUIRED`. The standard adapter preserves the previous bounded
  baseline-versus-candidate Microsoft 365 schema check and is now also reused by the
  older Native package mutation paths instead of duplicating validation logic.
- Hardened `OpcAtomicPackageWriter` against a non-cooperative last-millisecond writer.
  After atomic replacement it verifies that the displaced package is the reviewed
  fingerprint; on mismatch it restores those exact displaced bytes and returns
  `VERSION_CONFLICT`. A second destination change during compensation is never silently
  deleted: it is retained as an opaque sibling `.conflict` artifact and produces
  `RECOVERY_REQUIRED`. Failed compensation reports only names of artifacts that still
  exist, never an absolute path or document content, and claims no artifact when none was
  retained.
  Validator exceptions are now treated as untrusted and never copied into public error
  text, while `OOXML_SCHEMA_INVALID` retains its bounded count/issue diagnostics for
  existing MCP clients.
- Added operation versions, explicit filesystem/network/Word permissions, reversibility
  records and closed successful MCP output schemas for semantic-style plan/apply. The
  actual compact plan and apply envelopes validate against Draft 2020-12, while capability
  v1 keeps its existing closed summary shape and reports five metadata-complete actions.
- Verified this semantic-style interoperability checkpoint with 385 Engine tests, 260
  Native tests and the full 1,273-test Python/OOXML suite with 16 intentional skips;
  Ruff and the maintained 28-module mypy lane are clean. The packaged executable planned
  the checked-in real-DOCX request in a 1,811-character response, left the source bytes
  and zero Word-process set unchanged, and completed 15 cold process runs at 747.58 ms
  median and 779.22 ms p95. No historical before-change public style-CLI latency baseline
  exists.
- Ran packaged plan/apply against a disposable copy of the checked-in LibreOffice DOCX.
  Apply reproduced the reviewed plan ID and predicted semantic fingerprint, changed only
  `word/styles.xml`, passed Microsoft schema comparison, retained a byte-exact backup,
  returned no XML, left the original fixture byte-identical and did not start Word.
- Built the checkpoint twice through the supported Windows PowerShell path. Both builds
  produced identical 196-file, 85,271,141-byte trees and identical 36,238,851-byte ZIPs
  at SHA-256 `9a3fc1ae866ca9d5e1f9c1f40b21683491175b8e63125511f4dd6529bff6596e`;
  expanded manifests also matched exactly at SHA-256
  `cd728f01ea4da97e303a1ad488ebd79d64b3b21113bc80aee8438cc29d074104`.
  `WordToolkit.OpenXmlSdk.dll` is present and the package contains zero Python files.
  Mandatory CI run `29977852683` passed all five jobs. Its downloaded 36,238,851-byte
  Windows distributable matched both local ZIPs byte for byte at the same SHA-256.
- Added `wordtoolkit.query_ooxml_semantics/1.0` as the third public
  transport-neutral Engine/CLI/MCP operation. Direct saved-package and process-memory
  indexed queries now share one typed result builder and canonical JSON instead of an
  adapter-owned anonymous response. Stable node IDs are joined by high-level object
  category, story, child count and identity fields; local reads can require the exact
  package fingerprint. Properties remain optional and author/name/date/GUID/field
  instruction/anchor values require a second explicit sensitive-data flag. The operation
  also suppresses complex-field instructions from text previews until that second flag
  is present, reports property shortening explicitly, never returns raw XML, follows
  external relationships or opens Word, and a new
  `query-package --request <json|->` CLI accepts the same closed flat query shape.
- Made semantic query the first action with an explicit operation version, closed MCP
  lazy-action successful structured-content output schema, normalized
  filesystem/network/Word permissions and reversibility record. Capability v1 pages keep
  their existing closed operation-summary shape and count this metadata without adding
  incompatible fields; the exact values and full output schema remain available through
  `inspect_wordtoolkit_action`, while the lazy action stays under a 10,000-character
  contract budget.
  The public JSON codec now writes enums as non-numeric `snake_case` strings; request
  boundaries reject unknown members while result decoding retains additive v1 forward
  compatibility.
- Verified the semantic-query slice with 375 engine tests, 257 native-host tests and the
  complete 1,273 Python/OOXML tests with 16 intentional skips. The checked-in request
  produced five equation-containing paragraphs, a 3,195-byte compact result conforming
  to the published Draft 2020-12 output schema; the complete action contract is 9,461
  bytes, below its 10,000-byte budget. No raw XML or Word process appeared. Fifteen cold
  CLI runs measured 197.76 ms median and 206.68 ms p95 on the development machine; no
  historical before-change latency baseline exists.
- Built the semantic-query checkpoint twice through the supported Windows PowerShell
  package path. Both builds produced identical 195-file, 85,195,358-byte trees and
  36,214,740-byte ZIPs at SHA-256
  `04af23655f262389c16a9b8eb94cc08d39173c5a9d80e74606929d26f9beb9e9`.
  The packaged self-contained executable returned the same five schema-valid matches,
  discovered the query action, wrote nothing to stderr and left the Word-process count
  at zero. Mandatory CI run `29973644971` passed all five jobs; its downloaded Windows
  ZIP matched both local archives byte for byte at the same size and SHA-256.
- Added `wordtoolkit.transform_ooxml_package/1.0` as the second public
  transport-neutral Engine/CLI/MCP operation. Its typed core can replace the first
  ordinary text occurrence across run boundaries, accept all supported tracked changes
  or reject them without opening Word. It preserves untouched entries and opaque bytes,
  excludes OfficeMath from text matching, refuses MCE/revision ambiguity, blocks signed
  packages and output collisions, validates the complete candidate before atomic write
  and returns one canonical result through SDK, `transform-package` CLI and MCP. Deleted
  paragraph-mark acceptance and inserted paragraph-mark rejection now merge only a
  proven-safe immediate following paragraph; paragraph properties or following revision
  content make the shape unsupported rather than guessed.
- Added a direct protocol-v1 `docx-platform-tests` adapter and pinned the neutral harness
  at `fe0ee996...` against safe-docx `3615e213...`. Across the same 42 hidden-assertion
  scenarios, WordToolkit recorded 19 pass, 2 invariant-pass and 21 honest unsupported;
  safe-docx recorded 18, 2 and 22. Both produced zero failures, errors, divergent passes
  or protocol mismatches. Exact commits, environment, commands, caveats and the raw
  74,657-byte result at SHA-256
  `e0103e86940d285027494fd86a7916007943cda31ccad68a52fcde858df324dd` are checked in.
- Expanded the research matrix from eight to twelve pinned AI/Word repositories, split
  Microsoft Copilot's public service APIs from the preview host-bound Office API plugin,
  and added DevExpress, Telerik, TX Text Control, GroupDocs, Google Docs and Adobe PDF
  Services. The audit still refuses a global leadership claim: the neutral corpus is
  narrow and does not yet measure Word layout, preservation, tokens, latency or hostile
  package security.
- Verified the preceding transform/benchmark slice with 366 engine tests, 247 native-host
  tests and the complete 1,273 Python/OOXML tests with 16 intentional skips. Focused
  transform/protocol parity tests
  cover cross-run replacement, first-only behavior, OfficeMath exclusion, MCE rejection,
  accept/reject-all, clean no-op clone, signatures, output collisions, unsafe-input
  decline, exact revision inverse and no Word invocation. Two local self-contained builds
  produced identical 195-file, 85,150,302-byte trees and 36,200,235-byte ZIPs at SHA-256
  `e1ef21c763cae801f48bcfa43d1513ff279936a8fd026e00b28a04133755afe8`.
  The package contains zero Python files; its CLI reports 85 actions and 15 exposed MCP
  tools, finds `transform_ooxml_package`, and changes neither the zero Word-process count
  nor document state during discovery/help probes. Mandatory CI run `29970012359`
  passed all five jobs; its downloaded Windows ZIP matched both local archives byte for
  byte at the same size and SHA-256.
- Added the first public transport-neutral operation,
  `wordtoolkit.inspect_ooxml_package/1.0`, to `WordToolkit.Engine`. Typed file and
  seekable-stream requests, results and stable errors now feed the .NET SDK surface,
  `wordtoolkit-native inspect-package` and the existing MCP action through one bounded
  implementation. A public canonical JSON codec gives SDK, CLI and compact MCP data the
  same `snake_case` shape and null policy; legacy MCP runtime/timing fields remain only
  at the adapter edge. The CLI emits success JSON on stdout, failure JSON on stderr and
  stable sysexits-style codes without constructing the Word COM host.
- Tightened Word-package identity from an unsafe `/officeDocument` suffix test to exact
  Transitional/Strict relationship URIs, one internal resolved main part, one of four
  extension-compatible Word main content types and a Transitional or Strict
  `w:document` root with exactly one direct `w:body`. Inspection and semantic
  projection share these rules, so a valid OPC ZIP with
  `urn:evil/officeDocument` is no longer reported as a valid Word document. New
  regression tests prove read-only file hashes, stream-position restoration, external
  target redaction, default diagnostic-location redaction, false-Word rejection, ZIP
  limits, million-character/path-like stream labels, closed MCP arguments, canonical
  SDK/CLI/MCP parity and stable error codes without invoking or launching Word.
- Verified this operation slice with 357 engine tests, 243 native-host tests, 1,273
  Python/OOXML tests with 16 intentional skips, Ruff, active-service mypy, schema export,
  JSON/PowerShell parsing and an independent red-team. The review found and closed two
  initial P1 defects (false Word identity and MCP response compatibility), then two
  adversarial P1 defects (missing `w:body`/extension checks and an unbounded stream
  filename); its final rerun reported no unresolved P0/P1. Two local self-contained
  builds produced identical 195-file, 85,107,806-byte trees and 36,189,483-byte ZIPs at
  SHA-256 `ff910c0c314ccb98b7f716f40cfa6e4580659b763ee3d657e138c1d6732b4632`.
  Mandatory CI run `29967113037` passed all five jobs, and its hosted Windows ZIP
  matched both local archives byte for byte at the same size and SHA-256.
  Through that packaged executable, CLI and MCP returned identical 6,827-character
  canonical data for the same real DOCX, global help returned zero, and the Word-process
  count stayed 0. Across 20 cold runs, inspection measured 265.965 ms p50 / 286.932 ms
  p95 through CLI and 321.721 / 348.233 ms through MCP on the local Windows x64 host.
- Added a vendor-neutral, schema-versioned capability manifest shared by the native MCP
  `get_wordtoolkit_capabilities` gateway and `wordtoolkit-native capabilities` CLI.
  It preserves the embedded schema/MCP/compatibility header, publishes deterministic
  source, action-contract and capability-schema SHA-256 values, pages sorted summaries
  for all 84 actions, reports effect hints, hard limits and explicit metadata-coverage
  gaps, and exposes operation-specific format support without opening Word, reading a
  document or using the network. Native/core membership now comes from the embedded
  schema's `native_runtime` registry instead of duplicate C# lists. The normative JSON
  Schema is checked in, embedded and retrievable byte-for-byte through MCP
  `view=schema` or CLI `--schema`; its advertised hash is therefore independently
  verifiable. Fixed schema compaction so legitimate input properties named `title`
  survive while presentation annotations are still removed;
  malformed, unbounded and unknown input fails closed. The public MCP surface is now 15
  tools while full action schemas remain lazy.
- Verified the capability-contract slice with 340 document-engine tests, 239 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, schema-export drift
  checks, Draft 2020-12 validation and an independent red-team. The red-team found and
  closed two P1 defects: schema compaction had removed the real image `title` input, and
  an installed client could not retrieve the schema whose hash it was asked to trust.
  Two local self-contained builds produced identical 195-file, 85,083,230-byte trees
  and 36,179,845-byte ZIPs at SHA-256
  `1ebf765215f58ca22de6204a382dcedd83ec9e51dae15a9ba1c47509b237627f`.
  Through the exact packaged executable, CLI and MCP returned byte-equivalent canonical
  manifest/schema data; the schema hash validated, the image schema retained all 13
  properties, no Word process appeared and the package contained zero Python files.
  The 15-tool catalogue is 8,885 compact characters versus 8,172 without the capability
  gateway; the default 12-operation manifest is 5,359 and the opt-in schema view 6,769.
  Across 20 cold process runs, CLI manifest discovery measured 107.350 ms p50 / 121.159
  ms p95, MCP discovery 157.168 / 176.796 ms and CLI schema retrieval 86.148 / 94.272
  ms. The public release remains 0.34.0.

- Added a bounded, source-linked logical table graph and lazy
  `inspect_ooxml_tables` action. Transitional and Strict WordprocessingML tables now
  expose declared grids, physical-to-logical cell placement, row skips, `gridSpan`,
  exact-span vertical merge chains, separate legacy `hMerge` state, nested tables,
  contiguous repeating headers, row table-property exceptions, widths, fixed/autofit
  layout and Word-effective floating positioning. The dependency graph links nesting and
  merge continuations; the accessibility linter consumes the same typed header result.
  Cell text and raw XML have no response field, while names, layout and source require
  independent opt-ins.
- Verified the local table checkpoint with 340 document-engine tests, 228 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, schema validation,
  generated-document structural/visual checks and a warning-free Open XML validator
  build. Two local self-contained builds produced identical 195-file, 85,056,480-byte
  trees and 36,172,619-byte ZIPs at SHA-256
  `910a1cece37397f61568ea0a230fe663fdf54d834d8a8d367fa56b37ddfe1c13`.
  The packaged MCP returned 2 tables, 34 rows and 251 cells from the advanced torture
  DOCX in a 2,308-character compact response with zero issues, no Word process change,
  no source path, no cell text and no raw XML. Checked-in 10,000/100,000-cell scale
  points expose both throughput and allocation cost. Mandatory CI run `29959678412`
  passed all five jobs with zero annotations; its downloaded Windows artifact matched
  both local ZIPs and the 195-file expanded tree byte for byte. The public release
  remains 0.34.0.

- Added a bounded, read-only ECMA-376 Part 3 Markup Compatibility graph and the lazy
  `inspect_ooxml_markup_compatibility` action. The engine now models inherited
  `mc:Ignorable`, `mc:ProcessContent`, `mc:MustUnderstand` and
  `mc:AlternateContent`, separates branch selection from effective output, records
  legacy preservation hints without executing them, and keeps explicitly configured
  application-defined extension subtrees opaque. Namespace/source details are redacted
  by default; the action does not preprocess, mutate, open Word or claim a Word-version
  compatibility profile. Corrected scale fixtures remain below their declared ceilings
  at 99,999, 499,999 and 998,998 XML elements; the largest retains about 1.03 GB of
  managed memory, so the million-element boundary is documented as a hard limit rather
  than an ordinary workload.
- Verified the local MCE checkpoint with 316 document-engine tests, 218 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET
  formatting, schema drift checks, generated-document structural/visual checks and a
  warning-free Open XML validator build. Two local self-contained builds produced
  identical 195-file, 84,713,190-byte trees and 36,077,499-byte ZIPs at SHA-256
  `5312432d0ff5e8ea2c0c4ca664011225be7ea7ad49a0b7b091aa9db4efba2ea3`, with zero
  Python files. The exact packaged MCP inspected a real LibreOffice document in a
  3,054-character response without opening Word, following external targets, mutating
  the package or returning namespace URIs, source-part names, formulas or raw XML.
  All five jobs in mandatory CI run `29951495361` passed with zero annotations. Its
  hosted Windows artifact matched both local ZIPs and expanded trees byte for byte at
  the same size and SHA-256.
- Added fail-closed primary-name rename to the typed saved-package semantic edit actions.
  `rename_style` changes only `w:name` for an explicitly selected custom, non-default
  style; it never changes the stable `w:styleId`, aliases, formatting or ID-based
  references. Missing primary names are inserted losslessly. Existing ID/name/alias
  collisions, latent-style name consumers, risky `STYLEREF`, macros, `altChunk`, linked
  templates, unmodeled field consumers and `stylesWithEffects` block before mutation.
  Rename composes with assignment in the same deterministic transaction, reports a
  bounded rename count and retains an exact byte inverse without returning document
  content.
- Verified the rename slice with 283 document-engine tests, 208 native-host tests,
  1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET formatting,
  schema-export drift checks and the standalone Open XML validator build. Two local
  self-contained builds produced identical 195-file, 84,409,303-byte trees and
  35,998,031-byte ZIPs with SHA-256
  `85919959a2176627a6a973ad61ccabe189eaf36fabc44c83c5eb53e7479a59f2`.
  Through that packaged MCP, an 83-character `rename_style` command produced a
  946-character compact plan stable under JSON property order, with one positive byte
  delta and no new SDK errors. Apply matched the predicted fingerprint, changed only
  `word/styles.xml`, retained a byte-exact pre-apply backup, preserved the stable style
  ID and every original non-style ZIP-entry payload, returned no XML, used no Python and
  did not open Word. A packaged attempt to rename the default `Standard` style failed
  closed and left the file byte-identical. All five jobs in mandatory CI run
  `29942107590` passed, and its hosted Windows artifact matched the local ZIP and
  expanded tree byte for byte at the same counts, sizes and SHA-256.
- Added fail-closed unused-style deletion to the typed saved-package semantic edit
  actions. `delete_unused_style` removes only an explicitly selected custom, non-default
  definition after proving that no surviving semantic, revision, style, numbering,
  glossary, latent-style, `STYLEREF` or unmodeled XML consumer refers to it. One explicit
  batch may remove a closed graph of mutually dependent unused styles. The operation
  removes only exact `styles.xml` byte spans, participates in the same deterministic
  fingerprint-bound create/consolidate/delete/assign transaction, retains an exact
  inverse and reports a bounded deletion count without returning document content.
  Built-in/default styles, retained references, graph damage, macros, `altChunk`, linked
  templates, `stylesWithEffects`, signatures and schema regressions fail before mutation.
- Verified the deletion slice with 280 document-engine tests, 207 native-host tests,
  1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET formatting,
  schema-export drift checks and the standalone Open XML validator build. Two local
  self-contained builds produced identical 195-file, 84,397,476-byte trees and
  35,995,361-byte ZIPs with SHA-256
  `417af08694ba33cd7c6fedc6ad06a6e2baf00b51f0f2af76c34d703b3976f9b6`.
  Through that packaged MCP, a clone first created one unused custom style. Its
  55-character deletion command produced a 934-character compact plan stable under JSON
  property order, with one negative byte delta and no new SDK errors. Apply matched the
  predicted fingerprint, changed only `word/styles.xml`, retained an exact pre-apply
  backup, removed the style, preserved every original ZIP-entry payload, returned no XML
  and did not change the running Word process set. A packaged attempt to delete the
  default `Standard` style failed closed and left the file byte-identical. All five jobs
  in mandatory CI run `29938834101` passed, and its hosted Windows artifact matched the
  local ZIP byte for byte at the same size and SHA-256.
- Added fail-closed exact style consolidation to the typed saved-package semantic edit
  actions. `consolidate_style` rewrites type-checked paragraph/run/table, glossary,
  style-graph and numbering references, removes an explicitly selected
  canonical-equivalent custom source definition and retains one predicted fingerprint
  plus exact inverse. Linked
  paragraph/character pairs can be consolidated in one explicit batch; create/clone,
  consolidation and assignment stages compose only through an exact byte chain. Built-in
  or non-equivalent sources, chained targets, graph damage, matching latent-style
  exceptions, unsafe `STYLEREF`, unmodeled XML consumers, macros, `altChunk`,
  linked-template updates and `stylesWithEffects` fail
  before mutation. Plan responses expose bounded consolidation/reference counts, accept
  property-order-stable intent, return no XML/text and remain below the 4,500-character
  lazy-schema ceiling.
- Verified the consolidation slice with 278 document-engine tests, 206 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET formatting
  and the standalone Open XML validator build. Two local self-contained builds produced
  identical 195-file, 84,386,430-byte trees and 35,992,947-byte ZIPs with SHA-256
  `c7f68a994a185b80e211a751a05881537f0d836c53e195107cd4ae2fd4d11a76`.
  Through the packaged MCP, two clones and one server selector first created an exact
  duplicate and assigned it to six Apache POI paragraphs. A 108-character consolidation
  command then produced a 902-character compact plan, stable under JSON property order,
  with six reference rewrites and no new SDK errors. Apply matched the predicted
  fingerprint, changed only `word/document.xml` and `word/styles.xml`, retained a backup,
  removed the source definition, left six target uses and zero source uses, returned no
  XML and did not change the running Word process set. All five jobs in mandatory CI run
  `29936061264` passed, and its hosted Windows artifact matched the local ZIP byte for byte
  at the same size and SHA-256.
- Added atomic typed style-definition creation and cloning to the existing semantic edit
  actions. `create_style` emits a minimal custom paragraph, character, table or numbering
  definition with bounded inheritance/UI metadata; `clone_style` preserves an existing
  definition's modeled and opaque formatting while stripping unsafe default/link identity.
  The same stateless plan can server-select nodes and assign a newly created style, joining
  `styles.xml` and document-part changes under one predicted fingerprint, exact inverse,
  Microsoft Open XML validation and atomic backup-capable apply. Duplicate IDs, missing or
  wrong-type references, cycles, changed intent, signatures and `stylesWithEffects` mirror
  drift fail closed. No command accepts or returns raw XML, and the lazy schemas remain
  below 4,500 serialized characters.
- Verified the style-definition slice with 274 document-engine tests, 205 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET
  formatting and the standalone Open XML validator build. Two local self-contained
  builds produced identical 195-file, 84,359,203-byte trees and 35,982,900-byte ZIPs
  with SHA-256
  `ca713cbec08fa31dc67de8ef503bbf33c28a08e187980cc95d18093144c53f2f`.
  Through the packaged MCP, a 218-character two-command batch cloned `Standard` as
  `CodexDefinition` and server-assigned it to six paragraphs in an Apache POI DOCX.
  The compact seven-operation plan response was 2,146 characters. Candidate validation
  found no new errors; apply matched the predicted fingerprint, changed only
  `word/styles.xml` and `word/document.xml`, retained a backup, returned no XML, left
  the source fixture unchanged and did not alter the running Word process set. All five
  jobs in mandatory CI run `29932018346` passed, and its Windows artifact matched the
  local ZIP byte for byte at the same size and SHA-256.
- Added token-lean bulk `set_style_where` commands to the typed saved-package semantic
  edit actions. One compact selector now resolves paragraph, run, or table targets from
  bounded text, exact properties, ancestor/descendant, subtree and source-part evidence
  without making the model echo every node ID. The caller must declare `max_matches`;
  empty, excessive, overlapping, invalid-kind and over-200 selections fail closed, with
  at most 16 selectors per transaction. Canonical typed intent makes plan IDs stable
  across harmless JSON property order while preventing changed selector replay. Compact
  responses expose submitted/selector/resolved counts and no document text or XML.
- Verified the bulk-selector slice with 270 document-engine tests, 203 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET
  formatting and the standalone Open XML validator build. Two local self-contained
  builds produced identical 195-file, 84,327,130-byte trees and 35,973,436-byte ZIPs
  with SHA-256
  `65ccaed09b608be6fb851a4ae38e54b4fc4ff9d1b7ac8b6f1e5aaa9d0dcbaeac`.
  Through the packaged MCP, one bounded selector resolved six paragraph style changes
  in an Apache POI DOCX; candidate validation found no new errors, apply matched the
  predicted fingerprint, changed only `word/document.xml`, retained a recovery backup,
  returned no XML, left the source unchanged and did not alter the running Word process
  set. The selector used 77.53% fewer serialized command-input characters than the six
  exact commands on that fixture; the 200-target regression fixture reduced command
  input from 19,014 to 186 characters (99.02%). All five jobs in mandatory CI run
  `29928388511` passed, and its downloaded Windows ZIP matched both local archives
  exactly in length and SHA-256.
- Added lazy `plan_ooxml_semantic_edits` and `apply_ooxml_semantic_edits` as the first
  extensible high-level semantic mutation surface. The initial `set_style` command
  assigns existing compatible paragraph, character, or table styles to exact stable
  paragraph/run/table IDs under package, plan, and explicit-style preconditions. The
  lossless planner preserves unrelated bytes, avoids namespace-prefix rebinding,
  predicts the result fingerprint, retains an exact inverse, and validates the exact
  candidate against the baseline with Microsoft Open XML SDK. Apply is atomic, keeps a
  recovery backup by default, rejects signed/stale/drifting/invalid candidates, never
  opens Word, and returns neither XML nor document text. The public catalog remains 14
  tools and grows to 80 lazy actions.
- Verified the semantic-style slice with 270 document-engine tests, 200 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET
  formatting and the standalone Open XML validator build. Two local self-contained
  builds produced identical 195-file, 84,302,924-byte trees and 35,963,647-byte ZIPs
  with SHA-256
  `fc92ec8ae48273f972426934497b1217bd0d660fd55f6fa7f001b36215eb06e4`.
  The packaged MCP changed an exact source-linked paragraph in an Apache POI DOCX from
  `berschrift1` to the existing `Standard` style, validated the candidate, matched the
  predicted fingerprint, changed only `word/document.xml`, retained a backup, returned
  no XML, kept every complete response below 2,900 characters, left the source fixture
  unchanged and did not alter the running Word process set. All five jobs in mandatory
  CI run `29925881340` passed, and its downloaded Windows ZIP matched the local archive
  exactly.
- Added strict `ancestor` and `descendant` predicates to saved-package semantic
  queries. A related-node predicate combines semantic kinds and exact properties on
  one node, excludes self, and is propagated through the tree in linear time. Indexed
  queries resolve related matches from existing postings, add relationship positions
  to the smallest-candidate plan, and still recheck every predicate. This selects such
  objects as paragraphs containing equations and equations inside table cells without
  returning raw XML or spending model tokens on tree traversal. The public and lazy
  action counts remain unchanged.
- Verified the structural-query slice with 262 document-engine tests, 196 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET
  formatting and the standalone Open XML validator build. Two local self-contained
  builds produced identical 195-file, 84,250,176-byte trees and 35,951,915-byte ZIPs
  with SHA-256
  `f416bfbd8e37adcce2a3a88a97ad4f7e5b698001d9d9f2c4784b9e15715899fe`.
  The packaged MCP queried a real 194-node equation DOCX, selected 5 equation-bearing
  paragraphs after scanning 11 candidates, released the index, kept every complete
  JSON-RPC response below 3,200 characters, left the source unchanged and did not
  alter the running Word process set. The downloaded artifact from mandatory CI run
  `29923044649` matched the local ZIP exactly, and all five jobs passed.
- Added a bounded native semantic index for repeated AI queries. `WordSemanticIndex`
  precomputes source-ordered postings for node kind, source part and exact property
  values, then chooses the smallest posting as the candidate seed while rechecking every
  predicate. Lazy `manage_ooxml_semantic_index` creates/reuses, inspects, lists and
  releases package-fingerprint-bound handles; `query_ooxml_semantics` can consume one
  only with the exact package fingerprint and reports index use, candidate seed and scan
  counts. Indexes stay only in process memory, return no raw text, expire within 30
  minutes and are capped at four handles, 100,000 nodes each and 250,000 cached nodes.
  The public catalog remains 14 tools and grows to 78 lazy actions.
- Verified the semantic-index slice with 260 document-engine tests, 194 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET formatting
  and the standalone Open XML validator build. Two local self-contained builds produced
  identical 195-file, 84,237,559-byte trees and 35,947,755-byte ZIPs with SHA-256
  `5c4b3ef4d420259463d2cca0e7ebef8d647781c541be761025861c1a73db004a`.
  The downloaded artifact from mandatory CI run `29920866791` matched that ZIP exactly.
  The packaged MCP indexed the 142-node LibreOffice TOC fixture, reduced a paragraph
  query to 13 candidates, explicitly released the handle, returned no raw index text,
  left the source unchanged and did not change the running Word process set.

- Verification checkpoint: 245 document-engine tests, 185 native-host tests, 1,273
  Python/OOXML tests with 16 intentional skips, Ruff and every mandatory GitHub job pass.
  A fresh local checkout and two hosted Windows builds produced the same 35,886,733-byte
  ZIP with SHA-256 `e8f2e4b74fe65213197126c7aafb445452bd0e80bc05f7206d82672e4b09e59b`.
  The exact package passed all 48 real-Word actions and Open XML validation while the
  pre-existing user-owned Word process and document remained open and unchanged.

- Added the first native saved-package linter. Eighteen deterministic core, styles,
  accessibility and security rules consume one fingerprint-bound set of typed graphs
  and report stable finding/rule IDs, severity, confidence, privacy-safe subject
  fingerprints, bounded evidence, optional exact source byte spans, validated
  suppressions and explicit fix safety. It detects graph/package corruption, unused and
  formatting-equivalent styles, direct formatting, external relationships, hidden text,
  heading-order gaps, absent drawing alt text, unmarked table headers and a missing
  document title. Coverage omissions and unmodeled domains prevent a false complete
  verdict; at that read-only checkpoint every fix remained marked unimplemented.
- Added lazy `lint_ooxml_document` with compact summary and paged finding/rule views.
  The action never opens Word, follows an external target or mutates a package. Source
  data is off by default, suppression arrays and pages are bounded, the default result
  stays below 5000 characters and the complete mirrored JSON-RPC response below 10000.
  The token-lean public surface remained 14 tools while the linter-only action catalog
  grew to 75; the repair slice below raises it again.
- Verified the linter checkpoint with 250 document-engine tests and 189 native-host
  tests. Two independent local package builds produced identical 195-file,
  35,918,887-byte archives with SHA-256
  `e0d162feac71679efedfeac0de6982447f4856298d9b7334a0195a04c27f7400` and no Python
  files. The packaged MCP inspected the LibreOffice TOC fixture with all 18 rules,
  returned 35 visible findings in a 3700-character complete response, reported
  incomplete document-domain coverage, and proved Word remained unopened and the
  package unmodified. The last complete 48-action live-Word gate remains the preceding
  exact 0.35 package checkpoint because this slice changes only saved-package analysis.
- Canonicalized copied plugin JSON/Markdown and the embedded native MCP schema to
  BOM-less UTF-8 with LF during packaging. A stale pre-`.gitattributes` checkout had
  silently produced different manifest and assembly bytes from a clean checkout. After
  this guard, the same exact ZIP hash is produced by that stale working tree, a clean
  detached worktree and the hosted Windows CI artifact.
- Added the first fail-closed native lint repair. `WordLintRepairPlanner` accepts only a
  package-bound document-title finding backed by exactly one existing, empty, leaf
  `dc:title`; it losslessly replaces that element, changes only the core-properties
  part, predicts the result fingerprint, reparses the candidate, proves the finding is
  gone and retains an exact byte inverse. Missing, duplicate, nonempty or mixed-markup
  titles are refused instead of synthesized or guessed.
- Added lazy `plan_ooxml_lint_repair` and `apply_ooxml_lint_repair`. The plan binds the
  package, finding, replacement and new same-extension output path, hashes rather than
  echoes the title, and compares baseline/candidate Open XML validation. Apply rebuilds
  the exact plan, blocks signatures and new validation errors, creates a new file
  atomically, never overwrites the source or output and never opens Word. The public
  catalog remains 14 tools and grows to 77 lazy actions.
- Verified the repair slice with 254 document-engine tests, 192 native-host tests,
  1,273 Python/OOXML tests with 16 intentional skips, Ruff and the standalone Open XML
  validator build. Two local self-contained builds produced the same 195-file,
  84,193,862-byte tree and 35,935,327-byte ZIP with SHA-256
  `49aae752c5d2457d4474f63ff1142fe5909bac20cfdf7b534e97044dd3e29ca8`.
  The packaged MCP repaired the empty title in the LibreOffice TOC fixture, changed only
  `docProps/core.xml`, matched the predicted package fingerprint, returned no raw title
  or XML, removed the exact lint finding and kept Word unopened and the source unchanged.
- Normalized Windows packaging onto Windows PowerShell 5.1 even when the caller starts
  the build through `pwsh`. The two hosts use different `System.IO.Compression`
  implementations and previously produced different ZIP bytes from the same 195-file
  tree. Local `pwsh`, direct Windows PowerShell and the hosted Windows artifact now
  produce the exact same distributable hash above.

- Made the document-engine and native .NET test suites mandatory CI inputs and added a
  clean Windows job that builds the exact distributable plugin ZIP. Tag builds on the
  licensed self-hosted Word runner now execute the full 48-action live acceptance gate.
- Repaired that gate for Windows PowerShell 5.1 by removing parser-unsafe source text,
  avoiding unsupported JSON parameters, escaping Unicode MCP values and preventing a
  UTF-8 preamble from corrupting the first request.
- Bounded line-delimited MCP input at 8 MiB, added per-request cancellation tokens,
  active request IDs, synchronized concurrent responses and MCP cancellation
  notifications. A cancelled in-flight COM call now blocks new Word work until it
  returns and directs supervisors to restart only the WordToolkit runtime if it hangs.
- Centralized the native base version in `native/Directory.Build.props`; package builds
  fail when it drifts from the plugin manifest. Corrected stale 0.30/0.33 README claims.
- Stopped the legacy Python schema exporter from overwriting the native MCP catalog.
  CI now rejects generated remote-schema/documentation drift, while native catalog
  coverage remains enforced by `WordToolkit.Native.Tests`.
- Pinned native builds to .NET SDK 8.0.423, enforced LF for repository text and mapped
  checkout-dependent compiler paths to stable virtual roots. This removes SDK/runtime-
  pack, Windows checkout conversion and absolute PDB paths from cross-host ZIP hashes;
  build reports now include the exact SDK version.
- Documented that version-1 `.wtpatch` files contain full confidential payloads, are
  materialized in memory and currently have no trusted signature or encryption envelope.
- Added a reproducible engine benchmark harness, scheduled/manual benchmark workflow and
  checked-in Windows x64 baseline. Roughly one million dependency nodes peaked at
  4,173.1 MiB in 38.56 seconds; a 400 MiB patch payload peaked at 2,158.1 MiB in
  15.61 seconds. These expose the current allocation debt instead of hiding it behind
  permissive safety ceilings.
- Reduced default `.wtpatch` limits after measurement: 128 MiB aggregate payload,
  64 MiB per blob, 4 MiB manifest and 100:1 compression ratio. Higher limits now require
  an explicit caller configuration rather than silently consuming multi-gigabyte memory.
- Added an optional authenticated patch envelope in the engine. AES-256-GCM uses a fresh
  nonce and binds canonical metadata as associated data; ECDSA-SHA256 signs metadata,
  tag and payload and binds a restricted signer key ID. Wrong keys, tampering, missing
  verifiers and unexpected signer identities fail closed. Raw `.wtpatch` remains
  unencrypted by default, and MCP key provisioning is still deliberately absent.

## 0.34.0 — 2026-07-22

- Added the initial unified `WordDependencyGraph`. Deterministic `wddn_` nodes and
  `wdde_` edges join OPC reachability, source-linked semantic containment, explicit
  paragraph/run/table style usage, style defaults and inheritance/link chains,
  numbering definitions and uses, field/bookmark targets, nested fields and section
  header/footer bindings. Missing and external targets remain explicit, every endpoint
  and input fingerprint is checked, stable-ID collision detection is constant-time,
  and node/edge/key/issue budgets fail closed.
- Added lazy `inspect_ooxml_dependencies` with compact summary, paged node/edge,
  unresolved-target, issue and bounded one-to-four-hop impact views. Keys and source
  provenance are redacted by default, external relationships are never followed, field
  codes are never executed and the response carries an explicit unmodeled-domain list.
  Page selection is a cancellable single pass that retains only the requested page;
  summary aggregation is bounded by the fixed edge-kind vocabulary. The public MCP
  surface remains 14 tools while the lazy action catalog grows to 74.
- Projected explicit table style, paragraph numbering instance and numbering-level
  references into the source-linked semantic properties so the dependency spine does
  not infer those relationships from rendered appearance.
- Refreshed all eight pinned AI/Word repository heads on 2026-07-22. Seven were
  unchanged; OfficeCLI advanced 28 commits to `e7916a2...` (`1.0.140`). No Word handler
  changed in that range, while its missing-destination atomic-write fallback was
  recorded as a persistence-semantic change rather than ignored as release noise.
- Verified 238 document-engine tests and 183 native-host tests. A field-heavy corpus
  document keeps the default dependency result data below 5000 serialized characters,
  and tests prove deterministic identities, complete edge endpoints, redaction,
  unresolved/orphan evidence, traversal limits and zero Word COM invocation.
- Built the self-contained Windows x64 plugin twice from independent output
  directories. Both 195-file archives are byte-identical at 35,867,498 bytes with
  SHA-256 `f4625c2c15827e78c9b5c54eaa50adf6aeeb64644235cafc46aa8374812b3944`;
  the native runtime assembly SHA-256 is
  `d93d12ab573a72547bc4db1992c997c1cb44c6b235439ca72ec1abd16d45840f`.
- Corrected the package report to hash the native runtime assembly separately from the
  generic .NET apphost executable, whose unchanged stub hash is not proof that the
  WordToolkit implementation stayed unchanged.

## 0.33.0 — 2026-07-22

- Added loss-aware native style preservation for Presentation MathML and OMML. MathML
  resolves inherited `mathvariant` values on `math`/`mstyle`, token overrides, all four
  weight/slant styles and ten representable mathematical alphabets. OMML preserves all
  four `m:sty` values (`p`, `b`, `i`, `bi`) separately from `m:ctrlPr/w:rPr` bold and
  italic properties for recognized structural controls. The full 19-name control
  property vocabulary is known; unsupported OMML structures still fail instead of
  being flattened under a broad support claim.
- Expanded the private build protocol from four to 24 stable, reserved sentinels so
  run-only, run-and-control and first-control scopes remain distinct. Marker injection,
  imbalance, unsupported placement and contextual Arabic MathML variants that cannot
  survive the linear Word path fail before mutation instead of silently flattening.
- Made native style readback semantic instead of byte-fragile. The verifier normalizes
  Word's documented default `m:sty="i"` and `m:scr="roman"`, coalesces only adjacent
  sibling runs with identical effective properties, and still hashes text, normal/literal
  flags, mathematical script, run style and structural-control placement. Diagnostics
  expose bounded property traces without formula text or raw OMML.
- Extended the complete real-Word gate with normal, bold, italic and bold-italic MathML
  tokens, all ten representable mathematical alphabets and separate OMML run/control
  scopes. The packaged Release runtime completed
  122 MCP requests, all 48 live actions, 12 editable equations, save/reopen/reconnect,
  Open XML validation and a 166,347-byte PDF while preserving the pre-existing Word
  process and closing only its own acceptance document.
- Verified 234 document-engine tests, 179 native-host tests, Ruff, mypy and 1273
  Python/OOXML tests with 16 intentional skips.
- Built the self-contained Windows x64 plugin twice from independent output
  directories. Both 195-file archives were byte-identical at 36,989,699 bytes with
  SHA-256 `8a64ed4f9b69b80f338de5c30bc687852bf29d24bddcb9b99eb078d15e06d1b1`;
  the runtime executable SHA-256 is
  `dd5bf30493826db37d43b027928a7f9b9a881b070264b18b6824c80ad15440db`.

## 0.32.0 — 2026-07-22

- Added native editable `\mathbf{...}` and `\boldsymbol{...}` authoring without
  per-character COM formatting. Balanced private build sentinels survive Word's
  `BuildUp()`, are removed by a bounded internal OMML rewrite, and become native
  `m:sty="b"` / `m:sty="bi"` runs plus matching `m:ctrlPr/w:rPr` weight on
  fraction, radical, delimiter and n-ary controls.
- Added independent semantic and style-placement contracts after reinsertion.
  Marker loss, style drift, control-placement drift, malformed XML, an extra
  equation or a changed formula now rolls back the complete Word Undo record.
  Strict OfficeMath creates Strict wordprocessing control properties, and generated
  `w:b` / `w:i` properties follow schema order.
- Repaired visible text spacing in LaTeX `cases`. Word-discarded ordinary spaces are
  replaced by U+2003 case-column and U+2005 text-boundary spacing; both survive
  build-up, save/reopen and PDF rendering and are now significant in native readback.
  The full-live UnicodeMath sample now uses a real canonical `∑` operator instead of
  the literal word `sum`.
- Kept AI responses bounded. Full preflight returns clean Word linear math without
  internal sentinels; compact preflight and mutation responses expose only aggregate
  style counts and verification facts. The complete packaged live gate remains at
  339 serialized characters for compact equation preflight.
- Verified 234 document-engine tests, 161 native-host tests and 1273 Python/OOXML
  tests, plus the 48-action real-Word acceptance with 12 native equations, SDK
  validation, save/reopen/reconnect and native PDF visual inspection. Two independent
  package builds produced the same 36,975,454-byte ZIP with SHA-256
  `dbe6a4553bcbcda1aeafcd366a240dc3c8d19b33f6ecd93bb13544c28bf6f71d`.

## 0.31.0 — 2026-07-22

- Corrected native Word round trips for named functions and limit operators. OMML
  function application no longer invents an extra argument delimiter, while `lim`,
  `min` and `max` now receive an explicit Word boundary after their lower limit so the
  following operand cannot be swallowed into that limit.
- Added LaTeX `\left\|...\right\|` and `\|...\|` norm delimiters, and canonicalized
  the edge whitespace that Word removes from mathematical text inside `cases`.
- Scoped differential-placement verification to differentials owned by integral
  operands. Ordinary derivatives such as `\frac{\mathrm{d}y}{\mathrm{d}x}` retain the
  same U+2146 contract without being falsely rejected for living outside `m:nary`.
- Preserved Word's mathematical-alphabet run properties for script, Fraktur,
  double-struck, sans-serif and monospace Latin letters and supported digits. LaTeX
  `\mathcal`, `\mathfrak`, `\mathbb`, `\mathsf`, `\mathtt` and simple `\mathrm`
  now create the intended native glyph family and survive readback. `\mathbf` and
  `\boldsymbol` fail closed because this linear OMath path does not preserve their
  weight; silently emitting an ordinary letter is no longer treated as success.
- Forced real-Word readback over the 48-family equation atlas, all 112 registered
  symbol commands, all 20 named functions, ten delimiter forms, mathematical
  alphabets and an ordinary derivative. Every accepted case matched its canonical
  contract and returned no raw OMML.
- Repaired the packaged full-live acceptance harness for the token-lean public
  surface: 14 exposed tools now discover and execute the 73-action catalog through
  the lazy gateways, detailed assertions explicitly request full responses, and a
  separate compact equation preflight remains capped at 339 serialized characters.
  The gate exercised all 48 live actions, reopened the saved DOCX, validated it,
  exported PDF and closed only its own test document. The older expanded acceptance,
  capability demo and Word atlas harnesses now use the same lazy public contract instead
  of demanding the retired 48-tool catalogue.
- Verified 234 document-engine tests, 143 native-host tests and 1273 Python/OOXML
  tests. Two independent Windows PowerShell 5.1 builds produced the same
  35,814,374-byte self-contained ZIP with SHA-256
  `17ef223ddac5b9b8ba02c7b86c29089b2e76d8cc730cbee58f9aa0225d088f25`.

## 0.30.0 — 2026-07-22

- Rebuilt live integral conversion around Word's actual UnicodeMath grammar. LaTeX
  `\,d x`, `\mathrm{d}x`, `\operatorname{d}x` and `\dd x`, normal-variant MathML
  `d`, native OMML normal `d`, and direct UnicodeMath `ⅆ` now converge on U+2146.
  Integral operands containing differentials are wrapped in Word's invisible `〖…〗`
  group, including nested `∫`, `∬` and `∭` forms, so `OMath.BuildUp()` cannot lift the
  differential into an exponent or leave it outside `m:nary/m:e`.
- Added a bounded native equation readback verifier. Structurally sensitive n-ary
  operators, differentials, matrices, cases, equation arrays, accents, hbar and dagger
  notation automatically read back the exact new OMath through `Range.WordOpenXML`.
  The verifier securely reparses one equation, compares canonical SHA-256 contracts,
  checks symbol counts and differential ancestry, returns no raw OMML, and rolls back
  the complete Word Undo transaction on drift. `verify_readback=true` can extend the
  same gate to otherwise low-risk equations.
- Hardened secure MathML/OMML conversion for Strict OfficeMath namespaces, real Word
  run/control formatting, omitted default integral characters and normal differential
  runs. Word's matrix/cases marker expansion and combining accent characters now map to
  the same canonical contract used by LaTeX input.
- Made equation preflight token-lean by default. Compact responses omit converted
  linear math, rule arrays and source markup, returning only bounded counts, flags and a
  short fingerprint; exact linear output requires `response_mode="full"`. Raw OMML is
  never returned by live verification.
- Added precise lazy equation-item schemas and explicit failure for misleading format
  aliases such as `source_format`; callers must use `input_format`. Fixed the direct
  single-equation action so its advertised token-verified `cursor`, `selection` and
  `document_end` targets are actually honored instead of always appending.
- Verified the release checkpoint with 234 native engine tests, 120 native host tests
  and 1273 Python/OOXML tests. Real Word MCP regression covered Gaussian, nested and
  double integrals, Presentation MathML, OMML, a parenthesized matrix, cases and
  combining accents with transactional rollback on the deliberately isolated failure.

## 0.29.0 — 2026-07-22

- Added `WordPackageThreeWayMergePlanner`, a bounded deterministic merge over an
  explicit ancestor plus left and right saved Word packages. It automatically selects
  one-sided changes, coalesces identical changes and composes disjoint lossless
  source-linked `w:t`, `w:delText` and `m:t` edits inside the same part. A branch must
  reconstruct byte-exactly from the ancestor through those text commands before the
  semantic path is trusted; hidden markup drift therefore falls back to conflict rather
  than being discarded.
- Added stable `wtmc_` conflict records and `wtmerge_` plan identities. Divergent add,
  modify, delete/modify and same-text-node changes expose hashes, sizes and bounded
  privacy-off-by-default text evidence without payloads or raw XML. Every conflict must
  be resolved exactly once with `use_ancestor`, `use_left` or `use_right`; unknown,
  duplicate and stale IDs fail closed.
- Added lazy `plan_ooxml_merge` and `apply_ooxml_merge`. Planning is summary-first and
  pages conflicts, entry decisions, resulting patch operations, risks or schema errors
  only on demand. Apply requires exact fingerprints for all three inputs and a
  destination-bound `wtmergeapply_` identity, reprojects and revalidates the candidate,
  preserves the patch engine's separate signature/active-content/external-link/binary/
  error gates, enforces the Word main-part type against the output extension, creates a
  new file atomically and never overwrites an existing path.
- Extended the atomic writer with a race-safe new-destination mode. Added merge coverage
  for no-op, one-sided/identical/disjoint edits, same-node conflicts, unknown markup,
  hidden XML drift, add/delete/modify conflicts, deterministic resolution order, stale
  identities, output-path binding, no-overwrite, macro and opaque-binary gates, type
  mismatch, cancellation and no-Word-host operation. This is an honest initial merge
  slice: arbitrary structural OOXML and revision-aware node merges remain unresolved
  work, not a fabricated success.
- Hardened the Windows release builder so it also runs under the system Windows
  PowerShell 5.1 instead of depending on a .NET-only `Path.GetRelativePath` method.
  Plugin ZIP entries are emitted in sorted order with a fixed OPC-compatible timestamp
  and explicit stream copying, removing source-file timestamp drift from repeated builds.

## 0.28.0 — 2026-07-22

- Added `OpcPackagePatchBuilder` and a deterministic reversible OPC entry-payload patch
  model. Add, replace and delete operations bind exact base/result package fingerprints,
  content types, byte lengths and before/after hashes; deduplicated payloads support an
  exact guarded reverse without the original files. The guarantee is explicit: entry
  names and uncompressed payloads are exact, while ZIP container metadata and compression
  layout are deterministic serializer output rather than byte-identical source records.
- Added the strict `.wtpatch` codec. Its canonical manifest and content-addressed payload
  archive rejects duplicate/unknown/missing fields, unsafe or duplicate ZIP names,
  noncanonical operation order, ID/hash/length/count drift, unreferenced payloads,
  excessive expansion and compression bombs. Read never extracts archive entries to the
  filesystem; create writes a sibling temporary file, flushes and rereads it, then moves
  only to a new path and never overwrites.
- Added semantic package-patch planning and risk analysis. Plans recompute the native
  semantic diff, detect OPC signatures, VBA/macro, OLE/embedded package, ActiveX/control,
  external relationship, opaque binary, custom XML, infrastructure and newly introduced
  structural changes. Signature invalidation, active content, external relationships,
  opaque binaries and new errors have independent false-by-default authorizations; no
  blanket force flag exists.
- Added five lazy token-lean actions: `plan_ooxml_patch`, `create_ooxml_patch`,
  `inspect_ooxml_patch`, `plan_ooxml_patch_apply` and `apply_ooxml_patch`. Apply requires
  exact base, patch and path-bound deterministic apply-plan identities, rematerializes the
  candidate, enforces package-main-type compatibility with the in-place destination
  extension, compares baseline/candidate Microsoft Open XML SDK results, writes atomically
  and keeps a recovery backup by default. Validation truncation, inability to open the
  candidate or a result-type/extension mismatch is non-overridable. No action opens Word,
  returns payload bytes/raw XML or stores a server-side document cache.
- Added adversarial codec, inverse, corpus, signature, macro, OLE, ActiveX, external-link,
  opaque-binary, custom-XML, inherited/new-error, stale-plan, artifact-tamper, no-overwrite,
  path-bound approval, package-type, atomic-backup and no-Word-host tests. The native
  checkpoint passes 221 engine tests and 85 host tests.

## 0.27.0 — 2026-07-22

- Added native `WordSemanticDiffEngine`, a bounded two-layer comparison of saved Word
  packages. It separates exact OPC entry drift from source-linked semantic added,
  removed, moved, text, declared-property, structure and unmodeled-markup differences
  across the main body, headers, footers, notes, comments, glossary entries and text
  boxes.
- Matching prefers document/story roles, exact semantic IDs, unique durable anchors and
  unique exact subtrees, then uses bounded contextual sibling alignment. Duplicate
  identities, near-tied candidates and alignment-budget fallbacks remain explicit;
  insertion-driven index shifts are not mislabeled as moves.
- Added lazy `compare_ooxml_semantics` with compact summary-first output, exact package
  fingerprint preconditions, filters and paging. Document text and property values are
  redacted by default; text previews, source paths and hashes are independent bounded
  opt-ins. The action never opens Word, returns raw XML or mutates either package.
- Added node, change, diagnostic, alignment, processed-text and captured-text budgets,
  deterministic diff/change IDs, cancellation, option-policy tests, adversarial
  ambiguity tests and no-op coverage across the bundled multi-producer DOCX corpus.
- The native checkpoint passes 192 engine tests and 78 host tests.

## 0.26.0 — 2026-07-22

- Added `WordReviewMutationPlanner`, a typed lossless saved-package transaction for
  accepting or rejecting bounded tracked-revision selections. It handles insertion,
  deletion and conflict wrappers, complete move pairs, run/paragraph/table/row/cell/
  section/numbering and `tblPrEx` property snapshots, numbering-change acceptance,
  inserted rows, cell-insertion acceptance and cell-deletion rejection while preserving
  every unrelated byte and retaining an exact guarded inverse. These cell decisions
  remove only safe markers; grid-blind
  cell reconstruction, vertical-merge restoration and fake `numberingChange` rejection
  are deliberately blocked.
- Added fail-closed dependency planning for nested revisions and named moves. Cascading
  is explicit; conflicting decisions, deleted paragraph-mark merges, table-grid
  reconstruction, custom XML and unsupported structural combinations remain blocked
  instead of being guessed into a corrupt document.
- Added lazy `plan_ooxml_review_decisions` and `apply_ooxml_review_decisions`. Selection
  uses stable revision IDs or redacted author fingerprints, or deliberate
  `select_all=true`; an empty implicit all-selection is forbidden. Apply rebuilds the
  deterministic plan under the original package fingerprint and exact plan ID, rejects
  signed packages, persists atomically and keeps a recovery backup by default.
- Candidate review packages are reparsed and compared against the baseline with the
  Microsoft Open XML SDK validator. Existing unrelated errors remain visible, while any
  newly introduced error blocks apply. Responses omit author names, document text and
  raw XML. Runtime bounds count every selector item, including duplicates, instead of
  trusting a caller to enforce the published JSON schema.
- Added shared hash-preconditioned package transaction primitives and lossless element
  remove, unwrap, local-name rename and replacement patches. Engine coverage includes
  UTF-preserving inverse tests, adversarial nesting and real Word/Pandoc/Apache POI
  tracked-change and move fixtures; native coverage includes plan/apply, redaction,
  selection safety and baseline-aware validation.
- The native checkpoint passes 172 engine tests and 73 host tests.

## 0.25.0 — 2026-07-22

- Added `WordReviewGraphBuilder`, a bounded, source-linked read graph for saved-package
  comments, story anchors, threaded replies, people, tracked revisions, named moves,
  permission ranges and review settings. It joins modern `commentsExtended`,
  `commentsIds`, `commentsExtensible` and `people` parts without flattening them into
  anonymous text.
- Added stable review identities, durable comment IDs, reply/root links, resolved state,
  reaction inventory, revision nesting and status, property/cell/custom-XML revision
  kinds, source/destination move pairs, editor/group permissions and explicit corruption
  diagnostics. Independent limits cap parts, comments, anchors, revisions, ranges,
  people, text, thread depth and issues.
- Added lazy `inspect_ooxml_review` with summary, comments, anchors, threads, revisions,
  move-range, move, permission, people, settings and issue views. Text and personal
  values are fingerprinted/redacted by default, bounded previews require explicit
  sensitive opt-in, source metadata is optional, raw XML is never returned and the path
  cannot open or mutate Word.
- Added four engine tests and four native host tests. The current native totals are 154
  engine tests and 67 host tests; end-to-end corpus smoke covers WordToolkit, Mammoth,
  Pandoc and Apache POI comments, tracked changes and moves with compact default MCP
  responses.

## 0.24.0 — 2026-07-21

- Added `WordEquationGraphBuilder`, a bounded source-linked canonical read graph for
  native OfficeMath in saved Word packages. It models all 19 standard OMML object
  families, argument roles, matrix rows/cells, math runs/text, display paragraphs,
  main math defaults, Strict markup, Word story boundaries, deleted content and
  preserved extension or unknown nodes.
- Added stable equation and math-node IDs derived from semantic source identities,
  normalized typed properties, and bounded diagnostics for missing or duplicate
  arguments, child order, matrix shape, invalid property values, nested equations,
  invalid placement, empty math, adjacent objects Word merges and unsupported
  extensions.
- Added lazy `inspect_ooxml_equations` with compact summary, equation, flat-node,
  paragraph, settings and issue views. Default responses expose counts and short
  fingerprints only; raw OMML is never returned, text previews require explicit
  sensitive opt-in, and the parse-only path neither starts Word nor converts or
  follows external content.
- Added nine engine tests and six native host/corpus cases, bringing the native
  document-engine totals to 150 engine tests and 63 host tests. The test corpus covers
  every standard OMML object family, malformed input, Strict markup, source-ID
  stability, safety limits, redaction, token-bounded paging, 23 tracked native
  equations and Microsoft Open XML SDK validation of all three tracked equation
  documents.

## 0.23.0 — 2026-07-21

- Added `WordReferenceGraphBuilder`, a bounded source-linked read graph for
  fields, bookmarks and dependencies. It separates Word stories, pairs
  bookmark starts/ends across paragraphs, preserves table-column ranges,
  resolves names case-insensitively with duplicate-last provenance, parses
  nested complex fields and recursive `w:fldSimple`, and keeps parent/child
  field identities plus source ordinals.
- Added a bounded field-instruction tokenizer and broad field-family
  classification. Explicit and implicit REF, PAGEREF, NOTEREF, TOC bookmark
  restrictions, HYPERLINK anchors, SEQ, variables, merge fields, citations,
  index entries, styles and external-resource fields now produce typed
  dependency edges. Malformed quotes, orphan/missing field characters,
  excessive switches, deleted instruction text and unresolved targets remain
  diagnostics instead of guessed results.
- Added lazy `inspect_ooxml_references`. Its default summary returns only
  field-type counts and bounded diagnostics; bookmark names, instructions,
  cached result text and dependency keys are redacted unless explicitly
  requested. DDE/LINK/INCLUDE/IMPORT/DATABASE targets are never followed and
  no field is evaluated or allowed to launch an application.
- Extended the semantic projector with source-linked `bookmark_end` nodes and
  marker properties, so both endpoints can be addressed without raw XML.
- Added nine engine tests and two native integration tests, bringing the native
  document-engine totals to 141 engine tests and 57 host tests. Coverage
  includes every bundled DOCX fixture, cross-paragraph ranges, nested and
  simple fields, malformed structures, story isolation, external-field safety,
  privacy redaction and a sub-5000-character default response budget on a
  field-heavy TOC fixture.

## 0.22.0 — 2026-07-21

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
- Extended the projection across Word's primary text-bearing stories: headers,
  footers, footnotes, endnotes, comments, glossary building blocks and text
  boxes. Story roots, note/comment bodies and their references carry source
  part provenance and durable relationship or Word IDs; the existing main-body
  node IDs do not drift when a related story changes.
- Added a typed section graph and lazy `inspect_ooxml_sections` action. It
  resolves section boundaries, page geometry and all six header/footer slots,
  distinguishing explicit, inherited, blank and display-fallback bindings under
  `titlePg` and `evenAndOddHeaders`, and reports unbound story parts.
- Added a typed, source-linked style graph and lazy `inspect_ooxml_styles`
  action. It discovers transitional and strict styles relationships, validates
  the styles part and optional styles-with-effects part, models document
  defaults, latent-style exceptions, four style types, UI metadata and common
  paragraph/run properties, and reports missing, mismatched or circular
  `basedOn` chains without discarding the remaining style inventory.
- Added `WordEffectiveFormattingResolver` and lazy
  `resolve_ooxml_formatting`. One paragraph or run is rebound to its exact XML
  element and resolved through document defaults, base-first paragraph and
  character style chains, effective numbering levels, and direct formatting,
  with per-property provenance and standard toggle-property state transitions.
  The result explicitly lists unresolved application defaults, conditional
  table styles, revision views, unmodeled elements and Microsoft's documented
  compatibility divergences instead of claiming false visual certainty.
- Added a typed, source-linked numbering graph and lazy
  `inspect_ooxml_numbering` action. It discovers transitional and strict
  numbering relationships, validates the dedicated part, inventories picture
  bullets, abstract definitions, instances, levels and overrides, resolves
  `numStyleLink`/`styleLink` indirection and `startOverride`, and reports
  missing, circular, mismatched, recursive or out-of-range references without
  flattening the surviving graph. Level paragraph/run properties now enter the
  Word-specific formatting hierarchy after paragraph styles and before
  character styles and direct formatting.
- Added a typed, source-linked Office theme graph and lazy
  `inspect_ooxml_theme` action. It validates transitional and strict theme
  relationships, the theme content type and DrawingML root; models all twelve
  color slots, system-color fallbacks, major/minor primary and supplemental
  fonts, format-scheme inventories, unknown markup and bounded diagnostics; and
  exposes metadata-first paged color/font/format views without opening Word.
- Integrated theme values into effective formatting. Word font tokens such as
  `minorHAnsi` now resolve to concrete typefaces and theme colors resolve to
  derived RGB properties with theme-part provenance. Composite `rFonts`, color,
  underline and shading declarations now correctly cut off stale inherited
  attributes when a later style or direct-formatting element replaces them.
  `themeFontLang` now selects an explicit or likely ISO 15924 script for
  supplemental theme fonts, including region-sensitive Chinese and Punjabi
  behavior. Unmappable language, environmental colors, unsupported DrawingML
  transforms, and the measured difference between deterministic HSL math and
  Word's private color quantization stay explicit instead of being hidden behind
  fabricated values.
- Added a typed, source-linked settings graph and lazy
  `inspect_ooxml_settings` action. It models view/zoom defaults, theme font
  languages, compatibility settings and derived compatibility mode, legacy
  compatibility switches, document and write protection metadata, document
  variables, attached templates, mail-merge references and bounded root
  inventory. Sensitive values are redacted by default; protection hashes and
  salts never leave the engine.
- Added a typed, source-linked font-table graph and lazy `inspect_ooxml_fonts`
  action. It models declared font classification, PANOSE/signatures and all four
  embedded faces, validates exact font relationships and Word-readable content
  types, diagnoses duplicate names and orphan relationships, and exposes bounded
  metadata without returning font bytes. Effective formatting now cross-references
  concrete theme fonts against this graph and records declared/embedded/readable
  provenance.
- Added the first lossless XML source model with exact byte spans, original
  prefixes/attributes/quotes/BOM retention, bounded secure parsing, guarded
  non-overlapping splices, and validated UTF-8/UTF-16/UTF-32/single-byte edits.
- Added the first typed semantic mutation: hash- and fingerprint-preconditioned
  replacement of source-bound Word or OfficeMath text leaves, including XML
  escaping, `xml:space` handling and fail-closed mixed-markup behavior.
- Added bounded multi-text transaction planning. A plan parses each affected
  part once, rejects duplicate targets, predicts the result package fingerprint,
  creates one isolated forward mutation and retains an exact part-byte inverse
  guarded by the applied fingerprint. Compact plan metadata omits text content
  and per-text hashes.
- Added streaming semantic selectors and the lazy `query_ooxml_semantics`
  native action. Queries filter node kinds, exact properties, source parts and
  semantic subtrees; text predicates can cross run boundaries without
  flattening the document and results remain paged and preview-bounded.
- Added stateless lazy `plan_ooxml_text_edits` and
  `apply_ooxml_text_edits` actions. Apply must reproduce the reviewed plan ID
  and base fingerprint, writes through the atomic package transaction, retains
  a recovery backup by default, performs no write for a no-op, and fails closed
  on digitally signed packages.
- Added the lazy, token-bounded `inspect_ooxml_semantics` MCP action.
- Added 132 document-engine tests, including deterministic malformed-input,
  randomized relationship metadata and opaque-part round-trip fuzz smoke, plus
  300 randomized lexical text splices, multi-encoding/BOM preservation and
  semantic mutation provenance. Theme/settings/font tests cover strict and
  transitional packages, all twelve color slots, language/script font resolution,
  embedded faces, privacy boundaries, HSL tint/shade behavior, composite
  overrides, provenance, malformed/limited parts, and every matching part found
  in the bundled corpus from Word, LibreOffice, Pandoc, POI and Mammoth.
  The same corpus exercises every typed XML part and exact no-op source
  preservation, while native end-to-end package/semantic inspection retains
  all existing runtime tests.
- The native host now has 55 tests, including end-to-end semantic query,
  cross-story header query/apply, schema parity, recovery-backup, plan-mismatch,
  signed-package and no-op coverage plus theme/settings/font inspection and
  resolution that proves the saved-package path never invokes Word COM.
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
