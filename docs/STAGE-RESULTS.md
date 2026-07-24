# Stage results

## Public destination-bound saved-package rollback — 2026-07-24

- Added `plan_ooxml_patch_rollback` and `apply_ooxml_patch_rollback`. The caller supplies
  the exact current package fingerprint and original artifact `patch_id`; the runtime
  verifies that the package is still the original result, derives `Reverse()` internally
  and returns a distinct destination-bound `wtrollback_` plan ID.
- Apply rebuilds the complete reverse candidate, requires the same fingerprint, source
  patch ID and rollback-plan ID, re-runs semantic/risk/package-type/Open XML validation,
  enforces separate signature, active-content, external-relationship, opaque-binary and
  new-error authorizations, then uses the existing atomic writer. The default backup
  contains the pre-rollback state and can support an explicit redo decision.
- The response exposes filenames, IDs, fingerprints, counts and bounded evidence only;
  no patch payload, raw XML or Word process crosses the action boundary. Exact no-op
  rollback performs no write and creates no backup.
- Eleven focused service regressions now cover forward patch behavior plus exact rollback,
  backup contents, stale result state, plan/path binding, macro authorization, no-op
  behavior, privacy and closed versioned contracts. The full checkpoint passes 504
  Engine tests, 394 Native tests and 1309 Python tests with 16 intentional skips; both
  changed .NET projects pass `dotnet format --verify-no-changes`, and both new output
  schemas pass Draft 2020-12 meta-validation.
- Installed runtime `0.39.0+codex.20260724161042` is enabled and reports 109 actions, 15
  exposed tools and 22 explicit metadata contracts. Build output, personal marketplace
  source and enabled cache each contain the same 196 files and 87,093,417 bytes with zero
  path/length/hash differences. A second same-checkout output tree also has zero
  differences and its 36,739,798-byte ZIP is byte-identical at SHA-256
  `77b8216c597e2462aafbffdaf0f60110841b171385ba35c0b505cdd4fc298609`.
  This proves reproducibility within the pinned host/toolchain and checkout; a second
  clean checkout remains a separate release gate.

## Independent recovery with semantic rollback proof — 2026-07-24

- Failed `apply_live_word_operations` publication no longer ends with Word's Undo as the
  only recovery mechanism. The staged batch retains the baseline Flat OPC in process and,
  when Undo fails or cannot prove restoration, opens it as a separate hidden, read-only
  Word document and copies the baseline main story back into the target with one
  cross-document `Range.FormattedText` assignment.
- Recovery equivalence now combines exact content/target/context boundaries and hashes,
  save state and structural counts with a namespace-aware whole-document semantic Flat OPC
  hash. It ignores only WordprocessingML attributes whose local name starts with `rsid`.
  Two consecutive semantic reads must match; four unstable reads return
  `ROLLBACK_SNAPSHOT_UNSTABLE`. `Document.Saved` is restored only after the rest of the
  checkpoint matches and is then verified again.
- A fake-COM fault-injection test forces `Undo=false`, performs the independent restoration
  and proves clean text, zero equations, unchanged `live_version=0` and a reusable handle.
  The failure remains the original `EQUATION_INVALID` because complete recovery was proved.
  Existing false, throwing, no-op, partial and visible-only Undo cases still fail closed
  when the independent path fails or leaves state drift.
- A forced real Word 16.0 test starts with native OMath, contaminates the target without
  Undo and performs the same Flat OPC recovery. Main-story text, paragraph count and OMath
  count return exactly to baseline, but the semantic whole-document Flat OPC hash still
  differs beyond `w:rsid*`. That document remains quarantined. The result proves useful
  cleanup, not package-exact recovery, and prevents a clean-looking document from being
  mislabeled as transactionally restored.
- Current gates pass **504 Engine**, **389 Native** and **1,309 Python tests**, with **16
  intentional Python skips**; Ruff and both modified C# project format verifiers pass.
  Full package-exact restoration or a sanctioned live document-identity swap remains the
  open boundary.
- Enabled runtime `0.39.0+codex.20260724154131` reports **107 actions**, **15 exposed
  tools** and **20 explicit metadata contracts**. Build, personal marketplace source and
  enabled cache contain the same **196 files** and **87,050,540 bytes**, with zero path,
  length or SHA-256 differences. The **36,731,827-byte** ZIP SHA-256 is
  `88fe233ed24107866d757677f809f80732427acf140859576dec0f3f74642c55`;
  executable, native runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `7239581ad34ecde54a192f70339d672e200507574a6023e450aa9ebb9784f841`,
  `318bbbbf411f979a4b308f0dc24ec003be324f041b83d0db2a4e2915296b1f9e`,
  `4e37936da2c499d7ebd79dbf94b572ce2509e0786f703f7abdeaed997ca7f09d`
  and `bdab24fb5ec8a5b0eae51cae2b3cf85258e363cd8a5f7d033dd1e47c54000068`.

## Complete staged live-batch publication — 2026-07-24

- Replaced equation-only staging with a full Flat OPC clone of the current target. Text,
  paragraph boundaries, styles, all supported formatting properties and native OMath are
  built and verified in that isolated Word document before the target is touched.
