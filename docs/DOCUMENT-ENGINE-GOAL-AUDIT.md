# Document-engine goal audit

Last updated: 2026-07-22. This is the completion ledger for the WordToolkit
document-engine redesign. `Implemented` means code and tests exist in the current
branch. `Partial` means a real slice exists but the stated capability is not complete.
Historical Python behavior does not count as implemented in the new native engine.

The goal must not be marked complete while any required row is `Not started`, `Planned`,
or `Partial`, unless the row is explicitly removed by the user. Documentation or a
vendor claim is not implementation evidence.

## Standards and research

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| ECMA-376 Parts 1–4 and OPC model | Partial | `RESEARCH-MATRIX-2026.md`; bounded OPC graph and tests | Strict/transitional corpus, MCE, URI conformance, signatures/encryption and extension rules measured |
| Microsoft Word extensions | Partial | MS-DOCX/MS-ODRAWXML/MS-OE376 sources recorded | Typed or opaque-preserving coverage with version probes and regression fixtures |
| Word COM, JS add-ins, Graph, Office Scripts, Copilot | Partial | Primary-source comparison recorded | Capability/version probes and measured adapter behavior |
| OfficeCLI, docx-cli and Word MCP servers | Partial | Eight pinned repositories inspected at source level | Shared corpus round trips, token, latency, and safety benchmarks |
| Open XML SDK, python-docx, docx4j, POI, docx.js, Pandoc, Mammoth | Partial | Primary docs and architectural limits recorded | Reproducible representative tests and license/maintenance snapshot |
| LibreOffice UNO and ONLYOFFICE | Partial | Primary API docs recorded | Version-pinned conversion/render corpus measurements |
| Aspose, GemBox, Spire, Syncfusion | Partial | Official documentation recorded; claims marked vendor evidence | Licensed evaluation on shared public fixtures |
| Remaining relevant libraries/services | Partial | Pinned implementation review now covers PHPWord, docxtpl, docxcompose, docx-rs, docx-templates, docxtemplater, Xceed DocX, Open-Xml-PowerTools, addFormula2docx, unoconv, unoserver, pdf2docx and docx2pdf | Cloud converters, remaining material engines and shared-corpus measurements |

## Package, XML, and semantic foundation

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Bounded ZIP/OPC reader | Implemented (initial) | `WordToolkit.Engine/Packaging`; entry/size/ratio/XML limits; cancellation; tests | External fuzz campaign, hostile corpus, memory/latency benchmarks |
| Complete entry/part/content-type graph | Implemented (initial) | Raw bytes, hashes, canonical URIs, Default/Override maps | Full OPC URI conformance and Flat OPC parity |
| Relationship graph and reachability | Implemented (strengthened initial slice) | Root/part relationships, RFC 3986 type/target checks, XML-ID checks, fragment retention, reserved relationship-part/content-type rules, target modes, resolution, missing/orphan diagnostics | Digital-signature/encryption semantics, format-specific cardinality/type rules, repair and broad corpus proof |
| Unknown/opaque part preservation | Implemented (initial) | Raw entry snapshots and deterministic random round-trip smoke | Large mixed-extension corpus with untouched-byte reports |
| Immutable snapshots and fingerprints | Implemented (initial) | Order-independent package fingerprint; read-only model | Content-addressed cache, snapshot lifecycle and cross-platform reproducibility proof |
| Lossless XML token/source model | Implemented (initial) | Raw bytes/hash; lexical element/attribute tree; prefixes, namespace URIs, quotes and exact byte spans; UTF-8/16/32 and single-byte mapping; DTD/size/depth/count bounds; validated non-overlapping patches; BOM/entity/whitespace/comment/CDATA/randomized splice tests; every typed XML part in 52 bundled multi-producer DOCX files parses and no-ops byte-exactly | Auxiliary/versioned story adapters, stateful encoding policy, token-level edits around mixed content, external hostile/version corpus and memory/latency proof |
| Typed WordprocessingML parser | Partial (strengthened) | Read-only main part plus relationship-driven headers, footers, footnotes, endnotes, comments, glossary and nested text boxes; typed section properties and strict/transitional relationship tests | Remaining auxiliary/versioned parts and the full required Word vocabulary |
| Source-linked semantic AST | Partial (strengthened) | Stable node IDs and exact lexical ordinals now span the primary text-bearing stories; story roots and note/comment/reference nodes retain part and relationship provenance | Full document graph, durable locator recovery, ambiguity model, threaded comments and auxiliary-story mutation provenance |
| Stable semantic identity | Partial | `w14:paraId`/`textId`, durable IDs, fallback fingerprints, duplicate occurrence tests | Cross-save, cross-producer, move/edit and ambiguity benchmark |
| Serializer | Partial (strengthened) | Package serializer preserves entry payloads and deterministic mode; lossless leaf-text and structural element remove/unwrap/local-name-rename/replacement patches preserve every unrelated byte and reparse the candidate | General namespace/MCE mutation rules and all typed part serializers |

