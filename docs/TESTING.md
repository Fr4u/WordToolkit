# Test and release verification

## Test layers

- Unit: canonical equation parsers/writers, error contracts and security primitives.
- Native document engine: the cross-platform `WordToolkit.Engine.Tests` suite covers
  bounded OPC parsing, typed semantic graphs, source spans, diff/patch/merge planning,
  review decisions, formatting resolution and deterministic identities.
- Native MCP host: `WordToolkit.Native.Tests` covers the compact/lazy tool catalog,
  input validation, response compaction, cancellation and service contracts without
  starting Microsoft Word.
- Public operation parity: Engine and native tests drive
  `wordtoolkit.transform_ooxml_package/1.0` through the typed SDK, CLI, MCP and neutral
  protocol-v1 adapter. They cover canonical parity, cross-run first-only replacement,
  OfficeMath exclusion, MCE/revision ambiguity rejection, opaque-entry preservation,
  accept/reject-all, exact paragraph-mark merge inverse, signatures, output collisions,
  candidate validation, honest unsupported codes and zero Word invocation. The pinned
  42-scenario external run and raw result are documented in
  `COMPETITOR-BENCHMARK-2026-07-23.md`.
- Semantic-style operation parity: Engine and native tests drive the versioned plan/apply
  pair through direct .NET, strict JSON CLI and MCP. They cover all existing planner
  stages, selector caps and empty-selection failure, exact fingerprint/intent binding,
  validator-required fail-closed behavior, baseline-aware Microsoft schema checks,
  signed/unsafe rejection inherited from the planner, atomic backup restoration,
  opaque-entry preservation, suppression of untrusted validator messages, preserved MCP
  schema-failure diagnostics, published output-schema conformance and zero Word-host
invocation. A deterministic writer hook also places a non-cooperative change after the
final pre-commit fingerprint check and proves that its exact bytes are restored rather
than overwritten. A second hook writes a newer version during compensation and proves
that the first displaced version returns to the destination, the newer version survives
byte-for-byte in an opaque `.conflict` artifact even with backup retention disabled, and
the artifact is not erased by cleanup. A separate failure case deletes the recovery
backup before compensation and proves that no nonexistent artifact is advertised; public
detail tests reject absolute paths and payload text.
- Numbering-repair parity: Engine and native tests drive one list-tail restart through
  direct .NET, strict JSON CLI and the real JSON-RPC MCP envelope. They cover exact
  instance cloning, source/style paragraph reassignment, target and unaffected counter
  proofs, text preservation, inverse bytes, plan/fingerprint drift, signatures, validator
  absence, unknown fields before filesystem access, backup policy, response privacy and
  the published closed output schema. `WORDTOOLKIT_REAL_WORD_NUMBERING_REPAIR_TEST=1`
  additionally opens the repaired package read-only in licensed Word, compares
  `ListValue`/`ListString` with the engine and proves that the oracle did not resave it.
- Numbering-rebuild parity: Engine and native tests drive typed candidate inspection,
  plan and apply through direct .NET, strict JSON CLI and the published MCP schemas. They
  cover creation of missing numbering infrastructure, append-only definitions, Strict and
  Transitional packages, independent stories, style-inherited sources, all supported
  deterministic formats, 205 targets across bounded inspection pages, stale evidence,
  revisions/MCE/tracked properties, signatures, exact inverse and zero Word dispatch.
  `WORDTOOLKIT_REAL_WORD_NUMBERING_REBUILD_TEST=1` additionally opens the reconstructed
  package read-only in licensed Word, compares exact `ListString` labels with the engine,
  validates with Microsoft Open XML SDK, exports PDF and proves the source hash unchanged.
- Semantic-role parity: Engine and native tests drive one conservative theorem/definition/
  proof evidence graph through direct .NET, strict `semantic-role-package` JSON CLI and
  the published lazy MCP schema. They cover exact enclosing SDT declarations, explicit
  and inherited style conventions, Polish/English leading labels, conflicts, revision
  ambiguity, unresolved styles, hard text/evidence/issue limits, fingerprint-bound paging,
  independent disclosure gates, compact-response bounds, unknown-field rejection and zero
  Word dispatch. `WORDTOOLKIT_REAL_WORD_SEMANTIC_ROLE_TEST=1` additionally saves the
  fixture in licensed Word, revalidates it with Microsoft Open XML SDK and proves that all
  three valid evidence classes survive while a run-level inline SDT does not become a
  paragraph declaration.
- AI capability contract: native tests verify the embedded schema/MCP/compatibility
  header, deterministic 64-hex digests, sorted paging, exact CLI/MCP data parity,
  JSON round-trip, fail-closed malformed/unknown input, sub-10,000-character default
  pages, exact retrievable Draft 2020-12 schema bytes/hash, preservation of legitimate
  input properties named `title`, and a handler trap proving discovery cannot reach
  Word or document content.
