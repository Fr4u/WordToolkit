# Migration 0003: Word Live local tools

WordToolkit 0.3.0 adds nine tools to the local STDIO server. The remote
Streamable HTTP schema remains at the existing 65-tool v1 contract.

Local clients should discover tools dynamically. A caller that wants to edit an
open desktop document must use this sequence:

1. `list_live_word_documents`
2. `connect_live_word_document`
3. `inspect_live_word_document`
4. `get_live_word_selection` for cursor/selection targets
5. `insert_live_word_text` or `insert_live_word_equation`
6. `save_live_word_document`
7. `validate_live_word_document`
8. `disconnect_live_word_document`

Cursor and selection mutations now require the selection token returned by the
immediately preceding selection read. Replacing selected content additionally
requires `replace_selection=true`.

No existing isolated-DOCX tool schema changed.