## Transactions, safety, and recovery

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Entry-hash preconditions | Implemented (strengthened initial slice) | Mutation builder plus XML source hash, package fingerprint, semantic node, source ordinal and expected-text gates | General semantic command predicates and destination/cloud version preconditions |
| Atomic file persistence | Implemented (strengthened initial) | Sibling temp, flush, validate, recheck, replace, optional backup; merge has a race-safe require-new mode that refuses every overwrite | Power-loss and filesystem fault injection across supported platforms |
| Rollback | Partial (strengthened) | Candidate rejection leaves original unchanged; backup path; text and review transactions retain exact original-part inverses; portable `.wtpatch` artifacts store every changed before/after OPC entry payload and expose an exact reverse patch guarded by the result fingerprint; live Word undo grants exist | General semantic inverses and injected failure proof for every transaction phase |
| Multi-command document transaction | Partial (text/review/patch/merge slices, native plan/apply) | Bounded text edits and selective review decisions resolve against one snapshot and retain inverse payloads. The generic OPC patch slice creates deterministic portable add/replace/delete artifacts between two snapshots. The initial three-way merge composes proven disjoint lossless text commands or requires explicit conflict choices, then turns the candidate back into the same reversible patch/risk transaction | Broader heterogeneous semantic commands, structural/revision-aware merge, permissions/approval policies and a unified validation profile across every command family |
| Optimistic concurrency | Partial (strengthened) | Forward plans require exact package fingerprints and part hashes; portable patch apply binds base/result fingerprints, patch ID and deterministic apply-plan ID. Merge binds ancestor/left/right fingerprints, every resolution and a normalized new output path. The atomic writer rechecks existing destinations or enforces nonexistence through the final move; live document versions exist | File identity/version integration, broader filesystem race/fault injection and Graph/Drive ETag support |
| Security policy | Partial (strengthened) | ZIP/XML bounds, DTD ban, external links never fetched, MCP redaction; patch archives reject unsafe/duplicate paths, unknown/duplicate manifest fields, hash/length drift, expansion bombs and unreferenced payloads. Patch and merge apply have separate fail-closed authorizations for signature invalidation, macro/OLE/ActiveX, external relationships, opaque binaries and new errors; unresolved merge conflicts, validation truncation/SDK-open failure and type mismatch are non-overridable | Protection enforcement, explicit signature verification/removal/resign workflow, encryption adapter, sandboxed backends and threat-model audit |
| Privacy/content-minimizing telemetry | Partial foundation | Text/review/patch/merge summaries omit document text and payloads; merge conflict previews and hashes are separate opt-ins; review selection accepts redacted author fingerprints; MCP plan/apply is stateless and retains no server-side document-content cache | Opt-in telemetry implementation, broader redaction tests, expiry and debug-bundle audit |

## Document intelligence and editing

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Compact inspect | Implemented (strengthened initial slice) | `inspect_ooxml_package`, `inspect_ooxml_semantics`, lazy `query_ooxml_semantics`, `inspect_ooxml_sections`, `inspect_ooxml_styles`, `inspect_ooxml_numbering`, `inspect_ooxml_theme`, `inspect_ooxml_settings`, `inspect_ooxml_fonts`, `inspect_ooxml_references`, `inspect_ooxml_dependencies` and filtered `resolve_ooxml_formatting`; projected-part inventory, privacy redaction, fingerprints, exact filters, per-field/item bounds and offset paging; field-heavy TOC fixtures enforce sub-5000-character default reference and dependency responses | Opaque continuation tokens, remaining auxiliary/versioned parts and a representative cross-action token benchmark suite |
| Semantic query/search | Partial (strengthened initial) | Source-ordered kind/property/part/subtree selectors across main, header/footer, note, comment, glossary and text-box stories; streaming contains/equals/starts/ends matching crosses run/field/tab/break boundaries; bounded optional previews/properties/provenance | Fields/math/metadata-aware predicates, structural relationship joins, aggregations and query planner |
| Document dependency graph | Partial (initial cross-domain spine) | Deterministic `wddn_` nodes and `wdde_` edges join OPC reachability, semantic containment, explicit paragraph/run/table styles, style inheritance/link/defaults, numbering instances/abstracts/levels/picture bullets, field/bookmark targets and section header/footer bindings. Missing and external targets remain explicit; every endpoint, input fingerprint and resource budget is checked. Lazy inspection redacts keys/source by default and provides bounded impact traversal | DrawingML/VML, charts/SmartArt, OLE, custom-XML bindings, citations/bibliography sources, macros/signatures/encryption, co-authoring sessions, semantic query joins, incremental invalidation and mutation-impact policies |
| Indexing | Not started | None | Incremental external index, invalidation and privacy controls |
| AI planner | Partial foundation (strengthened) | Deterministic bounded text plans, typed review-decision plans, exact package-patch plans and three-way merge plans report counts/impact without returning content. Merge exposes stable conflict IDs, explicit three-choice resolutions, independently authorized risk classes, exact identities and baseline/candidate validation; lazy stateless apply requires reviewed IDs and all source fingerprints | Natural-language intent -> evidence -> broader heterogeneous typed plan -> cost/risk -> richer permissions and approval policy |
| Typed semantic mutations | Partial (text + review transaction slices) | Text plans edit source-bound `w:t`, `w:delText` and `m:t`; review plans accept/reject supported insertion/deletion/conflict wrappers, complete moves and property snapshots, plus numbering-change acceptance, inserted rows, cell-insertion acceptance and cell-deletion rejection. Both preserve unrelated bytes, predict the result fingerprint and retain exact part-byte inverses; unsupported merges/table-grid/numbering-reconstruction/custom-XML cases fail closed | Paragraph/run/table/field/math command set, remaining review vocabulary, affected-node proof, permissions and durable recovery artifacts |
| Validator | Partial (strengthened) | OPC diagnostics plus exact serialized review, patch and merge candidate reparse with bounded Microsoft Open XML SDK baseline/candidate comparison; new errors block by default, while validation truncation or SDK-open failure hard-blocks apply | Unified OPC/schema/extension/semantic/Word-open profiles and incremental validation |
| Linter | Partial (initial native rule packs) | `WordDocumentLinter` evaluates 18 deterministic core/style/accessibility/security rules over one fingerprint-bound typed graph set. Findings carry stable rule/finding IDs, severity, confidence, privacy-safe subject fingerprints, bounded evidence, optional exact XML byte spans, validated rule/finding suppressions and explicit fix metadata. Lazy `lint_ooxml_document` is summary-first, paged, source-redacted by default, never opens Word or follows external targets, and distinguishes execution completeness from incomplete document-domain coverage. One safe empty-title shape reports an implemented reviewed fix | Broader style/numbering/reference/language/link/layout/security rules, corpus calibration, incremental execution and plugin rule registration |
| Formatter | Not started | Architecture only | Explicit previewed policies; no incidental formatting on save |
| Optimizer | Not started | Architecture only | Duplicate/dead-part/image/style/package optimizations with preservation proof |
| Repair engine | Partial (first fail-closed slice) | `WordLintRepairPlanner` accepts only a package-bound empty-title finding with exactly one existing, empty, leaf `dc:title`. It verifies the package/projection fingerprint, performs a lossless single-part edit, predicts the result fingerprint, reparses the candidate, proves the target finding disappeared and retains an exact inverse. Lazy plan/apply actions add destination binding, digital-signature blocking, baseline-aware Open XML validation and create-new atomic output; raw title/XML are not returned | Missing-title part creation, every other repair rule, dependency-aware repair sets, ranking, richer authorization/recovery, Word round-trip and corpus proof |
| Semantic diff | Partial (strengthened initial slice) | Native two-layer saved-package comparison reports OPC entry changes separately from source-linked semantic differences across all projected Word stories. Matching ambiguity, fallbacks and unclassified changes remain explicit. The exact snapshots feed both a reversible OPC patch and the result evidence for guarded three-way merge | Richer typed property vocabulary, review-friendly rendering, semantic-command patch format, Word `CompareDocuments` adapter, benchmarked accuracy and cross-version corpus |
| Three-way merge | Partial (initial lossless slice) | Explicit ancestor/left/right planner deterministically selects one-sided and identical entry states; disjoint source-linked text-leaf edits in one part auto-compose only after each branch reconstructs byte-exactly. Stable `wtmc_` conflicts cover divergent add/modify/delete and same-node edits; every conflict requires an explicit ancestor/left/right choice. Result becomes a reversible patch and passes risk, type, OPC and baseline-aware SDK gates before create-new atomic persistence | Typed structural and revision-aware conflicts, dependency edges, durable target recovery across Word rewrites, per-node resolution policies, visual/Word round-trip corpus and merge artifact format |

