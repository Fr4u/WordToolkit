# Test and release verification

## Test layers

- Unit: canonical equation parsers/writers, error contracts and security primitives.
- Round-trip: LaTeX/UnicodeMath/MathML/AST → OMML → DOCX → reopened OMML with semantic AST comparison.
- Integration: create/open/edit/save, package validation, Streamable HTTP initialization and bearer rejection.
- Regression: the vendored `docx-mcp` test corpus covers styles, tables, notes, comments, revisions, fields, raw parts, images, sections, headers/footers and security behavior.
- Rendering: LibreOffice DOCX→PDF, Poppler PDF→PNG and page heuristics.
- Golden artifacts: `examples/generated` includes validated DOCX, PDF, PNG previews and a JSON report.
- Runtime inventory: `tests/test_runtime_modules.py` imports every packaged
  module; `tests/test_clean_workspace.py` proves cleanup keeps only the current
  release and never removes `.venv` without an explicit flag.
- Advanced acceptance: `scripts/advanced_torture_test.py` builds a nine-page, four-section OPC/OOXML torture document, reopens it, verifies protected parts byte-for-byte, checks 17 native equations semantically in every export format, validates package/accessibility/layout, renders PDF/PNG and rejects blank, sparse, clipped or corrupt previews.
- Word interoperability: `scripts/word_interop.ps1` opens/saves generated documents through Word COM on a licensed self-hosted Windows runner.
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
uv sync --extra dev
pytest -ra
ruff check src/wordtoolkit scripts tests/test_*.py
python scripts/generate_samples.py
python scripts/advanced_torture_test.py
python scripts/real_word_live_gap_test.py
python scripts/export_tool_schemas.py
```

The Docker image builds the Microsoft Open XML SDK validator. A Python-only run records that validator as unavailable rather than pretending it ran.

## Release gates

1. All non-optional unit/integration/regression tests pass.
2. No DOCX export has `validation.valid=false`.
3. Round-trip preservation has no missing or unexpectedly changed unmodified parts.
4. Both basic samples and the advanced acceptance document render to PDF and PNG with no visual warning; every advanced page is decoded and manually reviewed before release.
5. MCP schema export contains every required tool and file-input metadata.
6. Plugin manifest validates.
7. Container health and unauthenticated MCP rejection are verified.
8. For production, the Windows/Word workflow passes before claiming Microsoft Word interoperability.
9. The tested plugin directory remains free of `.venv`, `__pycache__` and
   `.pyc`; after installation, the runtime's editable path resolves to the
   installed cache rather than the build directory.

## Interpreting visual results

The JSON report distinguishes structural validity from visual heuristics and explicitly identifies LibreOffice as the renderer. A human should inspect the PNGs for equation placement, table breaks, fonts, margins and floating objects. A clean LibreOffice preview is evidence of compatibility, not proof of Word-identical pagination.
