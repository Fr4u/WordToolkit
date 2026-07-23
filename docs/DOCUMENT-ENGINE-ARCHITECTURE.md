# WordToolkit document-engine architecture

Status: foundation under active development on `codex/document-engine-core`.

## Objective

WordToolkit must become a document engine that can inspect, understand, edit, validate,
repair, compare, render, and explain Word documents without destroying structures it
does not understand. Desktop Word remains an authoritative backend where available, but
the engine cannot require a running Word instance merely to know what is inside a DOCX.

The design is constrained by five invariants:

1. **Unknown does not mean disposable.** Unrecognized parts, attributes, namespaces,
   and MCE branches remain source-backed and byte-preserved unless an operation targets
   them explicitly.
2. **AI never edits raw package XML by default.** It plans against compact semantic
   nodes and typed commands with preconditions.
3. **Every mutation is transactional.** The engine can preview, validate, persist
   atomically, and roll back without consuming unrelated Word undo history.
4. **Fidelity is backend-qualified.** `Word-authoritative`, `cross-platform`, and
   `semantic-only` results are different promises and must be labelled as such.
5. **Claims require a corpus.** Feature count is not quality; preservation, layout,
   corruption behavior, token cost, and recovery are measured.

## The fracture in the current codebase

The public native plugin has a strong live Word COM runtime, undo grants, safe range
handles, compact MCP responses, equations, fields, review operations, and a generated
Word object-model catalogue. The historical Python engine has broad direct-OOXML
features and many regression tests. They do not yet share one package graph or semantic
model.

That split causes three forms of rot:

- COM can perform a high-fidelity operation but cannot explain every package-level
  consequence before saving;
- direct XML code can mutate a part but does not have one authoritative graph for
  reachability, preservation, repair, diff, render, and AI planning;
- two feature surfaces can disagree on identifiers, transactions, validation, and what
  “the same document” means.

The new `WordToolkit.Engine` library is the convergence point. It targets plain
`net8.0`; Windows-specific Word automation depends on it, never the reverse.

## Layered model

```text
MCP / CLI / SDK / future UI
        |
AI planner + capability negotiation + compact projections
        |
transactional semantic commands, diff/merge, query, repair
        |
source-linked semantic graph (document meaning)
        |
typed OOXML adapters + opaque extension islands
        |
lossless XML token/source representation
        |
OPC package graph: entries, parts, content types, relationships
        |
ZIP bytes / Flat OPC / storage adapters

Authoritative Word COM | LibreOffice | optional commercial/editor render adapters
run beside the core as capability-qualified execution and verification backends.
```

### Layer 0: bounded storage

Inputs are treated as hostile. Before XML parsing, the reader enforces limits for entry
count, single-entry expansion, aggregate expansion, compression ratio, and metadata XML
size. It rejects unsafe names and never resolves external relationships while parsing.

Storage adapters planned:

- seekable file and stream;
- in-memory snapshot;
- Flat OPC;
- encrypted/protected input handoff to an authorized backend;
- Graph/Drive download and version-aware upload;
- content-addressed cache for repeated inspection.

### Layer 1: lossless OPC package graph

The first implemented vertical slice is under
`native/WordToolkit.Engine/Packaging`. It currently provides:

- every ZIP entry with original name, raw content, compressed/uncompressed length, and
  SHA-256;
- canonical part URIs and content-type Default/Override resolution;
- package-level and part-level relationships with resolved internal targets;
- a deterministic package fingerprint independent of ZIP entry ordering;
- reachability analysis and opaque-part preservation in the immutable snapshot;
- diagnostics for duplicate and case-colliding entries, invalid names, missing or
  malformed content types, duplicate relationship IDs, unsafe or missing targets,
  external relationships, missing root relationships, and orphan parts;
- DTD prohibition and bounded metadata XML parsing.

Current diagnostic namespace:

| Code | Meaning |
|---|---|
| `OPC010` | exact duplicate ZIP entry name |
| `OPC011` | case-insensitive ZIP entry collision |
| `OPC012` | invalid/unsafe ZIP entry name |
| `OPC013` | multiple entries resolve to one canonical part URI |
| `OPC020` | missing content-type manifest |
| `OPC021` | malformed or unsafe content-type manifest |
| `OPC022` | duplicate Default content type |
| `OPC023` | duplicate Override content type |
| `OPC024` | part has no content type |
| `OPC030` | missing package relationship part |
| `OPC031` | malformed relationship part or absent source part |
| `OPC032` | duplicate relationship ID for one source |
| `OPC033` | invalid internal relationship target |
| `OPC034` | internal target part is absent |
| `OPC035` | external relationship recorded but not dereferenced |
| `OPC036` | invalid relationship target mode |
| `OPC037` | relationship ID is not a valid XML ID |
| `OPC038` | relationship type is not an RFC 3986 URI reference |
| `OPC039` | external relationship target is not an RFC 3986 URI reference |
| `OPC040` | part is unreachable from package-level relationships |
| `OPC041` | relationship part has the wrong content type |
| `OPC042` | relationship part illegally owns relationships |
| `OPC043` | internal relationship targets package infrastructure |

The implemented mutation path now preserves untouched entry bytes, supports SHA-256
preconditions, emits deterministic packages when requested, writes to a sibling
temporary file, flushes, validates the result, checks the destination fingerprint again,
and replaces the destination only after the gates pass. It can retain a recovery backup.
Power-loss durability and hostile-process races still require dedicated fault-injection
evidence; they are not inferred from a successful `File.Replace` call.

### Layer 2: lossless XML source model

`XDocument` and strongly typed SDK objects are useful views, but neither is sufficient
as the only source of truth for lossless editing. The engine needs a representation that
can retain:

- namespace prefixes and declaration placement;
- attribute ordering and insignificant whitespace when preservation mode requests it;
- comments, processing instructions, MCE choices/fallbacks, and extension elements;
- byte ranges or token ranges linking semantic nodes back to source;
- original raw XML for untouched subtrees.

Edits should splice only the smallest targeted subtree. Canonical formatting is a
separate opt-in operation; it must never happen merely because a file was opened and
saved.

The first source-model slice is now implemented under `WordToolkit.Engine/Xml`. It:

- retains the original byte array and SHA-256 plus immutable element, attribute,
  namespace, prefix, quote, parent/child and byte-span provenance;
- securely audits with `XmlReader` before building a lexical source map, prohibits
  DTDs, never installs a resolver, and bounds source bytes, decoded characters,
  elements, depth and text;
- maps UTF-8, UTF-16 and UTF-32 in both byte orders, plus runtime-supported
  single-byte XML encodings, without rewriting the declaration or BOM;
- applies ordered, non-overlapping byte patches behind a whole-source hash
  precondition and reparses the candidate before returning it;
- replaces a leaf element's text while escaping XML 1.0 characters, retaining every
  unrelated byte, expanding a self-closing element locally, and adding or correcting
  `xml:space="preserve"` when boundary whitespace demands it;
- updates or inserts one namespaced attribute without changing quote style or rebinding
  an in-scope prefix, and inserts a self-contained child fragment into normal or
  self-closing elements while retaining every unrelated byte.

The current regression lane also parses every typed XML part in 52 bundled DOCX files
produced by Word, LibreOffice, Pandoc, Apache POI and Mammoth, and proves an exact-byte
no-op for each. A separate versioned semantic oracle now fixes exact typed expectations
for nine representative fixtures from those producers: complete node- and dependency-
kind counts plus selected style, numbering, reference, review, section and effective-
formatting facts. That is stronger than crash/no-op smoke, but still not a claim of broad
format parity; the external hostile, Word-version and visual compatibility corpora are
still missing.

