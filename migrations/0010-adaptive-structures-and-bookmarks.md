# Adaptive structures and native bookmarks

WordToolkit 0.10.0 adds three local-STDIO-only tools:

- `inspect_live_word_structure_learning`
- `preflight_live_word_bookmarks`
- `insert_live_word_bookmarks`

The remote HTTP surface remains at 65 tools. Local STDIO grows from 84 to 87.

`map_live_word_structures` gains additive
`adaptive_type_histograms=true` behavior. Present typed collections are scanned
on the first and second observation, then at exponentially spaced presence
observations. Explicit `include_type_histograms=true` still scans every typed
collection. The learning store never receives document content, counts, paths,
handles or document-derived identifiers.

Bookmark batches contain at most 200 names and 500,000 text characters. The
complete payload is assigned once, native bookmarks are added and range-checked
inside one Undo transaction, and existing-name collisions fail before
mutation. Existing clients and live handles remain compatible.
