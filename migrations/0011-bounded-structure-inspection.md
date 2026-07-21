# Bounded live structure inspection

WordToolkit 0.11.0 adds one local-STDIO-only read tool:

- `inspect_live_word_structure_items`

The tool accepts one of the 23 collection names already exposed by
`map_live_word_structures`, a zero-based offset, a limit of at most 200 items
and an optional bounded text preview. It attaches to Word once, isolates
property failures per item and never returns raw field codes or external
hyperlink addresses.

This release extends the existing structure-learning file additively. Fixed
property names, aggregate read successes/failures, retry thresholds and timing
may be stored. Property values, optional text, document counts, paths, owners,
handles and document-derived identifiers never enter the store. Existing
0.10.0 learning files remain readable because the schema version is unchanged
and the new keys are optional.

Properties that have succeeded remain enabled. Repeated failures use
inspection observations 1, 2, 4, 8 and so on. Set
`adaptive_property_probing=false` only for an explicit diagnostic refresh.