## Word feature systems

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Paragraphs/runs/tables | Partial | Source-linked projection plus first lossless text-leaf edit; live COM editing | Structural typed edits, effective properties, merge/split and layout tests |
| Sections/headers/footers/notes | Partial (strengthened) | Primary story roots/bodies/references and lossless text edits; section boundaries/page properties plus first/even/default explicit/inherited/blank/effective bindings; unbound-part inventory | Section structural edits, link-to-previous mutation, separator/numbering semantics and layout proof |
| Styles/themes/direct formatting | Partial (typed graphs + modeled effective slice) | Source-linked style, theme, settings and font-table inventories; document defaults, latent metadata, four style types, all 12 theme colors, major/minor and supplemental fonts, `themeFontLang`, embedded-face metadata, format-scheme counts and inheritance diagnostics; paragraph/run resolver applies defaults, base-first paragraph styles, effective numbering levels, character styles and direct formatting, then derives deterministic theme fonts/RGB values and font-table provenance with explicit ambiguity. The initial linter reports unused styles, groups with equivalent fully modeled declared formatting and direct paragraph/run overrides | Add conditional table/revision/application-default resolution, exhaustive Word locale/version substitution behavior, exact Word color quantization, broader drift rules, safe refactor and template alignment |
| Numbering/lists | Partial (typed graph + effective-level slice) | Exact relationship/root validation; source-linked picture/abstract/instance/level/override inventory; `numStyleLink`/`styleLink` chains, start overrides, corruption diagnostics, compact MCP inspection and effective formatting integration | Counter-state traversal across paragraphs, restart semantics in sequence, label rendering, structural edits, repair/rebuild and layout proof |
| Fields/bookmarks/cross-references | Partial (typed read graph) | Story-scoped paired bookmark ranges; nested complex and simple field parser across paragraphs; explicit/implicit REF tokenizer; source-linked parent/child fields; typed dependency edges and corruption diagnostics; redacted lazy inspection; live allowlisted writes remain separate | Full field grammar/evaluation policy, unified element hyperlinks/notes/captions, update capability, safe structural edits and Word round-trip/layout proof |
| TOC/TOF/TOT/captions | Partial foundation | TOC/TC/SEQ field classification and TOC bookmark-restriction edges now enter the reference graph; existing live actions/historical tests | Typed switch/options AST, caption and style dependencies, TOF/TOT distinction, backend-qualified field update, layout and round-trip tests |
| Comments/threaded comments/revisions | Partial (typed read graph + bounded mutation) | Source-linked comment/thread/person graph and authorship-linked revision graph remain redacted; saved-package plan/apply selectively accepts or rejects supported wrappers, complete moves and property snapshots and handles numbering-change acceptance, inserted rows, cell-insertion acceptance and cell-deletion rejection by revision ID or author fingerprint, with exact inverse and SDK candidate validation; live review remains available | Remaining paragraph/table/numbering/custom-XML transforms, full reaction/comment mutation, merge and accept/reject proof across Word versions |
| Content controls/custom XML | Partial | Content-control projection; unknown part retention | Binding graph, repeats, locks, data update and lossless custom XML edits |
| Equations/OfficeMath | Partial (strengthened read graph + live authoring gate) | Source-linked graph covers all 19 standard OMML objects, argument roles, matrix rows/cells, runs/text, display paragraphs, main math defaults, Strict markup, story boundaries, invalid placement and preserved extensions; stable equation/node IDs; compact redacted lazy inspection. The native live adapter securely converts LaTeX/UnicodeMath/MathML/OMML, protects integral and limit-operator boundaries, reconstructs five Word mathematical-alphabet families, scopes differential placement to integral-owned operands and rolls back on canonical readback drift. `\mathbf` and `\boldsymbol` cross Word build-up through internal balanced sentinels. MathML inheritance/token overrides preserve all four weight/slant styles plus ten representable alphabet variants; supported OMML structures preserve every `m:sty` value independently from recognized `m:ctrlPr/w:rPr` bold/italic controls, while unsupported structures fail. Independent semantic/style-contract readback normalizes Word's documented italic/roman defaults and equivalent sibling-run coalescing while retaining text, normal/literal flags, script and control placement. Nested fractions, radicals, delimiters, n-ary objects and mixed weights pass real Word. Word-surviving U+2003/U+2005 spacing keeps `cases` text readable and is part of the readback contract. Forced real-Word gates pass the 48-family atlas, all 112 registered symbol commands, all 20 named functions, ten delimiter forms, all 14 representable MathML variants, styled alphabets and an ordinary derivative | Unified cross-format semantic AST, safe structural mutations, unsupported OMML structures, contextual Arabic MathML variants, general loss-aware round trips, mathematical-equivalence diagnostics and broader versioned Word visual proof |
| DrawingML/VML/images/text boxes | Partial (strengthened) | Drawing markers and opaque bytes; nested `w:txbxContent` is a source-linked semantic boundary with editable text; live image operations | Typed anchors/layout/wrap/group/geometry model and render corpus |
| Charts/SmartArt/OLE/embedded packages | Not started | Opaque retention plus conservative patch-risk classification: OLE/embedded-package and ActiveX/control changes require an explicit active-content authorization; unclassified binaries use a separate gate | Typed inspection/edit where safe, extraction policy, rendering and deeper security analysis |
| Citations/bibliography | Partial lexical foundation | CITATION/BIBLIOGRAPHY field classification and citation-key dependency edges | Bibliography source part, style/locale model, validation, rendering and reference updates |
| Templates/mail merge | Partial metadata foundation | Settings graph exposes bounded mail-merge mode, destination, SQL/source relationship references and redacted connection/query fields; historical generation remains separate | Typed slots/regions/constraints/data validation, relationship-type validation and repeatable execution |
| Macros/signatures/protection/encryption | Partial policy foundation (strengthened) | Raw parts retained; settings distinguish protection metadata from encryption. Generic patch apply detects VBA/macro content and OPC signature material, requires separate active-content/signature-invalidation authorizations, and never claims cryptographic validity after mutation | Cryptographic signature verification/removal/resign workflow, protected-operation enforcement, encrypted-package adapter and safe handoff |
| Accessibility | Partial (initial native rules) | Source-linked rules report a first heading below level one, skipped heading levels, DrawingML/VML objects without bounded title/description metadata, multi-row tables without a repeating first-row header and a missing core document title | Language, reading order, link text, contrast, decorative-object semantics, merged-table headers, false-positive calibration and Word Accessibility Checker comparison corpus |
| OCR | Not started | None | Pluggable OCR with provenance, confidence, language and privacy controls |

