# Safe live fields

WordToolkit 0.9.0 adds two local-STDIO-only tools:

- `preflight_live_word_fields`
- `insert_live_word_fields`

The remote HTTP surface remains at 65 tools. Local STDIO grows from 82 to 84.

The write tool accepts a typed allowlist instead of arbitrary Word field-code
text. External-data fields, macros, DDE, links and raw switches cannot enter
the live document through this API. A complete request is validated before
Word is resolved, written as one marker payload and converted from the end of
the range inside one custom Undo record.

Formula fields intentionally accept only numeric expressions and an allowlist
of deterministic built-in functions. References to document cells, bookmarks
or external data are not formula syntax; bookmark lookup uses the dedicated
`reference` field kind and verifies existence before mutation.

Formula requests use a stable period/comma syntax. At the last possible moment
the bridge reads Word's active international separators and translates the
expression and numeric picture. This prevents valid functions from failing on
installations whose list separator is a semicolon.

The same release hardens existing live-equation tools. Structured formulas
force native OMML readback and a recursive fidelity comparison against the
prepared AST. Native Word build-up text or scope drift fails closed, requests
Undo and is recorded as `NATIVE_FIDELITY_MISMATCH` without source content.
