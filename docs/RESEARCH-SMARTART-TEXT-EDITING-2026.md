# Guarded live SmartArt text editing — 2026-07-24

## Problem

Native Word SmartArt is not one string in one XML part. A saved package can contain a
DiagramML data model and a persisted diagram drawing. Rewriting only
`word/diagrams/data*.xml` can therefore leave the editable model and the cached/rendered
drawing inconsistent. The first mutation slice deliberately uses Microsoft Word as the
writer and limits the public operation to node text.

## Primary Microsoft contracts

- [`SmartArtNode.TextFrame2`](https://learn.microsoft.com/en-us/office/vba/api/office.smartartnode.textframe2)
  exposes a node's text frame, and Microsoft's example assigns through
  `TextFrame2.TextRange.Text`.
- The [SmartArtNode member list](https://learn.microsoft.com/en-us/office/vba/api/overview/library-reference/smartartnode-members-office)
  exposes hierarchy, child nodes, type, text frame and associated shapes.
- The [`dsp:dataModelExt` contract](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-odrawxml/052211cd-4f97-4860-aeb1-3fee41317937)
  can relate a diagram data model to a persisted Diagram Drawing. That is the reason a
  one-part package rewrite is not accepted as synchronized SmartArt mutation.

## Implemented operations

`wordtoolkit.prepare_live_word_smartart_text_edits/1.0` resolves one exact root from the
same `story_type`, `story_link_index`, `collection_kind` and `source_index` returned by
the live drawing inspector. It reads at most 128 nodes, 16,384 characters per node and
65,536 characters for the complete root. It returns at most 32 records and issues a
random one-time token only for existing single-line text no longer than 4,096 characters.

The token is stored only in process memory and is bound to:

- connected document ID and live version;
- exact story/collection/source locator;
- floating Word shape ID or inline range identity;
- anchor/range start, end and story type;
- SmartArt layout, quick-style and color IDs;
- every node's order, level, hidden/type state and child count;
- the SHA-256 of every node text and the whole-root context.

Returned text is optional. The prepare action states explicitly that it still reads the
bounded complete text set when previews are disabled because stale-token protection
requires the whole context. Public text fingerprints use an in-process random HMAC key;
raw COM objects and XML have no response path.

`wordtoolkit.apply_live_word_smartart_text_edits/1.0` accepts one to 32 unique tokens from
one prepared root plus a required `expected_version`. It consumes the tokens, resolves
the root again, recomputes the complete snapshot and rejects any drift before opening an
Undo record. Changed nodes are written through
`SmartArtNode.TextFrame2.TextRange.Text` in one custom Word Undo record. A second complete
snapshot must prove exact target text, unchanged structure and unchanged untargeted node
text. Failure requests exactly one bounded `Document.Undo(1)`. An exact no-op creates no
Undo entry, does not repaginate and does not advance the live version.

The COM request is non-replayable. If a client stops waiting after mutation begins, the
runtime reports an unknown outcome instead of silently running the edit twice.

## Automated evidence

Nine focused native tests cover the drawing-layout and SmartArt slice. New cases prove:

- closed versioned schemas for prepare and apply;
- a successful token-verified edit, one version increment and one repagination;
- rejection of an externally changed whole-root context before Undo starts;
- automatic Undo when Word does not preserve exact requested text;
- a stable no-op with no Undo, repagination or version increment;
- privacy-preserving preparation without returned node text.

The full local gates pass 497 Engine tests, 339 Native tests and 1,309 Python tests, with
16 intentional Python skips. Ruff passes.

## Real Microsoft Word proof

The installed `0.39.0+codex.20260724084026` runtime opened a disposable DOCX containing
one native five-node SmartArt root. Through the real MCP STDIO boundary it prepared node
1 at live version 0, changed `Etap źródłowy` to
`Etap zweryfikowany przez WordToolkit`, repaginated once and returned live version 1.
The before and after structure fingerprints were identical. A follow-up live drawing
inspection read the exact new text.

Word saved the document and the Microsoft Open XML SDK validation reported zero errors.
The source was 20,482 bytes at SHA-256
`152df1fc626f24e4900f7a8a748cb5cd1e2638fbed31bd4089050fada8488737`; the result was
20,512 bytes at SHA-256
`4b2a39bc136582fbc615c45ff23bcdf84c188dfa9c86ab6d002fa0e3c8a8388f`.

Package comparison found five Word-normalized parts. Crucially, both
`word/diagrams/data1.xml` and `word/diagrams/drawing1.xml` changed. In the source, each
contained the old text exactly once and the new text zero times. In the result, each
contained the old text zero times and the new text exactly once. Word therefore kept the
editable data model and persisted drawing synchronized for this fixture. The proof does
not claim that Word leaves unrelated serialization bytes untouched: `docProps/core.xml`,
`word/document.xml` and `word/settings.xml` also changed during the live save.

Word exported a 23,892-byte before PDF and a 23,183-byte after PDF. Poppler rendered one
1,191 by 1,684 pixel page for each. Visual inspection shows the same five boxes in the
same locations; the new three-line text remains inside the first box without clipping or
overlap. The primary blue-component boxes are unchanged:
`(287,408)-(553,567)`, `(582,408)-(849,567)`, `(878,408)-(1145,567)`,
`(435,596)-(701,755)` and `(730,596)-(997,755)`.

## Reproducible installed package

Two repository-pinned .NET SDK 8.0.423 builds produced identical 196-file,
86,690,088-byte expanded trees and identical 36,645,859-byte archives at SHA-256
`bb3ccf021e2135a1ca89c83920fcdd3c7aa73713936ac65db22febbe90096cf4`.
The executable is
`cf894595b6522cff0489b989e56f29b7810845b7b8d1e9fe7c8215808cf93d97`, the runtime
assembly is
`4308afb9f9008044d5cad68873a6e2e1b832ae568dbfd73046c5009d540b6437`, the Engine
assembly is
`c5479c59b8f6ae2827f485396a66c9989fcafad2fafc69d0580eafba792a3d2d`, and the Open XML
SDK adapter is
`d1fbfed7589ad3b9621024fcf3980a8bd15f450125935d34a86eb08f428d2d97`.

The personal source and enabled cache each contain the same 196 files with zero
path/length/hash differences. Installed discovery reports the exact version, 99 native
actions, 15 exposed MCP tools and 12 actions with explicit operation version, permission,
reversibility and output-schema metadata.

## Deliberate boundary

This is not general SmartArt editing. The runtime does not add, delete, reorder, promote
or demote nodes; change layout, quick style or color; create diagrams; expose durable
DiagramML-to-COM node IDs; or promise cross-version pixel equivalence. Multiline node
text and roots above the hard limits fail closed. Those gaps remain separate work rather
than being hidden behind a broad `edit SmartArt` label.
