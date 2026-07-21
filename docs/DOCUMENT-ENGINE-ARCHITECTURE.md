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
  `xml:space="preserve"` when boundary whitespace demands it.

The current regression lane also parses every typed XML part in 52 bundled DOCX files
produced by Word, LibreOffice, Pandoc, Apache POI and Mammoth, and proves an exact-byte
no-op for each. That is meaningful smoke evidence, not a claim of broad format parity;
the external hostile and versioned compatibility corpora are still missing.

Unsupported stateful encodings and unusual UCS-4 orders fail closed. A leaf whose
content contains comments, CDATA, processing instructions, or child elements is also
rejected by the plain-text editor because flattening it would erase source structure.
Those are explicit capability boundaries, not silent normalization. The encoding
detector follows the [XML 1.0 autodetection contract](https://www.w3.org/TR/xml/#sec-guessing),
and the parser uses documented .NET bounds and DTD prohibition rather than assuming
well-behaved input ([`MaxCharactersInDocument`](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.maxcharactersindocument),
[`DtdProcessing`](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.dtdprocessing)).

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
fields, revisions, bookmarks, comment anchors, content controls, drawings, MCE alternate
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
separate resolver because numbering, conditional table styles, themes, toggle-property
semantics and direct formatting must not be flattened into a dishonest last-value-wins
map.

The first bounded effective-format slice is now `WordEffectiveFormattingResolver`.
Given a stable paragraph or run ID, it rebinds that node to the exact lexical element in
its source story and applies modeled layers in order: document defaults, the base-first
paragraph-style chain, the base-first character-style chain, then direct formatting.
Each property retains every declaration, source layer, style ID, part, element ordinal
and intermediate result. The twelve ISO toggle properties use state transitions at
style levels and absolute values under direct formatting. The resolver deliberately
returns coverage omissions for application defaults, numbering, conditional table
styles, theme values, revision views and unmodeled property elements; it also surfaces
Microsoft's documented default-true multi-level toggle divergence rather than silently
pretending the base rule is Word-perfect. Lazy `resolve_ooxml_formatting` filters exact
property names, bounds each group, and omits provenance/source evidence unless asked.

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

The first transaction slice is implemented for batches of text-leaf commands.
`WordSemanticTransactionPlanner` resolves every node against one package fingerprint,
parses each affected part once, rejects duplicate targets, builds one ordered
non-overlapping patch set per part, and returns a compact plan with operation counts,
source ordinals, character counts and byte deltas. It deliberately does not expose
per-text hashes or document content in plan metadata. The plan predicts the complete
result package fingerprint, creates an isolated forward mutation, and can create an
exact part-byte inverse only while the applied package still matches that predicted
fingerprint. Neither forward nor inverse writes a file; atomic persistence remains a
separate gated step.

The native MCP exposes this slice without retaining a server-side plan cache. A client
first calls lazy `plan_ooxml_text_edits` with the inspected package fingerprint and
commands. To commit, it resubmits the same bounded commands to lazy
`apply_ooxml_text_edits` together with the returned deterministic plan ID. Apply
rebuilds the plan from the current file, rejects any fingerprint or plan mismatch, then
uses the version-checked atomic writer. The candidate must also match the plan's
predicted result fingerprint before replacement. A recovery backup is retained by
default, while a no-op does not touch the file. Packages carrying OPC digital-signature parts, content types,
or relationships fail closed because silently leaving an invalid signature would be
corruption disguised as success. This stateless design spends some repeated input
tokens, but avoids retaining document text in a long-lived MCP cache.

### Layer 6: analysis services

All analysis consumes the same package and semantic graphs:

- validator: OPC, XML, Open XML SDK, Word extensions, semantic cross-part rules;
- linter: styles, direct-formatting drift, numbering, references, language, layout risk;
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

## Equation engine

Equations require their own semantic AST rather than string substitution. Planned nodes
cover fractions, scripts, radicals, n-ary operators, integrals with differential
placement, delimiters, functions, matrices, equation arrays, cases, accents, bars,
limits, boxes, phantom elements, and text runs.

Import paths:

- LaTeX -> canonical math AST;
- UnicodeMath -> canonical math AST;
- MathML -> canonical math AST;
- OMML -> canonical math AST plus lossless OMML source attachment.

Export paths:

- canonical AST -> OMML with OfficeMath placement validation;
- canonical AST -> UnicodeMath/LaTeX/MathML with explicit loss diagnostics;
- canonical AST -> live Word equation, followed by `BuildUp` and structural inspection.

The renderer must distinguish `d x` as a differential from a generic superscripted or
adjacent identifier. Visual screenshots alone are regression evidence, not the internal
representation.

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

Responses default to summaries and stable handles. Raw XML, full text, binary payloads,
style tables, and object-model catalogues are fetched only on demand. The planner reports
estimated input/output token cost and can choose between live Word, direct OOXML, or a
hybrid transaction based on capability and fidelity requirements.

The first `document.query` slice is implemented as `WordSemanticQueryEngine` and the
lazy native `query_ooxml_semantics` action. It filters semantic kinds, exact properties,
source parts and a stable-node subtree; supports contains/equals/starts/ends text modes;
and streams matching across text, field, tab and break node boundaries instead of
flattening the document into one giant string. Results are source-ordered, offset-paged,
preview-bounded, and omit properties and source provenance unless requested. This is
still an in-memory scan of the main-part graph, not the incremental privacy-controlled
index required later.

The current native mapping is therefore:

- `document.query` -> lazy `query_ooxml_semantics`;
- style-map inspection -> lazy `inspect_ooxml_styles`;
- modeled paragraph/run formatting -> lazy `resolve_ooxml_formatting`;
- text-only `document.plan` -> lazy `plan_ooxml_text_edits`;
- text-only `document.apply` -> lazy `apply_ooxml_text_edits`.

These schemas stay outside the core catalog, so the default model context does not pay
for them until search/inspection selects the action.

## Template, style, numbering, and reference engines

These are resolvers, not bags of XML helpers:

- templates expose named slots, constraints, repeat regions, data types, and validation;
- styles compute effective formatting and retain provenance through based-on/link chains,
  defaults, latent styles, themes, and direct formatting;
- numbering resolves abstract numbering, instances, overrides, restarts, level text,
  legal numbering, and style links;
- references build a dependency graph across bookmarks, fields, captions, notes,
  citations, bibliography sources, TOC/TOF/TOT, and hyperlinks;
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
- read-only paragraph/run/table and OfficeMath projection — **implemented, initial tests
  passing**;
- stable node identity and compact semantic inspection — **implemented, initial tests
  passing**;
- section/style/numbering/reference adapters and semantic query;
- package-to-semantic provenance tests — **implemented for main-part nodes and first
  text mutation; full-story coverage remains**.

### Phase 3 — safe edits

- command schema, preconditions, plan/apply, inverse patches — **bounded multi-text
  planning, package/node/part/text preconditions, one patch set per part, predicted
  result fingerprint and exact part-byte inverse implemented; general commands,
  permissions, approval and semantic inverses remain**;
- style and numbering resolvers;
- fields/references and review graph;
- schema/semantic validation profiles.

### Phase 4 — hard Word structures

- canonical equation AST and all import/export paths;
- drawings, charts, SmartArt, VML, OLE, custom XML, macros, and signatures;
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
