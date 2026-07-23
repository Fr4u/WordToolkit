# WordToolkit Native

WordToolkit 0.35 (development line) is a local Windows MCP plugin that starts or attaches to the real Microsoft Word application and controls it through a persistent native .NET COM STA thread. The document-engine core can also inspect the package graph, semantic structure, section bindings, typed table/grid/merge topology, style, numbering, theme, settings, font-table, field/bookmark/reference, classic DrawingML chart, canonical OfficeMath and review/revision graphs, lint a saved package with deterministic source-linked rule packs, plan and apply the first source-bound lint repair, create, clone, safely consolidate exact typed style definitions, delete proven-unused custom definitions, rename only a custom style's primary visible name and assign styles through one semantic transaction, compare two saved packages at separate OPC-entry and source-linked semantic layers, create deterministic reversible package patches, plan guarded three-way merges, and resolve modeled effective formatting without starting Word. Theme-backed fonts resolve through `themeFontLang` and supplemental script mappings, then cross-reference declared and embedded font metadata; colors resolve to concrete RGB values when the source is deterministic. Nested complex and simple fields are parsed per Word story into inert dependencies rather than evaluated or exposed as raw XML. Classic chart inspection covers all 16 plot families, series, axes, cache metadata and related parts without retaining point values or opening workbooks. Native equations are classified into source-linked objects and argument roles without converting them or returning raw OMML. Comments are joined to story anchors, threaded replies, durable identifiers, people records and reaction inventory; revisions are classified with authorship, nesting, named moves and permission ranges. Every result retains its declaration and provenance. The lossless editing core binds text, style definitions, paragraph/run/table style references, tracked-review structures and one existing empty core-title element to exact XML byte spans, combines bounded commands into hash-preconditioned package mutations, predicts result fingerprints and retains exact guarded inverses without reserializing unrelated XML.

This is an advanced but experimental OOXML engine, not a verified claim of market leadership or complete Microsoft Word equivalence. Unsupported domains and release evidence are listed explicitly in [Known limitations](docs/KNOWN-LIMITATIONS.md) and [Testing](docs/TESTING.md).

The packaged plugin does not contain or launch Python, `uv`, `pywin32`, a virtual environment, an interpreter bootstrap, or a per-call helper process. Its MCP command points directly to:

```text
./runtime/win-x64/wordtoolkit-native.exe
```

The repository still retains the older Python/OOXML service as historical source and a possible remote-service reference. It is not copied into the 0.35 local plugin, does not participate in its startup, and is not required at runtime.

## Why the runtime was replaced

The old local plugin started through `uv run --frozen wordtoolkit-stdio`, created or reused a virtual environment, imported 41 packages, attached to Word through pywin32, and paid interpreter and dependency costs before useful work began.

The native runtime instead:

- starts as one self-contained Windows executable;
- reads line-delimited MCP JSON-RPC directly from STDIO;
- owns one persistent background STA thread for all Word COM work;
- attaches to the existing `Word.Application` Running Object Table entry or starts Word through native COM when explicitly requested;
- caches the Word application proxy for the process lifetime;
- retries bounded `RPC_E_CALL_REJECTED` / busy-Word calls through `IOleMessageFilter`;
- groups each mutation batch in one custom Word Undo record;
- suspends screen updates during large transactions;
- uses the Microsoft Open XML SDK directly for saved-DOCX validation.

## Measured result

Tests were run against real Microsoft Word 16.0 on 2026-07-20.

| Path | Work | Wall time |
|---|---:|---:|
| Old Python bridge | 100 text operations, 48,800 characters | 751.658 ms |
| Native .NET bridge | 100 text operations, 48,800 characters | 259.455–268.126 ms |
| Packaged self-contained MCP startup | process start through `initialize` | 106.767 ms |
| Packaged saved-package inspection CLI | 20 cold processes, `real_contract.docx` | p50 265.965 ms / p95 286.932 ms |
| Packaged saved-package inspection MCP | 20 cold processes, same file and operation | p50 321.721 ms / p95 348.233 ms |
| Native LaTeX equation in real Word | fraction, root, scripts and sum | about 100–158 ms |
| Full 48-tool real-Word acceptance | 71 MCP requests, save/validate/PDF/reopen/reconnect | 24.492–24.691 s |

The main 48,800-character batch is about 2.9× faster than the old Python bridge. The native process spawned no Python or `uv` child; the only observed child was Windows `conhost.exe`.

These numbers are machine-specific. They are recorded as test evidence, not universal promises.

## Supported local tools

The runtime implements 48 tested Word Live actions plus 36 standalone,
bounded OOXML engine actions. The initial MCP catalog exposes
only 11 common actions plus four token-lean gateways. Rare schemas are
searched and loaded one at a time:

```text
search_wordtoolkit_actions
inspect_wordtoolkit_action
execute_wordtoolkit_action
get_wordtoolkit_capabilities
```

`get_wordtoolkit_capabilities` is the vendor-neutral discovery entry point. It
returns the runtime, MCP and schema versions, deterministic contract hashes,
operation counts, explicit metadata-coverage gaps, security behavior, limits and
bounded operation summaries without opening Word or reading a document. The same
payload is available outside MCP:

