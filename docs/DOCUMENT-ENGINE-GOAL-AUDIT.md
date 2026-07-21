# Document-engine goal audit

Last updated: 2026-07-21. This is the completion ledger for the WordToolkit
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
| Remaining relevant libraries/services | Planned | Research backlog in `RESEARCH-MATRIX-2026.md` | PHPWord, docxtemplater, docxcompose, docx-rs, cloud converters and other material engines assessed |

## Package, XML, and semantic foundation

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Bounded ZIP/OPC reader | Implemented (initial) | `WordToolkit.Engine/Packaging`; entry/size/ratio/XML limits; cancellation; tests | External fuzz campaign, hostile corpus, memory/latency benchmarks |
| Complete entry/part/content-type graph | Implemented (initial) | Raw bytes, hashes, canonical URIs, Default/Override maps | Full OPC URI conformance and Flat OPC parity |
| Relationship graph and reachability | Implemented (strengthened initial slice) | Root/part relationships, RFC 3986 type/target checks, XML-ID checks, fragment retention, reserved relationship-part/content-type rules, target modes, resolution, missing/orphan diagnostics | Digital-signature/encryption semantics, format-specific cardinality/type rules, repair and broad corpus proof |
| Unknown/opaque part preservation | Implemented (initial) | Raw entry snapshots and deterministic random round-trip smoke | Large mixed-extension corpus with untouched-byte reports |
| Immutable snapshots and fingerprints | Implemented (initial) | Order-independent package fingerprint; read-only model | Content-addressed cache, snapshot lifecycle and cross-platform reproducibility proof |
| Lossless XML token/source model | Not started | Architecture only | Prefix, whitespace, attribute order, comments, PI, MCE and raw subtree splice tests |
| Typed WordprocessingML parser | Partial | Read-only main-part semantic projector | All stories/parts and required Word structures with strict/transitional parity |
| Source-linked semantic AST | Partial | Stable node IDs, paths, provenance, bounded semantic projection | Full document graph, durable locator recovery, ambiguity model and mutation provenance |
| Stable semantic identity | Partial | `w14:paraId`/`textId`, durable IDs, fallback fingerprints, duplicate occurrence tests | Cross-save, cross-producer, move/edit and ambiguity benchmark |
| Serializer | Partial | Package serializer preserves entry payloads and deterministic mode | Minimal XML splicing, namespace/MCE preservation, all typed part serializers |

## Transactions, safety, and recovery

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Entry-hash preconditions | Implemented (initial) | Mutation builder tests | Semantic-node and destination/cloud version preconditions |
| Atomic file persistence | Implemented (initial) | Sibling temp, flush, validate, recheck, replace, optional backup | Power-loss and filesystem fault injection across supported platforms |
| Rollback | Partial | Candidate rejection leaves original unchanged; backup path; live Word undo grants exist | Inverse semantic patches and injected failure proof for every transaction phase |
| Multi-command document transaction | Planned | Architecture contract | Plan/apply with isolated semantic commands, one validation gate and inverse set |
| Optimistic concurrency | Partial | Package fingerprint checked before replacement; live document versions exist | Race tests, file identity/version integration, Graph/Drive ETag support |
| Security policy | Partial | ZIP/XML bounds, DTD ban, external links never fetched, MCP redaction | Macro/OLE/signature/custom XML/policy profiles, sandboxed adapters and threat model audit |
| Privacy/content-minimizing telemetry | Planned | Architecture rules | Opt-in implementation, redaction tests, expiry and debug-bundle audit |

## Document intelligence and editing

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Compact inspect | Implemented (initial) | `inspect_ooxml_package`, `inspect_ooxml_semantics`; per-field/item bounds | Paging/continuations, selectors, all stories, stable response budgets |
| Semantic query/search | Not started | Outline projection only | Typed selectors, full-text/field/math/metadata queries and bounded aggregations |
| Indexing | Not started | None | Incremental external index, invalidation and privacy controls |
| AI planner | Not started | Architecture command example | Intent -> evidence -> typed plan -> cost/risk -> approval/apply |
| Typed semantic mutations | Not started | Package-entry mutation only | Paragraph/run/table/field/math commands with preconditions and inverses |
| Validator | Partial | OPC diagnostics; historical SDK validator | Unified OPC/schema/extension/semantic/Word-open profiles and incremental validation |
| Linter | Not started in new engine | Historical Python checks only | Rule packs with source spans, severity, suppression and fix metadata |
| Formatter | Not started | Architecture only | Explicit previewed policies; no incidental formatting on save |
| Optimizer | Not started | Architecture only | Duplicate/dead-part/image/style/package optimizations with preservation proof |
| Repair engine | Not started | Architecture only | Diagnosis, confidence/risk, candidate fix, inverse and postcondition evidence |
| Semantic diff | Not started | Historical Python comparison only | Node-aware diff with source fallback and review-friendly output |
| Three-way merge | Not started | None | Conflict graph, revision-aware merge and deterministic resolution policies |

