# Stage results

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
- Hosted CI/artifact parity and human review remain required. The public release stays
  at 0.34.0; no licensed Word release gate is claimed for this saved-package tranche.

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