## Rendering and conversion

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Desktop Word authoritative backend | Partial | Existing live COM save/validate/PDF and real-Word acceptance | Document-engine adapter with version/font/layout capability records |
| LibreOffice backend | Not started in new engine | Historical renderer | Isolated adapter, version probe and shared visual corpus |
| Semantic HTML/SVG preview | Not started | None | Source-linked accessible preview with explicit non-authoritative label |
| PDF/image rendering | Partial outside new core | Existing Word PDF export and historical Poppler path | Unified backend interface and object/page visual regression |
| Optional commercial/editor adapters | Not started | Research only | Licensed adapters with isolated capability and benchmark records |
| Layout diagnostics and visual diff | Partial outside new core | Existing live layout diagnostics | Page/object geometry, raster deltas, font inventory and reproducible environment |

## Extensibility and operations

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Plugin architecture | Not started | Architecture interfaces listed | Versioned registrations, permissions, resource limits and compatibility tests |
| Storage/cloud adapters | Not started | Local file/stream only | Flat OPC, Graph/Drive versioned upload, cache and remote authorization boundaries |
| Capability negotiation | Partial | Lazy action catalogue and Word COM member capabilities | Unified backend/document/feature matrix with runtime probes |
| Telemetry | Not started in engine | Existing runtime performance fields only | Privacy-safe sinks, opt-in controls, retention and failure diagnostics |
| Observability/audit log | Not started | Mutation results only | Correlation, command evidence, hashes without content, recovery references |

## Exact native Word/OOXML coverage ledger

This table mirrors every structure named in the goal. `Opaque` means its bytes survive;
it does not mean the engine understands or can edit it.

