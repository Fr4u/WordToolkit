# Equations, OfficeMath, and complete TeX

Read this reference for LaTeX, UnicodeMath, MathML, OMML, equation preflight, equation
updates, equation-only DOCX creation, or complete TeX document compilation.

## Choose the correct path

- For one editable Word equation, prefer `input_format="latex"`.
- Use `unicodemath` when the source is already Word-compatible linear math.
- Use Presentation MathML when the caller has a bounded semantic MathML expression.
- Use direct `omml` for an exact supported Word OfficeMath structure that has no safe
  linear representation.
- Use `compile_tex_document` only for a complete TeX/LaTeX document. Its result is PDF,
  not editable OfficeMath.

Never replace a requested native equation with an image or plain-text imitation.

## Fast equation workflows

For a new document containing only equations, prefer the core
`create_live_word_equation_document` action. It preflights the complete set before file
creation, publishes once, saves, validates, Word-renders, performs equation render QA,
and inspects the saved package. Use one new absolute DOCX path and an idempotency key.

For equations mixed with prose:

1. Build the complete logical batch.
2. Use `preflight_live_word_operations` for the exact target-bound batch when it is risky.
3. Apply the unchanged operations once.

For syntax exploration, `preflight_live_word_equations` returns every attributable
failure in input order. Repair only failed items using `equation_id`, `diagnostic`, and
`suggestion_code`, then preflight the final complete set once. `conversion_only` is a
cheap syntax check; `valid` is deliberately null and it is not Word insertion proof.

Native preflight runs in a dedicated worker Word process. Defaults are 20 seconds per
equation and 120 seconds total. Do not raise limits merely to hide a hanging formula.

To replace one existing equation, first call `inspect_live_word_equations` with a small
page and no text preview. Pass the returned one-based index, current version, and fresh
`equation_token` to `update_live_word_equation`; never update by raw index alone.

## LaTeX-to-OfficeMath boundary

The converter is a broad Word-oriented math dialect, not a programmable TeX runtime. It
supports more than 400 symbol aliases and major structures including fractions, roots,
n-ary and contour integrals, matrices, determinants, cases, aligned arrays, prescripts,
accents, phantom/smash geometries, boxed expressions, Dirac notation, and derivative
helpers. Packages, arbitrary user macros, file inclusion, TikZ, chemistry layout, and
page-layout commands do not belong in the native equation path.

Use explicit integral differentials such as `\int f(x)\,\mathrm{d}x`. WordToolkit groups
the complete n-ary operand and verifies differential placement. Prefer explicit `\cdot`
when adjacency is ambiguous. Use `\left\|u\right\|` for norms.

Mathematical alphabets and bold/italic equation scopes are read back and verified. A
successful conversion or native object count is not visual proof.

## Direct OMML

Direct OMML accepts exactly one bounded Transitional or Strict OfficeMath root. It is
inserted through a Word-owned XML template and checked in isolated staging and after
publication. Equation semantics and the bounded `m:oMathParaPr/m:jc` profile have
separate expected/actual proofs; actual justification comes from Word's
`OMath.Justification` readback.

Do not pass a complete document part, relationships, multiple equation roots, drawings,
active content, or arbitrary WordprocessingML. Raw OMML is never returned. Unsupported
or mixed namespaces fail closed.

## Complete TeX documents

`compile_tex_document` executes one bounded source with an explicit absolute Tectonic
binary and a new PDF output path. Supply the expected executable SHA-256 when qualified.
The provider always uses `--untrusted` and defaults to cached resources.

Set `allow_network_resource_fetch=true` only when the user accepts missing resources
being downloaded into Tectonic's external cache. WordToolkit does not claim network
isolation or bind the complete resource-bundle hash. A TeX PDF is never an editable OMath
fallback.

## Visual acceptance

After Word PDF/PNG export inspect `equation_render_qa`. Treat
`RAW_LINEAR_CONTROL_SYNTAX`, `PAGE_EDGE_INK`, and
`CONTENT_EXCEEDS_USABLE_PAGE_WIDTH` as mandatory review signals. PDF-only output can
detect structural raw syntax but cannot prove page-edge raster geometry. Render and
inspect pages before calling a complex equation document finished.
