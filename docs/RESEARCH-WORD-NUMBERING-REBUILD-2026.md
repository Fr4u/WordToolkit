# Semantic Word numbering reconstruction

Date: 2026-07-26

## Problem statement

The existing `restart_numbering_sequence` operation is not numbering reconstruction. It
clones one valid `w:num`, changes one level start and moves a bounded tail to the clone.
It deliberately cannot create `numbering.xml`, create an `abstractNum`, define missing
levels, replace a damaged list model, assign an arbitrary reviewed hierarchy or repair a
package whose intended list no longer has a usable instance.

`rebuild numbering` therefore needs a separate semantic operation. The model must accept
a reviewed list blueprint and exact paragraph targets, create an independent complete
numbering definition, and bind only those targets to it. It must not infer authorial intent
from indentation, visible text, localized style names or a plausible sequence of digits.

## Primary standards evidence

- ECMA-376/ISO 29500 defines an OPC package as parts, content types and relationships, so
  creating a previously absent numbering part is a three-entry package mutation rather
  than merely writing `/word/numbering.xml`:
  <https://ecma-international.org/publications-and-standards/standards/ecma-376/>.
- Microsoft documents `w:numbering` as the root containing picture bullets,
  `w:abstractNum` definitions and `w:num` instances:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.numbering?view=openxml-3.0.1>.
- A `w:num` is a unique instance which must refer to a base abstract definition and may
  carry level overrides:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.numberinginstance?view=openxml-3.0.1>.
- A paragraph `w:numPr` selects an instance and level. `w:numId=0` removes inherited
  numbering and is not an instance reference:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.numberingproperties?view=openxml-3.0.1>
  and
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.numberingid?view=openxml-3.0.1>.
- `lvlText` is literal text except for one-based `%1` through `%9` level substitutions;
  references deeper than the current level are ignored:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.leveltext?view=openxml-3.0.1>.
- The three declared list shapes are `singleLevel`, `multilevel` and
  `hybridMultilevel`:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.multilevelvalues?view=openxml-3.0.1>.
- Word's object model exposes the same nine-level `%1` through `%9` composition and
  requires a one-character number format for bullets:
  <https://learn.microsoft.com/en-us/office/vba/api/word.listlevel.numberformat>.
- Microsoft documents adding a new package part as a relationship-bearing package
  operation, not a filename convention:
  <https://learn.microsoft.com/en-us/office/open-xml/general/how-to-add-a-new-document-part-that-receives-a-relationship-id-to-a-package>.

Existing WordToolkit research remains authoritative for `lvlRestart`, legal numbering,
`restartNumberingAfterBreak`, the 31-character Word label ceiling and the qualified Word
16.0 conflict between replacement-level `w:start` and `w:startOverride`; see
`RESEARCH-WORD-NUMBERING-SEQUENCE-EXECUTION-2026.md` and
`RESEARCH-WORD-NUMBERING-REPAIR-2026.md`.

## Semantic input model

One transaction can contain multiple independent rebuild commands. Each command owns:

1. a short caller command ID used only to correlate results;
2. one complete list blueprint containing one to nine uniquely indexed levels;
3. one or more exact paragraph candidates, each with a stable semantic node ID, a
   package-bound candidate fingerprint and one blueprint level index;
4. an explicit list shape and section-break restart policy.

A level is typed rather than XML. The initial public vocabulary covers the deterministic
formats already executable by WordToolkit: decimal, decimal-zero, upper/lower Roman,
upper/lower Latin letter, bullet and none. It also carries a non-negative start, validated
`lvlText`, suffix, justification, legal-numbering flag, restart mode and typed twip-based
tab/indent geometry. No namespace, element name, relationship ID, `abstractNumId`,
`numId`, `nsid`, template code or raw property fragment crosses the AI boundary.

The engine allocates IDs, deterministic `nsid`/template codes and relationship IDs. It
uses the main story's actual Strict or Transitional namespace and the corresponding
numbering relationship type.

## Package creation and preservation

If a valid numbering part exists, the transaction appends one new `abstractNum` and one
new `num` per command in schema order and updates `numIdMacAtCleanup` when present. It
does not normalize, reorder, merge or remove any existing definition.

If the main document has no numbering relationship, the transaction creates all of the
following atomically:

- a new sibling numbering part with the standard numbering content type;
- an internal main-part relationship with a collision-free ID and a target relative to
  the actual main-part URI;
- the matching `[Content_Types].xml` override;
- every selected paragraph's direct `w:numPr`.

The operation refuses multiple numbering relationships, unsafe relationship parts,
duplicate package entries, signatures, malformed infrastructure XML or an existing
unrelated part at the proposed URI. It never guesses that an orphan `numbering.xml` is the
intended target.

Every unplanned entry remains byte-identical. The exact inverse must remove newly added
entries and restore every replaced entry byte-for-byte.

## Paragraph targeting

Inspection accepts a bounded exact set of paragraph node IDs. A candidate fingerprint
binds the package fingerprint, semantic identity, source part and ordinal, subtree and
structural fingerprints, and the current explicit numbering properties. Plan/apply reject
stale or duplicate candidates.

Revision, unresolved `mc:AlternateContent`, extension-island and unsupported story
ancestry block a target. The first version materializes direct paragraph numbering and
does not rewrite paragraph styles. That is a preservation rule, not a claim that style-
linked future paragraphs are rebuilt; a later style-binding command must update the style
definition and list level together under the same transaction.

## Required proof before publication

The candidate is reparsed from the complete package and must prove all of the following:

- OPC structure and Word package type remain valid;
- Microsoft Open XML SDK validation ran, the candidate is valid and introduced no new
  errors;
- each new abstract definition and instance exists exactly once and every declared level
  resolves to the requested typed values;
- every selected paragraph uses the expected new instance and requested level;
- no unselected paragraph's explicit numbering, identity, text or structure changed;
- complete document text and non-numbering semantic topology are preserved;
- targeted sequence counters are exact and labels are exact for every supported format;
- the new list introduces no numbering or list-sequence error;
- changed entries equal the reviewed set and the predicted package fingerprint is exact;
- the inverse reconstructs the original uncompressed OPC entries exactly.

Planning writes nothing. Apply recomputes the complete plan from the current package and
identical semantic intent, requires the exact plan ID, writes through atomic package
replacement and keeps a sibling backup by default. Responses expose IDs, counts, hashes,
states and bounded diagnostics, never paragraph text or package XML.

## Explicit remaining boundaries

This design does not pretend that every list format is locale-independent. Picture
bullets, custom `w:format`, East Asian/locale-sensitive labels, bidirectional layout,
revision-view selection, style-definition binding, field refresh and list merging require
separate qualified modules. They remain part of the broad `rebuild numbering` objective;
the operation must advertise those missing capabilities rather than silently synthesize
plausible output.