| Explicit structure | State in new engine | What remains |
|---|---|---|
| package relationships | Implemented (strengthened initial slice) | Format-specific relationship constraints, signature transforms, repair and broad corpus proof |
| content types | Implemented (initial) | MIME/parameter validation and repair |
| `document.xml` | Partial (strengthened) | Main part is projected through the lossless byte-span model and supports guarded leaf-text splice; full typed vocabulary remains |
| `styles.xml` | Partial (typed read graph) | Defaults, latent metadata, four style types, inheritance diagnostics, theme integration and modeled effective formatting exist; conditional table behavior and mutations remain |
| `numbering.xml` | Partial (typed read graph) | Picture/abstract/instance/level/override/style-link graph and one-level effective resolution exist; sequential counters, mutations and repair remain |
| theme | Partial (typed read graph + effective resolution) | Exact relationship/content/root validation; all twelve color slots; system fallback, transform and environment diagnostics; major/minor primary and supplemental fonts; `themeFontLang`-driven explicit/likely script selection; format-scheme inventory; compact MCP views; deterministic theme font/RGB provenance in effective formatting | Exhaustive Word locale/version substitution behavior, full DrawingML color transform evaluation, exact Word quantization, mutation and visual proof |
| settings | Partial (typed read graph) | Exact relationship/content/root validation; bounded view/zoom/defaults, `themeFontLang`, compatibility profile and derived mode, legacy switches, protection metadata, document variables, attached-template and mail-merge references, separators and root inventory; compact redacted MCP views | Broader settings vocabulary, relationship-specific validation, typed mutations, version behavior and Word round-trip proof |
| fontTable | Partial (typed read graph) | Exact relationship/content/root validation; font identity/classification/PANOSE/signature metadata; four embedded faces; content-type/key readability checks; duplicate/orphan diagnostics; compact byte-free MCP views and effective-format cross-reference | Font substitution/fallback engine, obfuscation/deobfuscation workflow, mutation, licensing policy and measured render portability |
| comments | Partial (typed read graph) | Standard bodies/IDs/authorship, story-scoped complete/point/damaged anchors, text counts/fingerprints/source links and guarded lossless text edits; comment-state structural mutation remains |
| commentsExtended | Partial (typed read graph) | Last-paragraph `paraId` joins, reply/root links, done state, durable-ID joins, extensible UTC/placeholder/extensions/reaction inventory and bounded corruption diagnostics; write/version compatibility remains |
| revisions | Partial (typed read graph + bounded mutation) | Authorship/date/text/property/nesting/source links span insertion/deletion/move/conflict/property/cell/custom-XML kinds; selective lossless decisions cover supported wrappers, complete moves, property snapshots, numbering-change acceptance, inserted rows, cell-insertion acceptance and cell-deletion rejection with exact inverse; paragraph merges, table-grid/vertical-merge/numbering reconstruction, custom XML and full vocabulary remain blocked |
| tracked changes | Partial (typed read graph + bounded mutation) | Revision inventory, status, named move pairs and tracking settings feed fingerprint-guarded plan/apply by stable revision ID or redacted author fingerprint; nested/move cascades are explicit and Microsoft SDK candidate validation is baseline-aware; merge and unsupported structural decisions remain |
| bookmarks | Partial (typed read graph) | Start/end markers are source-linked; ranges pair by `w:id` per story across paragraphs; duplicate case-insensitive names, missing/orphan ends and table-column ranges are diagnosed; safe edits remain |
| hyperlinks | Partial (strengthened read graph) | Semantic element node/relationship ID plus local/external HYPERLINK-field dependency edges; element and field forms are not yet unified and edits remain |
| fields | Partial (typed read graph) | Nested complex begin/separate/end parser, recursive `fldSimple`, bounded tokenizer, field-family classification, source links, parent/child graph, cached-result bounds and dependency edges; evaluator/update/safe edits missing |
| TOC | Partial foundation | TOC/TC fields and `\\b` bookmark dependencies are recognized; options/styles/result refresh and layout semantics remain |
| footnotes | Partial | Story container/items/references and lossless text edits; separator and numbering semantics missing |
| endnotes | Partial | Story container/items/references and lossless text edits; separator and numbering semantics missing |
| OfficeMath / OMML | Partial (typed read graph + bounded live verification) | All standard object families, roles, normalized properties, source anchors, settings, display/inline placement and malformed/extension diagnostics are modeled and paged; raw OMML and formula text are hidden by default. Live sensitive equations are read back under XML bounds and checked by canonical hash, symbol count, integral-owned differential ancestry and semantic style/control placement without returning OMML. Script, Fraktur, double-struck, sans-serif and monospace Latin runs are reconstructed from native `m:scr` properties. LaTeX, MathML and OMML now author and verify all four native `m:sty` values plus separate `m:ctrlPr/w:rPr` control bold/italic, with documented default normalization and no exposed build markers | Unified cross-format semantic algebra, serializers, mutation/repair, equation numbering integration, mathematical equivalence, contextual Arabic MathML variants and broader Word-version round-trip/visual proof |
| sections | Partial | Source-linked boundaries, break/page/margin/column/numbering properties and header/footer inheritance graph; structural edits missing |
| headers | Partial (strengthened) | Related parts, text edits and effective default/first/even bindings across sections; link mutation and layout missing |
| footers | Partial (strengthened) | Related parts, text edits and effective default/first/even bindings across sections; link mutation and layout missing |
| page layout | Not started | Section/page properties, backend pagination and diagnostics |
| page breaks | Partial | Generic break nodes; break kind and pagination semantics missing |
| tables | Partial | Read-only row/cell projection; grid/merge/style/layout edits missing |
| floating tables | Not started | Positioning, wrapping, anchors and layout |
| floating images | Partial | Drawing marker/opaque bytes only; anchor/wrap/geometry model missing |
| DrawingML | Partial | Drawing nodes/opaque source; typed geometry/effects/relationships missing |
| SmartArt | Opaque only | Diagram data/layout/colors/styles and rendering |
| charts | Opaque only | Chart/workbook/series/style model and edits |
| diagrams | Opaque only | Typed graph/layout relationships and render |
| embedded Excel | Opaque only | Safe inventory/extraction, workbook linkage and policy |
| embedded Visio | Opaque only | Safe inventory/extraction, relationships and rendering |
| OLE objects | Opaque only | Type detection, extraction policy, preview and security |
| VBA | Opaque only | Inventory, signature/policy, never implicit execution |
| custom XML | Opaque only | Item/properties/SDT binding graph and safe edits |
| custom properties | Opaque only | Typed property values, linkage and serialization |
| document variables | Partial (typed read/redacted inspect) | Bounded settings variable names and values; names/values hidden by default in MCP | Field dependency graph, typed mutation, policy and targeted disclosure |
| glossary | Partial | Glossary story and building-block names/GUIDs are projected and text-editable; typed insertion and gallery/category behavior missing |
| content controls / SDT | Partial | ID/tag projection; properties, bindings, repeats, locks and edits missing |
| mail merge | Partial (metadata read graph) | Settings destination/mode and relationship references plus redacted query/connection metadata | Field/data-source graph, relationship validation, regions, policy and deterministic execution |
| macros | Opaque only | Same VBA policy gap; no execution surface |
| signatures | Opaque only | Signature origins, validation, invalidation and resign workflow |
| encryption | Not started | Encrypted OOXML detection, authorized decrypt/encrypt adapter |
| permissions | Partial (typed read graph) | Story-scoped `permStart`/`permEnd` pairing with editor/group and table-column scope plus orphan/duplicate/reversed diagnostics | Mutation enforcement, authorization policy and Word-version proof |
| protection | Partial (metadata/policy only) | Document/write-protection modes and algorithm metadata are typed; secrets never returned; editing restriction is explicitly not treated as encryption | Permission-range integration, mutation enforcement, password workflow, authorized encryption adapter and Word probes |
| revision IDs | Partial (strengthened) | Stable graph IDs, native `w:id` values, nested parents and move-range links are distinct from comment paragraph/durable IDs; duplicates and unresolved links are diagnosed | Cross-document/version identity, merge collision policy and mutation-safe ID allocation |
| style inheritance | Partial | Base-first `basedOn` graph, default selection, link diagnostics, modeled property provenance and deterministic theme dereferencing exist; conditional table/version behavior and mutations remain |
| numbering inheritance | Partial | Abstract/instance/full-level/start override and numbering-style indirection resolve with provenance; paragraph-sequence counters, restart execution and edits remain |
| XML namespaces | Partial (strengthened) | Prefixes, declaration placement, expanded element/attribute names and untouched bytes are retained; general namespace-changing edits remain |
| compatibility mode | Partial (typed read graph) | Bounded `compatSetting` tuples, legacy switches and explicit derived `compatibilityMode` with duplicate/conflict diagnostics | Versioned behavioral profiles, broader setting interpretation and Word probes |
| Word version differences | Not started | Versioned capability profiles and corpus |
| co-authoring metadata | Partial read-only | People author/provider/user records, comment thread/durable/extension/reaction inventory and revision author links; collaboration sessions, live presence, change logs and merge semantics remain opaque |