- The target receives the candidate through one cross-document `Range.FormattedText`
  assignment. Before `live_version` advances, readback proves exact published length and
  text fingerprint, operation ranges, requested formatting, global/range-local equation
  counts, display type, scoped equation styles and optional semantic equation contracts.
- The clone opens with macros and link updates disabled, must close without saving and its
  temporary Flat OPC file must be deleted. Full target Flat OPC/text/structure is checked
  before publication. Hidden drift during staging returns `ROLLBACK_FAILED`, quarantines
  the identity and never risks undoing unrelated user history.
- Fourteen focused tests cover exact/partial/visible-only/false/throwing Undo, failed
  custom-record closure, isolated equation rejection, success through exactly one target
  assignment with zero target-side OMath builds, rejection before target mutation,
  failed-open artifact deletion and hidden target drift during staging. The full gates
  pass **504 Engine**, **389 Native**
  and **1,309 Python tests**, with **16 intentional Python skips**; Ruff passes.
- Real Word 16.0 published two independently formatted paragraphs and the eight-line
  complex integral derivation in one staged batch. Post-publication semantic readback
  proved all six native integrals, all six differentials and their lower-baseline
  placement. This is direct Word evidence, not only fake-COM evidence.
- Installed runtime `0.39.0+codex.20260724154131` reports 107 actions, 15 exposed tools
  and 20 explicit metadata contracts. Build, personal marketplace source and enabled
  cache are identical at 196 files and 87,050,540 bytes with zero path, length or hash
  differences. The 36,731,827-byte ZIP SHA-256 is
  `88fe233ed24107866d757677f809f80732427acf140859576dec0f3f74642c55`.
- One boundary remains open: independent reconstruction is attempted after a partially
  applied `FormattedText` publication, but real Word can normalize the recovered package
  beyond the accepted `w:rsid*` session metadata. Without complete semantic equality,
  WordToolkit reports `ROLLBACK_FAILED` and quarantines that state; it does not call the
  document clean or allow the agent to continue.

## Operation-wide verified Word rollback — 2026-07-24

- Removed the legacy rollback helper that closed a custom record, called `Undo(1)` once
  and swallowed both failures. All current native custom-Undo mutation families now use
  one fail-closed verifier and quarantine the live handle/document identity when exact
  restoration cannot be proved.
- The checkpoint covers whole-document Flat OPC, main-story text/OOXML, every accessible
  linked story range, exact target/context ranges, save state and structural counts.
  SmartArt text and review properties add supplemental state fingerprints because Word's
  custom Undo does not reliably cover those COM states.
- Ten adversarial rollback tests include visible-state-only restoration that leaves hidden
  OOXML residue, supplemental-state drift and a reflection contract proving that the old
  silent `Rollback` entry point cannot return. The complete native suite passes 382/382.
- A forced real Word 16.0 acceptance probe inserted one line and called the shared failure
  path. Word returned `true` from `Undo(1)` and restored the visible text, paragraph count
  and equation count, but whole-document Flat OPC, range OOXML and the story graph still
  differed. The runtime returned `ROLLBACK_FAILED` and quarantined the identity. This is
  direct evidence that a successful Boolean Undo result is not transaction proof.
- This closes the silent-rollback lie, not live publication atomicity. Complete
  heterogeneous staging and target publication recoverable without Word Undo remain the
  next P0 boundary.
- Full gates pass **504 Engine**, **382 Native** and **1,309 Python tests**, with **16
  intentional Python skips**; Ruff and all four C# format verifiers pass. Installed
  runtime `0.39.0+codex.20260724142108` reports 107 actions and 15 exposed tools. Build,
  marketplace source and enabled cache are identical at 196 files and 86,989,407 bytes.
  The 36,718,529-byte ZIP SHA-256 is
  `4df5585249c0e5cfbad23b0273ce97170fdc2598da34dda7d0ff09e01be3ede0`.

## Verified mixed-operation rollback and equation staging — 2026-07-24

- Replaced the false `Undo(1)` success assumption in the shared mixed text/equation
  mutation path with an exact pre-mutation checkpoint and mandatory post-Undo proof.
  The checkpoint covers live version, Word save state, main-story text, target/context
  WordOpenXML, range boundaries and paragraph/equation/table/field/bookmark/shape/comment/
  note/section counts. A false or throwing Undo, failed custom-record closure, unreadable
  state or any mismatch now returns `ROLLBACK_FAILED` and quarantines both the handle and
  document identity instead of exposing a reusable version-zero lie.
- Every equation in a batch is built, styled and read back in an unsaved hidden Word
  staging document before the target is touched. A staging failure discards that document
  and preserves target content, counts, live version and handle. Publication still uses
  verified rollback because target-side Word normalization can fail independently.
- Seven adversarial fake-COM regressions cover exact rollback, residue after a no-op or
  partial Undo, false/throwing Undo, failed custom-record closure, handle invalidation,
  reconnect blocking, explicit quarantine release and isolated staging rejection. Full
  release gates pass **504 Engine**, **378 Native** and **1,309 Python tests**, with **16
  intentional Python skips**; Ruff lint and all four C# format verifiers pass.
