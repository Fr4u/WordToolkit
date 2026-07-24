# Figure and caption graph

`WordFigureCaptionGraphBuilder` projects declared WordprocessingML drawing containers,
their DrawingML/VML representations, package resources and caption evidence into a
bounded, source-linked read graph. The graph is evidence, not a renderer. It never
opens Word, decodes image or embedded-object payloads, evaluates fields, follows an
external relationship or executes active content.

## Object boundary

One logical `WordFigureDefinition` owns one or more declared representations:

- `w:drawing/wp:inline` and `w:drawing/wp:anchor` become typed modern
  representations;
- `w:pict` becomes a legacy VML representation;
- `w:object` becomes a preserved legacy-object representation;
- a direct representation becomes one logical figure;
- representations inside one `mc:AlternateContent` group collapse into one logical
  figure while every branch remains visible.

The object-family classifier distinguishes picture, classic chart, diagram, shape,
ink/content part, embedded object and unknown payloads. This is a conservative family
classification. It is not a full geometry, SmartArt, OLE or rendering model.

`PrimaryRepresentationId` is present only when there is one direct declared
representation. `RepresentationSelectionBasis` prevents a false MCE claim:

- `SingleDeclaredRepresentation` means no alternate branch was present;
- `AlternateContentChoicePresentNotEvaluated` means a declared `mc:Choice` exists, but
  application capability and MCE branch selection were not evaluated; the logical
  figure therefore has no primary representation and its object kind is `Unknown`;
- `AlternateContentFallbackOnly` means only a fallback representation was found;
- `AlternateContentUnclassified` preserves an irregular alternate group without
  inventing a selected branch.

This distinction matters. `mc:Choice` is conditional on the consuming application's
understood namespaces; its mere presence does not prove that Word selected it. Use
`inspect_ooxml_markup_compatibility` when actual branch evaluation is required.

## Placement, accessibility and resources

Modern placement retains declared extent, anchor distances, relative stacking height,
behind-document, layout-in-cell, overlap, simple-position and lock flags. Horizontal
and vertical positioning preserve the reference frame and the declared alignment or
EMU offset. Effect extents, simple-position coordinates, Office 2010 relative width
and height (normalized to thousandths of a percent), wrap-side and wrap-distance
metadata are typed. Tight/through wrapping polygons preserve a bounded start point and
line-point list.

Legacy VML placement is no longer only an opaque style string. Known declarations for
position mode, left/top/width/height, z-order, horizontal/vertical alignment and
reference frames, tenth-percent offsets, wrap mode/distances, bounded `wrapcoords`
polygons and visibility are parsed into typed fields. Physical `pt`, `pc`, `in`, `cm`,
`mm` and `px` lengths are normalized to EMU while their bounded lexical source is retained for trusted consumers. Unknown
style declarations remain untouched in the saved OPC bytes. Preferred dimensions and
coordinates are declarations, not a promise of final page geometry.

Non-visual drawing properties retain bounded name, title, description, hidden and
decorative evidence. Original character counts and truncation flags survive even when
text is bounded. Missing alternative text, invalid/duplicate `docPr` metadata and
invalid extents remain stable diagnostics.

Relationship-bearing payload elements produce typed resource objects for embedded or
linked images, charts, four diagram resources, content parts, embedded objects,
hyperlinks, VML images and unknown roles. Internal relationships resolve only against
the already-read OPC snapshot. The model records target part, content type, byte length
and precomputed SHA-256 when resolution succeeds. External targets and direct VML
sources are recorded as external and unresolved; they are never fetched. Missing,
ambiguous and internally unresolved relationships are not discarded.

## Caption evidence and association policy

WordprocessingML does not contain a structural figure-to-caption relationship. Word's
caption command inserts text and normally a `SEQ` field near the selected object. The
graph therefore keeps caption detection separate from association inference.

A paragraph is a caption candidate when it has either:

- a caption-style identifier/name; or
- at least one parsed `SEQ` field.

The graph records both evidence types, parsed sequence labels, bounded visible result
text and the exact source-linked paragraph. Table and equation labels are never linked
to a figure. Deleted/move-from figures and captions remain visible evidence but cannot
be selected.

Associations are considered only inside the same story and semantic container, within
the configured paragraph distance (default two). The deterministic score is:

| Evidence | Score change |
|---|---:|
| same paragraph | 95 base |
| caption immediately after figure | 90 base |
| caption immediately before figure | 85 base |
| caption two paragraphs after figure | 68 base |
| caption two paragraphs before figure | 63 base |
| caption style and `SEQ` both present | +5 |
| no `SEQ` field | -15 |
| sequence label classified as figure | +3 |