## Exact semantic-operation ledger

These are not aliases for string replacement. Each requires a typed selector, plan,
preconditions, affected-node proof, transaction, validation, and inverse.

| Operation named in the goal | State |
|---|---|
| replace every definition with style `Definition` | Not started |
| find every theorem | Not started |
| rewrite only the paragraph containing an equation | Not started |
| change table style from APA to IEEE | Not started |
| create a table of figures | Not started |
| repair numbering | Not started |
| repair styles | Not started |
| repair references | Not started |
| repair footnotes | Not started |
| repair XML relationships | Diagnostics partial; repair not started |
| repair a corrupted document | Detection partial; repair not started |
| detect unused styles | Partial: dependency-backed native rule excludes defaults and styles referenced by semantics, numbering, inheritance or links; unmodeled consumers remain an explicit coverage boundary and deletion is not authorized |
| detect duplicate styles | Partial: native rule groups styles only when their fully modeled declared formatting is equivalent and separately surfaces typed style-graph duplicate/corruption diagnostics; names, UI metadata, usage and safe consolidation remain unresolved |
| detect dead relationships | Partial (strengthened): `OPC040` plus source-linked dependency part reachability and unresolved relationship edges; typed repair not started |
| minimize package size | Not started |
| rebuild numbering | Not started |
| repair OfficeMath | Not started |
| rewrite comment bodies only | Not started |
| accept only changes by author X | Partial: redacted author-fingerprint selection, explicit dependency cascade, deterministic plan/apply, exact inverse and atomic backup cover supported wrappers/moves/property snapshots/inserted rows plus proven one-sided cell/numbering decisions; unsupported paragraph/table-grid/vertical-merge/numbering/custom-XML structures block |
| revert changes by author Y | Partial: the same saved-package planner rejects the selected author's supported revisions under exact fingerprint/plan preconditions; unsupported dependencies block and can be routed to guarded live Word |
| align styles with a template | Not started |
| compare two documents | Partial: lazy `compare_ooxml_semantics` separates package and semantic verdicts, compares every projected Word story without opening Word, pages redacted differences, exposes match confidence/ambiguity/fallbacks and preserves opaque changes as explicit entry evidence; it does not yet create a tracked-change document or visual diff |
| create a patch | Partial: deterministic portable `.wtpatch` stores exact before/after uncompressed OPC entry payloads, hashes and reversible operations under exact fingerprints; a semantic-command patch and byte-identical ZIP-container replay remain future work |
| create a merge | Partial: explicit three-way plan/apply creates a new package, auto-composes proven disjoint lossless text edits and requires stable explicit resolutions for every other entry/text conflict; structural and revision-aware merge remain future work |
| render to HTML | Not started |
| render to SVG | Not started |
| render to PNG | Not started in new engine |
| render to PDF | Not started in new engine |
| render one page | Not started |
| render only a table | Not started |
| render one equation | Not started |
| generate a document AST | Partial read-only semantic AST |
| generate a dependency graph | Partial: one bounded graph now joins OPC, semantic containment, styles, numbering, references and sections with explicit coverage gaps; drawings, embedded objects, custom XML, bibliography, active content and collaboration remain outside the typed spine |
| generate a style map | Partial typed, paged graph plus filtered paragraph/run effective-property slice and provenance |
| generate a section structure | Partial typed boundary and effective header/footer binding graph |
| generate a document analysis | Package/semantic counts partial; full analysis not started |

## Exact AI object-model ledger