- A real Word 16.0 failure probe proved the original defect rather than merely simulating
  it: Word returned `false` from `Undo(1)` after a post-payload validation failure and the
  target changed from one empty paragraph to 33 paragraphs. Runtime
  `0.39.0+codex.20260724131918` returned `ROLLBACK_FAILED`, reported the structural/hash
  mismatch names, invalidated the handle and blocked inspection until explicit quarantine
  release. The contaminated unsaved document was deliberately neither saved nor closed.
- The final installed `0.39.0+codex.20260724132826` runtime then staged and published the
  full eight-line complex derivation of `int x^3 e^(2x) sin(3x) dx`. Native and semantic
  readback both passed; six n-ary integrals and six correctly placed differentials were
  counted, and expected/actual contract SHA-256 values were identical at
  `d736af3f7abfdce5d381bf23d5bd2d5ececeb8bac6cd8cf1d001f41a8dd8c708`.
  The saved package contains two paragraphs and one native OMath object; Microsoft Open
  XML SDK validation reports zero errors.
- The exact 14,332-byte DOCX proof has SHA-256
  `3206be53194410220de5afb4304bb280a16fdf80320fb59f01d6e94ee7de64f7`.
  Word's one-page 61,013-byte PDF has SHA-256
  `020f9ece9711fbe40bacdf6bc8b544a3c9019e959e4bbf48452c73c13860ee2b`.
  Its 180-DPI page was inspected directly: all eight aligned lines are legible, integral
  differentials stay on the baseline, and there is no raw `eqarray(...)`, clipping,
  overlap or broken glyph.
- Build output, personal source and enabled cache contain the same **196 files** and
  **86,962,059 bytes**, with zero path/length/hash differences. Installed discovery
  reports **107 actions**, **15 exposed tools** and **20 explicit metadata contracts**.
  The 36,709,200-byte ZIP SHA-256 is
  `bf6b236f7f9191559d4cb3a16ff49b8013d2daf527187a728dbb202c6861ebfc`;
  executable, runtime assembly, Engine assembly and SDK adapter SHA-256 values are
  `4ade449b2ef2dc6fb44a649c120e6866574a68586019b57b939c802df1ee8a42`,
  `e30f0ae103c0d84f7106a8b362f6b18f24d801a4f6ed2aba61360528f30b8174`,
  `b653e2e59d9bed94016ed2787fc9d64b7f9f0a55ed2776df9bcb852ccc37080c`
  and `e4d0fbe83f8d66105d84417705142da82fa3db64ec2027b98957aa9e0c6fdbe8`.

## Native index entries and indexes — 2026-07-24

- Added guarded native `XE` marking and `INDEX` insertion as actions 106 and 107.
  High-level inputs cover up to eight hierarchy levels, cross-references, an existing
  bookmark page range, page-number emphasis, heading separators, indented/run-in layout,
  zero-to-four columns, accented-letter grouping and six tab leaders. Neither action
  accepts raw field instructions or returns entry/bookmark/cross-reference/generated
  index text.
- Both actions require the current live version and one fresh target token where needed,
  run in one non-replayable custom Undo record, verify exact native collection/field
  deltas and option readback, and roll back on mismatch. Reference-table contract 1.1 now
  updates native indexes alongside contents, figures and authorities.
- Saved-package reference and dependency graphs now resolve complete non-deleted `XE`
  entries to concrete complete `INDEX` field nodes. Real-Word, package, PDF and release
  evidence for this slice is recorded only after those gates complete.

## Native authority citations and table of authorities — 2026-07-24

- Added `wordtoolkit.mark_live_word_authority_citation/1.0` and
  `wordtoolkit.insert_live_word_table_of_authorities/1.1` as native actions 104 and 105.
  Marking requires one fresh non-empty range/selection token and creates one exact native
  `TA` field in category 1–16. Insertion accepts one category or category 0 for all,
  requires matching native marks and creates one editable native `TOA` through Word.
- Both mutations require the current live version, use one non-replayable custom Undo
  record, verify exact native field/collection deltas and roll back on mismatch. TOA
  insertion additionally reads back all separators, `Passim`, entry formatting,
  category-header and tab-leader settings. The default is a real tab with dotted leaders;
  citation text, separator values, generated table text, field instructions and COM
  objects never cross the response boundary.
- Real Word exposed two defects before release. An empty `IncludeSequenceName` produced
  false `0-N` page ranges, so the action now passes `Type.Missing`. An empty entry
  separator crushed entries against page numbers, so the default is one tab plus the
  semantic `dots` leader. Exact option readback makes both fixes fail closed.
- Saved-package reference analysis now completes all field parsing before cross-field
  resolution. Complete category-compatible `TA` entries resolve to concrete complete
  `TOA` fields; a category-zero table matches every valid authority category. The unified
  dependency graph emits three resolved `field_reference` edges instead of false
  missing-target warnings.
- Full local gates pass **500 Engine**, **362 Native** and **1,309 Python tests** with
  **16 intentional skips**. Ruff lint and all four C# format verifiers pass. The broader
  Ruff format check still reports 14 pre-existing Python files that this slice did not
  touch; they were not mechanically rewritten into an unrelated release change.
