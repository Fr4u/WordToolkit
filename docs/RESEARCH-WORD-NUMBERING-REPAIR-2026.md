# Transactional Word numbering-sequence repair

Date: 2026-07-25

## Result

WordToolkit now implements one narrow saved-package numbering mutation:
`restart_numbering_sequence` with fixed scope `remaining_instance_in_story`. It restarts
the list at one source-linked paragraph and moves only that paragraph and later uses of
the same numbering instance in the same Word story to a cloned `w:num`. It does not
rewrite paragraph text, renumber earlier items, mutate unrelated list instances or open
Microsoft Word during plan/apply.

The repair is exposed through the direct .NET operation, the strict
`numbering-repair-package` CLI and the lazy
`plan_ooxml_numbering_repair`/`apply_ooxml_numbering_repair` MCP actions. All three use
the same planner, plan ID, candidate validation and atomic writer.

## Why cloning the numbering instance is necessary

A paragraph's `w:numPr/w:numId` selects a numbering instance. That instance points to an
abstract numbering definition and can carry level overrides. Changing the existing
instance's start would also change earlier paragraphs that still reference it. The safe
tail restart therefore creates a fresh instance and reassigns only the intended tail.

The relevant normative object-model mappings are Microsoft's
[`NumberingInstance`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.numberinginstance?view=openxml-3.0.1),
[`NumberingId`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.numberingid?view=openxml-3.0.1)
and
[`StartOverrideNumberingValue`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.startoverridenumberingvalue?view=openxml-3.0.1).

## Planning algorithm

1. Read the exact package fingerprint and project source-linked semantic paragraphs,
   styles, numbering definitions and executable list sequences.
2. Resolve the target stable paragraph node and require its expected `numId` and level.
3. Reject revision/MCE ambiguity, corrupt definitions, stale IDs, signed packages and a
   tail above 10,000 paragraphs.
4. Select the target plus later sequence items with the same `numId` in the same story.
5. Clone the exact numbering instance under a fresh ID and write the requested start at
   the selected level.
6. Materialize direct paragraph numbering where style inheritance would otherwise keep
   the old instance.
7. Rebuild and reparse the complete candidate package.
8. Prove text preservation, exact affected reassignment, exact target start, unchanged
   earlier/unrelated sequence outputs, no new numbering errors and changes confined to
   the planned parts.
9. Produce a deterministic plan ID, result fingerprint, changed-part hashes and an exact
   inverse transaction.

## Word compatibility boundary

Microsoft's published interoperability note says Word ignores `w:start` inside a
replacement `w:lvl` and uses `w:startOverride`. A guarded fixture on Word 16.0 build
16.0.20131 produced the opposite result when both values conflicted: replacement-level
`w:start` won. The source is Microsoft's
[`MS-OI29500` note](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/8f145055-5422-4df0-933d-e67a81c633cd).

WordToolkit does not hide that conflict. It writes `w:startOverride` for conforming
consumers and, only when the cloned instance already contains a replacement level,
synchronizes its nested `w:start` to the same value. The plan returns the corresponding
compatibility rule. This is qualified behavior, not a universal cross-version claim.

## Apply and recovery contract

Apply rebuilds the entire plan from the current package and intent. The package
fingerprint and plan ID must match exactly. Microsoft Open XML SDK validation must have
run, and the candidate may introduce no new schema errors. Digital signatures block the
operation. Persistence uses atomic replacement and keeps a sibling recovery backup by
default; disabling the backup is explicit.

The MCP response returns filenames, stable IDs, counts, states and hashes. It returns no
paragraph text or raw XML. Detailed paragraph evidence is optional, capped at 200 items
and accompanied by `affected_paragraph_details_truncated`; omission is never presented as
complete detail.

## Verification

Unit and adapter tests cover tail restart, style-inherited numbering, exact inverse,
replacement-level start synchronization, stale or ambiguous targets, hard bounds,
cancellation, unknown JSON fields, validator absence, plan drift, CLI parity and the
actual JSON-RPC MCP output schema.

The guarded `WORDTOOLKIT_REAL_WORD_NUMBERING_REPAIR_TEST=1` acceptance creates a valid
four-item list, restarts the second item at seven, validates the candidate with the
Microsoft Open XML SDK and opens it read-only in Word. The engine and Word both return
counter values `1, 7, 8, 9` and labels `1., 7., 8., 9.`. Closing Word without saving
leaves the repaired package hash unchanged.

## Explicit non-goals

This slice does not repair corrupt numbering, create abstract definitions or levels,
merge independent lists, choose tracked-revision views, render picture bullets or
locale/custom formats, update fields derived from list labels, or prove pagination. Those
domains remain visible limitations, not guessed behavior.
