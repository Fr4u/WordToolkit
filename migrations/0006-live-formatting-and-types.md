# 0006 — Live selection formatting and typed structure map

WordToolkit 0.6.0 adds one local-STDIO-only write tool:

- `format_live_word_selection`

The remote HTTP surface remains at 65 tools. Local STDIO grows from 79 to 80.

`insert_live_word_text` and `type="text"` mixed operations gain an optional
`formatting` object. Existing calls remain compatible.

`map_live_word_structures` gains optional `type_histograms` with bounded numeric
`Type` counts. They are disabled by default to avoid per-object COM cost. No
document content is returned.
