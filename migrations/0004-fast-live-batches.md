# 0004 — Fast mixed Word Live batches

WordToolkit 0.4.0 adds two local-STDIO-only tools:

- `preflight_live_word_equations`
- `apply_live_word_operations`

The remote HTTP tool surface remains unchanged at 65 tools. Local STDIO grows
from 75 to 77 tools.

Existing single text, single equation and equation-batch calls remain
compatible. Callers that generate long documents should send ordered text and
equation objects through `apply_live_word_operations`. The method appends at
the document end; it does not replace cursor or selected content.

The mixed batch increments `live_version` once per operation but uses a single
Word Undo record. Invalid input is rejected during preflight before the Word
document is resolved. Any failure after mutation requests one Word rollback and
leaves the live version unchanged.