- The exact installed `0.39.0+codex.20260724113419` runtime marked three citations and
  inserted one all-category authority table in Word 16.0. Microsoft Open XML SDK
  validation returned zero errors. Independent inspection found four complete fields
  (`TA` x3, `TOA` x1), three resolved reference dependencies, a 158-node/239-edge fully
  resolved dependency graph and zero issues.
- The exact 14,399-byte DOCX has SHA-256
  `28515ac5afbbffd489bae3e6ed62e68b6c7c38d33230b50d52ed28db0a4e3562`.
  Its three-page A4 Word PDF is 44,174 bytes at SHA-256
  `a824863c425a598dc79be2ec764f34e31a9e122ca1eb24ae2bb7f5b4f6a9d82b`.
  Every page was inspected at 144 DPI: `Brown v. Board of Education` displays page 2 and
  `Forrester v. Craddock` displays pages 1, 3 with clean dotted leaders, no clipping,
  overlap, glyph boxes, raw field syntax or false `0-N` prefix.
- Build output, personal source and enabled cache each contain the same **196 files** and
  **86,856,508 bytes**, with zero path/length/hash differences and zero Python files. The
  self-contained 36,685,680-byte ZIP has SHA-256
  `c90c7dcf4b8ee3f2a341ddb2e2254e7fc3b942be040c65b8732744699e50460d`.

## Native table-of-contents insertion — 2026-07-24

- Added `wordtoolkit.insert_live_word_table_of_contents/1.0` as native action 103.
  It inserts at the document start/end or a fresh collapsed cursor, accepts semantic
  heading levels and heading/outline source flags, optionally repaginates and updates,
  and never accepts raw field instructions.
- One non-replayable custom Undo record contains native `TablesOfContents.Add` plus the
  optional repagination/update. Success requires a one-object collection delta, a unique
  exact range reacquisition, a non-empty range and at least one field. Any mismatch
  requests one rollback; generated contents text, field code and COM objects are absent
  from the response.
- Full local gates pass **498 Engine**, **355 Native** and **1,309 Python tests** with
  **16 intentional skips**. Ruff and both C# format verifiers pass. Exact installed
  discovery reports **103 actions**, **15 exposed tools** and **16 explicit metadata
  contracts**.
- The final installed `0.39.0+codex.20260724102603` runtime created a disposable Word
  document with five Heading 1/2 entries over three pages, inserted the contents table
  at position zero, repaginated, updated, saved, validated and exported it. Microsoft
  Open XML SDK was available and returned zero errors.
- Independent inspection of the exact 14,992-byte unlocked copy found **one complete
  `TOC` and five complete `PAGEREF` fields**, five resolved dependencies and zero issues,
  incomplete/external/application-invoking fields. Its SHA-256 is
  `718627cd5f91b126aced63f5f1cc3890cc15fefa3fb9cd99567a4c2d63ff0982`.
- The three-page A4 Word PDF is 40,742 bytes at SHA-256
  `bafdd436de71c37e8cc481948b16fed4b839818e9383907d9d10e94af472f221`.
  Every 150-DPI page was inspected: the two-level contents table has legible leaders and
  page numbers 1–3, with no clipping, overlap, black glyph boxes or raw field syntax.
- Build output, personal source and enabled cache each contain the same **196 files** and
  **86,798,454 bytes**, with zero path/length/hash differences. The self-contained
  36,669,666-byte ZIP has SHA-256
  `6a5761da11cd6cf7769b9e669d636474a55053b5799887760d6912570604190d`.

## Guarded update of native reference tables — 2026-07-24

- Added `wordtoolkit.update_live_word_reference_tables/1.0` as native action 102. One
  request selects all native contents/figures/authorities tables or one exact kind and
  one-based index, rejects more than 128 objects, repaginates by default and performs the
  native full `Update` inside one custom Undo record. It requires the current live
  version, rechecks all three collection counts and every resulting field range, rolls
  back on mismatch and returns no generated table text, raw field instruction or COM
  object.
- Five new fake-COM regressions cover all-kind success and response privacy, exact-index
  selection without repagination, zero targets, the 128-object ceiling and full rollback
  after invalid post-update readback. A separate Engine regression fixes valid
  Word-generated `TA \l ... \s ... \c ...` fields so the long citation creates the
  typed index-entry edge without a false `FIELD_TARGET_MISSING` warning.
- Full local gates pass **498 Engine tests**, **351 Native tests** and **1,309 Python
  tests** with **16 intentional skips**. Ruff and C# formatting checks pass. Capability
  discovery reports **102 actions**, **15 exposed MCP tools** and **15 explicit metadata
  contracts**.
- The final installed `0.39.0+codex.20260724100603` runtime updated one
  `TablesOfContents`, one `TablesOfFigures` and one `TablesOfAuthorities` object together
  in Word 16.0. Counts before/after stayed 1/1/1, repagination and range/field readback
  succeeded, and Microsoft Open XML SDK validation returned zero errors.
