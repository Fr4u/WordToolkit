# Word document-engine research matrix (2026-07-23)

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

Broad product scores are intentionally omitted. One pinned 42-scenario neutral harness
checkpoint now exists, but converting that narrow intersection into a universal `5/5`
would still be theatre.

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

The current normative MCE baseline is
[ECMA-376 Part 3, fifth edition](https://ecma-international.org/publications-and-standards/standards/ecma-376/).
It defines a three-step reference model: first mark unknown ignorable elements and
attributes as ignored or unwrapped through `ProcessContent`; independently choose the
first `Choice` whose required namespaces are understood, or `Fallback`; then construct
the effective output and signal `MustUnderstand` mismatches. The selection of a nested
choice is computed even when an ancestor choice is not selected, but that nested content
does not reach the output. Application-defined extension elements suspend MCE processing
for their complete subtree. Those details rule out the common shortcut of deleting every
unknown namespace or treating `AlternateContent` as an ordinary first-child switch (B).

`PreserveElements` and `PreserveAttributes` are a version trap. They existed as
preservation hints in earlier MCE editions and remain exposed by the
[Open XML SDK `MarkupCompatibilityAttributes` API](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.markupcompatibilityattributes?view=openxml-3.0.1),
but they are absent from the fifth-edition Part 3 syntax and processing model. Office
Open XML never obliged applications to honor those hints and instead defines native
extension-list round-tripping rules. WordToolkit therefore inventories and preserves
the legacy attributes, reports their edition status, and does not execute them as
current rules. Pretending either that they never existed or that they remain normative
would corrupt one side of the compatibility boundary (B).

Microsoft's own extension specification states that Word extensions integrate with
ISO/IEC 29500 through `Ignorable` and `AlternateContent` rather than by becoming base
WordprocessingML. The target consumer's understood namespaces are therefore part of the
meaning of the document, not ambient global truth.
[MS-DOCX structure overview](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/728a7abc-7f55-40dc-90a7-1276ff53c8b2)
(B). The engine consequently accepts an explicit application configuration and explicit
application-defined extension names. It does not claim that a marketing label such as
“Office 2016” is a complete namespace-capability profile without a version-pinned probe.

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

Active content is topology before it is code. The Open XML SDK exposes Word object-link
markup as a typed WordprocessingML element, VBA as a distinct `VbaProjectPart`, and
package signatures through a distinct `DigitalSignatureOriginPart`. Microsoft's DOCM
to DOCX procedure removes the VBA project part and changes the main-part content type;
that is direct evidence that macro presence is jointly expressed by package topology and
container type, not by a filename guess. These sources justify a metadata inventory but
do not justify executing payloads or treating signature-part presence as verified trust.
[`ObjectLink`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.objectlink?view=openxml-3.0.1),
[`DigitalSignatureOriginPart`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.wordprocessingdocument.digitalsignatureoriginpart?view=openxml-3.0.1), and
[DOCM-to-DOCX conversion](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-convert-a-word-processing-document-from-the-docm-to-the-docx-file-format)
(B). WordToolkit therefore types exact declaration/relationship/payload topology while
keeping binary decoding, embedded-package opening, code execution and cryptographic
signature validation outside the read graph.

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

Document metadata is not one untyped map. OPC core properties, Office extended
application properties and custom properties occupy separate parts and vocabularies.
Microsoft's custom-property example shows the fixed property-set `fmtid`, integer
`pid` values starting at 2, and a value element whose name carries the type. Microsoft's
extended-property example reads application metadata through the dedicated extended
part. This justifies three typed families, exact relationship/content-type admission,
lexical validation and an explicit refusal to decode complex values through a generic
string interface. `w:docVars` remains a settings child and must not be mistaken for the
custom-property part.
[Set a custom property](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-set-a-custom-property-in-a-word-processing-document),
[retrieve application property values](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-retrieve-application-property-values-from-a-word-processing-document),
[`CustomFilePropertiesPart`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.wordprocessingdocument.customfilepropertiespart?view=openxml-3.0.1), and
[`DocumentVariables`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.documentvariables?view=openxml-3.0.1) (B).

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
| Microsoft 365 Copilot APIs | Retrieval, usage reporting, package management and agent integration around Microsoft 365 Copilot. | The public API families are not a Word package graph, lossless OOXML editor, layout engine or document transaction API. | [Copilot APIs overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/copilot-apis-overview) (B). |
| Microsoft Agent 365 Word MCP | Microsoft's official MCP catalog now advertises a tenant-scoped remote Word server for reading and understanding documents, creating content and collaborating through comments. | This is a closed remote Microsoft 365 product surface requiring tenant identity and authorization, not an inspectable standalone lossless OPC/OOXML engine. The catalog does not establish byte preservation, package transactions, layout determinism, offline execution or a portable public semantic graph. | [official Microsoft MCP catalog](https://github.com/microsoft/mcp) (B/C). |
| Copilot Edit in Word | Product-level create, edit, refine and formatting assistance in the current Word document with a preview/review workflow. | Microsoft's current support page says this mode works on the current document, cannot create new files, does not support external tools and cannot insert images. It is an interactive AI editing surface, not a neutral public OOXML transaction, validation or rendering API. | [Edit with Copilot in Word](https://support.microsoft.com/en-US/word/edit-with-copilot-in-word) (B/C). |
| Copilot declarative-agent Office API plugin (preview) | A declarative agent can call Office JavaScript APIs in the currently open Word, Excel or PowerPoint document. | Host-bound preview on Windows and web, not Mac; it acts through the open Office application and still does not expose a standalone lossless OPC/OOXML engine. | [Build API plugins with Office APIs](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/build-api-plugins-local-office-api) (B). |

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
| Aspose.Words | Rich document DOM, broad import/export, fields, mail merge, page layout, PDF and image output without Word. Its public rendering API includes individual `ShapeRenderer` and `OfficeMathRenderer` objects, making object-level rendering a concrete competitor capability rather than a speculative feature. | Closed source, commercial license, and its own layout engine; unsupported-feature preservation and Word parity must be tested on our corpus. | [features](https://docs.aspose.com/words/net/features/), [formats](https://docs.aspose.com/words/net/supported-document-formats/), [rendering API](https://reference.aspose.com/words/net/aspose.words.rendering/) (B/C). |
| GemBox.Document | Managed cross-platform DOM, import/export, pagination/rendering, PDF/images, and documented preservation of unsupported DOCX content. | Official documentation says DOCX support is not complete and equations are not exposed through its API. Performance figures are vendor measurements. | [introduction](https://www.gemboxsoftware.com/document/docs/introduction.html), [format support](https://www.gemboxsoftware.com/document/docs/supported-file-formats.html), [platforms](https://www.gemboxsoftware.com/document/docs/supported-platforms.html) (B/C). |
| Spire.Doc | Broad .NET document creation, conversion, and manipulation surface without requiring Word. | Closed implementation, commercial constraints, and no independent evidence yet in this repository for lossless extension preservation or Word-identical pagination. | [Spire Office for .NET](https://www.e-iceblue.com/Introduce/spire-office-for-net.html) (C). |
| Syncfusion DocIO | Large .NET Word-processing API and conversion ecosystem. | Closed implementation and license; fidelity, unsupported-part preservation, equations, and performance still need corpus measurements. | [DocIO overview](https://help.syncfusion.com/document-processing/word/word-library/net/overview) (C). |
| DevExpress Office File API / RichEdit Document Server | Server-side rich document model with DOC/DOCX/RTF/HTML import/export and PDF/image rendering routes. | Closed implementation and independent layout engine; exact unknown-part preservation, Word pagination, equation fidelity and repair behavior remain corpus questions. | [Word Processing Document API formats](https://docs.devexpress.com/OfficeFileAPI/15441/word-processing-document-api/import-and-export) (B/C). |
| Telerik RadWordsProcessing | Typed `RadFlowDocument` model with DOCX, RTF, HTML and text import/export plus PDF export. | Importing into and exporting from its own flow model does not by itself prove byte-preserving arbitrary-DOCX round trips or Word-identical layout. | [RadWordsProcessing overview](https://www.telerik.com/document-processing-libraries/documentation/libraries/radwordsprocessing/overview) (B/C). |
| TX Text Control | Mature server/editor control for DOC/DOCX/RTF/PDF workflows, mail merge and interactive editing. | Closed commercial runtime. Its PDF-import documentation explicitly describes reconstructing semantic structure from appearance/positions as heuristic, so PDF-to-DOCX cannot be treated as lossless recovery. | [ASP.NET introduction](https://docs.textcontrol.com/textcontrol/asp-dotnet/article.aspnet.introduction.htm), [PDF import limits](https://docs.textcontrol.com/textcontrol/windows-forms/article.techarticle.pdf.htm) (B/C). |
| GroupDocs.Editor | Converts supported documents into editable HTML and back through an intermediate representation. | That model is useful for browser editing, but the documented route does not establish preservation of arbitrary OOXML parts, Word-only semantics or pagination. | [GroupDocs.Editor for .NET](https://docs.groupdocs.com/editor/net/) (B/C). |

These commercial engines belong in an optional benchmark/adaptor lane. A public
WordToolkit core cannot quietly require them or copy their behavior by guesswork.

### Cloud document and conversion APIs

| Surface | Documented strength | Structural limit | Evidence |
|---|---|---|---|
| Google Docs API `documents.batchUpdate` | Cloud-native JSON document mutations with ordered requests, write controls and suggestion-view options. | It edits Google's document model, not the source DOCX package; import/export cannot be assumed to preserve opaque OPC parts or Word layout. | [`documents.batchUpdate`](https://developers.google.com/workspace/docs/api/reference/rest/v1/documents/batchUpdate) (B/D). |
| Microsoft Word JavaScript `ExportRange` | Word-hosted fixed-format export supports the whole document, current page, explicit page ranges or the active selection. It is direct evidence that selection-scoped rendering/export belongs in a serious Word automation surface. | The API is host-dependent and fixed-format oriented; it does not provide a vendor-neutral semantic HTML subtree, stable package node locator or server-side layout engine. | [`Word.ExportRange`](https://learn.microsoft.com/en-us/javascript/api/word/word.exportrange?view=word-js-preview) (B/D). |
| Adobe PDF Services / Extract API | Cloud PDF-to-DOCX, OCR, conversion and structured PDF extraction into JSON. | PDF is the source model. Extraction and conversion cannot reconstruct Word-specific package semantics or prove DOCX round-trip preservation. | [PDF Services overview](https://developer.adobe.com/document-services/docs/overview/pdf-services-api/), [API list](https://developer.adobe.com/document-services/docs/apis/), [Extract API](https://developer.adobe.com/document-services/docs/overview/pdf-extract-api/) (B/D). |

### Differential object-rendering evidence — 2026-07-23

The next renderer decision was checked against primary interfaces rather than product
brochures. This is a bounded differential check, not a new claim to have surveyed every
converter.

- **FACT:** Word COM `Range.ExportAsFixedFormat` exports a range only to PDF or XPS; it
  does not define semantic SVG or stable package-node rendering. Word JavaScript
  `ExportRange` selects the whole document, current page, page range or active selection,
  not an OOXML equation/table ID. Sources: [Word VBA
  `Range.ExportAsFixedFormat`](https://learn.microsoft.com/en-us/office/vba/api/Word.range.exportasfixedformat),
  [Word JavaScript
  `ExportRange`](https://learn.microsoft.com/en-us/javascript/api/word/word.exportrange?view=word-js-preview).
- **FACT:** LibreOffice UNO `XRenderable` exposes numbered render jobs through
  `getRendererCount`, `getRenderer` and `render`; its contract does not promise a
  Word-equivalent SVG for one semantic object. Source: [LibreOffice
  `XRenderable`](https://api.libreoffice.org/docs/idl/ref/interfacecom_1_1sun_1_1star_1_1view_1_1XRenderable.html).
- **FACT:** Aspose.Words exposes separate per-object `ShapeRenderer` and
  `OfficeMathRenderer` APIs. That is concrete competitor evidence for true object-level
  rendering, but it remains a closed licensed layout engine whose Word fidelity and text
  mode need corpus tests. Sources: [Aspose
  `ShapeRenderer`](https://reference.aspose.com/words/net/aspose.words.rendering/shaperenderer/),
  [Aspose
  `OfficeMathRenderer`](https://reference.aspose.com/words/net/aspose.words.rendering/officemathrenderer/).
- **FACT:** SVG 2 text can remain selectable/searchable text, while glyph placement still
  depends on fonts, CSS, kerning, bidi and the consuming renderer. SVG can also contain
  scripts and external links, so safe generation requires an explicit static profile.
  Sources: [W3C SVG 2 text](https://www.w3.org/TR/SVG2/text.html), [W3C SVG
  integration](https://www.w3.org/TR/svg-integration/), [W3C SVG
  linking](https://www.w3.org/TR/SVG/linking.html).
- **DECISION:** the built-in `render_ooxml_semantic_svg/1.0` claims exact target identity
  and deterministic semantic vector output only. It does not claim Word object bounds,
  pagination, exact font metrics or pixel parity. Word PDF/XPS, LibreOffice best-effort
  output and optional licensed per-object renderers remain separate backend classes with
  their own version, environment, fidelity and security evidence.

## AI-oriented CLI and MCP implementations

Pinned source snapshots were cloned under a temporary research directory. The original
eight AI/Word repository heads were rechecked on 2026-07-23 and four additional current
competitors were inspected. Repository metadata is volatile; commit IDs make the
observations reproducible. This is a bounded search set, not a claim that every GitHub
repository containing `docx`, `Word` or `MCP` has been found.

| Project and snapshot | Observed architecture and strength | Observed failure boundary | Evidence |
|---|---|---|---|
| [OfficeCLI](https://github.com/iOfficeAI/OfficeCLI) `9c78827d25d33f53664e68e7ec841d577c763632` (`1.0.140`) | .NET/Open XML SDK, wide command surface, selectors, issue views, dump/replay, resident mode, Word/HTML render routes, one compact generic MCP command, sibling-temp atomic replacement and atomic-by-default multi-operation batch rollback. The current head also adds native Markdown-subset expansion and fixes range-split and stale body-index defects. | Source contains explicit unsupported warnings and optional lossy `--best-effort` replay paths; no independent Word pagination; broad handlers are not a unified repair/diff semantic engine. Atomic batch rollback is real parity evidence, but it does not by itself bind persistence to a previously reviewed filesystem identity or demonstrate power-loss durability. | A |
| [docx-cli](https://github.com/kklimuk/docx-cli) `3c2e2721ed90cbb42626c270d183a09d3b6d08b0` | TypeScript/Bun, substantial AST, stable locators, annotated Markdown, XML-in-place edits, equations, comments, revisions, raw parts, schema validation, and practical AI benchmark design. | Documentation admits no undo and in-place overwrite. Rendering delegates to Word/LibreOffice/PDFium routes. Raw escape hatches remain necessary for unsupported structures. | A |
| [Office Word MCP Server](https://github.com/GongRzhe/Office-Word-MCP-Server) `a3bbbb6d6167e68cf855d73ef7dc6cd8cfbfedba` | Accessible python-docx MCP tool set for common document construction. | Archived in March 2026; small regression surface; several advanced features are simplified or placeholder-backed; no complete package graph or native layout. | A |
| [word-mcp-live](https://github.com/ykarapazar/word-mcp-live) `c6c76179f66b27846d8f6a822a683e144d9288cb` | Broad live Word COM surface on Windows plus a macOS JXA path; over one hundred MCP tools. | Raw positional indices, weak optimistic concurrency, undo can cross unrelated user edits, macOS undo grouping is a no-op, and equation parity is platform-dependent. | A |
| [SecurityRonin/docx-mcp](https://github.com/SecurityRonin/docx-mcp) `b141be8153eff38ffac838b983ccc32f85f71acb` | Direct ZIP/lxml OOXML work, unusually strong tests and coverage enforcement, comments/notes/revisions and many advanced operations. WordToolkit's historical Python engine adapts this lineage. | Large flat MCP catalog, Python process/runtime, no single lossless semantic graph shared by parsing, repair, rendering, and AI. | A |
| [hongkongkiwi/docx-mcp](https://github.com/hongkongkiwi/docx-mcp) `d3fbbcfd7c93b0403de65d31f733c01b1cb2234f` | Small Rust package with an attractive standalone deployment story. | Source inspection found placeholder feature flags and placeholder rendering/TOC behavior behind broad README claims. Marketing breadth is not implementation evidence. | A |
| [mcp-msoffice-interop-word](https://github.com/mario-andreschak/mcp-msoffice-interop-word) `e50e339f1ac11fde6904addebef8c0b070879160` | Thin TypeScript/winax bridge to desktop Word. | Raw COM enums, basic failure handling, no package model, transactions, version tokens, validation, or semantic locators. | A |
| [OfficeMCP](https://github.com/OfficeMCP/OfficeMCP) `188140dc784f53d66da566696072f47d29fa795a` | Generic access to Office automation. | Its generic tool executes supplied Python with `exec` against COM objects. That is an arbitrary-code-execution boundary, not a safe document API. No detected repository license at the research snapshot. | A |
| [safe-docx](https://github.com/UseJunior/safe-docx) `7e1dc9752e5a9848658045de88c5a88bc80bb1dd` (`0.17.0`) | Serious Apache-2.0 TypeScript competitor: session-backed compact reads, stable IDs, tracked and clean saves, comparison, comments, notes, revisions, layout/export routes, archive guards, 26 MCP tools and an existing `docx-platform-tests` adapter. The current head preserves validated unchanged direct-body block SDTs plus their relationship closure during forced comparison rebuild and adds focused/real-corpus tests. | It does not claim a visual editor, native layout engine or pixel-exact pagination. The new block-SDT slice deliberately rejects mutation, movement, nesting and unsupported ownership rather than flattening them; other unsupported revision/rebuild families and the distinction between validation and Word-open proof remain corpus obligations. | A |
| [LegalRabbit DOCX MCP](https://github.com/LegalRabbit-AI/legalrabbit-docx-mcp) `a1c9be831f0e161c8965392968702e3735680daa` | Plugin metadata and downloadable binaries advertise comments, tracked changes, offline use and token savings. | The repository contains no implementation source or regression tests; the downloadable binaries are roughly 70–105 MB. Architecture, preservation and token claims cannot be audited independently from this snapshot. | C |
| [che-word-mcp](https://github.com/PsychQuant/che-word-mcp) `b59d5f24fb9524b04f0ccac40e6b1abca40adef5` | Native macOS Swift implementation over `ooxml-swift`, with a broad declared capability surface and 41 test files. | One server file exceeds 10,000 lines; direct tools are a small subset of the advertised surface. Script export upgrades only the main document, rich/legacy documents can demote the whole main part to raw data, real fixtures and tracked-change tests are skipped, and `listCustomXmlParts` is still an empty stub. | A |
| [word-mcp](https://github.com/juanocampo400/word-mcp) `16ab829e32e1520e72f2eda5e78e29fb8c99892c` (`0.1.0`) | Accessible Windows Python bridge combining `python-docx` and `pywin32` COM across a broad common-edit surface. | The pinned repository has no tests. Mutations depend on positional indices and direct saves, with no package graph, version tokens, atomic persistence, rollback or preservation proof. | A |

The final 2026-07-23 refresh moved OfficeCLI from `e7916a2...` to `9c78827...` and
safe-docx from `3615e2...` to `7e1dc9...`; docx-cli remained at `3c2e27...`. The
OfficeCLI delta adds Markdown import plus range/index fixes and does not change the
already documented atomic-batch contract. The safe-docx delta materially strengthens
forced-rebuild preservation for direct-body block controls. The neutral 42-scenario
checkpoint below remains intentionally pinned to the older declared revisions until the
same hidden protocol is rerun; current-head claims are not laundered into old benchmark
numbers.

For transaction design, OfficeCLI is no longer a weak comparison target: its primary
skill contract states that v1.0.137+ batches are atomic by default, report every failure
and leave the on-disk file byte-identical when any item fails; `--best-effort` is the
explicit partial-apply escape hatch. The legacy WordToolkit remote gateway previously
had optimistic versioning but still mutated its active engine directly. The current
slice first closed that partial-failure hole for all 33 ordinary mutators through an
isolated, validated clone. `apply_document_operations/1.0` now adds a bounded
heterogeneous remote batch over that complete surface: 1-16 closed-schema operations,
one clone, one final validation and one version advance, with no partial-apply escape
hatch. A three-operation 15-sample Windows point measured 70.901 ms median versus
189.479 ms for three standalone COW calls (-62.58%) and 427 versus 480 compact request
JSON characters (-11.04%). Unlike OfficeCLI's on-disk transaction, this remains an
in-process draft transaction. Its image transport follows the Apps SDK top-level file
parameter constraint through `files` plus `file_index`; complete image staging can still
consume session quota before the document lock, and neither crash-durable draft state,
result-reference syntax nor shared immutable parsed parts exists. Sources:
[OfficeCLI current skill](https://github.com/iOfficeAI/OfficeCLI/blob/9c78827d25d33f53664e68e7ec841d577c763632/SKILL.md) (A);
[OpenAI Apps SDK file handling](https://developers.openai.com/apps-sdk/build/mcp-server#file-handling) (A).

The useful ideas are clear: OfficeCLI's compact gateway and resident mode, docx-cli's
locators and benchmark methodology, SecurityRonin's regression discipline, and live
Word automation for authoritative operations. Their limitations are equally useful:
flat tool catalogs, unsafe generic code execution, binary-only claims, in-place
overwrites, weak versioning, and ASTs that silently flatten what they do not understand.

## Neutral public conformance checkpoint

The Apache-2.0 [`docx-platform-tests`](https://github.com/kklimuk/docx-platform-tests)
harness was pinned at `fe0ee99602e6f982255ecaa2b45d4936a7f46150`. Its protocol-v1
runner supplied the same operation and input DOCX to both adapters without exposing the
assertions or expected output. WordToolkit at `65f75be...` produced 19 `pass`, 2
`invariant-pass` and 21 honest `unsupported` outcomes; safe-docx at `3615e2...`
produced 18, 2 and 22 respectively across 42 scenarios. Neither adapter produced a
failure, execution error, divergent pass or protocol mismatch.

The one-row lead is narrow evidence, not a verdict. WordToolkit uniquely passed the
accept-deleted-table-row and reject-inserted-table-row scenarios; safe-docx uniquely
passed compatibility-mode-15 composition. Both passed safe paragraph-mark merge cases.
The exact pins, environment, protocol, commands, SHA-256, caveats and raw result live in
[`COMPETITOR-BENCHMARK-2026-07-23.md`](COMPETITOR-BENCHMARK-2026-07-23.md) (A).

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

## Content-control and Custom XML binding findings

The Open XML SDK's [`SdtProperties`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtproperties?view=openxml-3.0.1)
inventory shows why one generic `ContentControl` node is too weak: alias, tag, native
ID, lock, placeholder, temporary state, mutually exclusive control types and
[`DataBinding`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.databinding?view=openxml-3.0.1)
all live in the property set. Office 2010 and 2013 add checkbox/entity and repeating
section vocabularies. The engine therefore keeps the semantic `w:sdt` identity but adds
a separate typed binding graph instead of pushing raw property XML into the AI context.

Content controls are not only inline wrappers. The SDK content models for
[`SdtContentBlock`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtcontentblock?view=openxml-3.0.1)
and
[`SdtContentRow`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtcontentrow?view=openxml-3.0.1)
permit tables and recursively nested SDTs/rows. A selection renderer must therefore
normalize table context recursively through the chosen subtree; looking only at the
target's immediate children produces invalid `tbody` placement on legal Word markup.

The Office storage notes for
[Custom XML](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/59d313b6-b9a8-4850-83f1-e87ad9abd509)
and the SDK's
[`CustomXmlPropertiesPart`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.customxmlpart.customxmlpropertiespart?view=openxml-3.0.1)
make the OPC relationship chain the source of truth. Numbered `itemN.xml` filenames are
not identities. A physical data item points to one properties item, whose
[`itemID`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.customxmldataproperties.datastoreitem.itemid?view=openxml-3.0.1)
must be unique across the package. The implementation follows those relationships,
normalizes GUIDs, retains schema-reference metadata and refuses to choose between
duplicate stores.

Real LibreOffice fixtures also bind SDTs to Word's well-defined core and extended
properties stores without physical `customXml` items. Microsoft's
[well-defined Custom XML part notes](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/52769434-bde1-4e81-a128-7001873acb2b)
and Office API documentation identify the core-properties store. The observed extended
store is retained as a producer-backed interoperability fact. Both resolve through exact
content types; neither is fabricated from a missing physical item.

MS-DOCX's Office 2013
[`dataBinding`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/2805f4e9-9333-4e7a-bb56-f1ce0e9e8e25)
extension is not equivalent to the standard property. It can bind rich-text controls by
storing escaped flattened WordprocessingML, while the standard binding is ignored for
rich text and building-block gallery controls. The graph records which dialect was
declared and emits a warning when the standard form is semantically ignored; it does
not decode escaped rich-text markup or claim write support.

Microsoft's repeating-section documentation requires the control's item count to track
the XML elements selected by its binding. WordToolkit therefore joins direct
`repeatingSectionItem` children to the container and reports cardinality mismatch.
The SDK's row-content contract also states that row-level SDT content cannot be mapped;
a resolved row binding remains an error rather than a silently accepted shape.

Arbitrary XPath evaluation was rejected as the public engine contract. The implemented
subset accepts only absolute child-element paths, namespace prefixes and positive
integer positions. Descendant axes, attributes, functions, wildcards and arbitrary
predicates are explicit `XPathUnsupported` evidence. This loses some legitimate Word
bindings, but it does not turn an inspector over attacker-controlled packages into an
unbounded expression runtime. Values are never copied into either the graph or MCP
response.

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

The interoperability notes settle two readback ambiguities that otherwise look like
corruption. Microsoft documents that Word's default
[`m:sty` is italic](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/7022fc09-f507-4341-a711-9ad2e0221434)
and its default
[`m:scr` is roman](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/e555991c-eb42-4af8-b206-028eb7ebb6a2).
Word may therefore remove explicit default-valued elements or merge adjacent runs with
the same effective properties without changing the equation. The native style contract
normalizes only those documented defaults and equivalent sibling-run boundaries; it
still includes effective style, mathematical script, normal/literal flags, text and
structural-control placement. The Open XML SDK's
[`StyleValues`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.math.stylevalues?view=openxml-3.0.1)
enumeration confirms the complete OfficeMath run vocabulary: plain, bold, italic and
bold-italic.

MathML has a different default rule: token elements are upright except a
single-character `mi`, which is italic. Microsoft 365's current
[MathML support table](https://learn.microsoft.com/en-us/office/math/mathml)
also lists all 18 MathML 3 `mathvariant` values and their Word mappings. The native
converter preserves the four weight/slant styles and ten mathematical-alphabet variants
that can be represented through Word linear math plus verified OMML. The four contextual
Arabic forms (`initial`, `tailed`, `looped`, `stretched`) remain explicit loss errors;
claiming support by emitting the unchanged token would be a lie.

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

The 0.30 live adapter closes that specific failure without pretending to solve general
mathematical equivalence. Explicit LaTeX/MathML/OMML differentials converge on U+2146
`ⅆ`; the complete integral operand is enclosed in Word's invisible `〖…〗` group before
`BuildUp()`. Bounded immediate OMML readback then requires every differential text node
to remain under `m:nary/m:e` and the canonical content hash and symbol counts to match.
Real Word regression covers simple, Gaussian, nested and double integrals. The broader
saved-package cross-format algebra and equation repair remain open work.

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

The first neutral checkpoint covers 42 scenarios and 21 operation names through a public
protocol, with exact pins and raw results. It closes the `no public benchmark at all`
gap; it does not close the benchmark obligation. The next corpus expansion must contain
at least:

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

- Run licensed evaluation builds of Aspose, GemBox, Spire, Syncfusion, DevExpress,
  Telerik, TX Text Control and GroupDocs against the same corpus; current entries are
  documentation-backed, not independent benchmarks.
- Exercise Google Docs import/export, Adobe PDF Services and additional cloud editor,
  OCR and PDF-to-DOCX routes at pinned API versions. Build an adversarial corpus to
  measure reading order, tables, equations, floating objects, fonts and accessibility.
- Record Word-version capability probes for COM, JavaScript requirement sets, equation
  imports, field updates, PDF export, and CompareDocuments.
- Extend the protocol adapter set beyond safe-docx to OfficeCLI, docx-cli and other
  source-inspected implementations, then measure round-trip preservation rather than
  relying on README or source inspection alone.
- Separate licensing compatibility from technical capability for every optional adapter.

The matrix is deliberately unfinished. Declaring the search complete while those rows
remain unmeasured would be another polished lie.