- Classic charts: the engine uses a real LibreOffice chart plus synthetic Strict,
  malformed, external-target and resource-limit packages to verify series, axes,
  caches and related parts. Native tests prove that default and complete MCP envelopes
  remain bounded, cached point values are never serialized, sensitive/source opt-ins
  are independent, and neither Word nor an embedded/external workbook is opened.
- Markup Compatibility: engine tests cover inherited and aliased `Ignorable`,
  `ProcessContent`, `MustUnderstand`, first-choice/fallback selection, output
  reachability beneath discarded branches, ignored attributes, explicit extension
  islands, legacy preservation hints, malformed XML/structure, cancellation and limits,
  plus a real LibreOffice package. Native tests prove namespace/source redaction,
  explicit application configuration, zero COM calls and sub-5,000-character default
  data plus a sub-8,000-character complete JSON-RPC envelope.
- Content controls and Custom XML: engine tests cover real LibreOffice and generated
  packages, physical and built-in stores, standard/Office 2013 bindings, repeating
  sections, restricted XPath, malformed stores, identity conflicts, cancellation and
  limits. Native tests prove exact filters, independent metadata/source opt-ins, zero
  COM calls, default and complete envelope bounds, and non-disclosure of Custom XML,
  visible bound values and raw XML.
- Round-trip: LaTeX/UnicodeMath/MathML/AST → OMML → DOCX → reopened OMML with semantic AST comparison.
- Integration: create/open/edit/save, package validation, Streamable HTTP initialization and bearer rejection.
- Regression: the broad bundled corpus catches parser crashes, byte drift and open graph
  endpoints. A separate versioned semantic golden corpus fixes exact typed expectations
  for nine fixtures from five producer families, including styles/effective formatting,
  numbering, fields, comments, moves, text boxes, headers/footers and chart-part
  preservation. Its provenance and update discipline are documented in
  `SEMANTIC-GOLDEN-CORPUS.md`. Native saved-package review tests additionally exercise
  modern comment threads/durable IDs/reactions/people, malformed anchors,
  nested/property revisions, named moves, permission ranges, redaction and paging.
- Rendering: semantic HTML/SVG tests share one immutable presentation snapshot;
  execution-contract tests reject unresolved capability/fallback claims and inject
  transactional publication failures; fixed-render tests cover Word PDF options,
  Poppler process/resource/MediaBox/PNG validation, strict CLI/MCP schemas and source
  preservation. Set `WORDTOOLKIT_REAL_WORD_FIXED_RENDER_TEST=1` together with explicit
  `WORDTOOLKIT_PDFINFO_PATH` and `WORDTOOLKIT_PDF_RASTERIZER_PATH` executables to run
  the licensed one-page Word→PDF→PNG geometry/source-hash oracle.
- Golden artifacts: `examples/generated` includes validated DOCX, PDF, PNG previews and a JSON report.
- Runtime inventory: `tests/test_runtime_modules.py` imports every packaged
  module; `tests/test_clean_workspace.py` proves cleanup keeps only the current
  release and never removes `.venv` without an explicit flag.
- Advanced acceptance: `scripts/advanced_torture_test.py` builds a nine-page, four-section OPC/OOXML torture document, reopens it, verifies protected parts byte-for-byte, checks 17 native equations semantically in every export format, validates package/accessibility/layout, renders PDF/PNG and rejects blank, sparse, clipped or corrupt previews.
- Word interoperability: `native/scripts/live-full-capabilities-timed.ps1` exercises
  every installed live action through the packaged native runtime on a licensed
  self-hosted Windows runner. It preserves a pre-existing user Word process and closes
  only its explicitly disposable acceptance document.
- Word Live competitor-gap acceptance: `scripts/real_word_live_gap_test.py`
  creates one disposable Word 16.0 document, then exercises native Find,
  transactional replacement, comment add/reply/resolve, Track Changes,
  tokenized revision acceptance, guarded Undo, same-path save and both
  structural/Open XML SDK validation. The test harness may launch Word; the
  shipped bridge never does.
- Native caption/table-of-figures acceptance: focused fake-COM tests prove closed
  contracts, localized label handling, response privacy and rollback. The installed MCP
  proof creates two captions and one table of figures in Word 16.0.20131, saves and
  validates the DOCX, inspects complete `SEQ`/`TOC`/`PAGEREF` fields, exports PDF and
  visually checks a 144-DPI raster for clipping and overlap.
