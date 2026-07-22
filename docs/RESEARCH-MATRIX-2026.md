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

Styles are not a single `w:pStyle` lookup. WordprocessingML defines paragraph,
character, linked, table, numbering, and document-default style forms; paragraph styles
can contribute both paragraph and run properties. Microsoft also records that Word
requires an acyclic `basedOn` chain even though the standard permits a loop, while
toggle properties such as bold and italic do not obey ordinary last-value-wins merging.
The styles part itself is optional, and Word 2013+ may retain a separate
styles-with-effects part for round trips. These facts force a typed dependency graph,
explicit unresolved states, and a later compatibility-aware effective-format resolver.
[Style types and paragraph styles](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-create-and-add-a-paragraph-style-to-a-word-processing-document),
[`basedOn` interoperability note](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/2c24fcd8-38fb-467d-b9d1-fd2654e5fea6),
[toggle-property interoperability note](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/f7130225-2368-48f3-acae-a9d278d0fb25), and
[styles-part storage](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-replace-the-styles-parts-in-a-word-processing-document)
(B).

OPC relationship validation is not optional glue. Microsoft's packaging contract says
that internal targets are relative URI references, external targets may be relative or
absolute, relationship types must follow RFC 3986 URI syntax, and a relationship cannot
target another relationship. Relationship parts also have a reserved naming convention
and content type. These rules now map to explicit engine diagnostics rather than being
left for Word's repair dialog.
[Package.CreateRelationship](https://learn.microsoft.com/en-us/dotnet/api/system.io.packaging.package.createrelationship),
[PackagePart.CreateRelationship](https://learn.microsoft.com/en-us/dotnet/api/system.io.packaging.packagepart.createrelationship),
[PackUriHelper.GetRelationshipPartUri](https://learn.microsoft.com/en-us/dotnet/api/system.io.packaging.packurihelper.getrelationshipparturi)
(B).

Settings, theme language and fonts form one dependency chain; treating them as three
unrelated XML bags produces the broken substitutions that show up only after Word lays
out the document. `w:themeFontLang/@val`, `@eastAsia` and `@bidi` select language
mappings for the corresponding major/minor theme-font roles, while Word documents known
interoperability differences around that setting. DrawingML supplemental fonts are
keyed by ISO 15924 script, so BCP 47 language tags need an explicit script or a bounded,
versioned likely-script mapping; CLDR defines the likely-subtags algorithm and data, but
it does not make Word's private substitution behavior universal.
[ThemeFontLanguages](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.themefontlanguages?view=openxml-3.0.1),
[Word theme-language interoperability](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/701f92fe-a785-4829-a4fc-1f088669d87c),
[SupplementalFont](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.supplementalfont?view=openxml-3.0.1), and
[Unicode CLDR likely subtags](https://unicode.org/reports/tr35/#Likely_Subtags) (B).

The font table is also not proof that a typeface is installed. It stores declarations
and may reference embedded regular/bold/italic/bold-italic font parts. Microsoft exposes
those relationships through `FontTablePart`/`FontPart`, and the embedded-face `r:id`
must target a font relationship. Embed/subset settings affect portability, while Word
documents additional font-part interoperability behavior. The safe engine contract is
therefore metadata plus validated relationships and explicit readability state, never
silent byte exposure or invented system-font availability.
[FontTablePart](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.fonttablepart?view=openxml-3.0.1),
[embedded-font relationship](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.relationshiptype.id?view=openxml-3.0.1),
[EmbedTrueTypeFonts](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.embedtruetypefonts?view=openxml-3.0.1),
[SaveSubsetFonts](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.savesubsetfonts?view=openxml-3.0.1), and
[Word font-part interoperability](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/1663dabc-5d98-463f-889e-bcd9b77c3d34) (B).

Finally, `w:documentProtection` restricts editing; Microsoft's own API description says
it does not provide document security. That line matters because calling a hash-bearing
settings element "encryption" would be a lie. Document variables and mail-merge settings
may contain private values, queries or connection material, so compact inspection must
redact them by default and never return protection hashes or salts.
[DocumentProtection](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.documentprotection?view=openxml-3.0.1) and
[DocumentVariables](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.documentvariables?view=openxml-3.0.1) (B).

Fields are not paragraph-local strings. Microsoft's complex-field model requires a
begin and end, permits an optional separator, explicitly supports nested fields, and
treats an unclosed field at the end of a document story as no field. The older binary
format grammar says the same thing compactly: a field contains nested fields on both
sides of the optional separator, and every document story owns its own field list.
`w:fldSimple` is a different composite form whose instruction lives in `w:instr` and
whose children are the cached result; those children can themselves contain bookmarks,
revisions, math and nested simple fields. A sibling-run scan inside one paragraph is
therefore structurally wrong before performance even enters the room.
[`FieldChar`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.fieldchar?view=openxml-3.0.1),
[`SimpleField`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.simplefield?view=openxml-3.0.1), and
[the Microsoft field-list grammar](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/751b09bb-72f0-45ef-8e87-666dea68219f)
(B).

Bookmarks are paired source ranges, not names pasted onto paragraphs. ISO pairing uses
the same `w:id` on a subsequent `bookmarkEnd`, ranges can cross paragraphs, and
`colFirst`/`colLast` can describe a logical table-column slice. Word adds sharp edges:
bookmark lookup is spelling-exact but case-insensitive, names are limited to 40
characters, and when duplicate names exist Word retains the last definition rather
than the first required by the base standard. REF has more Word-specific behavior:
zero general switches and multiple field-specific switches are accepted; unknown field
text can act as an implicit REF; and reserved automatic-bookmark names require the REF
keyword. These divergences belong in typed diagnostics and resolution policy, not in a
regex.
[`BookmarkStart`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.bookmarkstart?view=openxml-3.0.1),
[Word Bookmark object](https://learn.microsoft.com/en-us/dotnet/api/microsoft.office.interop.word.bookmark?view=word-pia),
[Word bookmark interoperability](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/88454e96-31cb-4112-b7c2-e6b0f84a2637), and
[Word REF interoperability](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/7088a8ce-e784-49d4-94b8-cba6ef8fce78)
(B).

A public Pandoc reproducer shows the practical failure mode: placing the bookmark
outside the intended heading paragraph can make REF capture too much content, and a
later insertion can silently expand that range. The report is not an independent
benchmark, but it is an adversarial fixture specification worth preserving. It proves
why a reference engine must retain both source endpoints and never reduce a bookmark to
its name alone.
[Pandoc issue 8825](https://github.com/jgm/pandoc/issues/8825) (C/D).

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
| [Open-Xml-PowerTools](https://github.com/OfficeDev/Open-Xml-PowerTools) `5881422a881f6ccefce2b9801b5dc6a753670d6e` | Substantial higher-level Open XML SDK transformations: document assembly/splitting, tracked-revision acceptance, comparison, chart plus embedded-workbook updates, formatting expansion, field parsing, metrics, regex replacement, and HTML conversion. The repository includes a large fixture/test corpus. | Microsoft archived it; the pinned source stopped in 2019. Source still contains unsupported chart/numbering/language paths and comparer rejections. It is a valuable mine of algorithms, not a maintained unified engine or a proof of modern Word fidelity. | A |

### Implementation-level long tail

The following repositories were inspected at pinned commits, not merely read from package
indexes. They matter because narrow tools often expose failure modes that broad product
pages hide.

| Project and snapshot | Observed strength | Failure boundary relevant to WordToolkit | Evidence |
|---|---|---|---|
| [PHPWord](https://github.com/PHPOffice/PHPWord) `5579bd257f5eabb39a71dfc0d54cf763358aa35d` | Large pure-PHP authoring model and separate readers/writers for OOXML, ODF, RTF, HTML, and PDF routes. It covers sections, headers/footers, tables, lists, notes, drawings, OLE Excel/Visio, charts, forms, templates, comments, protection, revisions, and SDTs, backed by CI and unit tests. | Its own README says features remain in progress and PDF output travels through HTML. The typed in-memory model plus separate readers/writers is useful for authoring but does not establish byte-preserving arbitrary-DOCX round trips, Word pagination, semantic transactions, or repair. LGPL-3 also requires a deliberate integration boundary. | A |
| [python-docx-template](https://github.com/elapouya/python-docx-template) `1f143fbe86c19ecb28c3205d5f4b1547c7e2d7ad` | Practical Jinja templating over an existing Word-designed document, with rich text, hyperlinks, images, table loops, subdocuments, headers/footers processing, and a strong template fixture suite. | Normal Jinja tags cannot cross runs, paragraphs, or rows; structural tags delete their host node; rich text loses the template run style and cannot use Jinja filters; new header/footer media cannot be added dynamically. The core patches serialized XML with regular expressions. This is a report templater, not a document graph. | A |
| [docxcompose](https://github.com/4teamwork/docxcompose) `28ecb77fba5213598f1ba21c2acafeae169f5982` | Focused DOCX concatenation with relationship copying, style/numbering reconciliation, image deduplication, diagrams, VML shapes, footnotes, bookmark and drawing-ID renumbering, custom properties, and black-box DOCX fixtures. | The first document owns all headers and footers. Source comments call section handling “really messy”, discard the appended document's final section properties, and explicitly admit lost landscape orientation in a common case. It solves composition, not general lossless editing or merge semantics. | A |
| [docx-rs](https://github.com/bokuweb/docx-rs) `4fdfe62dbe880bc670382ddc3fede41ffc2f478e` | Rust/WASM writer and reader with a substantial typed object model, comments, revisions/history, notes, headers/footers, tables, images, numbering, styles, TOC, JSON projection, and snapshot-heavy tests. | The public feature list still marks sections and text boxes incomplete. Source contains `todo!`/`unimplemented!` branches for text-box, shape, hyperlink-instruction, and some nested-table/comment paths. Rebuilding from the typed model does not prove preservation of unknown markup. | A |
| [docx-templates](https://github.com/guigrpa/docx-templates) `54c2e80a090d0219503df1f26af91228b9880d77` | Capable Node/browser report generation: queries, conditions, loops over paragraphs/table rows, images, SVG fallbacks, links, HTML, literal XML, `.docm`, command inspection, asynchronous data, and configurable execution. | Templates contain executable JavaScript. Its README explicitly warns that Node's `vm` is not a security boundary and that uploaded templates are a serious code-injection risk. It cannot paginate, and page counts remain stale until Word or LibreOffice saves. This execution model must never enter WordToolkit's untrusted template lane. | A |
| [docxtemplater](https://github.com/open-xml-templating/docxtemplater) `6fd5c9b6ffe3c5dd23c96bac4ee0ace88826287a` | Mature JavaScript placeholder/loop/condition/raw-XML compiler with a module API, extensive regression history, browser support, async rendering, inspection, mutation testing, and defensive XML handling. | Images, HTML, charts, subdocuments/subsections, table construction, styling, footnotes, metadata, and several other advanced features live in paid modules. The open core is a templating compiler, not a public lossless semantic model, layout engine, validator/repair engine, or transaction system. | A |
| [Xceed DocX](https://github.com/xceedsoftware/DocX) `4029607c533514fe990712ee1792bfcf35c491dd` | Friendly .NET DOM for paragraphs, formatting, sections, tables, images, equations, bookmarks, hyperlinks, charts, TOC, protection, templates, joins, and parallel document work without Office. | The community source is licensed for non-commercial use. PDF, floating objects, shapes/text boxes, chart editing breadth, field updates, HTML/RTF insertion, digital signatures, notes, comments, split/advanced join, and other features are reserved for the proprietary product. Source also has explicit unsupported SVG/encryption paths. | A |
| [addFormula2docx](https://github.com/Sun-ZhenXing/addFormula2docx) `0cb4e21f96e149ce2cedf3fe5af144b83b9b73b7` | Small proof that LaTeX can flow through `latex2mathml`, Microsoft's Word 2016 MathML/OMML XSLT, and `python-docx` to create editable OMML. It also exposes OMML-to-MathML conversion. | Eight files, no regression suite, direct XML insertion, working-directory-dependent XSLT loading, no canonical math AST, no structural validation, and no Word-version corpus. Its own safe-mode note admits repeated-conversion limits. It is evidence for one conversion route, not an equation engine. | A |

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

## Comparison evidence and adopted boundary

Microsoft Word's native `Application.CompareDocuments` can compare formatting,
whitespace and case, tables, headers and footers, footnotes and endnotes, text boxes,
fields, comments and moves, then returns a document containing tracked revisions. That
is the authoritative Windows workflow when the required artifact is a Word review
document, but it is not a compact, cross-platform semantic diff and it necessarily opens
Word. [CompareDocuments](https://learn.microsoft.com/en-us/office/vba/api/word.application.comparedocuments)
(B).

Word's `w14:paraId` is a useful durable paragraph anchor when it is valid and unchanged,
but the format requires uniqueness and constrains it to an eight-character hexadecimal
value. Real damaged or merged documents can violate the uniqueness assumption, so a
duplicate is ambiguity evidence, not permission to pair by wishful thinking.
[MS-DOCX `paraId`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/a0e7d2e2-2246-44c6-96e8-1cf009823615)
(B).

Open-Xml-PowerTools' `WmlComparer` is the strongest inspected open-source WordprocessingML
comparison reference in this research set. Its source demonstrates the scale of the real
problem: atomization, revision consolidation, table handling, correlation, hashing and
tracked-revision reconstruction are inseparable from Word markup details. WordToolkit
does not copy that code or claim equivalent tracked-revision output; it adopts the harder
boundary that unmatched and unsupported evidence stays explicit.
[WmlComparer source](https://github.com/OfficeDev/Open-Xml-PowerTools/blob/5881422a881f6ccefce2b9801b5dc6a753670d6e/OpenXmlPowerTools/WmlComparer.cs)
(A).

For ordered sibling alignment, the classical edit-script literature establishes why a
sequence algorithm is preferable to comparing raw positional indexes. WordToolkit's
current implementation uses bounded weighted alignment plus a longest-increasing matched
subsequence for move detection, not a claim of Myers-equivalent minimal edit scripts.
[Myers, “An O(ND) Difference Algorithm and Its Variations”](https://par.cse.nsysu.edu.tw/resource/lab_relative/Myer86.pdf)
(B).

The adopted service therefore emits two independent layers: exact OPC entry changes and
source-linked semantic object changes. It matches by role, exact ID, unique durable
identity, unique exact subtree and finally contextual sibling evidence. Near ties remain
unmatched, fallback is labeled, opaque changes survive at package level, and compact MCP
views hide text, property values, hashes and source paths unless explicitly requested.
This is a tested diff foundation. Patch, three-way merge, revision-producing comparison
and visual comparison remain separate unfinished work.

## Conversion and rendering adapters

DOCX-to-PDF and PDF-to-DOCX are not symmetric operations. The first can ask a layout
engine to paginate a semantic source document. The second must infer paragraphs, reading
order, tables, styles, headers, and relationships from positioned page marks. Calling both
operations “conversion” hides the information loss.

| Project and snapshot | Observed strength | Failure boundary relevant to WordToolkit | Evidence |
|---|---|---|---|
| [unoconv](https://github.com/unoconv/unoconv) `2d0a3a815e07094aca5ed094fd3825fbe6f0819d` | Broad CLI over LibreOffice/OpenOffice import and export filters, optional persistent UNO listener, remote execution, filter properties, and many formats. | The project declares itself deprecated in favor of unoserver and says conversion failures can be unclear, nondeterministic, and sometimes fixed by retrying or restarting. Python/pyuno version coupling, LibreOffice profiles, stale locks, filter packages, and the rule against concurrent requests make it an adapter with operational debt, not an engine core. GPL licensing also keeps it out-of-process. | A |
| [unoserver](https://github.com/unoconv/unoserver) `7bfdcee45ec65708ee1ca897c451cd3f52a61e13` | Persistent LibreOffice listener with conversion, document comparison, health probing, binary/path transfer, explicit filters/options, request-count recycling, and conversion timeout. Avoiding repeated LibreOffice startup is a sound service design. | Windows and macOS remain untested in its own documentation. Its XML-RPC and UNO ports have no security and must not be exposed. It relies on an external supervisor after crashes/timeouts and does not restart LibreOffice itself. Fidelity is still LibreOffice's, not Word's. | A |
| [docx2pdf](https://github.com/AlJohri/docx2pdf) `aef5cec1d93da629a3727df7d9955804213b7062` | Very small Windows/macOS bridge that delegates DOC/DOCX-to-PDF to installed Microsoft Word through win32com or JXA. This gives the installed Word build authority over pagination. | It launches or attaches to Word, opens files, uses `SaveAs`, closes documents, and normally quits Word without version tokens, transaction isolation, validation, timeout, or protection against colliding with an existing user session. Linux is explicitly unsupported. | A |
| [pdf2docx](https://github.com/ArtifexSoftware/pdf2docx) `3e1c2319d6a3fbf2ae4d46c3ab734b7fc87bd9b4` | Clear reconstruction pipeline: PyMuPDF extracts positioned text/images/drawings, heuristic analysis infers page margins, headers/footers, paragraphs, tables and formatting, then `python-docx` generates a new DOCX. It supports page ranges, table extraction, debug layouts, and multiprocessing. | Artifex no longer actively maintains it. OCR is marked planned but throws when requested; default settings may ignore page failures; dozens of geometric thresholds decide structure. It cannot recover original fields, styles, revisions, equations, relationships, or author intent because the PDF no longer contains them. | A |

WordToolkit therefore needs explicit backend capability and provenance records. Word PDF
export is the authoritative Windows backend; LibreOffice is a useful isolated fallback;
PDF import is a reconstruction/OCR workflow whose inferred objects carry confidence and
source geometry. None may masquerade as lossless package editing.

## Word story and related-part findings

Microsoft's current [Open XML overview](https://learn.microsoft.com/en-us/office/open-xml/about-the-open-xml-sdk)
states that a WordprocessingML document is a
collection of stories: the main document, glossary, headers and footers, comments, text
boxes, footnotes and endnotes. Those stories live either in related package parts or,
for text boxes, inside a containing story; flattening only `document.xml` therefore
silently loses editable content. The native projector now follows the standard internal
relationship types for the primary related stories, validates their content types and
root elements, projects each target part once, exposes reference IDs, and applies the
same lossless text transaction machinery across their source bytes. The implementation
is tested against constructed strict/transitional cases and the bundled Word,
LibreOffice, POI, Pandoc and Mammoth fixtures. Section inheritance and the initial
threaded-comment/revision read graph are now separate typed graphs. A first bounded,
lossless accept/reject transaction now covers supported revision wrappers, complete moves
and property snapshots; paragraph merges, table-grid reconstruction, custom XML and full
collaboration-session semantics remain unfinished and explicitly blocked.

Section ownership cannot be recovered from filenames or relationship order. The
[`headerReference` contract](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.headerreference)
defines default/odd, first and even variants plus inheritance when a reference is
omitted, while the document-wide
[`evenAndOddHeaders` setting](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.evenandoddheaders)
decides whether the even variant is displayed. `titlePg` independently gates the first
variant. The engine therefore preserves both the defined binding and the effective
display target; collapsing them would erase “Link to Previous” semantics.

## Review and collaboration part findings

The standard [comments root](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.comments?view=openxml-3.0.1)
contains [comment definitions](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.comment?view=openxml-3.0.1),
while the visible range markers live in one or more Word stories. Their shared `w:id`
is therefore only the first join. Modern Word adds several independent maps:
[`commentsExtended`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/31f689cd-4192-4c2d-8d2f-202b1f8f20e9)
joins the last comment-paragraph `w14:paraId` to reply parent and done state;
[`commentsIds`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/6164f0a7-58f1-439a-a110-f52532b20abd)
maps that paragraph identity to a durable ID; and
[`commentsExtensible`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/62c16828-8131-4d1f-99f8-afd7560a1c78)
attaches later metadata and extension/reaction inventory through the durable ID. The
[`people` part](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/f461e6b7-7a35-4bc4-8153-b60f5d925539)
is another relationship-scoped source, carrying author and optional provider/user
presence identifiers. Collapsing all of these into a comment index loses thread,
identity and corruption evidence.

Tracked review markup is not one wrapper type. Text insertions/deletions and moves sit
beside property-change records for runs, paragraphs, tables, rows, cells, sections and
numbering; named move start/end markers must also be paired before source and destination
can be joined. Editing permissions use separate
[`permStart`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.permstart?view=openxml-3.0.1)
and end markers with editor/group and optional table-column scope. The engine therefore
keeps comment IDs, paragraph IDs, durable IDs, revision IDs, move-range IDs and people
IDs as distinct keys, preserves source links and emits bounded diagnostics for every
missing, duplicate, orphaned or reversed join.

Microsoft's [accept-all Open XML example](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-accept-all-revisions-in-a-word-processing-document)
shows that acceptance is a structural transform: inserted content is retained while its
wrapper disappears, deleted content is removed, and property-change records need their
own treatment. Older Microsoft guidance on
[in-memory Open XML processing](https://learn.microsoft.com/en-us/previous-versions/office/developer/officetalk2010/ee945362%28v%3Doffice.11%29)
warns that tracked revisions span more than forty elements and attributes. A global
search-and-delete routine is therefore garbage: it may look plausible while silently
destroying paragraph, table or move semantics.

The installed Word object model exposes the broad application-authoritative
[`Revisions.AcceptAll`](https://learn.microsoft.com/en-us/office/vba/api/word.revisions.acceptall)
operation, while the archived Open-Xml-PowerTools
[`RevisionProcessor`](https://github.com/OfficeDev/Open-Xml-PowerTools/blob/5881422a881f6ccefce2b9801b5dc6a753670d6e/OpenXmlPowerTools/RevisionProcessor.cs)
contains a much wider document transform with block-level cleanup. Neither is a safe
license to filter wrappers independently by author: nested decisions, paired move ranges
and deleted paragraph marks create dependencies outside the selected record. The native
planner consequently expands only explicitly authorized same-decision dependencies and
otherwise fails closed; structures requiring Word's broader layout-aware behavior route
to the guarded live-Word review path.

The SDK's [`cellIns` contract](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.cellinsertion?view=openxml-3.0.1)
uses an added-column example with inserted cells in every row, while Microsoft's
[WordprocessingML table overview](https://learn.microsoft.com/en-us/office/open-xml/word/working-with-wordprocessingml-tables)
states that `tblGrid` independently defines the table's grid columns. It follows that
rejecting `cellIns` by deleting only its parent `tc` can leave grid semantics behind.
The first transaction slice therefore accepts a cell insertion by removing its marker,
but blocks rejection until table-grid reconstruction is modeled and proved.

Likewise, [`cellMerge.vMergeOrig`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.cellmerge.verticalmergeoriginal?view=openxml-3.0.1)
stores the vertical-merge setting removed by the revision, while
[`cellMerge.vMerge`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.cellmerge.verticalmerge?view=openxml-3.0.1)
stores the setting applied by it. Deleting the annotation cannot implement rejection.
Both merge decisions remain blocked until the engine restores the correct `w:vMerge`
state across the affected vertical cell chain.

The SDK's [`numberingChange` contract](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.numberingchange?view=openxml-3.0.1)
describes `w:original` as a cache of the former LISTNUM result or paragraph-numbering
state. Removing the marker accepts the current result. The inverse decision cannot be
implemented by removing the same marker: it requires restoring or recalculating the old
field/numbering state. Reject therefore remains blocked until the reference/numbering
engines can prove that reconstruction.

## OfficeMath and equation-specific findings

Word stores professional equations as OMML, while accepting user-facing forms such as
UnicodeMath, LaTeX, and MathML through version-dependent import paths. Microsoft's
[OfficeMath rules](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/1d77457b-2884-4749-9b4a-c150ca13cc19)
also constrain where `oMath` and `oMathPara` may appear and how adjacent math behaves.
The current Microsoft 365 documentation describes
[MathML](https://learn.microsoft.com/en-us/office/math/mathml) and
[LaTeX](https://learn.microsoft.com/en-us/office/math/latex) support and its limits.
Microsoft's current [Math in Office](https://learn.microsoft.com/en-us/office/math/)
overview states that Office stores math in OMML, while the Open XML SDK
[Math namespace](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.math?view=openxml-3.0.1)
enumerates the typed vocabulary. The current MathML page gives an explicit
Presentation-MathML-to-OMML mapping. These are stronger sources for the object map than
reverse-engineering whatever one Word build happens to emit.

The repository scan found 28 real `m:oMath` equations across five local DOCX files and
1,421 math-namespace elements across 55 packages. The three tracked equation documents
contain 23 equations: 17 in the advanced torture document, five in the dedicated
equation atlas and one in the showcase. They cover common fractions, roots, scripts,
n-ary operators, matrices and arrays, but not every standard object such as phantom,
pre-scripts or border boxes. The regression design therefore combines these
Word-generated files with a synthetic all-object structural corpus and malformed cases;
pretending the real corpus alone is exhaustive would be false.

Microsoft's interoperability notes add failure rules that a schema-only parser misses:
adjacent `m:oMath` elements without a `w:br` are merged, display math uses
`m:oMathPara`, and Word rejects nested equations or math outside `w:p`. The equation
graph records these as bounded diagnostics instead of silently normalizing them.

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
- Inspect commercial cloud conversion APIs, additional editor servers, OCR engines, and
  more dedicated PDF-to-DOCX pipelines at pinned versions. Build an adversarial corpus to
  measure reading order, tables, equations, floating objects, fonts, and accessibility.
- Record Word-version capability probes for COM, JavaScript requirement sets, equation
  imports, field updates, PDF export, and CompareDocuments.
- Measure OfficeCLI and docx-cli round-trip preservation rather than relying on source
  inspection alone.
- Separate licensing compatibility from technical capability for every optional adapter.

The matrix is deliberately unfinished. Declaring the search complete while those rows
remain unmeasured would be another polished lie.
