# WordToolkit Native

WordToolkit 0.19 is a local Windows MCP plugin that starts or attaches to the real Microsoft Word application and controls it through a persistent native .NET COM STA thread. The new document-engine core can also inspect the package graph, semantic structure, section bindings, and typed style graph of a saved Word OOXML file without starting Word. Its lossless editing slice binds text in the main body, headers, footers, notes, comments, glossary building blocks and text boxes to exact XML byte spans, combines bounded commands into one hash-preconditioned package mutation, predicts the result fingerprint and retains an exact guarded inverse without reserializing unrelated XML.

The packaged plugin does not contain or launch Python, `uv`, `pywin32`, a virtual environment, an interpreter bootstrap, or a per-call helper process. Its MCP command points directly to:

```text
./runtime/win-x64/wordtoolkit-native.exe
```

The repository still retains the older Python/OOXML service as historical source and a possible remote-service reference. It is not copied into the 0.19 local plugin, does not participate in its startup, and is not required at runtime.

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

The runtime implements 48 tested Word Live actions plus eight standalone,
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
inspect_ooxml_sections
inspect_ooxml_styles
resolve_ooxml_formatting
plan_ooxml_text_edits
apply_ooxml_text_edits
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
- sums, products and integrals with limits;
- common Greek letters, mathematical symbols and functions;
- vectors, hats, bars, tildes and dots;
- text spans;
- matrices, aligned equation arrays and cases.

Malformed or unsupported LaTeX fails before Word changes. MathML and OMML are parsed with DTD and external entity resolution disabled, strict root/namespace checks, bounded depth and element counts, then converted before Word changes. Equation AST input remains unsupported.

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

## Build

Requirements for building:

- Windows x64;
- .NET 8 SDK;
- PowerShell 7.

Build and test the self-contained plugin:

```powershell
pwsh -File scripts/build_native_plugin.ps1
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
0.19.0+codex.20260721195319
```

Windows x64 ZIP:

[WordToolkit native plugin](dist/WordToolkit-0.19.0+codex.20260721195319-native-win-x64.zip)

Live demonstration document:

```text
C:\Users\Admin\Desktop\WordToolkit-Native-Mechanika-Kwantowa-2026-07-20.docx
```

The document contains 16 paragraphs, four editable native equations and a native four-item list. It was saved through Word and validated with zero Microsoft Open XML SDK errors.

See [native migration details](docs/NATIVE-MIGRATION.md) for architecture, benchmarks, package audit and known limits.
