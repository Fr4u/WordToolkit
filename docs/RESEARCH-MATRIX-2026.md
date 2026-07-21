# Word document-engine research matrix (2026-07-21)

This document records evidence that shapes WordToolkit's document-engine design. It is
not a vendor scorecard disguised as science. A feature advertised on a landing page is
not equivalent to a behavior reproduced against a hostile corpus and opened in Word.

## Evidence rules

| Grade | Meaning |
|---|---|
| A | Source code was inspected at a pinned commit and the relevant behavior is covered by tests or was reproduced locally. |
| B | The behavior is stated in a primary specification, official API documentation, or an official repository. |
| C | The behavior is a vendor/project claim that has not yet been independently benchmarked. |
| D | Architectural inference. It must not be presented as measured fact. |

The comparison uses these axes:

- package preservation: unknown parts, namespaces, MCE islands, relationships, and
  original bytes survive an unrelated edit;
- semantic coverage: paragraphs, fields, equations, drawings, revisions, content
  controls, references, styles, numbering, and Word extensions are understood;
- layout fidelity: pagination and rendering agree with desktop Word;
- editing safety: preconditions, transactions, rollback, validation, and repair risk;
- deployment: operating systems, Office dependency, service footprint, and licensing;
- AI ergonomics: stable locators, compact inspection, planning, bounded output, and
  low-token mutation;
- evidence quality: implementation and regression evidence rather than README breadth.

Scores are intentionally omitted until the shared corpus and benchmark harness exist.
Writing `5/5` before measuring is theatre.

## The hard constraints imposed by the format