- Independent saved-package inspection found **15 complete native fields**: two `TOC`,
  nine nested `PAGEREF`, two `SEQ`, one `TA` and one `TOA`; issue, external-field and
  application-invoking-field counts are all zero. The 16,891-byte DOCX has SHA-256
  `d8e2bb820e821bcd77bbd9d800e786749260316face98259a51fc05aa5f83263`.
- Word exported a four-page, 80,453-byte PDF at SHA-256
  `8bef6305acb5d5d4d51d8fb2e5b1381521ff86c38d4109a453ce0afb062ee141`.
  All four 150-DPI page rasters were inspected: the contents table, table list,
  authorities entry, captions, leaders and page numbers are present and legible, with no
  clipping, overlap, black glyph boxes or broken page margins.
- Build output, personal source and enabled cache each contain the same **196 files** and
  **86,772,501 bytes**, with zero path/length/hash differences. The self-contained
  36,663,305-byte ZIP has SHA-256
  `8a8fcdaf462a07f6bf5de1f4a2512d70ba522a002b822e5686a9813cf6ef9466`.

## Native captions and table of figures — 2026-07-24

- Added `wordtoolkit.insert_live_word_caption/1.0` and
  `wordtoolkit.insert_live_word_table_of_figures/1.0` as native actions 100 and 101.
  Both require the current live version, reject raw field code, resolve built-in labels
  through Word, use one custom Undo record, verify native field/collection counts and
  request one bounded rollback on failed verification. Custom labels must already exist.
- Seven focused fake-COM tests cover closed versioned contracts, localized and custom
  captions, a bounded custom-label scan, non-disclosure of caption title text, rollback
  after a bad field delta, successful table-of-figures update and rejection when
  matching captions are absent. Full local gates pass **497 Engine tests**, **346 Native
  tests** and **1,309 Python tests** with
  **16 intentional skips**. Ruff and C# formatting checks pass.
- The installed `0.39.0+codex.20260724093257` runtime reports **101 actions**, **15
  exposed MCP tools** and **14 explicit metadata contracts**. Build output, personal
  source and enabled cache each contain the same 196 files with zero path/length/hash
  differences.
- Through the installed MCP STDIO boundary, Word 16.0.20131 created two native table
  captions in the label-configured `above` position and one updated table of figures.
  The live version advanced from 0 to 3; Word exposed two matching captions, one
  `TablesOfFigures` object and seven live fields.
- The saved DOCX passes Microsoft Open XML SDK validation with zero errors. Independent
  reference inspection reports two complete `SEQ`, one complete `TOC` and two nested
  `PAGEREF` fields with zero issues. The 14,541-byte DOCX has SHA-256
  `3532d2bd0a8f90badfb021c57a4fd0477981c3853979d7647f40eed92f7eb9c9`.
- Word exported a 44,536-byte one-page PDF at SHA-256
  `c9fdeba24ede315342003cbe512ec8d266044fd61811e225e8bdd03e7945dea0`.
  Its 144-DPI raster shows both captions, both tables, two table-of-figures entries,
  dotted leaders and page numbers without clipping or overlap.
- The self-contained 36,657,762-byte ZIP has SHA-256
  `0f75b9cf43413080b2a9cc4765b52c3fbaf07ce24338d8044c2566c71c43943c`.

## Guarded live SmartArt text — 2026-07-24

- Added `wordtoolkit.prepare_live_word_smartart_text_edits/1.0` and
  `wordtoolkit.apply_live_word_smartart_text_edits/1.0` as native actions 98 and 99.
  Preparation reads a bounded complete root, returns at most 32 nodes and issues
  one-time tokens bound to Word root identity, layout/style/color, structure and every
  node text hash. Apply uses one non-replayable custom Undo record, exact post-write
  readback and automatic rollback. Stable no-ops do not mutate Word state.
- Nine focused drawing/SmartArt tests pass, including real mutation semantics in the COM
  fake, stale-context rejection before Undo, rollback on normalized readback, privacy and
  stable no-op. Full local gates pass **497 Engine tests**, **339 Native tests** and
  **1,309 Python tests** with **16 intentional skips**. Ruff passes.
- The installed `0.39.0+codex.20260724084026` runtime edited one native five-node
  SmartArt through the real MCP STDIO boundary. Live version advanced from 0 to 1,
  structure fingerprint remained unchanged, the exact new text was read back, the file
  saved and Microsoft Open XML SDK validation returned zero errors.
- The 20,482-byte source and 20,512-byte result have SHA-256
  `152df1fc626f24e4900f7a8a748cb5cd1e2638fbed31bd4089050fada8488737` and
  `4b2a39bc136582fbc615c45ff23bcdf84c188dfa9c86ab6d002fa0e3c8a8388f`.
  Word updated both DiagramML `data1.xml` and persisted `drawing1.xml`; both contain the
  new text exactly once and the old text zero times after save.
- Word exported 23,892-byte before and 23,183-byte after PDFs. Their one-page 144-DPI
  rasters retain the same five box bounds; the new three-line text remains inside its
  original box without clipping or overlap.