## Word feature systems

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Paragraphs/runs/tables | Partial | Read-only semantic projection; live COM editing | Lossless typed edits, effective properties, merge/split and layout tests |
| Sections/headers/footers/notes | Not started in new engine | Existing live COM actions and historical Python engine | Full typed projection/edit/serialize across linked sections and all stories |
| Styles/themes/direct formatting | Not started | Architecture only; historical/live features | Effective-format resolver with provenance, drift lint and safe refactor |
| Numbering/lists | Not started | Existing live actions only | Abstract/instance/override/restart/style-linked resolver and edits |
| Fields/bookmarks/cross-references | Partial | Semantic field/bookmark recognition; live actions | Dependency graph, nested-field parser, update capability and safe edits |
| TOC/TOF/TOT/captions | Not started in new engine | Existing live actions/historical tests | Reference graph, field update, layout and round-trip tests |
| Comments/threaded comments/revisions | Partial | Semantic anchors/revision wrappers; live review actions | All review parts, moves, authorship, merge and accept/reject semantics |
| Content controls/custom XML | Partial | Content-control projection; unknown part retention | Binding graph, repeats, locks, data update and lossless custom XML edits |
| Equations/OfficeMath | Partial | Every nested math element projected; mature live equation insertion | Canonical math AST, all constructs, LaTeX/MathML/UnicodeMath/OMML round trips and Word visual proof |
| DrawingML/VML/images/text boxes | Partial | Drawing markers and opaque bytes; live image operations | Typed anchors/layout/wrap/group/text-box model and render corpus |
| Charts/SmartArt/OLE/embedded packages | Not started | Opaque retention only | Typed inspection/edit where safe, extraction policy, rendering and security gates |
| Citations/bibliography | Not started | Architecture only | Source model, citation fields, style/locale handling and reference updates |
| Templates/mail merge | Not started in new engine | Historical functionality only | Typed slots/regions/constraints/data validation and repeatable generation |
| Macros/signatures/protection/encryption | Not started | Macro extension recognized only by file type; raw parts retained | Explicit policy, signature invalidation rules, protected operations and safe handoff |
| Accessibility | Not started in new engine | Historical checks only | Heading/table/alt text/language/reading order/link/metadata rule suite |
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
| `document.xml` | Partial | Lossless token model and full typed vocabulary |
| `styles.xml` | Not started | Parse, inheritance, effective formatting, edit/serialize |
| `numbering.xml` | Not started | Abstract/instance/override graph and inheritance |
| theme | Not started | Theme/font/color resolution and mutation |
| settings | Not started | Compatibility/protection/mail-merge/settings model |
| fontTable | Not started | Font metadata, embedding, fallback and substitution |
| comments | Not started | Comment bodies, anchors, authorship and edits |
| commentsExtended | Not started | Thread/status/person graph and version compatibility |
| revisions | Partial | Wrapper recognition only; full authorship/move/property revisions |
| tracked changes | Partial | Recognition only; filtered accept/reject and merge |
| bookmarks | Partial | Start recognition only; paired ranges and safe edits |
| hyperlinks | Partial | Semantic node and relationship ID; full target/range/edit model |
| fields | Partial | Basic markers/instructions; nested parser/evaluator/dependency graph missing |
| TOC | Not started | Field/options/styles/dependency/update model |
| footnotes | Not started | Story part, separators, references and safe numbering |
| endnotes | Not started | Story part, separators, references and safe numbering |
| OfficeMath / OMML | Partial | Full tree visible; canonical semantic math AST and serializers missing |
| sections | Not started | Section chain, inheritance, page setup and story linkage |
| headers | Not started | Variants, links-to-previous, fields and layout |
| footers | Not started | Variants, links-to-previous, fields and layout |
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
| document variables | Not started | Settings variable model and field dependencies |
| glossary | Opaque only | Building blocks, relationships and typed insertion |
| content controls / SDT | Partial | ID/tag projection; properties, bindings, repeats, locks and edits missing |
| mail merge | Not started | Settings, fields, data sources, regions and deterministic execution |
| macros | Opaque only | Same VBA policy gap; no execution surface |
| signatures | Opaque only | Signature origins, validation, invalidation and resign workflow |
| encryption | Not started | Encrypted OOXML detection, authorized decrypt/encrypt adapter |
| permissions | Not started | PermStart/PermEnd ranges and enforcement |
| protection | Not started | Settings hashes, edit restrictions and capability checks |
| revision IDs | Partial | Some durable IDs read; global identity/collision/version semantics missing |
| style inheritance | Not started | Based-on/link/default/theme/direct provenance resolver |
| numbering inheritance | Not started | Level/override/style-linked effective-number resolver |
| XML namespaces | Partial | Secure namespace-aware parse and opaque retention; lossless token declarations missing |
| compatibility mode | Opaque only | `compatSetting` interpretation and version behavior probes |
| Word version differences | Not started | Versioned capability profiles and corpus |
| co-authoring metadata | Opaque only | People, comments, session/change metadata and merge semantics |

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
| detect unused styles | Not started |
| detect duplicate styles | Not started |
| detect dead relationships | Partial (`OPC040` reachability); typed repair not started |
| minimize package size | Not started |
| rebuild numbering | Not started |
| repair OfficeMath | Not started |
| rewrite comment bodies only | Not started |
| accept only changes by author X | Not started in new engine |
| revert changes by author Y | Not started in new engine |
| align styles with a template | Not started |
| compare two documents | Not started in new engine |
| create a patch | Not started |
| create a merge | Not started |
| render to HTML | Not started |
| render to SVG | Not started |
| render to PNG | Not started in new engine |
| render to PDF | Not started in new engine |
| render one page | Not started |
| render only a table | Not started |
| render one equation | Not started |
| generate a document AST | Partial read-only semantic AST |
| generate a dependency graph | OPC graph only; semantic dependencies not started |
| generate a style map | Not started |
| generate a section structure | Not started |
| generate a document analysis | Package/semantic counts partial; full analysis not started |

