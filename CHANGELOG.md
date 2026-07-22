# Changelog

## Unreleased

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