- Two pinned SDK builds produced byte-identical **196-file**, **86,690,088-byte** trees
  and **36,645,859-byte** archives at SHA-256
  `bb3ccf021e2135a1ca89c83920fcdd3c7aa73713936ac65db22febbe90096cf4`.
  The personal source and enabled cache have zero path/length/hash differences. Installed
  discovery reports **99 actions**, **15 exposed MCP tools** and **12 explicit metadata
  contracts**.
- General SmartArt authoring is still absent. The new slice does not create/delete/reorder
  nodes, change hierarchy or mutate layout/style/color, and it does not claim a durable
  DiagramML-to-COM node identity or cross-version pixel parity.

## Group-aware safe saved-package formatter — 2026-07-24

- Added `wordtoolkit.plan_ooxml_format/1.0` and
  `wordtoolkit.apply_ooxml_format/1.0` as native actions 96 and 97. The initial
  `remove_redundant_direct_formatting` policy removes fully modeled scalar
  paragraph/run property elements whose direct cascade result equals the preceding
  resolved value. It now also admits `rFonts`, `color`, `u` and paragraph/run `shd`
  only after a bounded candidate-by-candidate package reparse proves complete group
  equivalence. Conditional-table, revision and unmodeled cascade layers remain
  untouched; more than 64 composite proofs aborts the whole plan.
- Every candidate must preserve OPC structure, the exact predicted fingerprint, the set
  of changed parts, projected semantic content and effective formatting on every
  affected paragraph/run; paragraph edits include descendant runs. Baseline-aware Open
  XML SDK validation then gates apply. Signed packages, unavailable/truncated validation,
  new validation errors and changed apply-plan evidence are blocked.
- Plan is read-only. Apply rebuilds the exact output-path-bound plan and atomically
  creates only a new same-extension file. The source and existing destinations cannot be
  overwritten. Stable no-op apply creates no file. Neither operation opens Word or
  returns document text/raw XML.
- Full local gates pass **497 Engine tests**, **334 Native tests** and **1,309
  Python/OOXML tests**, with 16 intentional optional-environment skips. Ruff is clean.
  The native schema exposes 97 actions; ten actions now have explicit operation version,
  permissions, reversibility and output schemas.
- Two pinned .NET SDK 8.0.423 builds produced byte-identical 196-file,
  86,623,437-byte expanded trees and 36,627,205-byte ZIPs. The archive SHA-256 is
  `6bb2fce0a85bf61f03aeab320c68af985061bbcbff02e09b55299872f759a66f`;
  the executable is
  `62b0829110b427af883309a7bac951518a52a7c586b768b87f51fcea4d1aee76`,
  the native runtime assembly is
  `3491726cbeea318bec438d099b8db47c4362e9309f9c034b6ddadaaa7f41f2cd`,
  and the engine assembly is
  `a8444f95d58f76193e31866c27c48074fb2c043e44f86d3b32ce0c747df23756`.
- The personal source and enabled cache at `0.39.0+codex.20260724080018` each contain
  the same 196 files with zero path/length/hash differences. Installed capability
  discovery reports 97 actions and the exact runtime version.
- A cold installed-runtime plan scanned 12 candidates, performed five composite proofs
  and removed exactly 11 elements/330 source bytes from `word/document.xml`. Engine,
  semantic, effective-formatting and baseline-aware Open XML validation all passed,
  and apply produced the exact predicted fingerprint
  `ce2bb1fa46ff438053b9ff4e7c0b498198c9130783e56431dbd57817cfe8e8dc`.
  A second plan was a stable no-op and no-op apply created no file.
- The installed runtime then connected the source and opened the result read-only in
  Microsoft Word. Both snapshots were valid with zero Microsoft Open XML SDK errors and
  exported to one-page 23,821-byte PDFs. Their 144-DPI page PNGs were byte-identical at
  SHA-256 `2a882af2560fb684e55664c647e964ae3eebd98403292eaf07def3463895c966`;
  visual inspection found no clipping, overlap or formatting drift. Word PID 14820 was
  unchanged. This proves one licensed Word equality point, not a broad multi-version
  rendering corpus.

## Word-executed drawing layout — 2026-07-24

- Added `wordtoolkit.inspect_live_word_drawing_layout/1.0` as native action 95. It
  projects bounded layout evidence calculated by the connected Microsoft Word build for
  floating shapes, inline shapes, groups and optional SmartArt nodes. Positions preserve
  their Word reference frame and distinguish alignment constants from point offsets;
  viewport pixels remain a separate explicit opt-in and are never called page geometry.
- The action caps the root scan at 10,000, output at 100, group expansion at 128 members
  and depth 16, SmartArt expansion at 128 nodes and 256 associated shapes, diagnostics at
  100 and opt-in text at 4,096 characters. Text-bearing COM getters are not called when
  `include_text=false`. Raw XML, raw COM objects and external content are never returned.
- Full gates pass **488 Engine tests**, **329 Native tests** and **1,309 Python/OOXML
  tests**, with 16 intentional environment/model skips. Ruff is clean. The PowerShell
  5.1 acceptance scripts parse without errors, the native schema exposes 95 actions and
  `git diff --check` reports no whitespace errors.
