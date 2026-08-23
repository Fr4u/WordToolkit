# Migration 0018: guarded live SmartArt creation

- Native action count: 151.
- Added `inspect_live_word_smartart_layouts` and `insert_live_word_smartart` as lazy
  native actions; the public MCP surface remains 15 tools.
- Callers must inspect the connected Word layout catalog, bind the returned opaque
  layout token, and provide exactly one fresh `range_token` or `selection_token`.
- Layout tokens are bound to the connected document and `live_version`, are consumed
  before mutation, and must be reacquired after any edit.
- Creation is intentionally inline-only. It inserts one native SmartArt object in one
  custom Word Undo record and verifies the collection delta, inserted range, SmartArt
  identity, and exact layout ID before advancing `live_version`.
- A failed readback triggers verified rollback or the existing live-document quarantine
  path. Raw COM objects, layout XML, and document content never cross the contract.
- Generic `Application` object-model execution remains blocked. The dedicated layout
  catalog is the only supported source of a creation token.
- `save_live_word_document` now states explicitly that persistence is not a content
  mutation and therefore does not increment `live_version`; runtime behavior is
  unchanged.
