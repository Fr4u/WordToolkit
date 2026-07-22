# Test and release verification

## Test layers

- Unit: canonical equation parsers/writers, error contracts and security primitives.
- Native document engine: the cross-platform `WordToolkit.Engine.Tests` suite covers
  bounded OPC parsing, typed semantic graphs, source spans, diff/patch/merge planning,
  review decisions, formatting resolution and deterministic identities.
- Native MCP host: `WordToolkit.Native.Tests` covers the compact/lazy tool catalog,
  input validation, response compaction, cancellation and service contracts without
  starting Microsoft Word.
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
- Rendering: LibreOffice DOCX→PDF, Poppler PDF→PNG and page heuristics.
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
   48-action live gate on the exact packaged runtime. A sample open/resave is not a
   substitute for this evidence.
9. The tested plugin directory remains free of `.venv`, `__pycache__` and
   `.pyc`; after installation, the runtime's editable path resolves to the
   installed cache rather than the build directory.
10. Large dependency graphs and `.wtpatch` inputs are claimed only up to a checked-in
    benchmark result reporting elapsed time and peak memory. The configured rejection
    ceilings alone are not performance evidence.
11. Content-control binding scale claims use the checked-in `bindings` benchmark. Its
    100,000-control point must report every binding resolved and must disclose any
    benchmark-only resource limit raised above the production default.

Run the binding-graph scale points without Word:

```powershell
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release -- bindings --target-nodes 10000
dotnet run --project native/WordToolkit.Engine.Benchmarks -c Release -- bindings --target-nodes 100000
```

## Interpreting visual results

The JSON report distinguishes structural validity from visual heuristics and explicitly identifies LibreOffice as the renderer. A human should inspect the PNGs for equation placement, table breaks, fonts, margins and floating objects. A clean LibreOffice preview is evidence of compatibility, not proof of Word-identical pagination.