- Two pinned .NET SDK 8.0.423 builds produced byte-identical 196-file,
  86,518,137-byte expanded trees and 36,600,389-byte ZIPs. The archive SHA-256 is
  `b7e9476d605e630c370e76a8982f9efac99579b281c9b4048bb2447cfce13952`;
  the executable SHA-256 is
  `b07d2d666cd5cbdc73a3c904ed57c59acea31589f3d9449317f3a62bfe3f589b` and
  the runtime assembly SHA-256 is
  `8511af2b7069add7dd0bdc0e0331500a1e0e88043a60fca75fbac483edf146a0`.
  All trees contain zero Python runtime files.
- The complete 49-action licensed Word gate passed on the packaged runtime in 51.614
  seconds. Capability discovery returned 95 actions; the gate produced a validated DOCX,
  a 166,264-byte PDF, 49 paragraphs, one table, 12 editable equations, one image, one
  comment, one footnote and one endnote, then closed only its own document. The existing
  Word process ID was unchanged before and after the run.
- The personal source and enabled cache at `0.39.0+codex.20260724063719` each contain
  the same 196 files with zero path/length/hash differences from the release tree. A cold
  installed-runtime proof over `lo_groupshape_sdt.docx` returned one floating Word group
  and one group-local child normalized to native `msoAutoShape`, page 1/section 1,
  150.2 by 815.35 points, right-aligned relative to the page, -44.25 points relative to
  the paragraph and in-front-of-text wrapping. The response was exact and untruncated,
  had zero diagnostics and returned no sensitive text, XML or COM objects. The source
  hash remained
  `83c47ec672afd0bce726f90582f40ebe96e10514c1f6da3bfec5bc9507db456c`,
  and the pre-existing Word process remained running.

## Declared DrawingML/VML shape topology and path model — 2026-07-24

- Extended `WordFigureCaptionGraph` instead of adding a competing graph. Shape
  representations now own stable `wdsh_` group/shape/picture/graphic-frame/content-part
  nodes with parent/child topology, DrawingML transforms, recognized preset geometry,
  bounded custom paths and move/line/arc/quadratic/cubic/close commands, formula points,
  fill/line summaries, known effect kinds and text-flow declarations. VML path source is
  reduced to length plus SHA-256 evidence. No formula, effect, text or page layout is
  executed.
- Source limits cap shape nodes at 100,000, paths at 200,000, commands/effects at
  500,000 and formula points at 1,000,000. Individual formulas stop at 256 characters;
  every retained node/path/command/point/effect is charged to the shared `wop1` operation
  lease. Unknown enum-like tokens remain diagnostics rather than trusted values.
- The dependency graph adds typed `FigureShape` nodes plus
  `FigureRepresentationContainsShape` and `FigureShapeContainsShape` edges. The existing
  honest gap remains `drawingml_vml_rendered_geometry_and_layout_execution`: declared
  shape structure is covered, rendered Word geometry is not.
- `inspect_ooxml_figures` remains counter-only by default. Full shape data requires
  `include_shape_details=true`, `view=representations`, `detail=declared` and
  `max_items<=2`; one response is capped at 64 nodes. Path commands/formula points still
  require `include_geometry=true` and stop at 64 paths, 128 commands, 256 points and
  4,096 formula characters. Names/text still require `include_text=true`.
- The Microsoft 365 Open XML SDK validates the group/custom-geometry/effect/text-box
  fixture with zero errors. Hostile tests cover over-limit path points and formulas plus
  untrusted text-wrap and line-cap tokens.
- Full gates pass **488 Engine tests**, **325 Native tests** and **1,309 Python/OOXML
  tests** with 16 intentional environment/model skips. Release builds have zero warnings;
  Ruff is clean and mypy passes all 29 maintained Python files.
- Two pinned .NET SDK 8.0.423 package builds produced byte-identical 196-file,
  86,418,307-byte expanded trees and 36,572,351-byte ZIPs. The canonical archive SHA-256
  is `efdb5c5a78b2284296bc4ee3d8f85afa3f98a5d83c0fdb93b2314160babfb9db`;
  the executable SHA-256 is
  `b870d1d17891e36137d8e3d5aa4998478b0d519639d5bea2dcf426d9f7dce866` and
  the runtime assembly SHA-256 is
  `3e92a7bd9f4f688805cb0d3762049748ee3a6d1a24ea90f8a3577361a16b9ae4`.
  All trees contain zero Python files.
- The personal source and enabled cache at `0.39.0+codex.20260724055428` each contain
  the same 196 files with zero path/length/hash differences from the release tree.
  Installed capability discovery reports that exact version, 94 actions and the updated
  shape-aware `inspect_ooxml_figures` contract.
- The installed runtime inspected real `lo_groupshape_sdt.docx` without opening Word or
  changing the source hash. A 6,598-character complete MCP line returned one shape
  representation, both declared group nodes and no truncation. Installed dependency
  inspection returned 67 nodes and 72 edges, including one representation-to-shape and
  one shape-to-shape edge, while retaining the exact rendered-layout coverage gap.
- The preceding licensed real-Word equation gate remains the latest 48-action live Word
  proof. It was not rerun for this package-only read slice.

## Declared DrawingML/VML placement — 2026-07-24

