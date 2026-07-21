# 0005 — Live Word structure map and equation learning

WordToolkit 0.5.0 adds two local-STDIO-only read tools:

- `map_live_word_structures`
- `inspect_live_word_equation_learning`

The remote HTTP surface remains at 65 tools. Local STDIO grows from 77 to 79.

Equation inserts now write privacy-preserving structural outcomes after the COM
transaction. No API input changes are required. Preflight responses gain
`features` and `learning`; equation mutation responses gain the structural
features and whether the learning observation was recorded.

Existing live handles, versions, selection tokens and save semantics remain
compatible.
