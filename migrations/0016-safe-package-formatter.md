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
package. The policy excludes structural properties. Scalar properties use contribution
equivalence; `rFonts`, `color`, `u` and paragraph/run `shd` use a stricter bounded
candidate-by-candidate package reparse and full group-equivalence proof. A missing
inherited theme/fallback member, unresolved table/revision/unmodeled cascade layer or
the 64-proof ceiling keeps the request fail-closed. A no-op apply deliberately creates
no output. Signed packages and any candidate without a complete passing engine plus Open
XML validation proof cannot be applied.

No action was removed. The later group-aware extension adds one bounded scan counter to
the already-open `scan` object and preserves the local v1 action names and request shape.
