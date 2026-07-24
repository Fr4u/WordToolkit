# 0016 — Safe saved-package formatter

## Added

- Lazy actions: `plan_ooxml_format` and `apply_ooxml_format`.
- Operation contracts: `wordtoolkit.plan_ooxml_format/1.0` and
  `wordtoolkit.apply_ooxml_format/1.0`.
- Native action count: 97.
- Initial explicit policy: `remove_redundant_direct_formatting`.

## Client behavior

Inspect the source first and pass its exact 64-character package fingerprint. Choose a
new same-extension `output_path` that does not exist. Call the plan action with an
explicit `policies` array; review counts, validation and `apply_blocked`, then repeat the
same source fingerprint, output path and policies with the returned
`formatter_apply_plan_id`.

The action never treats a formatting request as permission to rewrite the whole XML
package. The initial policy excludes structural and composite properties and removes
only scalar direct formatting proven equivalent to the preceding resolved cascade. A
no-op apply deliberately creates no output. Signed packages and any candidate without a
complete passing engine plus Open XML validation proof cannot be applied.

No existing action was removed or tightened. The addition is compatible within the
local v1 schema policy.
