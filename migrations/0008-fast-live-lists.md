# 0008 — Fast native Word Live lists

WordToolkit 0.8.0 adds one local-STDIO-only write tool:

- `insert_live_word_list`

The remote HTTP surface remains at 65 tools. Local STDIO grows from 81 to 82.

The operation is additive and does not change existing text, table, equation,
formatting, save or validation inputs.