| Required high-level object | State |
|---|---|
| Paragraph | Partial read-only |
| Run | Partial read-only |
| Equation | Partial canonical OfficeMath read object; cross-format algebra and edits missing |
| Heading | Not typed; style metadata only |
| Section | Partial read-only boundary, page properties and story bindings |
| Table | Partial read-only |
| Figure | Not started |
| Bookmark | Partial paired, source-linked read object; no safe range edits |
| Reference | Partial typed dependency edge across fields/bookmarks and named targets |
| Caption | Not started |
| Comment | Partial source-linked body/anchor/thread/durable/person/reaction read object; structural edits and full collaboration semantics missing |
| Field | Partial nested complex/simple read graph; no evaluator or safe edits |
| Style | Partial typed graph, defaults, metadata, declared properties, inheritance diagnostics and modeled effective paragraph/run properties |
| Numbering | Partial read-only graph and effective level; no sequence counter or structural edits |
| Footnote | Partial body/reference object; numbering semantics missing |
| Image | Drawing marker only |
| Shape | Drawing marker only |
| Chart | Opaque only |

AI currently receives package summaries, bounded semantic nodes and redacted typed
field/bookmark/reference, OfficeMath and review graphs. It now also receives deterministic
text and tracked-review plans and can execute those bounded commands without raw XML; it
still lacks the complete object model, general query/planning language and broad typed
mutation/repair/render execution required by the goal.

## Proof, performance, and release gates

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Unit/regression tests | Partial | 250 engine, 189 native, 1273 Python passing at current checkpoint | Coverage for every required feature and published failure corpus |
| Property/fuzz testing | Partial | Deterministic malformed bytes and random opaque round-trip smoke | Continuous coverage-guided fuzzing, minimized corpus and resource assertions |
| Fault injection | Not started | Validation/concurrency failure tests only | Every persistence/transaction phase, disk-full, denied, crash and race tests |
| Preservation benchmark | Partial | Entry hashes and random no-op round trip | Public producer/feature corpus with untouched part/subtree metrics |
| Performance benchmark | Not started for new engine | Existing native COM benchmark only | Parse/edit/save/render latency, allocation, peak memory, scaling and long run |
| AI token benchmark | Partial (strengthened) | Lazy catalogue and bounded responses; earlier 83.5% schema reduction; field-heavy reference and dependency result data plus default equation result data are regression-capped below 5000 serialized characters. The default 18-rule lint summary is also capped below 5000 characters and its complete mirrored JSON-RPC envelope below 10000. Dependency and lint source evidence is redacted by default, while compact live-equation preflight omits converted linear math and rule arrays in favor of lengths, flags and a short fingerprint; live verification returns hashes/counts but never raw OMML. The MCP compatibility envelope still mirrors successful data in both `content` and `structuredContent`; that duplication is not hidden under the smaller result-data claim | Representative task suite against competitors with raw token logs, followed by a compatibility-safe removal of duplicated payload tokens |
| Visual regression | Not started for new engine | Historical screenshots and live acceptance | Versioned PDF/page/object baselines across rendering backends |
| Cross-platform CI | Partial | Mandatory Linux engine job plus clean hosted-Windows engine/native/package jobs; licensed Word gate remains separate | macOS core job, qualified backend matrix and routinely available self-hosted Word release evidence |
| Public competitor benchmark | Not started | Research matrix only | Same fixtures, versions, commands, results, caveats and reproducible harness |
| Release packaging | Partial (strengthened) | The 0.35 development package is self-contained Windows x64, contains the engine/runtime/manifest and zero Python files. Packaging canonicalizes copied JSON/Markdown and the embedded schema to BOM-less UTF-8/LF, closing a stale-checkout CRLF divergence. Two local repair-slice builds produced the same 195-file, 84,193,862-byte tree and 37,087,639-byte ZIP with SHA-256 `316344c8c034daf3f2166062989d8b02ddc46476a5227761ce85363b67636e2d`; hosted confirmation is pending the pushed commit. The packaged MCP exposed the lint and repair schemas, then planned, validated and created a repaired corpus DOCX without opening Word or modifying its source. The preceding linter-only package was identical across the existing working tree, a clean detached worktree and hosted Windows CI; the earlier exact 0.35 package checkpoint, SHA-256 `e8f2e4b74fe65213197126c7aafb445452bd0e80bc05f7206d82672e4b09e59b`, passed the complete 48-action live-Word gate, and no live COM code changed in either saved-package slice | Hosted artifact equality for this slice, optional signing/provenance policy, published artifact and refreshed licensed Word gate before release |

## Current checkpoint evidence

- `dotnet test native/WordToolkit.Engine.Tests` — 250 passed.
- `dotnet test native/WordToolkit.Native.Tests` — 189 passed.
- `.venv/Scripts/python -m pytest -q` — 1273 passed, 16 intentionally skipped.
- Native MCP regression against real Word verified Gaussian, nested and double
  integrals, Presentation MathML, OMML, a parenthesized matrix, cases and combining
  accents. Every sensitive result returned matching canonical hashes; all
  integral-owned differentials remained in `m:nary/m:e`, raw OMML was absent, and the
  isolated accent mismatch rolled back before its combining-character normalization was
  fixed.
- `scripts/build_native_plugin.ps1` — self-contained native package built with no
  Python runtime.
- The current 0.35 repair checkpoint exposes 14 public tools and 77 lazy actions. Two
  local builds produced the same 195-file, 84,193,862-byte tree and 37,087,639-byte ZIP
  with SHA-256 `316344c8c034daf3f2166062989d8b02ddc46476a5227761ce85363b67636e2d` and no Python
  files. The packaged MCP exposed the embedded lint/repair schemas, found the exact
  source-linked empty-title finding in `lo_toc_preserve.docx`, reported one implemented
  fix, planned a candidate with both engine and baseline-aware Open XML validation
  passing, atomically created a new file, changed only `docProps/core.xml`, matched the
  predicted fingerprint, removed the target finding, returned no raw title/XML and kept
  Word unopened and the source unmodified. Hosted artifact equality remains pending the
  pushed commit.
- The preceding linter-only checkpoint was identical across the existing checkout, a
  clean detached worktree and hosted Windows CI: 195 files, 35,918,887-byte ZIP,
  SHA-256 `e0d162feac71679efedfeac0de6982447f4856298d9b7334a0195a04c27f7400`.