## Exact AI object-model ledger

| Required high-level object | State |
|---|---|
| Paragraph | Partial read-only |
| Run | Partial read-only |
| Equation | Partial read-only tree, not canonical math object |
| Heading | Not typed; style metadata only |
| Section | Not started |
| Table | Partial read-only |
| Figure | Not started |
| Bookmark | Partial start marker only |
| Reference | Not started |
| Caption | Not started |
| Comment | Anchor partial; body object missing |
| Field | Partial read-only |
| Style | Not started |
| Numbering | Not started |
| Footnote | Not started |
| Image | Drawing marker only |
| Shape | Drawing marker only |
| Chart | Opaque only |

AI currently receives package summaries and bounded semantic nodes; it still lacks the
complete object model, query language, planning layer, typed mutation commands, and
automatic OOXML execution required by the goal.

## Proof, performance, and release gates

| Requirement | State | Current evidence | Exit condition |
|---|---|---|---|
| Unit/regression tests | Partial | 26 engine, 42 native, 1273 Python passing at current checkpoint | Coverage for every required feature and published failure corpus |
| Property/fuzz testing | Partial | Deterministic malformed bytes and random opaque round-trip smoke | Continuous coverage-guided fuzzing, minimized corpus and resource assertions |
| Fault injection | Not started | Validation/concurrency failure tests only | Every persistence/transaction phase, disk-full, denied, crash and race tests |
| Preservation benchmark | Partial | Entry hashes and random no-op round trip | Public producer/feature corpus with untouched part/subtree metrics |
| Performance benchmark | Not started for new engine | Existing native COM benchmark only | Parse/edit/save/render latency, allocation, peak memory, scaling and long run |
| AI token benchmark | Partial | Lazy catalogue and bounded responses; earlier 83.5% schema reduction | Representative task suite against competitors with raw token logs |
| Visual regression | Not started for new engine | Historical screenshots and live acceptance | Versioned PDF/page/object baselines across rendering backends |
| Cross-platform CI | Not started | Engine targets `net8.0`; current verification is Windows | Windows/Linux/macOS core tests and qualified backend matrix |
| Public competitor benchmark | Not started | Research matrix only | Same fixtures, versions, commands, results, caveats and reproducible harness |
| Release packaging | Partial | Self-contained Windows build succeeds and contains engine DLL | Version bump, migration, signed release, clean-install and rollback exercise |

## Current checkpoint evidence

- `dotnet test native/WordToolkit.Engine.Tests` — 26 passed.
- `dotnet test native/WordToolkit.Native.Tests` — 42 passed.
- `.venv/Scripts/python -m pytest -q` — 1273 passed, 16 intentionally skipped.
- `scripts/build_native_plugin.ps1` — self-contained native package built with no
  Python runtime.
- Packaged/native MCP graph inspection was exercised end to end.
- Semantic inspection was exercised through lazy search -> schema inspection -> execute.

These numbers prove only the rows they touch. They do not collapse the remaining work.
