# Installed Word object model and table-field refresh

WordToolkit 0.13.0 adds three local-STDIO-only tools:

- `inspect_live_word_object_model_types`
- `inspect_live_word_object_model_members`
- `update_live_word_table_fields`

The two read-only inspectors build and page through a bounded local catalog of
the actual Word COM type library installed on the PC. The cache stores only API
metadata and excludes document data, documentation text and Help paths.

The write tool updates up to 5,000 existing native fields in one selected table
through one collection call and Undo transaction. It verifies field count and
numeric type stability and never accepts or returns field codes.

This is an additive local schema change. The remote 65-tool contract is
unchanged. Existing equation/structure learning files need no migration; the
object-model catalog uses its own schema-versioned cache and regenerates safely
when absent or invalid.
