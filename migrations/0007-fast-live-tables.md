# 0007 — Fast native Word Live tables

WordToolkit 0.7.0 adds one local-STDIO-only write tool:

- `insert_live_word_table`

The remote HTTP surface remains at 65 tools. Local STDIO grows from 80 to 81.

The operation is additive and does not change existing text, equation,
formatting, save or validation inputs.