A relation becomes `Selected` only when it is the unique highest score for both the
figure and the caption and the score is at least 70. Equal highest scores become
`Ambiguous`; lower alternatives remain `Candidate`. No tie is broken by source order.
Unresolved and ambiguous evidence produces diagnostics. The score is a documented
heuristic, not a hidden claim that OOXML declared the relation.

## Stable identity and dependency graph

Figures (`wdfig_`), representations (`wdfr_`), resources (`wdfrs_`), captions (`wdfc_`)
and associations (`wdfca_`) receive deterministic package-fingerprint-bound IDs. The
graph owns the fingerprint; its objects retain source part, XML element ordinal and
semantic paragraph/story/container handles. Builders reject semantic, reference or
style graphs from a different package snapshot.

The unified dependency graph adds typed figure, representation, resource and caption
nodes plus these edges:

- semantic drawing `DefinesFigure`;
- figure `FigureHasRepresentation`;
- representation `FigureUsesResource`;
- resource `FigureResourceTargetsPart`;
- semantic paragraph `DefinesCaption`;
- figure `FigureCaptionAssociation`.

Only a selected association is a resolved dependency. Candidate and ambiguous edges
stay unresolved with an explicit status qualifier. This prevents an impact or deletion
analysis from silently treating proximity as a proven reference.

## AI boundary

The lazy `inspect_ooxml_figures` action has `summary`, `figures`, `representations`,
`captions`, `associations`, `resources` and `issues` views. It accepts exact figure or
caption IDs, object-kind filtering, offset paging and a maximum page size of 100.

The default response exposes counts, kinds, stable handles and policy flags only.
Sensitive fields are split into independent opt-ins:

- `include_text` exposes bounded accessibility/caption metadata;
- `include_source` exposes part paths, semantic IDs, XML paths and ordinals;
- `include_relationship_targets` exposes bounded relationship targets;
- `include_geometry`, valid only with `view=representations`, `detail=declared` and
  `max_items` from one to two,
  exposes at most 128 declared DrawingML or VML wrapping-polygon points per
  representation;
- `include_issues` explicitly adds a bounded issue preview to a non-issue view; it is
  false by default so pagination does not repeat diagnostics.

Raw XML, cached binary bytes and image pixels have no response field. The result always
states that Word was not opened, binary resources were not decoded, external targets
were not followed and active content was not executed. Default-response and complete
JSON-RPC envelope size caps are regression-tested.

The typed Engine graph is a trusted in-process model and deliberately retains bounded
declared text and targets for non-AI consumers. It is not itself a safe telemetry or AI
serialization format. The lazy action above is the privacy boundary; callers that bypass
it must implement an equivalent redacted projection.

## Limits and scale evidence

Production defaults cap projected stories at 256, logical figures/captions at 100,000
each, representations at 200,000, resources/associations at 500,000 each, issues at
10,000, one wrapping polygon at 4,096 line points, one story part at 128 MiB,
aggregate story XML at 512 MiB, parsed elements at 5,000,000 and selected retained
string metadata at 32 Mi characters. Relationship IDs also have a 4,096-character
hard ceiling before graph retention. Cancellation is
checked at bounded intervals across package projection, XML parsing, representation and
caption extraction, and response selection. Caption text is counted while streaming
into a `MaxTextCharacters` buffer, so the metadata ceiling is enforced before an
unbounded concatenated caption string can be allocated.

The checked-in Windows x64 benchmark creates 10,000 figures, 10,000 distinct
relationship IDs, 10,000 `SEQ` captions, 10,000 resources and 19,999 association
candidates. Seven graph builds produced a 1,897.6 ms median and 2,084.6 ms p95 after
package, semantic, reference and style setup. The full retained managed delta was
317,194,760 bytes and peak process working set 1,251,549,184 bytes on that host. These
are boundary measurements, not throughput promises. See
`benchmarks/2026-07-23-windows-x64/figures-10k.json`.

The benchmark exposed and removed three quadratic scans: all captions per figure, all
associations per ambiguity diagnostic and all fields per paragraph. This is why the
benchmark belongs to the contract rather than a release-note ornament.

## Honest exclusions

This slice is read-only. It does not insert or edit figures/captions, renumber `SEQ`
fields, generate a table of figures, evaluate MCE against a particular Word version,
calculate rendered anchor geometry, execute page layout, group shapes, parse DrawingML
shape paths/effects,
inspect image pixels, synchronize chart workbooks, interpret SmartArt, activate OLE,
render a page or prove visual equivalence across Word versions. Unsupported payloads
remain in the OPC snapshot and are reported; lack of a typed child model is never
permission to delete them.