```powershell
wordtoolkit-native capabilities --query patch --limit 8 --format json
wordtoolkit-native capabilities --schema --format json
```

The schema form returns the exact embedded JSON Schema text plus its verifiable hash;
the installed client therefore does not need repository access. The default manifest
page is 12 operations and the hard page ceiling is 32. Full input
schemas remain behind `inspect_wordtoolkit_action`, so capability negotiation does
not flatten the 88-action schema set into model context. The normative shape is
checked in as [`schemas/wordtoolkit-capabilities.v1.schema.json`](schemas/wordtoolkit-capabilities.v1.schema.json)
and the runtime reports its SHA-256. See
[`docs/AI-INTEROPERABILITY.md`](docs/AI-INTEROPERABILITY.md) for the contract and
compatibility rules.

The first public operation shared by the cross-platform .NET engine, CLI and MCP is
saved-package inspection. It does not create a Word COM host in SDK/CLI use, does not
launch Microsoft Word, does not mutate the input and never follows an external
relationship:

```powershell
wordtoolkit-native inspect-package .\input.docx --include-details --max-items 40 --format json
```

Successful data is the versioned
`wordtoolkit.inspect_ooxml_package/1.0` contract on stdout. Failures are JSON on stderr;
the stable exit classes are 64 (invalid input), 65 (invalid or safety-limited package),
66 (not found), 74 (I/O), 77 (access denied) and 70 (unexpected internal failure).
The same operation is available to .NET callers without the Windows host:

```csharp
using WordToolkit.Engine.Operations;

var result = new InspectWordPackageOperation().Execute(
    new InspectWordPackageRequest("input.docx", IncludeDetails: true)
);
Console.WriteLine(WordToolkitOperationJson.Serialize(result));
```

The second shared operation performs three high-level, source-preserving transforms
without launching Word: replace the first ordinary text occurrence, accept all supported
tracked changes, or reject them. Input and output paths must differ, an existing output
is never overwritten, signed packages are blocked and the complete candidate is parsed
and validated before atomic persistence:

```powershell
wordtoolkit-native transform-package .\input.docx .\output.docx `
  --operation replace_first_text_occurrence `
  --find-text "old text" --replace-text "new text" --format json

wordtoolkit-native transform-package .\review.docx .\accepted.docx `
  --operation accept_all_tracked_changes --format json
