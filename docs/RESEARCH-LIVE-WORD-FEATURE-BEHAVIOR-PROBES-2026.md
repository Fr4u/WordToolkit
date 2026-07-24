# Isolated live Word feature-behavior probes — 2026

Contract: `wordtoolkit.probe_live_word_feature_behaviors/1.0`

## Problem

`Application.Version`, `Document.CompatibilityMode` and successful property access are
useful environment evidence, but they do not prove that Word can execute a feature in the
current build, channel, policy and process state. Calling a destructive test against the
user's document would replace one uncertainty with a worse one. The probe therefore uses
fixed operations in disposable documents and refuses to return success when cleanup is not
proved.

## Authoritative Microsoft surface

- [`Documents.Add`](https://learn.microsoft.com/en-us/office/vba/api/word.documents.add)
  creates a new empty Word document and accepts `Visible=false`.
- [`Document.Close`](https://learn.microsoft.com/en-us/office/vba/api/word.document.close%28method%29)
  accepts `wdDoNotSaveChanges`, used here as integer value `0`.
- [`OMaths.Add`](https://learn.microsoft.com/en-us/office/vba/api/word.omaths.add)
  creates an equation from a text range and returns a range; the equation is obtained from
  that returned range.
- [`OMath.BuildUp`](https://learn.microsoft.com/en-us/office/vba/api/word.omath.buildup)
  converts the equation to professional format.
- [`ContentControls.Add`](https://learn.microsoft.com/en-us/office/vba/api/word.contentcontrols.add)
  creates a specified content-control type at an optional range.
- [`Shapes.AddSmartArt`](https://learn.microsoft.com/en-us/office/vba/api/word.shapes.addsmartart)
  inserts one selected SmartArt layout and accepts an anchor range.
- [`UndoRecord.StartCustomRecord`](https://learn.microsoft.com/en-us/office/vba/api/word.undorecord.startcustomrecord)
  begins an application-level custom Undo record; Microsoft documents the name limit as
  64 characters.
- [`UndoRecord.EndCustomRecord`](https://learn.microsoft.com/en-us/office/vba/api/word.undorecord.endcustomrecord)
  completes that record.

These pages define the callable surface. They do not promise identical behavior across
every Word build, so the operation returns observed local evidence rather than a global
compatibility claim.

## Fixed probes

| Probe | Scratch operation | Required readback |
|---|---|---|
| native OMath | write `x^2`, call `OMaths.Add`, obtain `returnedRange.OMaths(1)`, call `BuildUp` | document OMath count increases by exactly one |
| content controls | add one type-0 rich-text control at range `(0,0)` | collection count increases by exactly one |
| SmartArt | take local layout 1 and insert it with a scratch anchor | shape count increases by one and `HasSmartArt` is true; zero layouts is `unavailable` |
| custom Undo | open one named record, insert fixed scratch text, close the record and call `Undo(1)` | Undo returns true and exact scratch text equals its baseline |

Each probe has its own document. A failed OMath construction therefore cannot poison the
content-control, SmartArt or Undo evidence.

## Cleanup contract

Before each probe WordToolkit records the document count and exact prior active document
and window objects. It then creates one `Visible:false` document, activates it, runs the
fixed operation and closes it with `Close(0)`. It reactivates the previous document and
window and compares COM identity plus document count.

A feature may return `failed` only after all those checks pass. A close exception,
document-count drift, active-object mismatch, unreadable post-state or failed
`EndCustomRecord` produces `TEMPORARY_DOCUMENT_CLEANUP_FAILED`. The connected handle is
removed from normal use and placed in quarantine until explicit disconnect. Exception
messages, document text, paths and COM objects are not returned.

## Security and limits

- exactly four fixed probes and at most four scratch documents;
- explicit `confirm_scratch_documents=true` is mandatory;
- no arbitrary member names, text, paths, templates, layouts or operation graphs;
- no filesystem write, Save/SaveAs, network, macros, DDE or external relationship access;
- scratch content is read only for fixed Undo verification;
- the connected document's content, style and object mutation surfaces are never touched;
- Word may refresh volatile view/session package metadata when active documents change,
  so byte-identical or whole-package-identical target state is not claimed;
- the Word process must already be connected; the action will not launch it;
- COM exceptions collapse to fixed issue codes.

## Remaining boundary

One passing local probe is evidence for that running Word process, not proof for every
document type, compatibility mode, locale, update channel or Office product edition. A
qualified release matrix still needs multiple Word builds and broader document-mode
fixtures. SmartArt layout zero is reported as unavailable rather than guessed unsupported.

## Verified release evidence

The guarded acceptance test ran against Microsoft Word 16.0 build 16.0.20131. The four
fixed behaviors passed. The target document retained exact rollback-snapshot text, range,
structural count and `Saved` invariants; only the documented volatile semantic
whole-package projection changed after active-window switching.

The enabled `0.39.0+codex.20260724230914` cache then repeated the action through the lazy
MCP gateway. It reported four passes, created and closed four scratch documents, restored
the previous active document/window and document count, kept `live_version=0`, validated
against the output schema returned by that same installation, exposed none of the checked
content/path/user/licence/COM field names and disconnected the handle.

Two builds made with the pinned SDK 8.0.423 produced byte-identical 196-file,
87,523,060-byte trees and byte-identical 36,870,505-byte archives at SHA-256
`6b942f24de52bd82c41dd1cae69dc12c929ea9472d9731cde7babf7763dee1d9`.
Build, personal source and enabled cache have zero path/length/hash differences and zero
Python files.
