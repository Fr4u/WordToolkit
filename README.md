# WordToolkit Native

WordToolkit 0.30 is a local Windows MCP plugin that starts or attaches to the real Microsoft Word application and controls it through a persistent native .NET COM STA thread. The document-engine core can also inspect the package graph, semantic structure, section bindings, typed style, numbering, theme, settings, font-table, field/bookmark/reference, canonical OfficeMath and review/revision graphs, compare two saved packages at separate OPC-entry and source-linked semantic layers, create deterministic reversible package patches, plan guarded three-way merges, and resolve modeled effective formatting without starting Word. Theme-backed fonts resolve through `themeFontLang` and supplemental script mappings, then cross-reference declared and embedded font metadata; colors resolve to concrete RGB values when the source is deterministic. Nested complex and simple fields are parsed per Word story into inert dependencies rather than evaluated or exposed as raw XML. Native equations are classified into source-linked objects and argument roles without converting them or returning raw OMML. Comments are joined to story anchors, threaded replies, durable identifiers, people records and reaction inventory; revisions are classified with authorship, nesting, named moves and permission ranges. Every result retains its declaration and provenance. The lossless editing core binds text and tracked-review structures to exact XML byte spans, combines bounded commands into hash-preconditioned package mutations, predicts result fingerprints and retains exact guarded inverses without reserializing unrelated XML.

The packaged plugin does not contain or launch Python, `uv`, `pywin32`, a virtual environment, an interpreter bootstrap, or a per-call helper process. Its MCP command points directly to:

```text
./runtime/win-x64/wordtoolkit-native.exe
```

The repository still retains the older Python/OOXML service as historical source and a possible remote-service reference. It is not copied into the 0.30 local plugin, does not participate in its startup, and is not required at runtime.

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
| Native LaTeX equation in real Word | fraction, root, scripts and sum | about 100–158 ms |
| Full 48-tool real-Word acceptance | 71 MCP requests, save/validate/PDF/reopen/reconnect | 24.492–24.691 s |

The main 48,800-character batch is about 2.9× faster than the old Python bridge. The native process spawned no Python or `uv` child; the only observed child was Windows `conhost.exe`.

These numbers are machine-specific. They are recorded as test evidence, not universal promises.

## Supported local tools

The runtime implements 48 tested Word Live actions plus 25 standalone,
bounded OOXML engine actions. The initial MCP catalog exposes
only 11 common actions plus three token-lean gateways. Rare schemas are
searched and loaded one at a time:

```text
search_wordtoolkit_actions
inspect_wordtoolkit_action
execute_wordtoolkit_action
```

The complete lazy action set is:

```text
list_live_word_documents
start_word_application
create_live_word_document
open_live_word_document
connect_live_word_document
inspect_ooxml_package
inspect_ooxml_semantics
query_ooxml_semantics
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
inspect_ooxml_equations
inspect_ooxml_review
inspect_ooxml_fonts
resolve_ooxml_formatting
plan_ooxml_text_edits
apply_ooxml_text_edits
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

Run the complete packaged 14-tool/73-action live acceptance gate:

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

## Current artifact

Version:

```text
0.32.0+codex.20260722091136
```

Windows x64 ZIP:

[WordToolkit native plugin](https://github.com/Fr4u/WordToolkit/releases/download/v0.32.0/WordToolkit-0.32.0%2Bcodex.20260722091136-native-win-x64.zip)

SHA-256: `17ef223ddac5b9b8ba02c7b86c29089b2e76d8cc730cbee58f9aa0225d088f25`

Live demonstration document:

```text
C:\Users\Admin\Desktop\WordToolkit-Native-Mechanika-Kwantowa-2026-07-20.docx
```

The document contains 16 paragraphs, four editable native equations and a native four-item list. It was saved through Word and validated with zero Microsoft Open XML SDK errors.

See [native migration details](docs/NATIVE-MIGRATION.md) for architecture, benchmarks, package audit and known limits.