```

Successful data uses `wordtoolkit.transform_ooxml_package/1.0`. Ordinary text matching
may cross run boundaries but excludes OfficeMath and fails closed around tracked-change
or markup-compatibility ambiguity. The same core backs a protocol-v1 adapter for the
neutral `docx-platform-tests` harness. Its pinned comparison and raw result are in
[`docs/COMPETITOR-BENCHMARK-2026-07-23.md`](docs/COMPETITOR-BENCHMARK-2026-07-23.md).

The third shared operation is the read-only semantic object query contract
`wordtoolkit.query_ooxml_semantics/1.0`. One Engine implementation backs direct .NET
calls, the lazy MCP action and a non-interactive JSON CLI. A request file uses the same
flat query fields as MCP:

```json
{
  "local_path": "input.docx",
  "kinds": ["paragraph"],
  "descendant": {"kinds": ["equation"]},
  "max_results": 40,
  "include_source": true
}
```

```powershell
wordtoolkit-native query-package --request .\query.json --format json
Get-Content .\query.json -Raw | wordtoolkit-native query-package --request - --format json
```

[`examples/query-package.request.json`](examples/query-package.request.json) is an
executable repository-relative request against the checked-in equation corpus.

Matches retain stable `node_id` compatibility and now declare `object_category`,
`story_kind`, child count and identity semantics. Results are bounded and paged; raw XML
is never returned, external relationships are never followed and Word is never opened.
An optional `expected_package_fingerprint` rejects stale local reads. Semantic properties
are opt-in, while author/name/date/GUID/field-instruction/anchor values require the second
explicit `include_sensitive_properties` flag. Shortened property values are named in
`truncated_property_names`, and source locators fail at their public limit instead of
being silently cut. Complex-field instruction text is suppressed from every preview
under the same second opt-in, so paragraph/subtree previews cannot bypass property
redaction. The .NET operation also accepts a readable, seekable package stream
and restores its original position. The MCP action is the first catalogue entry
with a version, closed output schema, filesystem/network/Word permission record and
reversibility declaration. Capability discovery counts their presence without widening
the closed v1 operation summary; `inspect_wordtoolkit_action` returns the exact selected
contract only when it is needed.

Stream labels are portable leaf names capped at 512 characters, and Word validity additionally
requires the filename extension to agree with the main content type. MCP rejects unknown
arguments instead of silently ignoring misspelled closed-schema fields.
`WordToolkitOperationJson` is the public canonical JSON codec used by all adapters;
SDK, CLI and compact MCP data therefore share `snake_case`, null handling and field
order. Enums are non-numeric `snake_case`; request adapters reject unknown members while
result decoding retains additive v1 forward compatibility. Full MCP responses retain
legacy runtime timing fields only in the transport adapter; those fields are deliberately
absent from deterministic operation data.

The fourth shared operation exposes the existing seven-command semantic style
transaction through the public Engine, JSON CLI and lazy MCP actions. Plan and apply use
the same strict request parser and exact `wseplan_` intent identity; selectors are
resolved inside the Engine, capped explicitly and rebuilt at apply time:

```json
{
  "local_path": "tests/upstream/fixtures/lo_toc_with_styles.docx",
  "expected_package_fingerprint": "f9759772a36c230823fdcf3f818749619d72f25afa6f6d92673202692dd657b9",
  "commands": [
    {
      "type": "clone_style",
      "source_style_id": "Normal",
      "style_id": "DefinitionProof",
      "name": "Definition proof"
    }
  ],
  "include_details": true
}
```

```powershell
wordtoolkit-native style-package --mode plan --request .\examples\style-package.plan.request.json --format json
wordtoolkit-native style-package --mode apply --request .\style-apply.json --format json
```

Supported command discriminators are `create_style`, `clone_style`,
`consolidate_style`, `delete_unused_style`, `rename_style`, `set_style` and
`set_style_where`. Apply requires the reviewed package fingerprint and plan ID, blocks
signed packages, validates the exact candidate against its baseline with Microsoft Open
XML SDK, writes atomically and retains a recovery backup by default. The dependency-free
Engine fails closed with `VALIDATOR_REQUIRED` when no schema-validator adapter is
supplied; `WordToolkit.OpenXmlSdk` provides the standard adapter without pulling the
Microsoft dependency into the domain core. Neither CLI nor MCP invokes or launches
Word. Request JSON is capped at 256 Ki characters; a transaction can resolve at most 200
edits and change at most 200 package parts. Validation issue locations are withheld unless
`include_details=true`. The atomic writer verifies the package displaced at commit time
and restores it if a non-cooperative writer wins the final race. If another writer changes
the destination during that compensation, WordToolkit preserves the newer bytes in an
opaque sibling `.conflict` artifact and returns `RECOVERY_REQUIRED`; public error details
contain at most two existing artifact names, never absolute paths or document content. The
normative contracts are
`wordtoolkit.plan_ooxml_semantic_edits/1.0` and
`wordtoolkit.apply_ooxml_semantic_edits/1.0`.

Comment-body rewrites have a separate high-level, token-lean boundary. Select one
comment through `inspect_ooxml_review`, then submit its stable `comment_id`, exact
`find_text`, replacement and expected match count to
`wordtoolkit.plan_ooxml_comment_body_edits/1.0`; apply the reviewed plan through
`wordtoolkit.apply_ooxml_comment_body_edits/1.0` or the non-interactive
`comment-body-package` CLI. Matches may span adjacent Word runs in the same ordinary
comment paragraph, but never paragraphs, table cells, tabs, breaks, fields, content
controls or other rich structural boundaries. Only the selected comment's editable text
leaves can change. Plan/apply return counts and hashes rather than comment
text or XML and prove that anchors, authors, threads, durable IDs, reactions, revisions,
permissions, unselected comments and unrelated package parts remain unchanged.

The complete lazy action set is:

```text
list_live_word_documents
start_word_application
create_live_word_document
open_live_word_document
connect_live_word_document
inspect_ooxml_package
transform_ooxml_package
inspect_ooxml_semantics
query_ooxml_semantics
manage_ooxml_semantic_index
compare_ooxml_semantics
plan_ooxml_patch
create_ooxml_patch
inspect_ooxml_patch
plan_ooxml_patch_apply
apply_ooxml_patch
plan_ooxml_merge
apply_ooxml_merge
inspect_ooxml_sections
inspect_ooxml_styles
inspect_ooxml_numbering
inspect_ooxml_theme
inspect_ooxml_settings
inspect_ooxml_references
inspect_ooxml_dependencies
lint_ooxml_document
plan_ooxml_lint_repair
apply_ooxml_lint_repair
inspect_ooxml_equations
inspect_ooxml_review
inspect_ooxml_fonts
inspect_ooxml_charts
inspect_ooxml_content_controls
inspect_ooxml_tables
inspect_ooxml_markup_compatibility
resolve_ooxml_formatting
plan_ooxml_text_edits
apply_ooxml_text_edits
plan_ooxml_semantic_edits
apply_ooxml_semantic_edits
plan_ooxml_comment_body_edits
apply_ooxml_comment_body_edits
plan_ooxml_review_decisions
apply_ooxml_review_decisions
inspect_live_word_document
map_live_word_structures
inspect_live_word_structure_items
inspect_live_word_equation_learning
inspect_live_word_structure_learning
inspect_live_word_object_model_types
inspect_live_word_object_model_members
inspect_live_word_member_capabilities
preflight_live_word_member_operations
execute_live_word_member_operations
find_live_word_text
replace_live_word_text
inspect_live_word_review
manage_live_word_review
diagnose_live_word_layout
get_live_word_selection
inspect_live_word_undo
undo_live_word_operation
insert_live_word_text
format_live_word_selection
insert_live_word_table
preflight_live_word_table_formulas
insert_live_word_table_formulas
update_live_word_table_fields
insert_live_word_list
preflight_live_word_bookmarks
insert_live_word_bookmarks
preflight_live_word_fields
insert_live_word_fields
insert_live_word_image
insert_live_word_comment
insert_live_word_note
set_live_word_header_footer
insert_live_word_equation
insert_live_word_equations_batch
preflight_live_word_equations
apply_live_word_operations
validate_live_word_document
export_live_word_pdf
save_live_word_document
close_live_word_document
quit_word_application
disconnect_live_word_document
```

The catalog describes all 12,167 public members found in the installed Word type library on the release machine. It does not lie that all of them are safe edits: stable capability IDs expose metadata for every member, while lifecycle, macro, DDE, print/mail/web, sensitive, global, event, restricted and unknown operations fail closed. Dedicated tools remain the preferred path.

Saved-package settings, font, reference and review inspection is metadata-first and
bounded. Document variable values, mail-merge query/connection details and targets are
redacted unless explicitly requested. Protection hashes and salts are never returned,
and embedded font bytes are never exposed. Bookmark names, field instructions, cached
results and dependency keys are also redacted by default. External field targets are
classified but never followed or executed. Comment/revision text, author/editor/person
names, provider/user identifiers and move names are fingerprinted and redacted by
default. Font hashes are opt-in metadata only. Protection is reported as an editing
restriction, not misrepresented as document encryption.

Saved-package semantic queries select source-ordered nodes by kind, bounded text,
exact properties, source story, subtree, and strict ancestor/descendant predicates.
This lets an agent ask directly for a paragraph containing an equation or an equation
inside a table cell without downloading XML or walking the semantic tree in model
context. Repeated queries can reuse a fingerprint-bound process-memory index; every
indexed result is still checked against the full predicate and reports the candidate
seed and scanned-node count.

Saved-package semantic style edits are typed, bounded, stateless and heterogeneous.
`create_style` adds a minimal custom paragraph, character, table or numbering definition
with optional `basedOn`, paragraph `next`, quick-format and UI-priority metadata.
`clone_style` copies an existing definition, including opaque extension formatting, under
a new ID/name while removing default and linked-style identity. The same atomic plan can
then assign the new style. `consolidate_style` accepts one explicit custom, non-default
source ID and one same-type target ID only when their complete canonical definitions are
identical after normalizing ID, name, aliases, revision ID and batch-remapped relations.
It rewrites recognized references across projected Word stories, revision snapshots,
glossary metadata, `styles.xml` and `numbering.xml`, removes the source definition,
reports the exact reference count and retains a byte-exact inverse. An explicit
`delete_unused_style` command removes only a custom, non-default definition after proving
that no surviving semantic, style, numbering, glossary, latent-style, `STYLEREF` or
unmodeled XML consumer names it. One batch may remove a closed graph of mutually
dependent unused styles. `rename_style` changes only the primary visible `w:name` of an
explicit custom, non-default style. Its internal `w:styleId`, aliases, formatting and all
ID-based references remain unchanged; ID/name/alias collisions, name-addressed or
ambiguous `STYLEREF`, latent-style name consumers and unmodeled field consumers block the
plan instead of triggering a guessed rewrite. Exact `set_style` commands consume
stable paragraph, run, or
table IDs. Token-lean `set_style_where`
commands instead resolve all nodes server-side from one strict kind plus optional bounded
text, exact-property, ancestor/descendant, subtree and source-story predicates. Every
selector declares `max_matches`, rejects zero or excessive matches, and is bound
canonically into the plan ID independent of JSON property order. At most 16 selectors
and 200 resolved operations enter one transaction. A command may require the exact
current explicit style or require that every selected node have none. The target style
must already exist, match paragraph/character/table type, and have a resolvable
inheritance chain. Planning losslessly constructs and Microsoft-SDK-validates the exact
candidate without writing or returning XML. Apply must reproduce the same intent-bound
plan, rejects signed/stale packages and new schema errors, writes atomically, and keeps a
recovery backup by default. Creation does not accept arbitrary formatting blocks; clone
is the path for preserving an existing style's modeled and unmodeled formatting. This
slice does not change stable style IDs, delete referenced or built-in definitions,
perform broad style repair, align
definitions to templates, infer “APA”/“IEEE” semantics, select a missing property
directly, or model conditional table-style rendering. Consolidation fails closed for
non-equivalent or built-in sources, chains, graph damage, unmodeled XML consumers,
latent-style exceptions, source-addressing or ambiguous `STYLEREF`, macros, `altChunk`,
automatic linked-template style updates and packages containing a `stylesWithEffects`
mirror. Linked paragraph/
character pairs can be consolidated only as one explicit, exactly equivalent batch.

Saved-package dependency inspection joins OPC reachability, semantic containment across
projected stories, explicit paragraph/run/table style use, style inheritance/defaults,
numbering definitions and uses, field/bookmark targets, section header/footer bindings,
classic charts/series/axes/related parts, content controls, physical and built-in XML
stores, resolved binding targets, repeating-section topology, nested tables and
vertical-merge continuation cells into one deterministic
graph. Missing and external targets remain explicit
nodes; every edge endpoint is verified. The default view returns only bounded edge-kind
counts and coverage gaps. Node keys and source provenance are separate opt-ins, external
targets are never followed, and impact traversal is capped at four hops plus an
independent hard edge budget. DrawingML layout, SmartArt, OLE, bibliography sources,
active content and co-authoring remain openly unmodeled.

Saved-package content-control inspection joins source-linked `w:sdt` type, level, lock,
placeholder and parent state to physical Custom XML stores, Word's built-in core and
extended property stores, standard/Office 2013 bindings, selected target ordinals and
repeating-section items. XPath is deliberately restricted to absolute child-element
paths with namespace prefixes and positive positions. The default response omits
aliases, tags, titles, GUIDs, XPath, namespace mappings, part names and source ordinals;
separate opt-ins reveal bounded metadata only. Custom XML values, visible bound values
and raw XML are never returned, and no external target or Word process is opened.

Saved-package table inspection constructs a source-linked logical grid for every
Transitional or Strict `w:tbl`. It maps physical cells through `gridBefore`, `gridSpan`
and `gridAfter`, validates the declared grid, builds exact-span vertical-merge chains,
retains legacy horizontal merges separately, links nested tables, applies Word's
contiguous repeating-header rule and reports declared versus Word-effective floating
positioning. The default result is topology-only. Style IDs, captions and descriptions;
width/layout details; and source provenance require three independent opt-ins. Cell text
and raw XML have no response field. See [the table-graph contract](docs/TABLE-GRAPH.md).

Saved-package chart inspection is parse-only and metadata-first. It understands all 16
classic DrawingML plot families in Transitional and Strict OOXML, series source roles,
formulas, cache counts/indexes, axes and cross-axis links, `externalData`, embedded
packages and related chart parts. Titles and formulas are redacted by default; source
relationships use a separate opt-in. Cached point values are never returned, Word is
never started, and neither embedded workbooks nor external targets are opened. Office
2016 extended charts are preserved and explicitly reported as unmodeled. The default
summary and complete JSON-RPC envelope have regression size caps.

Saved-package markup-compatibility inspection evaluates ECMA-376 Part 3 across every
XML-typed OPC part without destructively preprocessing the document. It inventories
`mc:Ignorable`, `mc:ProcessContent`, `mc:MustUnderstand`, `mc:AlternateContent`,
`mc:Choice` and `mc:Fallback`, then reports the selected branch, ignored or unwrapped
elements and attributes, and unresolved must-understand requirements for one explicit
application configuration. Application-defined extension islands are also explicit;
the engine never guesses which private vocabulary is opaque. Legacy
`mc:PreserveElements` and `mc:PreserveAttributes` hints are retained and reported but
are not misrepresented as part of the current fifth-edition processing model. The
action never rewrites the source, opens Word, or follows relationships. Custom
namespace URIs and local names are redacted unless separately requested, while part
paths, source hashes and XML ordinals require the source-provenance opt-in.

Saved-package linting builds on those typed graphs without opening Word. The initial 18
rules cover package/dependency diagnostics, typed style/numbering/reference/theme/
settings/font diagnostics, unbound section stories, unused and formatting-equivalent
styles, direct formatting, external relationships, hidden text, heading order, drawing
alternative text, table headers and the document title. Findings have deterministic
`wtlint_` IDs, severity, confidence, privacy-safe subject fingerprints and optional XML
byte spans. Suppressions are explicit and bounded. Fix metadata is truthful: only one
existing, unambiguous, lexically safe empty `dc:title` reports `implemented=true`. An
unused-style finding names the separate `delete_unused_style` command but remains
`implemented=false`; the semantic planner independently re-proves every deletion
precondition instead of treating a lint finding as authorization.
Linting itself never mutates a package. The separate plan/apply repair path binds the
exact finding and package fingerprint to a privacy-safe preview, validates the lossless
candidate structurally and with the Open XML SDK, blocks signed packages, and creates a
new same-extension output without overwriting the source. Every other finding-bound fix
remains unimplemented. Lint responses keep execution completeness separate from whole-document
coverage, so unmodeled domains cannot be mistaken for a clean audit.

Saved-package equation inspection is also metadata-first. Its default response groups
equations by story, display mode and structural status without returning formula text or
raw OMML. Exact equation IDs, a flat paged OfficeMath node graph, normalized properties,
source provenance and bounded text previews are separate opt-ins. Inspection never opens
Word, converts notation or follows external content.

Saved-package semantic comparison is two-layered and read-only. Its compact summary
keeps package equivalence, semantic equivalence and matcher completeness separate.
Bounded pages expose source-linked added, removed, moved, text, property, structure and
unmodeled-markup changes, or exact OPC entry changes. Duplicate durable IDs, near-equal
context candidates and alignment fallbacks remain explicit instead of being guessed.
Text/property values, hashes and source locations are independent opt-ins; raw XML is
never returned and Word is never opened.

Saved-package patching turns that comparison into a portable `.wtpatch` artifact without
pretending the ZIP container itself is sacred. Every changed OPC entry carries its exact
before and after uncompressed payload, length and SHA-256; operation and patch IDs bind
both complete package fingerprints. The codec rejects unknown or duplicate manifest
fields, unsafe or duplicate archive paths, unreferenced payloads, noncanonical operation
order, hash/length drift, excessive expansion and compression bombs. This preserves OPC
entry names and payload bytes exactly. ZIP compression, timestamps and container record
layout are deterministic serializer output, not byte-identical copies of either source
archive.

Version-1 raw `.wtpatch` files are **confidential recovery artifacts**, not lightweight
or public diffs. They may contain enough before/after payload data to reconstruct
sensitive document content and are materialized in memory. Their internal hashes detect
corruption but do not authenticate an author. The engine's optional
`OpcPackagePatchEnvelopeCodec` can wrap a patch with AES-256-GCM encryption and/or an
ECDSA-SHA256 signature bound to a caller-managed signer key ID. Raw patches remain
unencrypted by default; key provisioning and MCP exposure are deliberately separate from
the artifact format. Store every unencrypted patch with the same controls as the source
DOCX.

The strict lazy workflow is `plan_ooxml_patch` -> `create_ooxml_patch` ->
`plan_ooxml_patch_apply` -> `apply_ooxml_patch`; `inspect_ooxml_patch` validates an
artifact independently. Create requires both source fingerprints and the reviewed patch
ID and never overwrites. Apply rematerializes the candidate, recomputes semantic/risk
evidence, compares baseline and candidate Open XML SDK errors, requires an exact apply-
plan ID bound to the reviewed destination path, verifies that the result's Word main-part
type matches the in-place file extension, and rechecks the destination before and after
candidate serialization. Signature invalidation, macro/OLE/ActiveX changes, external
relationships, opaque binaries and new structural errors have independent explicit
authorizations. Validation truncation, an SDK-open failure or a result-type/extension
mismatch cannot be overridden. Successful replacement is atomic and retains a recovery
backup by default; a no-op does not touch the file.

Saved-package three-way merge requires an explicit common ancestor. It automatically
selects one-sided changes, coalesces byte-identical branch changes and can combine
disjoint source-linked text-leaf edits in the same XML part only after proving that each
branch is reproduced byte-exactly by lossless text commands from the ancestor. A change
to the same text node, a delete/modify pair, divergent additions, arbitrary structural
XML drift or opaque payload divergence becomes a stable `wtmc_` conflict instead of a
guess. Conflict text is absent by default; bounded previews and hashes are independent
opt-ins.

The strict lazy workflow is `plan_ooxml_merge` -> review/page conflicts -> resubmit
explicit `use_ancestor`, `use_left` or `use_right` resolutions -> `apply_ooxml_merge`.
The apply call requires all three exact package fingerprints and the returned
destination-bound `wtmergeapply_` ID. It recomputes the merge, validates the candidate,
reuses the independent patch-risk authorizations, checks the Word main-part type against
the requested extension, and creates a new file through a flushed sibling temporary
file. It never overwrites. This is not yet a general revision-aware or arbitrary
structural semantic merge; those cases remain explicit conflicts.

Saved-package review inspection links standard comments to story-scoped start/end/reference
anchors, `commentsExtended` threads and resolved state, `commentsIds` durable IDs,
`commentsExtensible` metadata/reaction inventory and `people` identities. It separately
classifies text, property, move, conflict, cell and custom-XML revisions; pairs named move
ranges; and reports permission ranges plus tracking settings. The inspector is parse-only
and never returns raw XML. Separate fingerprint-guarded plan/apply actions can accept or
reject a bounded selection by stable revision ID or redacted author fingerprint. They
handle supported text/conflict wrappers, complete move pairs, property snapshots,
numbering-change acceptance, inserted-row decisions, cell-insertion acceptance and
cell-deletion rejection; unsupported paragraph merges, table-grid/vertical-merge/
numbering reconstruction, custom XML and conflicting nested decisions are reported and
not guessed.

## Saved-package review decisions

First inspect only the required revision records and retain the returned package
fingerprint and stable IDs or redacted author fingerprints. Then build a dry plan:

```json
{
  "local_path": "C:\\docs\\reviewed.docx",
  "expected_package_fingerprint": "<64-hex fingerprint>",
  "decision": "accept",
  "author_fingerprints": ["<16-hex fingerprint>"]
}
```

Apply only after reviewing `can_apply`, `apply_blocked_reasons`, `plan_id`, changed counts,
byte delta and the baseline/candidate schema result. Send selectors that reproduce the
same resolved decision set and the exact plan identity:

```json
{
  "local_path": "C:\\docs\\reviewed.docx",
  "expected_package_fingerprint": "<64-hex fingerprint>",
  "expected_plan_id": "wrplan_<returned-id>",
  "decision": "accept",
  "author_fingerprints": ["<16-hex fingerprint>"],
  "keep_backup": true
}
```

Use `revision_ids` for surgical selection or explicit `select_all=true` for a deliberate
whole-document decision. Empty implicit selection is rejected. Neither action opens Word,
returns document text, nor needs author names.

## Fast model-to-Word path

For generated material, use `apply_live_word_operations` and send a coherent array of text and equation operations once:

```json
{
  "live_document_id": "live_...",
  "expected_version": 0,
  "optimize_screen_updates": true,
  "operations": [
    {
      "type": "text",
      "text": "Mechanika kwantowa — równanie Schrödingera",
      "as_new_paragraph": true,
      "formatting": {
        "font_size_pt": 24,
        "bold": true,
        "paragraph_alignment": "center"
      }
    },
    {
      "type": "equation",
      "value": "i\\hbar\\frac{\\partial}{\\partial t}\\Psi=\\hat{H}\\Psi",
      "input_format": "latex",
      "display": true
    }
  ]
}
```

The model still generates text before the tool call. Word cannot safely accept half-token fragments as a transactional document structure. The optimization is one native batch per coherent section, not fake keystroke streaming.

Successful batches return only identifiers, the new live version, operation
counts, native verification and compact document state. They do not echo the
generated text or equations back into the model context. Set
`response_mode="full"` through the lazy execution gateway only when exact
diagnostic detail is needed.

## Native equations

The runtime accepts LaTeX, UnicodeMath, Presentation MathML and OMML strings. Every input is converted in-process to Word linear math, then Word creates an editable native `OMath`.

Supported conversion includes:

- fractions and nested groups;
- square and indexed roots;
- superscripts and subscripts;
- sums, products, integrals and `lim`/`min`/`max` with protected operand boundaries;
- common Greek letters, all registered mathematical symbols and named functions;
- angle, floor, ceiling, absolute-value and single/double-bar norm delimiters;
- upright text plus script, Fraktur, double-struck, sans-serif and monospace Latin
  mathematical alphabets;
- vectors, hats, bars, tildes and dots;
- text spans;
- matrices, aligned equation arrays and cases.

Write differentials explicitly. The recommended LaTeX is `\int f(x)\,\mathrm{d}x`;
`\,d x`, `\operatorname{d}x` and `\dd x` are also recognized. WordToolkit
canonicalizes them to the Unicode differential `ⅆ` (U+2146) and wraps the complete
integral operand in Word's invisible `〖…〗` group. A generic plain `d` without
differential notation stays an ordinary identifier.

`\mathcal`, `\mathfrak`, `\mathbb`, `\mathsf` and `\mathtt` are converted to
the corresponding Unicode mathematical alphabet and reconstructed from Word's native
`m:scr` run property during readback. Simple alphanumeric `\mathrm{...}` becomes an
upright Word math-text run. `\mathbf{...}` and `\boldsymbol{...}` preserve nested
fractions, radicals, scripts and n-ary structures as native `m:sty="b"` and
`m:sty="bi"` runs. Enclosing OfficeMath objects also receive native
`m:ctrlPr/w:rPr` weight so fraction bars, radicals, delimiters and n-ary glyphs do
not remain visually thin. The converter places private sentinels only in the temporary build
payload, lets Word create the real OMath tree, removes every sentinel through a bounded
internal OMML rewrite, reinserts one native equation and compares both semantic and
style-contract hashes. A missing marker, changed style or extra equation rolls back the
whole Word Undo transaction; sentinels and raw OMML are never returned.

Presentation MathML now retains inherited `mathvariant` values from `math` and
`mstyle`, token overrides and the fourteen variants that this native Word path can
represent without loss. OMML retains every OfficeMath run style—plain, bold, italic
and bold-italic—and independently carries bold/italic control properties onto the
first matching fraction, radical, delimiter, n-ary or other standard math object.
Word is allowed to omit its default `m:sty="i"` and `m:scr="roman"` or merge adjacent
semantically identical runs; readback compares the normalized meaning, not incidental
XML segmentation. Normal-text and literal flags, style/script changes, control drift,
formula changes and marker leakage still fail closed. MathML `initial`, `tailed`,
`looped` and `stretched` remain rejected with an explicit loss diagnostic because the
linear Word route cannot preserve those contextual Arabic forms.

LaTeX text inside `cases` no longer relies on an ordinary space that Word silently
discards. Case columns use an em space and trimmed `\text{... }` boundaries use a
four-per-em space; both survive `BuildUp()`, save/reopen and PDF export and now enter
the semantic readback contract.

Malformed or unsupported LaTeX fails before Word changes. MathML and OMML are parsed with DTD and external entity resolution disabled, strict root/namespace checks, bounded depth and element counts, then converted before Word changes. Equation AST input remains unsupported. Structurally sensitive equations are immediately read back from Word as bounded OMML after `BuildUp()`. Canonical hashes, symbol counts and integral-owned differential placement must agree or the complete Undo transaction is rolled back. Differentials in derivatives are valid outside an integral. Compact responses return only verification facts and hashes; source text and raw OMML are not returned.

For an existing saved package, lazy `inspect_ooxml_equations` takes the opposite path:
it performs no conversion and builds a canonical read graph over all 19 standard OMML
object families, matrix rows and cells, runs, text, WordprocessingML containers and
preserved extensions. It distinguishes inline `m:oMath` from display `m:oMathPara`,
keeps story/source anchors, validates argument cardinality and property vocabularies,
and reports malformed or Word-rejected placement instead of repairing it silently.

## Safety boundaries

- `start_word_application` may launch Word directly through COM; it never launches a shell or helper process.
- `open_live_word_document` accepts one explicit absolute local Word-readable path, including macro-capable formats, PDF, HTML/MHTML and XML. Macros are force-disabled and external links are not updated during open.
- `create_live_word_document` may add a new blank document to that process and optionally save it to an explicit new `.docx` path. It never overwrites.
- Connecting never opens a hidden file copy; opening is a separate explicit tool.
- Disconnecting never closes a document or quits Word.
- Closing requires a fresh live version and an explicit save/discard policy.
- Quitting requires `confirm=true` plus an explicit save-all/discard-all policy and fails before any blocking Save As prompt.
- Cursor and selection writes require a fresh token bound to document version, window, story, range and nearby context.
- Native Find returns content-bound range tokens for fully automated exact comments.
- Writes accept `expected_version` and fail on drift.
- One WordToolkit transaction creates one top-level Undo entry.
- Guarded Undo accepts only one fresh token for the current top entry beginning with `WordToolkit:`.
- Same-path save uses `Document.Save()`.
- PDF export writes and verifies a sibling temporary PDF before moving or atomically replacing the destination.
- Validation refuses unsaved changes, copies the saved DOCX to a temporary snapshot, validates with the Microsoft Open XML SDK, then deletes the snapshot.
- Saved-package review apply requires the original package fingerprint, an exact deterministic plan ID and identical selectors; signed packages are blocked.
- Review candidates are reparsed and compared with the baseline under the Microsoft Open XML SDK validator; apply stops if the mutation introduces any new schema error.
- Review mutations fail closed on unsupported structural dependencies, write atomically and retain a sibling recovery backup by default.
- Saved-package patch create never overwrites an artifact; read validates canonical metadata, every payload hash/length and bounded ZIP expansion without extracting files.
- Patch apply requires exact base, patch and path-bound apply-plan identities, and the result Word package type must match the in-place destination extension. Active content, signature invalidation, external relationships, opaque binaries and new errors cannot share one blanket bypass.
- Patch persistence uses a flushed sibling candidate, baseline-aware OPC and Open XML SDK validation, a second destination-version check, atomic replacement and a recovery backup by default.

## Build

Requirements for building:

- Windows x64;
- .NET 8 SDK;
- Windows PowerShell 5.1 or PowerShell 7+.

Build and test the self-contained plugin:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build_native_plugin.ps1
```