- Extended the existing `WordFigureCaptionGraph` instead of creating a second layout
  model. DrawingML anchors now retain typed simple position, reference frames,
  alignment/offset, effect extents, stacking/overlap flags, Office 2010 relative size,
  wrap side/distances and bounded tight/through polygons. Microsoft 365 validation of
  the canonical anchor fixture reports zero errors.
- Known VML position, margin/size, z-order, relative-frame, tenth-percent, wrap-distance,
  visibility and `wrapcoords` declarations are typed. Physical lengths normalize to EMU;
  arbitrary enum-like strings are not projected as trusted values. DrawingML and VML
  polygons share a 4,096-point source limit and operation-wide resource accounting.
- `inspect_ooxml_figures` returns declared scalar placement only with
  `detail=declared`; exact polygon coordinates additionally require
  `include_geometry=true`, `view=representations`, `max_items<=2` and are capped at 128 points per
  response item. Every placement says it is declared data, not rendered geometry. The
  package path never opens Word or runs layout.
- The former `drawingml_vml_advanced_layout` omission is gone. The dependency graph now
  names only `drawingml_vml_rendered_geometry_and_layout_execution` as the remaining
  boundary.
- Full gates pass **486 Engine tests**, **324 Native tests** and **1,309 Python/OOXML
  tests** with 16 intentional environment/model skips. Release builds have zero warnings;
  Ruff is clean and mypy passes all 29 maintained Python files.
- Two pinned .NET SDK 8.0.423 builds from commit `9b8c0c4` produced byte-identical
  196-file, 86,304,532-byte expanded trees and 36,538,553-byte ZIPs at SHA-256
  `fa1e93ba23963066f5e6b02db4367d41fd590e4254edc7abec448316a2e4f061`.
  Both trees and ZIPs are identical and contain zero Python files.
- The personal marketplace and enabled cache at
  `0.39.0+codex.20260724050626` each contain the same 196 files with zero path, length or
  hash differences from the release tree. Installed capability discovery reports the
  exact version, 94 actions and the unchanged lazy `inspect_ooxml_figures` action.
- The installed runtime inspected the real `poi_drawing.docx` fixture: 21 figures were
  found, the two-item geometry page returned in a 4,738-character complete MCP line,
  every placement was marked declared-only, Word remained closed and the source hash
  stayed unchanged. The installed dependency summary returned 3,515 nodes and 3,773
  edges, retained figure and SmartArt coverage, removed
  `drawingml_vml_advanced_layout` and exposed only
  `drawingml_vml_rendered_geometry_and_layout_execution` as the drawing-layout gap.

## Native SmartArt graph and compact inspector — 2026-07-24

- Added `WordDiagramGraph`, a bounded read-only model for Transitional and Strict
  DiagramML references, data/layout/quick-style/color parts, persisted drawings, points,
  connections and definition identities. Point text is counted and discarded. The
  Microsoft Open XML SDK validates the reference fixture before parser assertions run;
  malformed cardinality, duplicate model IDs, unresolved endpoints, invalid orders,
  unsafe XML and resource-limit breaches are all gated.
- Integrated diagram and point nodes plus definition, containment, connection and part
  edges into the shared dependency graph. `smartart_diagrams` is no longer an unmodeled
  dependency domain. Office layout execution, geometry rendering and mutation remain
  explicit boundaries.
- Added lazy `inspect_ooxml_diagrams` as native action 94. Six paged views, exact
  diagram/point-type filters, independent key/hash/source opt-ins, a shared `wop1` lease
  and a 10 KiB projected-item ceiling keep the response bounded. Point text and raw XML
  are unavailable under every option; Word, Office layout and external targets are never
  opened or executed. Default output remains below 5,000 characters and the maximal
  synthetic keyed/source page remains below the 32 KiB complete-response gate.
- Full gates pass **483 Engine tests**, **323 Native tests** and **1,309 Python/OOXML
  tests** with 16 intentional environment/model skips. Release builds have zero
  warnings. Ruff is clean and mypy passes all 29 maintained Python source files.
- Two pinned .NET SDK 8.0.423 builds from commit `2608cc9` produced byte-identical
  196-file, 86,260,370-byte expanded trees and 36,523,652-byte ZIPs at SHA-256
  `808ff3dd51faa0b76fb562da7dea5ea9222856a5158d2330e27c506c25e2844a`.
  Both trees and ZIPs are identical and contain zero Python files.
- The personal marketplace and enabled cache at
  `0.39.0+codex.20260724043611` each contain the same 196 files with zero path, length or
  hash differences from the release tree. The installed executable reports that exact
  version, 94 actions and the lazy, read-only, closed-world
  `inspect_ooxml_diagrams` contract.
- Fresh installed-runtime MCP calls over `lo_chart.docx` return a 961-character empty
  SmartArt summary with zero issues and a 3,225-character dependency summary with 75
  nodes, 83/83 resolved edges and `smartart_diagrams=true`. Both calls report
  `word_opened=false` and `python_used=false`; the source SHA-256 remains
  `222628bcdb587c232e968d6aa1ba0a70dfd80845a4a2b8050316ec9d142ad33f`.

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
