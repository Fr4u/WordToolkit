# Native live table formulas

WordToolkit 0.12.0 adds two local-STDIO-only tools:

- `preflight_live_word_table_formulas`
- `insert_live_word_table_formulas`

The tools add a separate typed contract for calculations inside one existing
uniform rectangular Word table. Each item selects a destination row and
column, one allowlisted aggregate function and either positional directions or
a bounded numeric cell range. The bridge generates the native formula; raw
formula strings and field codes are not accepted.

Formula fields calculate when inserted. The default path verifies their native
type and result range without repeating the calculation; callers may set
`force_update=true` for an additional checked `Field.Update()`.

This is an additive local schema change. Existing general formula fields keep
their prior grammar and continue to reject table-cell identifiers. No stored
document or learning-state migration is required.