Outputs:

```text
dist/wordtoolkit/
dist/WordToolkit-<version>-native-win-x64.zip
```

The build fails if the packaged tree contains `.py`, `.pyc`, `.pyo`, `uv`, `uv.lock`, `pyproject.toml`, or `.venv`, or if `.mcp.json` does not launch `wordtoolkit-native.exe`.

Run the destructive-but-self-restoring real-Word acceptance test:

```powershell
pwsh -File native/scripts/live-acceptance.ps1 `
  -RuntimeExecutable dist/wordtoolkit/runtime/win-x64/wordtoolkit-native.exe
```

Every test mutation is tracked, verified and undone. The script fails if cleanup leaves any outstanding WordToolkit operation.

Run the complete packaged live acceptance gate:

```powershell
pwsh -NoProfile -File native/scripts/live-full-capabilities-timed.ps1 `
  -RuntimeExecutable dist/wordtoolkit/runtime/win-x64/wordtoolkit-native.exe
```

This creates timestamped DOCX and PDF evidence, exercises all 48 live actions through
the lazy public gateways, requests full responses only for assertions, checks the
default compact equation preflight separately, and closes its own test document.

## Workspace cleanup

Dry-run:

```powershell
python scripts/clean_workspace.py
```

Apply:

```powershell
python scripts/clean_workspace.py --apply
```

The cleaner constrains every target to the repository root. It preserves only the current native plugin directory, current native ZIP and `dist/.gitignore`, and removes stale releases, failed publish experiments, test output and native `bin`/`obj` directories.

## Latest published artifact

The development manifest/runtime is 0.35.0. The latest immutable public release remains
0.34.0 until the strengthened CI, review and licensed Word release gate pass.

Version:

```text
0.34.0+codex.20260722105842
```

Windows x64 ZIP:

[WordToolkit native plugin](https://github.com/Fr4u/WordToolkit/releases/download/v0.34.0/WordToolkit-0.34.0%2Bcodex.20260722105842-native-win-x64.zip)

SHA-256: `f4625c2c15827e78c9b5c54eaa50adf6aeeb64644235cafc46aa8374812b3944`

Live demonstration document:

```text
C:\Users\Admin\Desktop\WordToolkit-Native-Mechanika-Kwantowa-2026-07-20.docx
```

The document contains 16 paragraphs, four editable native equations and a native four-item list. It was saved through Word and validated with zero Microsoft Open XML SDK errors.

See [native migration details](docs/NATIVE-MIGRATION.md) for architecture, benchmarks, package audit and known limits.