Unsupported stateful encodings and unusual UCS-4 orders fail closed. A leaf whose
content contains comments, CDATA, processing instructions, or child elements is also
rejected by the plain-text editor because flattening it would erase source structure.
Those are explicit capability boundaries, not silent normalization. The encoding
detector follows the [XML 1.0 autodetection contract](https://www.w3.org/TR/xml/#sec-guessing),
and the parser uses documented .NET bounds and DTD prohibition rather than assuming
well-behaved input ([`MaxCharactersInDocument`](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.maxcharactersindocument),
[`DtdProcessing`](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.dtdprocessing)).

The first package-wide Markup Compatibility layer now sits on that lossless source.
`WordMarkupCompatibilityGraphBuilder` inventories and evaluates `mc:Ignorable`,
`mc:ProcessContent`, `mc:MustUnderstand`, `mc:AlternateContent`, `mc:Choice` and
`mc:Fallback` across every XML-typed OPC part. It accepts explicit application and
markup configurations, keeps branch selection separate from effective output
reachability, suspends interpretation inside configured application-defined extension
elements, and reports ignored elements/attributes, unwrapping and must-understand
mismatches. Legacy `PreserveElements`/`PreserveAttributes` hints are retained and
identified as pre-fifth-edition advisory state. No preprocessing or transformed XML is
written back. The lazy `inspect_ooxml_markup_compatibility` projection pages stable IDs
and counts while redacting private namespace details and source provenance by default.
Current limitations are serializer/transform output, namespace-changing mutation rules,
automatic Word-version capability profiles and a cross-version Word corpus.

### Layer 3: typed OOXML and extension adapters

Typed adapters project known structures from lossless source without owning unknown
markup. Initial adapters, in dependency order:

1. WordprocessingML document/body/paragraph/run/text;
2. relationships, hyperlinks, images, headers/footers, footnotes/endnotes;
3. styles, themes, numbering, sections, and settings;
4. fields, bookmarks, comments, revisions, permissions, and content controls;
5. OfficeMath;
6. DrawingML, VML, charts, SmartArt, OLE, and embedded packages;
7. citations, bibliography, custom XML, macros, signatures, and protection metadata;
8. Microsoft versioned extensions and strict/transitional normalization views.

Open XML SDK validation is one validator behind this layer, not the entire engine. Word
extensions and semantic contradictions require additional rules.

### Layer 4: source-linked semantic graph

The semantic graph exists for editing, search, diff, accessibility, and AI. It does not
replace the package graph.

Every semantic node carries:

- `NodeId`: stable identifier derived from source part, semantic role, durable anchors,
  local content fingerprint, and disambiguating occurrence;
- `NodeKind`: document, section, paragraph, run, field, equation, table, row, cell,
  drawing, note, comment, revision, content control, reference, and extension island;
- `SourceSpan`: part URI plus XML token/subtree provenance;
- `ParentId` and ordered child IDs;
- effective properties and their provenance: direct, style, inherited, default, theme;
- capability flags describing which backends can inspect, edit, render, or verify it;
- opaque attachments for source that no typed adapter claims.

Identifiers are not raw paragraph indices. Indices decay after every insertion. A
locator may fall back through durable Word IDs, bookmark/content-control identities,
structural ancestry, neighboring fingerprints, and finally an explicit ambiguous match.
The engine must return ambiguity; it must not quietly edit the first similar paragraph.

An initial source-linked semantic projector is now implemented. It recognizes transitional
and strict WordprocessingML, paragraphs, runs, text, tabs/breaks, tables, hyperlinks,
fields, revisions, bookmark starts and ends, comment anchors, content controls, drawings, MCE alternate
content, equations, every nested OfficeMath element, and unknown namespace islands. It
also follows bounded internal relationships from the main part into headers, footers,
footnotes, endnotes, comments and glossary building blocks, while text boxes remain
source-linked inside their containing story. Microsoft describes these as separate
[WordprocessingML stories](https://learn.microsoft.com/en-us/office/open-xml/about-the-open-xml-sdk),
not as one flat `document.xml` stream. Story roots and note/comment/reference nodes expose
relationship or Word IDs; changing a related story does not change existing main-body
node IDs. The projector enforces XML character/element/depth/text/story-part limits, uses
durable Word anchors where they exist, and emits source paths, exact lexical element
ordinals, projected-part inventory and compact text previews. Known views still use
`XDocument` for semantic interpretation, but every projected node is bound back to the
independent lossless source model rather than treating the typed tree as storage.

The first cross-story dependency adapter is `WordSectionGraphBuilder`. It treats
`w:sectPr` as a source-linked section boundary, extracts break, page-size, margin,
column, numbering, orientation and first-page properties, and resolves all six
header/footer slots. A binding records both its defined part and its effective display
part, because an inactive first/even variant falls back to the default story while an
omitted active variant inherits the corresponding definition from the previous section.
The rules follow Microsoft's documented
[`headerReference`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.headerreference)
and [`evenAndOddHeaders`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.evenandoddheaders)
semantics. Missing `sectPr` yields one explicit implicit-default section; duplicates,
wrong relationship types, external targets, malformed settings and limit overflow fail
closed. Lazy `inspect_ooxml_sections` pages this graph without returning document text.

`WordStyleGraphBuilder` is the next dependency adapter. It locates only an exact
transitional or strict styles relationship from the main document, validates the
dedicated content type and `w:styles` root, and parses it through the same bounded,
DTD-free lossless XML layer. The graph types paragraph, character, table and numbering
styles; retains source element ordinals; separates style IDs, display names and aliases;
captures document defaults, latent-style metadata, UI flags and common declared
paragraph/run properties; and records the optional Word 2013+ styles-with-effects part.
Duplicate IDs and ambiguous syntax fail closed. Missing bases, cross-type inheritance,
cycles, broken `next`/`link` references and ambiguous defaults remain visible as bounded
diagnostics, so one damaged style cannot erase the rest of the inventory. Lazy
`inspect_ooxml_styles` defaults to metadata-only paging and makes declared properties,
latent exceptions and inheritance provenance opt-in. Effective node formatting is a
separate resolver because numbering, conditional table styles, theme references,
toggle-property semantics and direct formatting must not be flattened into a dishonest
last-value-wins map.

`WordNumberingGraphBuilder` is the first numbering adapter. It follows only the exact
transitional or strict numbering relationship, validates the content type and
`w:numbering` root, and retains source ordinals for picture bullets, abstract
definitions, instances, levels and overrides. It models `nsid`, multilevel type,
template/name metadata, `numFmt`, `lvlText`, suffix, restart, legal numbering,
paragraph-style bindings, justification and declared paragraph/run properties. A
numbering instance first inherits an abstract definition and may then replace a level
or only its start value, matching Microsoft's documented
[`num`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.numberinginstance)
and
[`lvlOverride`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.leveloverride)
semantics. Numbering-style indirection stays as a source-linked chain rather than being
flattened: `numStyleLink` follows a numbering style's effective `numId`, while
`styleLink` validates the reciprocal concrete definition. Duplicate IDs fail closed;
missing definitions/styles/picture relationships, circular links, mismatched overrides,
recursive `numPr` and levels outside Word's 0–8 range remain bounded diagnostics. Lazy
`inspect_ooxml_numbering` returns compact instance metadata by default and exposes
abstracts, declared levels or one resolved effective level only when requested.

`WordThemeGraphBuilder` is the first DrawingML dependency adapter. It follows only the
main document's exact transitional or strict theme relationship, validates the theme
content type and `a:theme` root, then preserves a typed view of all twelve color slots,
major/minor font collections, supplemental script fonts and the format-scheme inventory.
RGB sources and `sysClr/@lastClr` are deterministic; environment-only system colors,
scRGB/HSL/preset/scheme sources and nested DrawingML transform chains remain explicit
diagnostics instead of invented display values. The model follows Microsoft's
[`ThemeElements`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.themeelements)
and
[`FontScheme`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.themeelements.fontscheme)
contracts. Lazy `inspect_ooxml_theme` pages color, font or format objects and keeps raw
declarations, unknown markup and source ordinals opt-in.

`WordSettingsGraphBuilder` follows only the exact transitional or strict settings
relationship and validates the dedicated content type and `w:settings` root. Its
bounded read graph types view/zoom defaults, theme font languages, compatibility
tuples and legacy switches, document/write protection metadata, document variables,
attached-template and mail-merge relationship references, separators and the remaining
root inventory. Duplicate singleton state and conflicting compatibility modes fail
closed. Protection hash and salt bytes are deliberately reduced to non-secret metadata;
the lazy `inspect_ooxml_settings` action redacts variable values and mail-merge query,
connection and target details unless explicitly requested. Protection is an editing
restriction signal, not a claim that the package is encrypted.

`WordFontTableGraphBuilder` follows only the exact transitional or strict font-table
relationship and validates `w:fonts`. It types font names, alternate names, character
sets, family, pitch, PANOSE and Unicode/code-page signatures plus regular, bold, italic
and bold-italic embedded faces. Each face resolves through an exact font relationship;
the graph records content type, key presence, byte length and an existing package hash,
but never returns the bytes. Duplicate case-insensitive font names, missing or external
targets, invalid relationship types, unsupported font content and orphan relationships
remain bounded diagnostics. Lazy `inspect_ooxml_fonts` is metadata-first and makes
hashes/source ordinals opt-in.

`WordReferenceGraphBuilder` is the first field/reference dependency adapter. It does
not scan sibling runs inside one paragraph. It partitions every projected part into
independent Word stories, including individual notes, comments, glossary entries and
nested text boxes, then walks each story in complete XML document order. A bounded
stack assembles nested `w:fldChar` begin/separate/end sequences across paragraphs;
`w:fldSimple` is represented recursively with its result content; instruction
fragments retain source ordinals; and malformed, orphaned or unclosed field characters
remain explicit diagnostics. Bookmark starts and ends are paired by `w:id` within one
story, cross-paragraph ranges and table-column ranges survive, and case-insensitive
name lookup records Word's last-definition behavior without deleting duplicate source.
The instruction tokenizer recognizes explicit and implicit `REF`, classifies the broad
Word field family, and emits dependency edges for bookmarks, sequences, document
variables, merge fields, citations, index entries, styles and external resources. It
does not evaluate a field. DDE, LINK, INCLUDE, IMPORT, DATABASE and similar instructions
are inert metadata: no process starts and no target is fetched. Lazy
`inspect_ooxml_references` defaults to field-type counts; names, instructions, result
text and dependency keys remain redacted behind opt-in detail, exact filters and paging.

`WordContentControlBindingGraphBuilder` projects source-linked `w:sdt` controls across
all projected stories and types their block/run/row/cell level, lock, placeholder,
temporary state, parent control and Transitional/Strict plus Office 2010/2013 control
kind. It discovers physical Custom XML stores through exact OPC relationships, joins
item-property GUID/schema metadata, and models Word's built-in core and extended
property stores. Standard and Office 2013 `dataBinding` records resolve only a restricted
absolute child-element XPath subset with explicit namespace prefixes and positive
positions. A per-store QName child index makes positional lookup linear in source size
instead of quadratic in the number of bindings; intermediate target counts are bounded
during traversal. Repeating sections retain item topology and store-target cardinality.
Malformed stores, duplicate IDs, invalid mappings, missing targets and unsupported XPath
remain typed diagnostics. Lazy `inspect_ooxml_content_controls` pages controls, stores,
bindings, targets, repeating sections and issues. Values and raw XML never enter its
response; names, binding metadata and source provenance are independent opt-ins.

`WordTableGraphBuilder` projects each source-linked `w:tbl` into an actual logical
table rather than leaving `Table`, `TableRow` and `TableCell` as labels. It maps direct
physical cells through `gridBefore`, `gridSpan` and `gridAfter`, compares every row with
the declared `tblGrid`, constructs exact-span vertical-merge chains, retains legacy
`hMerge` separately, links nested tables, applies the contiguous repeating-header rule,
and types widths, fixed/autofit layout, row property exceptions and cell presentation.
`tblpPr` retains declared and Word-effective positioning, including Word's different
anchor defaults, ignored story contexts and bounded coordinate behavior. Adjacent
same-style sibling tables share a visual-continuation handle without losing separate
identities. The accessibility linter consumes the same header result. Lazy
`inspect_ooxml_tables` pages summary/table/row/cell/merge/issue objects; cell text and
raw XML have no response field, while names, layout and source are independent opt-ins.
The complete contract, sources, limits and scale evidence are in `TABLE-GRAPH.md`.

WordprocessingML theme tokens are resolved only when a deterministic theme source is
available. `majorAscii`/`majorHAnsi` and their minor equivalents select the Latin theme
face. East Asian and complex-script tokens first use the corresponding primary face;
when it is empty, the resolver combines `themeFontLang` with explicit BCP 47 scripts or
a bounded CLDR-derived likely-script map to select an exact supplemental ISO 15924
entry. Region-sensitive Chinese and Punjabi are handled explicitly; an unmappable
language fails rather than borrowing Latin. Theme tint/shade uses the documented HSL luminance transform, with tint
winning when both are present. Word's cached RGB examples expose private quantization
that differs by one or two channels from continuous HSL math, so the resolver preserves
both values and emits `theme_color_transform_word_quantization` on disagreement rather
than burying a magic exception in the algorithm.

The first bounded effective-format slice is now `WordEffectiveFormattingResolver`.
Given a stable paragraph or run ID, it rebinds that node to the exact lexical element in
its source story and applies modeled layers in order: document defaults, the base-first
paragraph-style chain, the resolved numbering level, the base-first character-style
chain, then direct formatting, followed by deterministic theme dereferencing. This
intentionally follows Word's documented behavior,
which applies paragraph styles before numbering styles even though the base standard
states the reverse. Each resolved number retains its instance, requested/effective
abstract definition, level, style-link chain, effective start and source kind.
Each property retains every declaration, source layer, style ID, part, element ordinal
and intermediate result. Theme-derived fonts and colors add their token, language,
script, resolution kind, color slot or font collection/role and theme-part source
ordinal. Concrete fonts are cross-referenced case-insensitively against the font table,
adding declared/embedded/readability state without treating an absent declaration as
proof that a system font is unavailable. Composite `rFonts`, color,
underline and shading elements clear stale inherited sibling attributes while
preserving same-key provenance; a direct concrete font therefore defeats an inherited
theme token. The twelve ISO toggle properties use state transitions at style levels and
absolute values under direct formatting. The resolver deliberately returns coverage
omissions for application defaults, conditional table styles, unmappable or
non-deterministic theme values, Office color quantization, revision views and unmodeled
property elements; it also surfaces Microsoft's documented default-true toggle and
paragraph-style `ilvl` divergences rather than silently pretending the base rules are
Word-perfect. Lazy
`resolve_ooxml_formatting` filters exact property names, bounds each group, and omits
provenance/source evidence unless asked.

`WordSemanticEditor.ReplaceText` is the first typed mutation vertical slice. It requires
the package fingerprint, semantic node identity, source part, lexical element ordinal,
projected text and part SHA-256 to agree; an optional caller-supplied expected value adds
another gate. It changes only `w:t`, `w:delText`, or `m:t`, returns an isolated OPC
mutation builder, and performs no write by itself. The same primitive now feeds a
bounded multi-command planner; broader commands, permission checks and incremental
validation remain Phase 3 work.

### Layer 5: transactional command engine

AI and SDK clients submit typed commands, not arbitrary XML:

```json
{
  "snapshot": "sha256:...",
  "commands": [
    {
      "op": "replace_text",
      "target": "node:...",
      "expected_node_hash": "sha256:...",
      "text": "corrected text",
      "preserve_run_formatting": true
    }
  ],
  "validation_profile": "word-authoritative"
}
```

Transaction phases:

1. resolve locators against the declared immutable snapshot;
2. evaluate preconditions and permissions;
3. produce a compact human/AI-readable plan and estimated impact;
4. apply to an isolated package mutation builder;
5. generate inverse operations and a recovery artifact;
6. run structural, schema, semantic, security, and requested backend validation;
7. serialize to a sibling temporary file and flush it;
8. replace only if the destination version still matches the precondition;
9. optionally open/repaginate/inspect in Word and roll back on failure;
10. record an audit event without storing document content by default.

Live Word commands use Word's custom undo record only for the scoped live mutation.
Package rollback remains independent, so a failure never calls broad `Undo()` across
unrelated user work.

The first transaction slices are implemented for batches of text-leaf, typed style-
definition creation/cloning/exact consolidation/proven-unused deletion, and style-
assignment commands.
`WordSemanticTransactionPlanner` resolves every node against one package fingerprint,
parses each affected part once, rejects duplicate targets, builds one ordered
non-overlapping patch set per part, and returns a compact plan with operation counts,
source ordinals, character counts and byte deltas. It deliberately does not expose
per-text hashes or document content in plan metadata. The plan predicts the complete
result package fingerprint, creates an isolated forward mutation, and can create an
exact part-byte inverse only while the applied package still matches that predicted
fingerprint. Neither forward nor inverse writes a file; atomic persistence remains a
separate gated step.

The style-definition slice inserts new custom definitions into the exact existing
`styles.xml` root without reserializing any prior byte. `create_style` emits a minimal
typed definition with optional same-type `basedOn`, paragraph `next`, quick-format and
UI-priority metadata. `clone_style` copies one existing definition including opaque
extension content, changes only its identity/name, removes `default` and `link`, marks it
custom, and remaps a self-`next` reference. The intermediate package is reprojected and
the new inheritance graph is required to resolve before any assignment is planned. A
`consolidate_style` takes an explicit custom, non-default source and same-type target.
Their complete source definitions must be canonically identical after normalizing only
the style ID, direct name/aliases/revision ID, self-`next` and same-batch relation IDs;
all formatting, UI metadata, opaque elements, attributes, text, comments and processing
instructions remain comparison-significant. Recognized `basedOn`, linked-style,
numbering-style and paragraph/run/table references are type-checked and rewritten across
projected stories, revision snapshots, glossary metadata, `styles.xml` and `numbering.xml`
before the source definition is removed. `delete_unused_style` takes only an explicit
custom, non-default style ID. It removes the exact definition span only after proving that
no surviving semantic, style, numbering, glossary, latent-style, `STYLEREF` or unmodeled
XML consumer refers to it. A single deletion batch may describe a closed graph of mutually
dependent unused styles. `rename_style` accepts an explicit custom, non-default style ID
and a new primary UI name. It changes only the exact `w:name/@w:val` span, or inserts a
missing `w:name`, while leaving `w:styleId`, aliases, formatting and every ID-based
reference untouched. The plan rejects every collision with an existing ID, name or alias
and fails closed around latent-name behavior, risky `STYLEREF`, macros, `altChunk`,
linked-template updates, unmodeled field consumers and `stylesWithEffects`. Multiple
creation, consolidation, deletion, rename and assignment stages compose
only through an exact byte chain. A single final plan joins every payload, predicts one
result fingerprint and retains one exact inverse. Missing sources, duplicate IDs,
wrong-type references, cycles, graph damage, non-equivalence, unmodeled XML consumers,
matching latent-style exceptions, unsafe `STYLEREF`, macros, linked-template updates, `altChunk`, signatures, schema drift
and `stylesWithEffects` packages fail closed. The mirror is blocked rather than silently
letting `styles.xml` and `stylesWithEffects.xml` diverge.

The typed `set_style` slice targets exact paragraph, run, or table node IDs. It accepts
an exact current-style precondition or an explicit absence precondition, requires an
existing compatible paragraph/character/table style with a resolvable `basedOn` chain,
and inserts or updates only `pStyle`, `rStyle`, or `tblStyle`. Missing property containers
are added as the first child through the lossless source model. Duplicate containers,
wrong-type or missing styles, broken inheritance, stale source identities, namespace
prefix collisions, signed packages, plan drift, and new Open XML SDK errors fail closed.
Creation does not accept arbitrary formatting-property payloads; cloning is the current
lossless formatting-preservation path. Stable style-ID changes, deletion of referenced or
built-in definitions, fuzzy consolidation,
automatic linked-style repair, template alignment and conditional table styles remain
future work.

Bulk assignment does not require the model to echo a list of node IDs. A bounded
`set_style_where` command reuses the semantic query engine over the exact projected
snapshot, with one assignable node kind plus optional text, exact-property,
ancestor/descendant, subtree and source-part predicates. The caller supplies a hard
`max_matches` ceiling; zero matches, more matches than authorized, more than 16 selector
commands, more than 200 resolved operations, or overlapping exact/selected targets are
rejected. Canonical typed selector fields and the expanded engine plan jointly determine
the external plan ID, so harmless JSON property reordering is stable while changed
selection intent cannot replay an old approval. Selector expansion returns only counts
and query-plan evidence unless operation details are explicitly requested.

The native MCP exposes this slice without retaining a server-side plan cache. A client
first calls lazy `plan_ooxml_semantic_edits` with the inspected package fingerprint and
commands. To commit, it resubmits the same bounded commands to lazy
`apply_ooxml_semantic_edits` together with the returned deterministic plan ID. Apply
rebuilds the plan from the current file, rejects any fingerprint or plan mismatch, then
uses the version-checked atomic writer. The candidate must also match the plan's
predicted result fingerprint before replacement. A recovery backup is retained by
default, while a no-op does not touch the file. Packages carrying OPC digital-signature parts, content types,
or relationships fail closed because silently leaving an invalid signature would be
corruption disguised as success. This stateless design spends some repeated input
tokens, but avoids retaining document text in a long-lived MCP cache.

The generic package-patch slice now carries exact saved-package changes across process
and machine boundaries. `OpcPackagePatchBuilder` compares two immutable snapshots and
emits canonical add/replace/delete operations ordered by OPC entry name. Every operation
binds the base and result package fingerprints, content types, byte lengths and before/
after SHA-256 values. The artifact stores every referenced before and after uncompressed
entry payload, deduplicated by hash, so `Reverse()` can construct the exact guarded
inverse without consulting either original file. This is payload-exact OPC patching, not
a claim that ZIP compression records, timestamps or central-directory layout are copied
byte for byte.

`OpcPackagePatchCodec` writes deterministic `.wtpatch` ZIP archives containing one
strict manifest and content-addressed payload blobs. Read performs no filesystem
extraction and rejects unknown, missing or duplicate JSON fields; unknown, duplicate,
rooted, traversal or backslash archive names; noncanonical operation order; mismatched
IDs, counts, lengths or hashes; unreferenced blobs; excessive entry/count/expanded-byte
budgets; and compression bombs. An expected patch ID obtained during planning is the
authenticity boundary: hashes alone detect corruption, while the separately retained ID
detects a maliciously rebuilt artifact.

The lazy native path is deliberately split into five actions:

1. `plan_ooxml_patch` recomputes package, semantic and security evidence and returns a
   summary by default;
2. `create_ooxml_patch` requires both exact source fingerprints and the reviewed patch
   ID, writes a sibling temporary artifact, flushes and rereads it, then moves only to a
   new `.wtpatch` path;
3. `inspect_ooxml_patch` validates the bounded archive and pages operation metadata
   without exposing payloads or raw XML;
4. `plan_ooxml_patch_apply` rematerializes the candidate from the exact base, reruns the
   semantic diff and risk analyzer, and compares baseline/candidate Microsoft Open XML
   SDK validation before returning a deterministic apply-plan ID;
5. `apply_ooxml_patch` requires base fingerprint, patch ID and apply-plan ID, then uses
   the existing atomic package writer and retains a recovery backup by default.

Signature invalidation, VBA/macro/OLE/embedded/ActiveX material, external relationship
targets, opaque binaries and newly introduced OPC or Open XML errors are separate policy
gates with false defaults. There is no blanket `force`. Inherited structural/schema
errors may survive when the candidate adds none; newly introduced errors require their
specific authorization. The apply-plan ID is bound to the normalized destination path,
and the candidate's Word main-part content type must match the in-place `.docx`, `.docm`,
`.dotx` or `.dotm` extension. Validation truncation, inability to open the candidate
through the Open XML SDK or a result-type/extension mismatch is non-overridable. The
atomic writer rechecks the destination before and after candidate construction, validates
the predicted result fingerprint and uses a flushed sibling file plus atomic replacement.
A no-op performs no write and creates no backup.

The initial three-way merge slice builds on this transaction boundary instead of
inventing a second serializer. `WordPackageThreeWayMergePlanner` requires an explicit
ancestor and two branches. For each exact OPC entry it applies four deterministic rules:
unchanged on both sides stays unchanged; a one-sided change wins; identical branch
states coalesce; every other state is either proven as a lossless semantic text merge or
becomes a conflict. The semantic path is deliberately narrow: text nodes are joined by
source part and lexical source path, each branch is reconstructed from the ancestor with
the existing hash-guarded text transaction, and its resulting entry must match the
branch byte for byte. Only then can disjoint text changes be composed. This proof keeps
unknown attributes, namespace declarations, extension islands and every unrelated byte
from being silently normalized away.

Conflicts are immutable `wtmc_` records covering divergent additions/modifications,
delete/modify pairs and same-node text edits. They contain bounded metadata and optional
text snapshots, never payloads or raw XML. A candidate exists only after every conflict
has one explicit `use_ancestor`, `use_left` or `use_right` resolution. The resulting
snapshot is converted back into the canonical reversible package patch, so semantic
diff evidence, risk classification and exact inverse machinery are reused rather than
forked.

Lazy `plan_ooxml_merge` returns a summary by default and pages conflicts, entry choices,
result operations, risks or schema errors on demand. `apply_ooxml_merge` recomputes the
entire plan from the three fingerprinted inputs and resolutions, requires a normalized-
output-path-bound `wtmergeapply_` ID, reruns baseline/candidate Open XML validation and
the independent patch risk gates, enforces the result main-part type against `.docx`,
`.docm`, `.dotx` or `.dotm`, then uses the atomic writer's require-new mode. The output
is a new file and cannot overwrite anything. Structural/revision-aware semantic merging
is not yet claimed; those cases remain whole-entry conflicts.

### Layer 6: analysis services

All analysis consumes the same package and semantic graphs:

- validator: OPC, XML, Open XML SDK, Word extensions, semantic cross-part rules;
- linter: implemented initial core/style/accessibility/security rule packs plus future
  numbering-sequence, language, link-text and layout-risk coverage;
- formatter: explicit policy-driven normalization with a preview;
- optimizer: duplicate images/styles, dead relationships, package size, embedded data;
- repair: diagnosed issue -> candidate fix -> risk -> evidence -> postcondition;
- semantic diff/merge: node-aware changes plus source/part fallbacks and conflict nodes;
- search/index: text, fields, equations, metadata, references, comments, revisions, alt
  text, OCR results, and embeddings stored outside the DOCX by default;
- accessibility: headings, reading order, contrast hints, table headers, alt text,
  language, link text, and document metadata;
- policy/security: external links, macros, OLE, signatures, protection, PII, hidden text,
  comments, tracked changes, custom XML, and embedded files.

Repair never means “rewrite until the validator stops complaining.” Every repair has a
rule ID, confidence, destructive-risk label, affected source spans, inverse, and proof
that no unrelated parts changed.

### Unified dependency graph

`WordDependencyGraph` is the first shared cross-domain dependency spine. It does not
flatten the existing typed graphs into anonymous strings. It joins nine proven domains:

- OPC package roots, parts and internal/external/invalid relationships;
- source-linked semantic containment across every projected Word story;
- style definitions, defaults, `basedOn`, `next`, linked styles and explicit paragraph,
  run and table usage;
- abstract numbering, instances, levels, picture bullets, style links and explicit
  paragraph/style numbering references;
- story-scoped fields, bookmarks, nested fields and typed reference targets;
- sections and effective header/footer story bindings;
- classic Transitional/Strict DrawingML charts, series, axes and related package parts;
- content controls, physical and built-in XML stores, resolved binding targets and
  repeating-section items;
- nested table topology and source-linked vertical-merge continuation cells.

Every node and edge has a deterministic content-derived `wddn_` or `wdde_` identity.
Every edge endpoint must exist, even when the target is missing or external; unresolved
evidence is represented as an unresolved node instead of being dropped. Part nodes
record reachability from the package root separately from semantic containment. Source
part URI, XML element ordinal, relationship metadata and semantic node identity remain
available as opt-in provenance.

The builder binds every input graph to one exact package fingerprint, uses constant-time
stable-ID collision checks, enforces node, edge, key and issue budgets, checks
cancellation during traversal, and never executes a field or follows an external
relationship. Coverage is explicit. DrawingML/VML layout, SmartArt, OLE, bibliography
sources, active content, signatures, encryption and co-authoring sessions are still
outside this graph. Office 2016 extended charts are preserved and
diagnosed, but are not projected as classic chart nodes.

Lazy `inspect_ooxml_dependencies` exposes compact edge-kind counts, filtered nodes and
edges, unresolved edges, issues and a bounded one-to-four-hop impact neighborhood. Keys
that may contain part names, bookmark names, field targets or external addresses are
fingerprinted and redacted by default. Source metadata is a separate opt-in. This is the
common substrate for the initial linter and later repair, optimizer, affected-node proof
and query-plan joins; the dependency inspector itself is not any of those engines.
Filtering and paging use one cancellable pass
and retain only the requested page instead of materializing every matching response
object; summary counts retain only one accumulator per fixed edge kind.

### Classic chart graph

`WordChartGraph` projects classic DrawingML chart parts without opening Word or an
embedded workbook. It understands all 16 classic plot families in Transitional and
Strict OOXML, chart references and reachability, series index/order, typed source roles,
formulas, cache metadata, four axis families, cross-axis links, `externalData` and
related packages/images/styles/color styles/chart drawings/theme overrides. Stable
chart and series IDs retain exact package fingerprint and source provenance.

Cached point values are deliberately absent from the public model. The builder counts
and validates point indexes, declared counts and cache shape, then discards the values.
Titles and formulas remain available to trusted callers, but lazy
`inspect_ooxml_charts` redacts both by default and exposes bounded text only through
explicit sensitive opt-in. Source part and relationship metadata use a separate opt-in.
External targets are never followed and embedded packages are never opened.

The builder has independent chart/byte/XML-element/series/source/cache-point/formula/
title/issue limits, cancellation checks and typed projection failures. It reports
unreferenced classic charts and preserved Office 2016 extended chart parts instead of
pretending to understand them. The dependency graph consumes the proven classic layer
through chart, series and axis nodes plus containment and related-part edges. Chart
mutation, workbook synchronization, rendering and SmartArt remain unfinished. The full
contract and evidence are recorded in `CHART-GRAPH.md`.

### Initial document linter

`WordDocumentLinter` is the first analysis engine consuming the shared package,
semantic, dependency, style, numbering, reference, section, theme, settings and font
graphs. Its 18 deterministic rules are divided into core, styles, accessibility and
security packs. They surface existing graph diagnostics plus unbound section stories,
unused styles, groups with equivalent fully modeled declared formatting, direct
paragraph/run formatting, external relationships, directly hidden text, heading-level
skips, missing drawing alternative text, unmarked multi-row table headers and a missing
core document title.

Every materialized finding has a stable rule ID, a package-state-bound `wtlint_` ID,
severity, confidence, bounded evidence, a privacy-safe subject fingerprint and source
provenance. Exact XML byte spans are returned where the source model can prove them,
including relationship entries; source disclosure remains opt-in at MCP. Rule and
finding suppressions are validated and counted. Finding, semantic-node and XML-source
budgets are hard bounds, and every exhausted source budget becomes a visible coverage
omission. `analysis_execution_complete` means the selected implementation ran without
an omission. It is deliberately not the same as `document_coverage_complete`, which
remains false while the dependency graph lists unmodeled domains.

The linter applies a stricter dependency budget than the standalone graph inspector:
100,000 nodes, 200,000 edges and 10,000 graph issues. Exceeding it fails with a typed
package-limit result instead of letting the analysis inherit the standalone builder's
million-node allocation ceiling. These are rejection bounds, not a performance claim;
streaming and incremental lint remain unfinished.

Lazy `lint_ooxml_document` exposes compact summary, paged findings and a paged rule
catalog without starting Word, following an external target or returning document text.
Every fix remains `review_required` and `requires_preview=true`. Only the exact finding
for one existing, unambiguous, empty and lexically safe `dc:title` is currently marked
`implemented=true`. An unused-style finding advertises the separate
`delete_unused_style` command but remains `implemented=false`; lint evidence alone never
authorizes mutation, and the semantic planner re-proves the stricter consumer gates.
`plan_ooxml_lint_repair` re-runs the relevant lint pass against the
expected package fingerprint, binds the finding to its XML element, creates a lossless
single-part candidate and proves that the title finding disappears. The native layer
adds baseline-versus-candidate Open XML validation and binds the reviewed plan to a new
same-extension output path. `apply_ooxml_lint_repair` rebuilds that exact plan, blocks
signed packages and validation drift, and atomically creates the new file without
opening Word or overwriting anything. Missing, duplicate or mixed-markup title elements
and every other finding-bound repair kind fail closed. The formatter and general repair
engine remain unfinished; the optimizer currently contains only the strict style slices.

### Semantic comparison

Saved-package comparison is now a two-layer read-only service. The OPC layer reports
added, removed and modified ZIP entries with size, content type, infrastructure and
projected-part classification. The semantic layer compares source-linked objects across
the main story, headers, footers, notes, comments, glossary entries and text boxes. The
two verdicts remain separate: byte/package equivalence is not semantic equivalence, and
semantic equivalence is not proof that opaque or currently unmodeled markup is equal.

Matching uses a conservative ladder: document/story role, exact node ID, unique durable
identity, unique exact subtree, then bounded sibling-sequence alignment with an explicit
similarity score. Duplicate durable anchors and equal or near-equal contextual candidates
remain unmatched instead of being guessed. Sequence moves are derived from a longest
increasing matched subsequence so an insertion-induced index shift is not mislabeled as
a move; descendant move noise is collapsed beneath the top-level moved object.

Every result reports whether matching was complete, the number and basis of matches,
ambiguity and fallback diagnostics, alignment work, changed projected entries not yet
classified by the semantic vocabulary, deterministic difference IDs and exact package
fingerprints. Text, property values, hashes and source paths are independent bounded
opt-ins. The engine enforces node, alignment-cell, diagnostic, change, processed-text and
captured-text budgets. It never opens Word, emits raw XML, mutates either package or
pretends that the output is a Word tracked-revision document.

This now feeds a generic reversible OPC payload patch and an initial three-way merge,
but not yet a portable semantic-command patch. The current patch artifact binds both
complete package fingerprints and exact entry payloads; it does not claim that each
entry operation is a stable high-level Word intent. Merge adds a common ancestor,
stable conflict records and deterministic explicit resolutions, while automatic
same-part composition is limited to text-leaf branches that reconstruct byte-exactly.
A future semantic patch and fuller merge still need broader typed structural commands,
revision-aware conflict semantics and durable target recovery across Word rewrites.

## Equation engine

Equations require their own semantic AST rather than string substitution. The first
read-only layer is now implemented as `WordEquationGraph`: a source-linked OfficeMath
tree covering all 19 standard OMML object families (`acc`, `bar`, `borderBox`, `box`,
`d`, `eqArr`, `f`, `func`, `groupChr`, `limLow`, `limUpp`, `m`, `nary`, `phant`,
`rad`, `sPre`, `sSub`, `sSubSup`, and `sSup`), plus matrix rows/cells, runs, text,
WordprocessingML containers and preserved extension/unknown nodes. Arguments have
semantic roles such as numerator, denominator, degree, base, limit and function name;
properties are normalized without discarding their source anchors.

The graph distinguishes inline `m:oMath` from display `m:oMathPara`, retains main,
header, footer, note, comment, glossary and text-box story identity, and reads the main
document's `m:mathPr` defaults. Stable equation and node IDs derive from semantic source
identities rather than paragraph indexes. Bounded validation reports malformed argument
cardinality/order, matrix structure, property vocabularies, nested math, Word-invalid
placement, empty equations, adjacent equations Word will merge and preserved extensions.
It does not repair or reinterpret them.

Lazy `inspect_ooxml_equations` exposes compact aggregate, equation, flat-node,
math-paragraph, settings and issue views. Formula text is absent by default, raw OMML is
never returned, source provenance and normalized properties are opt-in, and pages are
capped. The action parses a saved package without opening Word, converting notation or
following external content. This is a canonical OfficeMath read graph, not yet the
cross-format semantic algebra required for loss-aware round trips.

Import paths:

- LaTeX -> canonical math AST;
- UnicodeMath -> canonical math AST;
- MathML -> canonical math AST;
- OMML -> canonical OfficeMath read graph plus lossless source attachment — **initial
  structural implementation complete**;
- OfficeMath read graph -> cross-format canonical math AST — **not yet implemented**.

Export paths:

- canonical AST -> OMML with OfficeMath placement validation — **not yet implemented**;
- canonical AST -> UnicodeMath/LaTeX/MathML with explicit loss diagnostics;
- the separate native live adapter accepts bounded LaTeX, UnicodeMath, Presentation
  MathML or OMML, emits Word linear math, runs `BuildUp`, and immediately verifies
  sensitive result OMML — **initial structural implementation complete**.

The live adapter distinguishes an explicit differential from a generic adjacent
identifier, canonicalizes it to U+2146 `ⅆ`, and wraps the complete integral operand in
Word's invisible `〖…〗` group. Readback verifies that every differential remains inside
`m:nary/m:e`; screenshots alone remain regression evidence, not the internal
representation or mathematical-equivalence proof.

## Review and revisions engine

`WordReviewGraph` is the initial saved-package read model for review state. Standard
`w:comment` definitions are joined to start/end/reference anchors in each projected Word
story. The last paragraph identity then links a comment through `commentsExtended` to
its parent reply and done state, through `commentsIds` to a durable identifier, through
`commentsExtensible` to UTC/intelligent-placeholder/extension and reaction inventory,
and through `people` to author, provider and user metadata. These are separate keys with
separate failure diagnostics; the engine never invents one universal comment ID.

Tracked insertions, deletions, source/destination moves, conflicts, run/paragraph/table/
row/cell/section/numbering property changes and custom-XML/cell review markers are
source-linked per story. Nested revisions retain parents. Named move range markers are
paired and then joined into source/destination moves; `permStart`/`permEnd` markers are
paired with editor/group and table-column scope. Settings expose `trackRevisions`,
`doNotTrackMoves` and `doNotTrackFormatting` without treating them as proof that every
current node is tracked.

Lazy `inspect_ooxml_review` exposes summary, comments, anchors, threads, revisions,
move-range, move, permission, people, settings and issue views. Default output contains
counts, statuses and short fingerprints. Comment/revision text, author/editor/person
values, provider/user identifiers and move names are redacted unless explicitly and
boundedly requested; source metadata is separately opt-in and raw XML is never returned.
The action is parse-only. Accept/reject, comment resolution, merge and structural review
mutations remain live-Word operations or future hash-preconditioned package commands.

## Rendering and fidelity backends

Rendering is a capability interface:

| Backend | Promise |
|---|---|
| Desktop Word COM | Authoritative for that installed Word build, fonts, printer/layout settings, and platform. |
| Word JavaScript host | Word-authoritative for operations exposed by the active requirement set. |
| LibreOffice UNO/headless | Cross-platform fallback; fidelity is measured and labelled, never called identical. |
| Semantic HTML/SVG | Inspection, diff, accessibility, and previews; not pagination authority. |
| Optional Aspose/GemBox/other adapter | Licensed independent renderer/converter with a recorded version and benchmark profile. |

Render results include backend version, operating system, font inventory hash, locale,
page settings, warnings, and a fidelity class. Visual regression compares page count,
text geometry, raster deltas, and object-level anchors where available.

## Low-token AI contract

The MCP surface should stay small even while capability grows. A flat list of hundreds
of tools wastes schema tokens and makes planning brittle. The target gateways are:

- `document.inspect`: compact projections, selectors, paging, and evidence handles;
- `document.query`: semantic search and aggregations;
- `document.plan`: validate intent, resolve targets, estimate impact, return commands;
- `document.apply`: execute an approved plan against snapshot preconditions;
- `document.validate`: named profiles and incremental diagnostics;
- `document.render`: capability-qualified preview/verification;
- `document.capabilities`: lazy schema fragments and backend probes.

The first transport-neutral `document.capabilities` slice is implemented as
`get_wordtoolkit_capabilities` and the native `capabilities --format json` CLI. Both
call the same generator over the embedded local action schema. Native and core action
membership is also loaded from that schema's `native_runtime` registry instead of a
second C# list. The manifest retains the
contract, MCP and runtime versions; publishes deterministic source/action/schema hashes;
reports hard request, active-request, query and page limits; and returns at most 32 sorted
operation summaries containing exposure, an input-schema hash and the four MCP effect
hints. The default page is 12. It never opens Word, reads a package, returns document
content or accesses the network.

The same gateway accepts `view=schema`, while the CLI accepts `--schema`, to return the
exact embedded normative JSON Schema string and its UTF-8 SHA-256. This keeps the
default catalogue small without handing independent clients an unverifiable digest.

The first transport-neutral executable operation is now
`wordtoolkit.inspect_ooxml_package/1.0`. `WordToolkit.Engine.Operations` owns its typed
request, deterministic result, bounded file/seekable-stream execution, stable error
codes and canonical `snake_case` JSON codec. `wordtoolkit-native inspect-package` and
the existing MCP action are thin adapters over that operation. The SDK/CLI path never
constructs the COM host; the MCP host exists for the wider live-action surface, but this
operation does not invoke it or launch Word. Legacy MCP runtime/timing fields remain at
the adapter edge and are stripped from compact canonical data.

Inspection and semantic projection now share exact Word-package identity rules:
Transitional or Strict `officeDocument` relationship, one internal resolved main part,
one of the four extension-compatible Word main content types, and a Transitional or
Strict `w:document` root with exactly one direct `w:body`. A structurally valid OPC
archive with a look-alike relationship URI, empty root or generic XML main part is not
reported as a valid Word package.

This is one proved migration seam, not a claim that all 85 actions already have public
SDK operations. Per-operation JSON output schemas, a generic dispatcher and the
remaining operation migrations are still open work.

This is deliberately honest about what is still absent. `metadata_coverage` reports
zero explicit output schemas, permissions, reversibility records and per-operation
versions until those become canonical source data. Those fields are not inferred from
operation names. Format support is labelled operation-specific, and full input schemas
remain behind `inspect_wordtoolkit_action`. The normative JSON shape is
`schemas/wordtoolkit-capabilities.v1.schema.json`; the runtime embeds and hashes it.

Responses default to summaries and stable handles. Raw XML, full text, binary payloads,
style tables, and object-model catalogues are fetched only on demand. The planner reports
estimated input/output token cost and can choose between live Word, direct OOXML, or a
hybrid transaction based on capability and fidelity requirements.

The first `document.query` slice is implemented as `WordSemanticQueryEngine` and the
lazy native `query_ooxml_semantics` action. It filters semantic kinds, exact properties,
source parts, a stable-node subtree, and strict ancestor/descendant predicates whose
kind and exact-property constraints must hold on one related node. It supports
contains/equals/starts/ends text modes and streams matching across text, field, tab and
break node boundaries instead of flattening the document into one giant string.
Relationship evaluation propagates matching ancestry and descendant presence through
the semantic tree in linear time; it does not perform a per-candidate tree walk.
Results are source-ordered, offset-paged, preview-bounded, and omit properties and
source provenance unless requested.

Repeated queries can now bind to a `WordSemanticIndex` created through lazy
`manage_ooxml_semantic_index`. The immutable index stores postings for semantic kind,
source part and every exact projected property value, chooses the smallest available
posting as the candidate seed, and still re-evaluates all predicates before returning a
match. For a structural predicate it resolves related matches from those postings,
performs one bounded tree propagation, and adds the resulting relationship positions to
the same smallest-seed plan. A package fingerprint is mandatory when the handle is
queried. Cache state is explicitly process-memory-only: four handles, 100,000 nodes per
handle, 250,000 nodes in aggregate, and a 30-minute maximum TTL. Handles are random,
inspectable and releasable;
responses disclose counts and fingerprints but no raw document text. This removes
repeated package projection and most irrelevant-node scans for an agent's multi-query
session. It is not yet the durable encrypted/incremental external index required for a
large repository of documents.

The current native mapping is therefore:

- `document.capabilities` -> core `get_wordtoolkit_capabilities` or native
  `capabilities --format json`, with identical canonical data;
- `document.query` -> lazy `query_ooxml_semantics`, optionally over a handle from
  `manage_ooxml_semantic_index`;
- style-map inspection -> lazy `inspect_ooxml_styles`;
- numbering inventory/effective-level resolution -> lazy `inspect_ooxml_numbering`;
- theme color/font/format inspection -> lazy `inspect_ooxml_theme`;
- settings/compatibility/protection metadata -> lazy `inspect_ooxml_settings`;
- declared and embedded font metadata -> lazy `inspect_ooxml_fonts`;
- story-aware field/bookmark/dependency inspection -> lazy `inspect_ooxml_references`;
- cross-domain package/semantic/style/numbering/reference/section dependency inspection
  -> lazy `inspect_ooxml_dependencies`;
- content-control/store/binding/repeating-section inspection -> lazy
  `inspect_ooxml_content_controls`;
- logical table/grid/merge/nesting/floating-position inspection -> lazy
  `inspect_ooxml_tables`;
- source-linked core/style/accessibility/security lint -> lazy
  `lint_ooxml_document`;
- reviewed source-bound empty-title repair -> lazy `plan_ooxml_lint_repair`, then
  create-new `apply_ooxml_lint_repair` with the exact destination-bound plan ID;
- source-linked comments/threads/people/revisions/moves/permissions -> lazy
  `inspect_ooxml_review`;
- modeled paragraph/run formatting -> lazy `resolve_ooxml_formatting`;
- generic package patch planning -> lazy `plan_ooxml_patch`;
- reviewed portable patch creation -> lazy `create_ooxml_patch`;
- bounded patch integrity/operation inspection -> lazy `inspect_ooxml_patch`;
- patch application evidence and policy planning -> lazy `plan_ooxml_patch_apply`;
- atomic exact package patch application -> lazy `apply_ooxml_patch`;
- guarded ancestor/left/right merge planning -> lazy `plan_ooxml_merge`;
- create-new, destination-bound merge application -> lazy `apply_ooxml_merge`;
- text-only `document.plan` -> lazy `plan_ooxml_text_edits`;
- text-only `document.apply` -> lazy `apply_ooxml_text_edits`;
- typed existing-style `document.plan` -> lazy `plan_ooxml_semantic_edits`;
- typed existing-style `document.apply` -> lazy `apply_ooxml_semantic_edits`.

These schemas stay outside the core catalog, so the default model context does not pay
for them until search/inspection selects the action.

## Template, style, numbering, and reference engines

These are resolvers, not bags of XML helpers:

- templates expose named slots, constraints, repeat regions, data types, and validation;
- styles compute effective formatting and retain provenance through based-on/link chains,
  defaults, latent styles, themes, and direct formatting;
- numbering resolves abstract numbering, instances, overrides, restarts, level text,
  legal numbering, and style links;
- the implemented reference slice pairs bookmark ranges, parses nested complex/simple
  fields and emits typed dependencies for REF/PAGEREF/NOTEREF, SEQ, TOC bookmark
  restrictions, HYPERLINK anchors, variables, merge fields, citations, index entries,
  styles and external-resource fields;
- the unfinished reference layers must still unify element hyperlinks, captions,
  notes, bibliography sources and complete TOC/TOF/TOT semantics, then add safe edits;
- updates are backend-qualified because Word's field evaluator remains authoritative for
  fields it owns.

## Plugin and adapter boundaries

Extensions register capabilities through versioned interfaces:

- package format/storage adapter;
- typed part adapter;
- semantic node projector;
- validator/linter/repair rule pack;
- command handler with inverse generator;
- renderer/converter;
- OCR/extraction provider;
- index/embedding provider;
- policy and telemetry sink.

Plugins run with explicit document permissions and resource limits. Untrusted plugins do
not receive a live COM object, filesystem root, arbitrary process execution, or raw
credentials. Generic `exec`/`eval` tools are forbidden.

## Telemetry and privacy

Telemetry is opt-in and content-minimizing. Allowed defaults are operation name, engine
version, backend capability ID, duration, byte counts, diagnostic codes, and success.
Document text, paths, relationship targets, author names, comments, and binary hashes are
excluded unless a user explicitly enables a debugging bundle. Debug bundles have an
expiry and redaction report.

## Quality gates

No feature is “supported” until it passes the relevant gates:

1. unit and property tests for parser/serializer/command invariants;
2. corrupt-package and fuzz tests with time/memory ceilings;
3. untouched-part and untouched-subtree preservation checks;
4. Open XML SDK and semantic validation;
5. Word open/save/reopen verification when the feature claims Word authority;
6. visual regression for layout-affecting edits;
7. inverse/rollback test with injected failures at every transaction phase;
8. token and latency benchmark for the AI path;
9. compatibility corpus across producers and Word versions;
10. public limitation entry for every known loss path.

## Delivery sequence

### Phase 1 — package truth

- bounded OPC reader and immutable graph — **implemented, initial tests passing**;
- mutation builder, deterministic serializer, atomic file transaction — **implemented,
  initial tests passing**;
- lossless no-op and single-part-edit preservation tests — **implemented, initial tests
  passing**;
- Flat OPC adapter and corruption corpus.

### Phase 2 — semantic spine

- lossless XML source model — **implemented, initial tests passing**;
- read-only paragraph/run projection, typed logical table/grid/merge/floating read graph
  and canonical source-linked OfficeMath read graph — **implemented, initial tests
  passing**;
- stable node identity and compact semantic inspection — **implemented, initial tests
  passing**;
- section/style/numbering/reference adapters and semantic query;
- bounded cross-domain dependency spine — **initial implementation complete for OPC,
  semantic containment, styles, numbering, references, sections, classic charts and
  content-control/Custom XML binding plus nested-table/vertical-merge topology**;
- package-to-semantic provenance tests — **implemented for main-part nodes and first
  text mutation; full-story coverage remains**.

### Phase 3 — safe edits

- command schema, preconditions, plan/apply, inverse patches — **bounded multi-text plus
  heterogeneous style create/clone/exact-consolidation/proven-unused deletion/primary-name rename/exact-or-selected assignment planning,
  package/node/part/property preconditions, one patch set per part, predicted result
  fingerprint and exact part-byte inverse implemented; arbitrary style formatting,
  stable-ID change/referenced-or-built-in delete/fuzzy repair/template alignment, broader commands, permissions, approval and semantic
  inverses remain**;
- style and numbering resolvers;
- fields/references and source-linked review read graph — **initial implementations
  complete; mutation/evaluation layers remain**;
- schema/semantic validation profiles.

### Phase 4 — hard Word structures

- cross-format canonical equation AST, structural mutations and all import/export paths;
- drawings, chart mutation, SmartArt, VML, OLE, Custom XML mutation, macros, and
  signatures;
- accessibility, citations, bibliography, OCR, and policy scanners.

### Phase 5 — fidelity and proof

- Word/LibreOffice/optional renderer adapters;
- visual and semantic corpus harness;
- competitor benchmarks with public fixtures and version pins;
- fuzzing, fault injection, performance, token, and long-run stability gates.

## Definition of “best”

WordToolkit may call itself the best document engine only when public measurements show
that it:

- preserves more unrelated document state than the compared tools;
- completes the same semantic edits with fewer corruptions and repair dialogs;
- reports unsupported or ambiguous behavior instead of silently flattening it;
- gives equal or better Word layout fidelity under an identified backend;
- uses fewer AI tokens for representative tasks;
- survives hostile and malformed packages within bounded resources;
- can prove rollback and identify exactly what changed;
- publishes the corpus, versions, harness, raw results, and remaining failures.

Until then, “best” is a target. Calling it a fact early would be the same stale marketing
the research exposed elsewhere.
