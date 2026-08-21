# Word footnote/endnote integrity and guarded repair — 2026

## Scope

This checkpoint covers saved DOCX/DOCM/DOTX/DOTM footnotes and endnotes. It does not
claim page-layout equivalence, synthesize missing note prose, infer a missing special-note
type from an identifier, or edit a live unsaved Word document.

## Normative findings

Microsoft's Word object model treats footnotes as a document collection and exposes
numbering restart policy separately from note content. `Footnotes.Add` creates a note at
an explicit range; `Footnotes.NumberingRule` controls continuation across pages or
sections. The package model likewise separates ordinary references in document stories,
definitions in a dedicated note part, automatic reference marks inside definitions, and
document/section properties controlling placement, format, start and restart.

Primary references:

- [Word `Footnotes` collection](https://learn.microsoft.com/en-us/office/vba/api/word.footnotes)
- [Word `Footnotes.Add`](https://learn.microsoft.com/en-us/office/vba/api/word.footnotes.add)
- [Word `Footnotes.NumberingRule`](https://learn.microsoft.com/en-us/office/vba/api/Word.Footnotes.NumberingRule)
- [Word `FootnoteOptions`](https://learn.microsoft.com/en-us/office/vba/api/word.footnoteoptions)
- [Open XML `FootnoteProperties`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.footnoteproperties)
- [Open XML `FootnoteDocumentWideProperties`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.footnotedocumentwideproperties)
- [Open XML `EndnoteDocumentWideProperties`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.endnotedocumentwideproperties)
- [Open XML `FootnoteReference`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.footnotereference)
- [Open XML `FootnoteReferenceMark`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.footnotereferencemark)
- [Open XML `SeparatorMark`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.separatormark)
- [Open XML `FootnoteEndnoteType.Id`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.footnoteendnotetype.id)

The identifier is a signed 32-bit identity value. Examples and real Word packages do not
justify treating one numeric pair as universally magical. Separator semantics come from
the definition type and the document-wide special reference. Therefore the engine joins
by `(note kind, id)` and validates type; it does not hardcode `-1`, `0`, `1`, or any other
identifier as the separator policy.

## Implemented graph

`WordNoteGraphBuilder` projects one bounded, source-linked graph from the exact immutable
package snapshot:

- footnote and endnote definitions, including ordinary, separator, continuation separator,
  continuation notice and unknown types;
- ordinary references from every projected Word story, including a diagnostic for the
  non-conformant case where a reference appears inside a note story;
- document-wide special-note references;
- document and per-section placement/format/start/restart policies;
- invalid IDs, missing definitions, ambiguous duplicate definitions, invalid custom-mark
  values, missing automatic reference marks, unsupported structure and orphans.

Stable public identities bind package fingerprint, note kind, source part, lexical element
ordinal and local content. Output is bounded and token-lean: the default inspection returns
only guarded repair candidates and issues, never note text or raw XML.

## Repair boundary

The first repair slice intentionally supports only two deletion proofs:

1. remove an ordinary orphan definition whose text is empty and whose descendants are on
   a narrow allowlist;
2. remove a later redundant duplicate only when every definition sharing its `(kind, id)`
   has canonically identical XML.

Contentful or structurally complex orphans remain untouched. Non-equivalent duplicates,
missing definitions, missing special definitions and malformed numbering policies remain
diagnostics. A special definition cannot be synthesized safely from an ID alone because
the special reference does not carry the intended separator type.

Planning requires both the package fingerprint and exact definition fingerprint. The
candidate is materialized and reparsed, then must prove:

- structural OPC validity and complete note-graph coverage;
- removal of exactly one target definition;
- preservation of every untargeted definition, ordinary reference, special reference and
  numbering policy;
- no new note issue code/severity/count;
- byte preservation of every unplanned package entry;
- exact inverse reconstruction of the base package fingerprint.

Apply rebuilds the plan from the current file, rejects signatures and plan drift, requires
baseline-aware Microsoft Open XML SDK validation with no new errors, publishes through the
atomic writer and retains a sibling backup by default.

## Public surfaces

- `inspect_ooxml_notes`
- `plan_ooxml_note_repair`
- `apply_ooxml_note_repair`
- `wordtoolkit-native note-package --mode inspect|plan|apply --request ...`

Direct .NET, strict JSON CLI and lazy MCP routes share the same Engine owner. The actions
open neither Word nor the network and return neither raw XML nor note prose.

## Remaining work

Safe creation of missing definitions, ID renumbering, reference rewrites, content repair,
cross-note conversion, section-policy normalization, layout proof and live-unsaved repair
remain unimplemented. Those operations require stronger intent and compatibility evidence;
guessing would turn a repair engine into a quiet document shredder.
