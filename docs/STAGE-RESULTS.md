# Stage results

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