- Packaged 0.34.0 exposed 14 public tools, discovered the new dependency action and
  inspected the field-heavy LibreOffice TOC fixture as 205 nodes and 255 edges without
  opening Word or following an external target. Its compact result data was 2611
  serialized characters; the complete JSON-RPC line was 6750 characters because the
  compatibility layer mirrors the payload in both `content` and `structuredContent`.
  Two independent 195-file builds produced identical 35,867,498-byte ZIPs with SHA-256
  `f4625c2c15827e78c9b5c54eaa50adf6aeeb64644235cafc46aa8374812b3944`;
  the native runtime assembly SHA-256 is
  `d93d12ab573a72547bc4db1992c997c1cb44c6b235439ca72ec1abd16d45840f`.
- Packaged 0.33.0 compact equation preflight returned 339 serialized characters and
  omitted converted linear math. The same packaged executable exposed 14 token-lean
  tools, resolved the 73-action lazy catalog, exercised all 48 live actions through the
  public gateways through 122 MCP requests, inserted 12 native equations including
  four MathML styles and separate OMML run/control styles, reopened the saved DOCX,
  validated it
  with the Open XML SDK, exported PDF and closed its own test document. The separate
  forced equation gates matched canonical readback across the 48-family atlas, all 112
  registered symbols, all 20 named functions, ten delimiter forms, mathematical
  alphabets and an ordinary derivative. Two Windows PowerShell 5.1 builds produced
  identical 36,989,699-byte ZIPs with SHA-256
  `8a64ed4f9b69b80f338de5c30bc687852bf29d24bddcb9b99eb078d15e06d1b1`.
  A separate packaged live test left the Word cursor at offset zero, appended sentinel
  text at document end, then proved that single-equation `target="cursor"` returned an
  equation range beginning at zero under a fresh selection token.
- Packaged 0.29.0 `plan_ooxml_merge` and `apply_ooxml_merge` were executed directly
  through the released native MCP executable against the 16-entry showcase package.
  The stateless plan produced deterministic `wtmerge_`/`wtmergeapply_` identities,
  zero conflicts and a materialized no-op candidate; apply created a new DOCX, refused
  overwrite semantics, returned the exact predicted package fingerprint and did not
  start Word or Python.
- Unpacked 0.27.0 MCP lazy search, schema inspection and semantic comparison were
  exercised against real Word and Pandoc tracked-change/move fixtures. The compact
  summary was 1207 UTF-8 bytes and reported 18 package-entry plus 49 semantic
  differences; a redacted three-change page was 2150 bytes. Matching uncertainty stayed
  explicit, no sensitive values or raw XML were returned, no mutation occurred, Word was
  not started and the runtime spawned zero Python children.
- Packaged/native MCP graph inspection was exercised end to end.
- Saved-package review inspection was exercised end to end against WordToolkit,
  Mammoth, Pandoc and Apache POI comments, tracked revisions and move fixtures; default
  structured responses remained approximately 1.2–1.7 KB and raw XML/personal text was
  absent.
- Saved-package accept/reject planning and exact inverse were exercised against real
  Word, Pandoc and Apache POI tracked-revision/move fixtures. Native plan/apply tests prove
  redacted author selection, explicit-all enforcement, no Word startup, atomic backup and
  baseline-aware Microsoft Open XML SDK validation.
- Semantic inspection was exercised through lazy search -> schema inspection -> execute.
- Cross-domain dependency inspection was exercised without Word against constructed
  resolved, missing, external and orphan targets and the field-heavy LibreOffice TOC
  fixture. Stable graph identities repeated exactly, every edge endpoint existed,
  one-to-four-hop traversal stayed bounded, sensitive keys/source stayed redacted by
  default and the default response remained below 5000 serialized characters.
- Header-story query -> guarded plan -> atomic apply was exercised end to end while
  proving that the main document part remained byte-identical.
- Section inheritance and effective header/footer selection were exercised against
  constructed edge cases and a bundled Apache POI fixture.
- Style-part discovery, defaults, latent metadata, inheritance failures and token-bounded
  MCP inspection were exercised against constructed edge cases and every bundled DOCX
  style part.
- Effective paragraph/run property ordering, direct overrides, standard toggles,
  source provenance, explicit coverage omissions and a real header story were exercised
  through the engine; filtered MCP resolution was exercised without invoking Word COM.
- Numbering discovery, abstract/instance/override resolution, start overrides,
  numbering-style indirection/cycles, malformed references, effective-formatting
  precedence and token-bounded MCP inspection were exercised against constructed edge
  cases and every bundled DOCX numbering part. The POI, Mammoth and Pandoc list fixtures
  produced zero numbering-graph diagnostics.
- Reference inspection paired bookmark ranges and parsed nested complex/simple fields
  across every bundled DOCX fixture, kept main/header/text-box/note stories isolated,
  resolved case-insensitive REF dependencies, classified inert external fields, and
  proved default redaction plus a 1090-character packaged summary without invoking
  Word COM or following an external target.
- OfficeMath inspection covered every standard OMML object family, Strict markup,
  display/inline and cross-story placement, malformed/unknown/extension cases, stable
  source-derived IDs and bounded failure paths. The tracked corpus contains 23 native
  equations across three repository documents; all three pass Microsoft Open XML SDK
  validation, and the default MCP response stays below 5000 serialized characters
  without formula text, raw OMML, Word startup or conversion.
- Theme relationship/content/root validation, all color/font/format inventories,
  deterministic font and tint/shade resolution, direct composite overrides,
  provenance, quantization diagnostics and token-bounded MCP inspection were exercised
  against constructed edge cases and every theme part in the bundled DOCX corpus.

These numbers prove only the rows they touch. They do not collapse the remaining work.
