# WordToolkit versus word-mcp-live

## Scope and evidence

This comparison targets Windows desktop Word, where both projects can operate
on a document already open in Microsoft Word. It is not a count-the-tools
marketing exercise.

The reviewed competitor snapshot is
[`ykarapazar/word-mcp-live` commit `c6c76179`](https://github.com/ykarapazar/word-mcp-live/tree/c6c76179f66b27846d8f6a822a683e144d9288cb),
dated 2026-05-29. The audit counted MCP decorators from source, inspected the
COM and Undo implementations, and ran the repository's tests from a clean
clone on Windows.

At that snapshot:

- the README said 124 tools;
- an AST scan found 120 `@mcp.tool` functions;
- `TOOLS.md` said 115 tools;
- the GitHub About text said 114 tools;
- `uv run --frozen pytest -q` could not start because `uv.lock` referenced a
  missing workspace member;
- after allowing uv to repair the temporary clone, one test passed and one
  failed because the async test had no configured async pytest plugin.

These are reproducibility findings, not claims that every competitor feature
is broken.

## Current result

| Dimension | WordToolkit 0.18.0 | word-mcp-live reviewed snapshot |
|---|---|---|
| Open-document identity | Opaque connected handle bound to exact name/path; owner scoped | Active document or filename/path selection per call |
| Optimistic concurrency | Monotonic `live_version`; required for dedicated writes | No document version contract |
| Cursor/selection safety | Context-bound fresh selection token | Raw ranges and paragraph indexes |
| Find | Native bounded Word Find; no full-document response | Native Word Find |
| Replace | Complete bounded match preflight, reverse-range mutation, one Undo, rollback, Track Changes restoration | Native loop in one custom record; safety cap and tracking workaround |
| Comments | Fresh HMAC item token; add/reply/resolve/delete; selection token for add | Raw 1-based comment index |
| Revisions | Fresh HMAC item token; accept/reject one verified revision | Raw revision IDs, author filters or all |
| Track Changes state | Explicit target state, read-back verification and manual rollback | Direct property assignment |
| Undo | Only one current top `WordToolkit:` entry; HMAC token and version required; manual actions block | Arbitrary `times`; calls `Document.Undo(times)` |
| Undo failure rollback | Custom record ends, then one document Undo on exception | Custom record ends on exception without rollback |
| Layout diagnosis | Eight bounded checks; issue/result caps; no paragraph text returned | Five checks; scans all paragraphs |
| Native equations | Typed LaTeX/UnicodeMath/MathML/OMML input, editable OMath creation, automatic bounded OMML contract/placement readback for sensitive structures and rollback | Dedicated linear equation insertion |
| Installed Word API | 12,167 deterministic member profiles with typed schemas, policy and preflight on the release machine | Dedicated hand-written tools |
| DOCX round trip | Direct OPC/OOXML copy-on-write, opaque-part preservation audit, structural and Open XML SDK validation | Broad `python-docx`-based file tools |
| Native package evidence | 25 .NET tests; 48/48 live tools in 71 MCP requests; valid DOCX; native PDF; close/open/reconnect | Two discovered tests; one passed and one failed after lock repair |
| macOS live support | None | JXA-based live tools; some review/layout features unavailable |
| Dedicated live page/header/image helpers | Generic typed member path plus isolated DOCX tools; fewer dedicated wrappers | Broader dedicated live wrapper set |

## Why the Undo distinction matters

The competitor's [`undo_record`](https://github.com/ykarapazar/word-mcp-live/blob/c6c76179f66b27846d8f6a822a683e144d9288cb/word_document_server/core/word_com.py)
starts and ends a custom record. Its exception path does not call
`Document.Undo`. A tool can therefore fail after partially changing the
document while still returning an error.

Its public Undo tool accepts a raw count and calls `Document.Undo(times)`.
Nothing proves that all crossed entries came from the MCP server. A user keystroke
above the last MCP entry can be reverted.

WordToolkit uses two separate contracts:

1. Normal undoable writes run inside one custom record; an exception ends the
   record, requests exactly one rollback and leaves `live_version` unchanged.
2. User-requested Undo is allowed only when Word exposes the current top entry,
   that label begins with `WordToolkit:`, and a fresh HMAC token plus exact
   version still match. Only one entry can be undone.

Properties that Word does not reliably put in custom Undo, including Track
Changes and comment resolution, use verified assignment plus restoration of
the prior value on failure. They do not claim false Ctrl+Z coverage.

## Real Word acceptance

`scripts/real_word_live_gap_test.py` was executed against Microsoft Word 16.0
on Windows. It created one disposable document and verified:

- two native Find matches before and after replacement;
- two replacements in one custom Undo record with rollback enabled and Track
  Changes restored;
- comment add, reply and resolution;
- an inspectable tracked insertion;
- token-verified revision acceptance;
- a top entry named `WordToolkit: accept live revision`;
- guarded one-step Undo without crossing manual edits;
- same-path save;
- valid ZIP/OPC structure;
- zero structural errors;
- zero Microsoft Open XML SDK validation errors.

The machine-readable report is
`artifacts/wordtoolkit-live-competition-test/real-word-live-gap-test.json`.

## Where the competitor still leads

word-mcp-live still has broader dedicated live wrappers for page layout,
headers, footers, page numbers, images, watermarks and cross-references, and it
has macOS JXA support. WordToolkit can reach part of the installed Windows COM
surface through typed member capabilities and covers these structures in its
isolated DOCX engine, but that is not the same ergonomic promise.

Therefore the defensible claim for 0.18.0 is narrower and harder:
WordToolkit is stronger for **Windows live-document safety, concurrency,
rollback, tokenized review, native equation fidelity, OOXML preservation and
tested reproducibility**. It does not claim universal platform or dedicated
wrapper supremacy.

Future releases should close dedicated page/header/image/cross-reference
workflows without weakening the handle, token, version and rollback contracts.