- Packaged-plugin execution: `scripts/smoke_test_local_plugin.py` and
  `scripts/real_world_plugin_test.py` use `uv run --isolated` with bytecode
  writes disabled. A test must not create `.venv`, `__pycache__` or `.pyc`
  inside the release directory that will later feed the personal marketplace.

Run locally:

```bash
dotnet test native/WordToolkit.Engine.Tests/WordToolkit.Engine.Tests.csproj -c Release
dotnet test native/WordToolkit.Native.Tests/WordToolkit.Native.Tests.csproj -c Release
uv sync --extra dev
pytest -ra
ruff check src/wordtoolkit scripts tests/test_*.py
python scripts/generate_samples.py
python scripts/advanced_torture_test.py
python scripts/real_word_live_gap_test.py
python scripts/export_tool_schemas.py
```

### Coverage and mutation baseline

The legacy Python compatibility surface has a measured branch-coverage gate. Run the
same coverage command used by CI with:

```bash
uv run pytest -ra --cov=wordtoolkit --cov-report=term-missing:skip-covered
```

The repository-wide floor is 73%. CI also enforces independent floors for the current
critical legacy modules: `security.py` 65%, `engine/document.py` 56%, `sessions.py` 73%
and `engine/renderer.py` 82%. These are regression floors taken from the 0.60.3 baseline,
not claims that the missing branches are acceptable or that coverage proves correctness.
Raise a floor when tests improve it; do not lower a floor merely to make CI green.

Mutation testing starts as a bounded manual pilot for `security.py` and its focused test
module. It is intentionally not a required status until surviving mutants have been
classified and a stable score has been recorded:

```bash
uv sync --extra dev --extra mutation --locked
uv run mutmut run
uv run mutmut results
```

`mutmut` requires Linux or WSL; the `mutation-pilot` Actions workflow provides the
reviewed Linux execution environment. A green unit-test or coverage job does not imply a
green mutation result.

For a fast semantic-oracle check while changing a parser or graph builder:

```powershell
dotnet test native/WordToolkit.Engine.Tests/WordToolkit.Engine.Tests.csproj `
  --filter FullyQualifiedName~GoldenSemanticCorpusTests
```

On Windows, build the same native ZIP uploaded by CI. The script runs both .NET test
suites before publishing unless `-SkipTests` is explicitly supplied:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build_native_plugin.ps1
```

The Docker image builds the Microsoft Open XML SDK validator. A Python-only run records that validator as unavailable rather than pretending it ran.

## Release gates

1. `WordToolkit.Engine.Tests` and `WordToolkit.Native.Tests` pass, and the Windows CI
   job builds and uploads the final distributable ZIP from the same commit.
2. No DOCX export has `validation.valid=false`.
3. Round-trip preservation has no missing or unexpectedly changed unmodified parts.
4. Both basic samples and the advanced acceptance document render to PDF and PNG with no visual warning; every advanced page is decoded and manually reviewed before release.
5. MCP schema export contains every required tool and file-input metadata.
6. Plugin manifest validates.
7. Container health and unauthenticated MCP rejection are verified.
8. Before publishing a release, the tag/manual Windows/Word workflow passes its full
   49-action live gate on the exact packaged runtime. A sample open/resave is not a
   substitute for this evidence.
9. The tested plugin directory remains free of `.venv`, `__pycache__` and
   `.pyc`; after installation, the runtime's editable path resolves to the
   installed cache rather than the build directory.
10. Large dependency graphs and `.wtpatch` inputs are claimed only up to a checked-in
    benchmark result reporting elapsed time and peak memory. The dependency graph also
    proves its deterministic accounted-byte usage, rejects the identical fixture when
    configured one byte below that usage, validates compact incoming/outgoing ordering
    and keeps its three-field MCP byte-budget proof inside the existing token envelope.
    Configured rejection ceilings alone are not performance evidence.
11. Content-control binding scale claims use the checked-in `bindings` benchmark. Its
    100,000-control point must report every binding resolved and must disclose any
    benchmark-only resource limit raised above the production default.
12. Figure/caption scale claims use the checked-in `figures` benchmark. It builds the
    graph seven times over immutable projected inputs, reports median/p95, and must
    produce the requested figures/captions plus uniquely selected associations without
    diagnostics at the 10,000-object point.

Run the binding-graph scale points without Word:

```powershell
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release -- bindings --target-nodes 10000
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release -- bindings --target-nodes 100000
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release -- figures --target-nodes 10000
```

## Interpreting visual results

The JSON report distinguishes structural validity from visual heuristics and explicitly identifies LibreOffice as the renderer. A human should inspect the PNGs for equation placement, table breaks, fonts, margins and floating objects. A clean LibreOffice preview is evidence of compatibility, not proof of Word-identical pagination.