DOCX is not a single XML document. It is an OPC ZIP package whose content types,
relationships, parts, markup-compatibility rules, ISO vocabularies, and Microsoft
extensions interact. The normative baseline is [ECMA-376](https://ecma-international.org/publications-and-standards/standards/ecma-376/),
especially Part 2 (Open Packaging Conventions) and Part 3 (Markup Compatibility and
Extensibility). Word also emits documented extensions such as
[MS-DOCX](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/b839fe1f-e1ca-4fa6-8c26-5954d0abbccd)
and [MS-ODRAWXML](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-odrawxml/bdc95a77-957c-40f8-9ef2-47cbcdeb8af2).

Two consequences follow:

1. A library can be schema-valid and still lose Word behavior or layout.
2. A convenient paragraph API cannot be the storage model for a lossless engine.

The Open XML SDK documentation also warns that markup-compatibility preprocessing can
remove unsupported markup. That makes blind preprocessing unacceptable in a lossless
path; unsupported islands must remain available as opaque source-backed data.
[Open XML SDK MCE guidance](https://learn.microsoft.com/en-us/office/open-xml/general/introduction-to-markup-compatibility)
(B).

## Platform and API families

### Microsoft surfaces

| Surface | What it is good at | Structural limit | Evidence |
|---|---|---|---|
| Word COM object model | Highest practical parity with the installed desktop Word build; ranges, fields, revisions, layout, compare, and the native equation editor. | Windows, installed Word, single-threaded COM discipline, UI state, version-specific behavior; not a standalone package model. | [Word object model](https://learn.microsoft.com/office/vba/api/overview/Word/object-model), [VSTO overview](https://learn.microsoft.com/en-us/visualstudio/vsto/word-object-model-overview), [CompareDocuments](https://learn.microsoft.com/en-us/office/vba/api/word.application.comparedocuments) (B). |
| Word JavaScript add-ins | Cross-platform Word-hosted commands, content controls, ranges, and OOXML coercion where the typed API is insufficient. | Capability varies by Word requirement set; it still needs a Word host and does not expose the complete package graph. | [Word add-ins](https://learn.microsoft.com/en-us/office/dev/add-ins/word/), [OOXML in add-ins](https://learn.microsoft.com/en-us/office/dev/add-ins/word/create-better-add-ins-for-word-with-office-open-xml), [requirement sets](https://learn.microsoft.com/office/dev/add-ins/develop/office-versions-and-requirement-sets) (B). |
| Microsoft Graph | Storage, version, sharing, permissions, and download/upload around a Word file represented as a DriveItem. | Graph exposes a workbook object model for Excel, but not an equivalent Word content DOM. Package editing remains the client's job. | [DriveItem](https://learn.microsoft.com/en-us/graph/api/resources/driveitem?view=graph-rest-1.0) (B). |
| Office Scripts | Managed cloud automation for Excel workbooks. | It is not a Word automation surface. Treating it as one is a category error. | [Office Scripts documentation](https://learn.microsoft.com/en-us/office/dev/scripts/) (B). |
| Microsoft 365 Copilot in Word | Drafting, rewriting, summarizing, and user-facing transformations inside Word. | Product capability, not a public lossless OOXML engine or transaction API. | [Microsoft 365 Copilot app card](https://learn.microsoft.com/en-us/microsoft-365/copilot/microsoft-365-copilot-application-card) (B). |

WordToolkit therefore keeps Word COM as an authoritative Windows execution and
verification backend, not as the only representation of a document.

### Open-source document and OOXML libraries

| Project | Strongest contribution | Limitation relevant to WordToolkit | Evidence |
|---|---|---|---|
| Open XML SDK | Strongly typed ISO/ECMA elements, package APIs, schema validation, broad Office vocabulary, maintained .NET ecosystem. | It is a low-level format SDK, not a semantic editor, Word layout engine, repair planner, or automatic lossless representation for every extension. | [SDK overview](https://learn.microsoft.com/en-us/office/open-xml/open-xml-sdk), [features](https://learn.microsoft.com/en-us/office/open-xml/general/features), [validation](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-validate-a-word-processing-document) (B). |
| python-docx | Clear Python API for common paragraphs, runs, tables, sections, and styles. | Its own API-concepts documentation is centered on common block-level authoring. It does not claim complete Word/OOXML parity or page layout. | [API concepts](https://python-docx.readthedocs.io/en/latest/user/api-concepts.html) (B). |
| docx4j | Mature Java/JAXB package and part model, content controls, merging, HTML/PDF routes, and direct OOXML access. | Java deployment and conversion backends remain separate concerns; semantic AI locators, transactional repair, and Word-identical pagination are not its core contract. | [docx4j getting started](https://www.docx4java.org/docx4j/Docx4j_GettingStarted.pdf) (B). |
| Apache POI XWPF | Familiar Java API plus access to underlying XMLBeans types. | Official documentation calls XWPF incomplete and warns that missing high-level support may require low-level XMLBeans work. | [XWPF quick guide](https://poi.apache.org/components/document/quick-guide-xwpf.html), [component status](https://poi.apache.org/components/document/index.html) (B). |
| docx.js | Broad declarative DOCX generation in TypeScript/JavaScript. | Generation breadth does not establish lossless arbitrary-document round trips, native pagination, or repair semantics. | [official repository](https://github.com/dolanmiu/docx) (B/D). |
| Mammoth | Useful semantic DOCX-to-HTML conversion that prefers meaning over visual imitation. | It intentionally ignores much formatting and documents imperfect handling of complex documents; it is not a round-trip editor. | [official repository](https://github.com/mwilliamson/mammoth.js/) (B). |
| Pandoc | Excellent semantic format conversion, reference-DOCX styling, and tracked-change import modes. | Conversion normalizes into Pandoc's document model, so Word-only structures and layout cannot be assumed to round-trip. | [Pandoc manual](https://pandoc.org/MANUAL.html) (B/D). |
| LibreOffice UNO | Very broad Writer object model: text, tables, fields, redlines, indexes, notes, shapes, bookmarks, and embedded content; useful cross-platform conversion/rendering backend. | Writer's model and layout are not Word's model and layout. Fidelity must be measured by corpus and platform, not assumed from API breadth. | [UNO text namespace](https://api.libreoffice.org/docs/idl/ref/namespacecom_1_1sun_1_1star_1_1text.html) (B/D). |
| ONLYOFFICE Document Server/API | Rich editor runtime, document API, conversion, coauthoring, and plugin surface. | Service/runtime footprint and licensing matter; its editor model is not a public, byte-preserving OPC semantic engine. | [Docs API concepts](https://api.onlyoffice.com/docs/docs-api/get-started/basic-concepts/), [Office API](https://api.onlyoffice.com/docs/office-api/get-started/overview/), [Document Builder](https://api.onlyoffice.com/docs/document-builder/get-started/overview/) (B/D). |
| Open-Xml-PowerTools | Valuable higher-level transformations built over Open XML SDK, including document assembly and comparison patterns. | The original ecosystem is fragmented across old/forked repositories and is not a maintained complete Word engine. Any borrowed idea needs a current, isolated implementation and regression proof. | [Microsoft archive/forks search root](https://github.com/OfficeDev/Open-Xml-PowerTools) (B). |

### Commercial engines

Commercial packages are important benchmark targets because they fund large format and
layout teams. Their claims are not accepted as independent measurements.

| Product | Documented strength | Known/structural limit | Evidence |
|---|---|---|---|
| Aspose.Words | Rich document DOM, broad import/export, fields, mail merge, page layout, per-page/shape rendering, PDF and image output without Word. | Closed source, commercial license, and its own layout engine; unsupported-feature preservation and Word parity must be tested on our corpus. | [features](https://docs.aspose.com/words/net/features/), [formats](https://docs.aspose.com/words/net/supported-document-formats/), [rendering](https://docs.aspose.com/words/net/rendering/) (B/C). |
| GemBox.Document | Managed cross-platform DOM, import/export, pagination/rendering, PDF/images, and documented preservation of unsupported DOCX content. | Official documentation says DOCX support is not complete and equations are not exposed through its API. Performance figures are vendor measurements. | [introduction](https://www.gemboxsoftware.com/document/docs/introduction.html), [format support](https://www.gemboxsoftware.com/document/docs/supported-file-formats.html), [platforms](https://www.gemboxsoftware.com/document/docs/supported-platforms.html) (B/C). |
| Spire.Doc | Broad .NET document creation, conversion, and manipulation surface without requiring Word. | Closed implementation, commercial constraints, and no independent evidence yet in this repository for lossless extension preservation or Word-identical pagination. | [Spire Office for .NET](https://www.e-iceblue.com/Introduce/spire-office-for-net.html) (C). |
| Syncfusion DocIO | Large .NET Word-processing API and conversion ecosystem. | Closed implementation and license; fidelity, unsupported-part preservation, equations, and performance still need corpus measurements. | [DocIO overview](https://help.syncfusion.com/document-processing/word/word-library/net/overview) (C). |

Aspose, GemBox, Spire, and Syncfusion belong in an optional benchmark/adaptor lane. A
public WordToolkit core cannot quietly require them or copy their behavior by guesswork.

## AI-oriented CLI and MCP implementations

Pinned source snapshots were cloned under a temporary research directory on
2026-07-21. Repository metadata is volatile; commit IDs make the observations
reproducible.

| Project and snapshot | Observed architecture and strength | Observed failure boundary | Evidence |
|---|---|---|---|
| [OfficeCLI](https://github.com/iOfficeAI/OfficeCLI) `0b3557bbec29f073f5df6b92b4b8dcefa7e3c160` | .NET/Open XML SDK, wide command surface, selectors, dump/replay, resident mode, Word/HTML render routes, one compact generic MCP command, sibling-temp atomic replacement. | Source contains explicit unsupported warnings and replay-loss paths; no independent Word pagination; broad handlers are not a unified repair/diff semantic engine. Atomic replacement is crash-oriented, not demonstrated power-loss durability. | A |
| [docx-cli](https://github.com/kklimuk/docx-cli) `3c2e2721ed90cbb42626c270d183a09d3b6d08b0` | TypeScript/Bun, substantial AST, stable locators, annotated Markdown, XML-in-place edits, equations, comments, revisions, raw parts, schema validation, and practical AI benchmark design. | Documentation admits no undo and in-place overwrite. Rendering delegates to Word/LibreOffice/PDFium routes. Raw escape hatches remain necessary for unsupported structures. | A |
| [Office Word MCP Server](https://github.com/GongRzhe/Office-Word-MCP-Server) `a3bbbb6d6167e68cf855d73ef7dc6cd8cfbfedba` | Accessible python-docx MCP tool set for common document construction. | Archived in March 2026; small regression surface; several advanced features are simplified or placeholder-backed; no complete package graph or native layout. | A |
| [word-mcp-live](https://github.com/ykarapazar/word-mcp-live) `c6c76179f66b27846d8f6a822a683e144d9288cb` | Broad live Word COM surface on Windows plus a macOS JXA path; over one hundred MCP tools. | Raw positional indices, weak optimistic concurrency, undo can cross unrelated user edits, macOS undo grouping is a no-op, and equation parity is platform-dependent. | A |
| [SecurityRonin/docx-mcp](https://github.com/SecurityRonin/docx-mcp) `b141be8153eff38ffac838b983ccc32f85f71acb` | Direct ZIP/lxml OOXML work, unusually strong tests and coverage enforcement, comments/notes/revisions and many advanced operations. WordToolkit's historical Python engine adapts this lineage. | Large flat MCP catalog, Python process/runtime, no single lossless semantic graph shared by parsing, repair, rendering, and AI. | A |
| [hongkongkiwi/docx-mcp](https://github.com/hongkongkiwi/docx-mcp) `d3fbbcfd7c93b0403de65d31f733c01b1cb2234f` | Small Rust package with an attractive standalone deployment story. | Source inspection found placeholder feature flags and placeholder rendering/TOC behavior behind broad README claims. Marketing breadth is not implementation evidence. | A |
| [mcp-msoffice-interop-word](https://github.com/mario-andreschak/mcp-msoffice-interop-word) `e50e339f1ac11fde6904addebef8c0b070879160` | Thin TypeScript/winax bridge to desktop Word. | Raw COM enums, basic failure handling, no package model, transactions, version tokens, validation, or semantic locators. | A |
| [OfficeMCP](https://github.com/OfficeMCP/OfficeMCP) `188140dc784f53d66da566696072f47d29fa795a` | Generic access to Office automation. | Its generic tool executes supplied Python with `exec` against COM objects. That is an arbitrary-code-execution boundary, not a safe document API. No detected repository license at the research snapshot. | A |

The useful ideas are clear: OfficeCLI's compact gateway and resident mode, docx-cli's
locators and benchmark methodology, SecurityRonin's regression discipline, and live
Word automation for authoritative operations. Their limitations are equally useful:
flat tool catalogs, unsafe generic code execution, in-place overwrites, weak versioning,
and ASTs that silently flatten what they do not understand.

## OfficeMath and equation-specific findings

Word stores professional equations as OMML, while accepting user-facing forms such as
UnicodeMath, LaTeX, and MathML through version-dependent import paths. Microsoft's
[OfficeMath rules](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/1d77457b-2884-4749-9b4a-c150ca13cc19)
also constrain where `oMath` and `oMathPara` may appear and how adjacent math behaves.
The current Microsoft 365 documentation describes
[MathML](https://learn.microsoft.com/en-us/office/math/mathml) and
[LaTeX](https://learn.microsoft.com/en-us/office/math/latex) support and its limits.

The earlier broken differential (`d x` with `d` visually raised) demonstrated the
failure mode: inserting math-looking text is not the same as constructing the intended
equation tree and asking Word to build it up. The engine must retain a canonical math
AST, validate operator/argument placement, serialize OMML deliberately, and verify the
result through Word when authoritative fidelity is requested.

## What no surveyed solution gives us as one coherent contract

No surveyed system, based on current evidence, combines all of the following:

1. byte-preserving OPC storage including unknown extension islands;
2. a typed Word/OOXML view and a source-linked semantic AST;
3. stable semantic locators usable by an AI without dumping raw XML;
4. explicit preconditions, dry-run plans, inverse patches, atomic persistence, and
   rollback;
5. schema, relationship, semantic, accessibility, equation, and Word-open validation;
6. pluggable authoritative Word rendering plus cross-platform fallbacks;
7. repair, semantic diff/merge, search/indexing, and document-scale low-token planning;
8. security limits, fuzzing, corruption corpus, and preservation measurements.

This is the gap WordToolkit is being redesigned to occupy. The claim will only become
credible when those behaviors are measured in public tests.

## Benchmark obligations

The next benchmark corpus must contain at least:

- strict and transitional WordprocessingML;
- `mc:AlternateContent`, unknown namespaces, and Microsoft extension parts;
- equations covering every OfficeMath construct and malformed import cases;
- fields, nested fields, TOC/TOF/TOT, citations, bibliography, and cross-references;
- numbering restarts, linked styles, latent styles, themes, and direct formatting;
- charts, SmartArt, VML/DrawingML, text boxes, OLE, SVG, and grouped/floating shapes;
- comments, threaded comments, revisions, moves, permissions, and protected regions;
- content controls, custom XML mappings, macros, signatures, and embedded packages;
- RTL, CJK, combining characters, surrogate pairs, language metadata, and font fallback;
- corrupted ZIPs, duplicate/case-colliding entries, XML bombs, dangling relationships,
  orphan parts, malformed namespaces, and zip bombs;
- documents from multiple desktop Word generations, Word Online, LibreOffice,
  ONLYOFFICE, Google Docs export, and third-party generators.

Metrics must include part-byte preservation, relationship preservation, Word open/repair
dialogs, Open XML validation, semantic edit success, visual page deltas, latency, peak
memory, package growth, tokens per task, and rollback correctness. Vendor-specific
numbers stay marked `unverified` until the same harness produces them.

## Research still open

- Run licensed evaluation builds of Aspose, GemBox, Spire, and Syncfusion against the
  same corpus; current entries are documentation-backed, not independent benchmarks.
- Add PHPWord, docxtemplater, docxcompose, docx-rs, unoconv, commercial cloud conversion
  APIs, and additional editor servers to the implementation-level matrix.
- Record Word-version capability probes for COM, JavaScript requirement sets, equation
  imports, field updates, PDF export, and CompareDocuments.
- Measure OfficeCLI and docx-cli round-trip preservation rather than relying on source
  inspection alone.
- Separate licensing compatibility from technical capability for every optional adapter.

The matrix is deliberately unfinished. Declaring the search complete while those rows
remain unmeasured would be another polished lie.
