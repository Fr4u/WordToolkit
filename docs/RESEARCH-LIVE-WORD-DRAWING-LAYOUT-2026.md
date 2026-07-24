# Live Microsoft Word drawing-layout research — 2026-07-24

## Question

The saved-package engine already retains declared DrawingML/VML placement, shape
topology and SmartArt data. That is not the same thing as the object layout executed by
Microsoft Word. This note defines what Word's public object model can prove, what it
cannot prove, and the boundary implemented by
`wordtoolkit.inspect_live_word_drawing_layout/1.0`.

## Primary sources

- [Word Shape object](https://learn.microsoft.com/en-us/office/vba/api/word.shape): a
  drawing-layer object anchored to a text range, with position, size, wrapping, group,
  chart and SmartArt properties.
- [Word Shape.Anchor](https://learn.microsoft.com/en-us/office/vba/api/word.shape.anchor):
  every floating shape has an anchor and remains on the anchor's page.
- [Word Shapes collection](https://learn.microsoft.com/en-us/office/vba/api/word.shapes):
  the document collection excludes inline shapes; range/story collections are required
  outside the main story.
- [Word InlineShape object](https://learn.microsoft.com/en-us/office/vba/api/word.inlineshape)
  and [InlineShapes collection](https://learn.microsoft.com/en-us/office/vba/api/word.inlineshapes):
  inline objects behave as text characters; a document-level count covers the main story
  only, so other stories require range-scoped collections.
- [Shape.Left](https://learn.microsoft.com/en-us/office/vba/api/word.shape.left),
  [Shape.LeftRelative](https://learn.microsoft.com/en-us/office/vba/api/word.shape.leftrelative)
  and [WdRelativeHorizontalPosition](https://learn.microsoft.com/en-us/office/vba/api/word.wdrelativehorizontalposition):
  `Left` is either a point offset or an alignment constant and must be interpreted with
  its reference frame; relative percentage is undefined at `-999999`.
- [WdWrapType](https://learn.microsoft.com/en-us/office/vba/api/word.wdwraptype): Word's
  public wrap enum distinguishes square, tight, through, front, top/bottom, behind and
  inline behavior.
- [Range.Information](https://learn.microsoft.com/en-us/office/vba/api/word.range.information)
  and [WdInformation](https://learn.microsoft.com/en-us/office/vba/api/word.wdinformation):
  page/section values are available, but page-relative x/y return `-1` when the range is
  outside the visible screen area.
- [Window.GetPoint](https://learn.microsoft.com/en-us/dotnet/api/microsoft.office.interop.word.window.getpoint?view=word-pia):
  returns screen pixels for a Range or Shape and throws when the entire object is not
  visible. It is viewport evidence, not page geometry.
- [Document.Repaginate](https://learn.microsoft.com/office/vba/api/Word.Document.Repaginate):
  asks Word to repaginate the complete document.
- [SmartArtNode members](https://learn.microsoft.com/en-us/office/vba/api/overview/library-reference/smartartnode-members-office):
  a SmartArt node is a semantic data-model node with hierarchy, child nodes, text and an
  associated shape range.

All sources are Microsoft Learn pages for the public Office/Word object model. No
undocumented COM member and no arbitrary member-name dispatch enters the new action.

## What the Word object model actually exposes

| Domain | Reliable public evidence | Boundary |
|---|---|---|
| Floating shape | anchor range, page/section, type, size, rotation, visibility, z-order, lock/layout flags | the values are authoritative only for the connected Word build and current document state |
| Position | horizontal/vertical reference, point offset or alignment token, optional relative percentage | an offset is meaningless without its reference frame |
| Wrapping | type, side and four distances | this is Word's object-model state, not the final line-by-line text-flow mesh |
| Inline object | range, page/section, size and text-flow identity | no durable shape name; visible page x/y can be `-1` off-screen |
| Group | runtime child range and child-local transforms | Word can normalize or flatten package-declared group kinds |
| SmartArt | semantic node hierarchy, hidden/type/level state, text and associated shape range | node/shape identity is runtime-scoped; this is not a durable join to DiagramML IDs |
| Screen rectangle | active-window pixels through `GetPoint` | unavailable off-screen and never equivalent to document/page coordinates |

## Rejected shortcuts

1. **Returning raw COM properties.** That would leak an unstable application API into the
   AI contract and force the model to reconstruct enum semantics. The action instead
   emits typed high-level objects and keeps unknown/missing reads as diagnostics.
2. **Calling every `Left`/`Top` pair page coordinates.** This is wrong whenever a shape
   is relative to a margin, column, character, paragraph or line, or when the value is an
   alignment constant. A page-relative box is emitted only for page/page reference frames
   and numeric offsets.
3. **Treating `GetPoint` as rendering.** It is a visible-window pixel query and fails when
   the object is not fully visible. It is isolated behind `include_screen_pixels`, capped
   to ten returned roots and explicitly labelled viewport-dependent.
4. **Equating package nodes with runtime shapes.** Word can normalize legacy VML,
   DrawingML groups and diagrams. The two models remain separate evidence surfaces.
5. **Reading text and redacting later.** Names, titles, alternative text and SmartArt
   node text are not accessed unless `include_text=true`. Opt-in output then shares a
   4,096-character budget.

## Implemented contract

`inspect_live_word_drawing_layout` is a read-oriented, replay-safe live action with an
explicit operation version, permission record, reversibility record, input schema,
output schema and MCP effect hints. It:

- scans main-story `Document.Shapes`/`InlineShapes` and range-scoped collections for
  every other supported Word story;
- caps root scanning at 10,000 and paged output at 100;
- classifies roots as floating, inline, group, SmartArt, picture, chart, OLE, canvas or
  other without returning raw enum-only data;
- distinguishes alignment constants from numeric point offsets;
- emits a page-relative box only under the two-reference/numeric precondition;
- flattens at most 128 group members to depth 16 with group-local coordinates;
- returns at most 128 SmartArt nodes and 256 associated shapes;
- returns at most 100 bounded diagnostics containing error type but not exception text;
- optionally repaginates, while stating that the layout cache may change;
- returns no XML, COM object, external target or fetched content.

Traversal IDs such as `wdlo_000001` are stable only for that response traversal and live
document version. They are intentionally not advertised as persistent OOXML identities.

## Real-Word and installed-runtime evidence

The native Debug runtime opened `tests/upstream/fixtures/lo_groupshape_sdt.docx` read-only
in Microsoft Word, repaginated it, inspected it and closed it with discard. The source
SHA-256 stayed exactly
`83c47ec672afd0bce726f90582f40ebe96e10514c1f6da3bfec5bc9507db456c`.

The saved-package figure graph reported one VML representation whose declared shape model
contains two nested `group` nodes. The connected Word object model reported one floating
root `group` and one child normalized to native `msoAutoShape`, with page 1/section 1,
page-relative horizontal reference, paragraph-relative vertical reference, front-of-text
wrapping and no diagnostic. Both surfaces reported two runtime/declaration objects, but
the child kind differed. That discrepancy is expected evidence that OOXML declaration and
Word execution are not a lossless one-to-one type mapping.

The Word process ID before and after the proof remained the existing process; no unrelated
document or application was closed. Sensitive text was disabled and zero sensitive fields
or characters were returned.

The release proof then repeated this inspection with the enabled personal-plugin runtime
at `0.39.0+codex.20260724063719`. Its 196-file cache was path/length/hash-identical to
the reproducible release tree. Capability discovery returned 95 actions. The installed
runtime returned the same one-root/one-child topology, page/section, 150.2 by 815.35 point
size, page-relative right alignment, paragraph-relative -44.25 point vertical offset and
front-of-text wrapping, with zero diagnostics and no text/XML/COM disclosure. The source
hash and the pre-existing Word process ID remained unchanged.

Separately, the complete 49-action live acceptance ran against the packaged runtime. It
passed in 51.614 seconds, Open XML validation succeeded, and the 166,264-byte PDF plus its
saved DOCX contained 49 paragraphs, one table, 12 editable equations, one image, one
comment, one footnote and one endnote. The acceptance document was closed and the existing
Word application was left running.

## Remaining boundary

This slice does not provide an off-screen page-render tree, final text-line collision mesh,
pixel-perfect raster, font-substitution report, printer-independent layout, durable joins
between runtime shapes and `wdsh_`/DiagramML IDs, shape/SmartArt mutation, or cross-version
layout equivalence. The dependency graph still does not contain live runtime nodes. Those
gaps remain explicit; the new action narrows the old blanket "rendered layout missing"
statement to a concrete live object-model capability with named omissions.
