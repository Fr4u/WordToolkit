# Neutral DOCX conformance checkpoint — 2026-07-23

This checkpoint records one narrow, reproducible comparison. It is not a market-leadership
claim. The harness covers 42 WordprocessingML scenarios and 21 neutral operation names;
it does not cover page layout, visual fidelity, Word-open repair dialogs, token cost,
latency, hostile-package security, or the full public surface of either implementation.

## Pinned inputs

| Component | Pin | Role |
|---|---|---|
| [docx-platform-tests](https://github.com/kklimuk/docx-platform-tests) | `fe0ee99602e6f982255ecaa2b45d4936a7f46150` | Apache-2.0 neutral runner, fixtures, assertions and protocol |
| WordToolkit | `65f75be56436fe1f5f67cf17951540e02a4ff0e6` (`0.35.0+git.65f75be`) | `docx-platform-adapter` protocol-v1 implementation |
| [safe-docx](https://github.com/UseJunior/safe-docx) | `3615e2132672386bad2979e3f3fd20bdd9fe5e32` (`0.17.0+git.3615e2132672`) | Existing protocol-v1 implementation |

The run used Windows 10 build 19045, .NET SDK 8.0.423, Node.js 24.11.1 and npm
11.6.2. The result document reports schema version 3, DSL version 1.8, protocol
version 1 and timestamp `2026-07-23T00:30:25.620Z`.

## Why the comparison is neutral

The runner invokes every adapter with the same versioned command contract:

```text
<adapter> --protocol-version 1 --operation operation.json --input input.docx --output output.docx
```

The adapter receives the operation and input package, but not the assertions or expected
output. Exit code 0 means an output package was produced, 1 means execution failed, 2 is
an honest unsupported operation and 3 is a protocol mismatch. The runner alone grades
the output. ECMA-backed assertions produce `pass`; suite-declared metamorphic invariants
produce `invariant-pass`. There were no `fail`, `error`, `pass-divergent` or
`protocol-mismatch` outcomes in this run.

## Result

| Implementation | Pass | Invariant pass | Unsupported | Fail/error/divergent/protocol |
|---|---:|---:|---:|---:|
| WordToolkit | 19 | 2 | 21 | 0 |
| safe-docx | 18 | 2 | 22 | 0 |

Only three scenario outcomes differ:

| Scenario | WordToolkit | safe-docx |
|---|---|---|
| `acceptDeletedTableRowRemovesEntireRow` | pass | unsupported |
| `rejectInsertedTableRowRemovesEntireRow` | pass | unsupported |
| `composeCompatibilityMode15WritesCompatSetting` | unsupported | pass |

Both implementations pass the deleted-paragraph-mark and inserted-paragraph-mark merge
scenarios. WordToolkit's support is fail-closed: it merges only an immediate following
paragraph with no paragraph properties and no tracked revision content. Ambiguous
shapes remain unsupported instead of being guessed into data loss.

The raw 42-row result is checked in at
[`benchmarks/docx-platform-tests-wordtoolkit-safe-docx-2026-07-23.json`](benchmarks/docx-platform-tests-wordtoolkit-safe-docx-2026-07-23.json).
Its byte length is 74,657 and SHA-256 is
`e0103e86940d285027494fd86a7916007943cda31ccad68a52fcde858df324dd`.

## Reproduction

Use clean checkouts at the pins above. On Windows, clone the harness with
`git -c core.autocrlf=false clone ...`; its migration validator hashes one historical
fixture as raw bytes. Build the two adapters first:

```powershell
# WordToolkit checkout
& "$HOME\.dotnet8\dotnet.exe" build native\WordToolkit.Native\WordToolkit.Native.csproj -c Release

# safe-docx checkout
npm ci
npm run build -w @usejunior/docx-core

# docx-platform-tests checkout
Set-Location runner
npm ci
npm run check-fixtures
```

Create a temporary protocol-v1 registry whose `adapterCommand` entries point to:

```text
dotnet <wordtoolkit>/native/WordToolkit.Native/bin/Release/net8.0-windows/wordtoolkit-native.dll docx-platform-adapter
node <safe-docx>/packages/docx-core/dist/cli/conformance-adapter.js
```

Then run the real harness without modifying its checked-in registry or result:

```powershell
npm run suite -- --registry <temporary-adapters.json> --results <temporary-results.json>
npm run validate-results -- --results <temporary-results.json>
```

The recorded run used the suite command exactly. The checked-in artifact validates
directly against the harness's Draft 2020-12 `results/results.schema.json`; its counts,
schema metadata, size and SHA-256 were then verified after copying. On the research
checkout, the wrapper `validate-results` command stopped before reading this result
because Git `core.autocrlf=true` changed the raw SHA-256 of its historical schema-v2
fixture (`e093...` expected, `ac43...` working-tree bytes). Direct Ajv validation of the
schema-v3 result passed. This is a Windows checkout portability defect in the harness's
migration guard, not evidence to erase or quietly call success.

## What this proves — and what it does not

It proves that, at the exact pins above, WordToolkit's adapter obeyed protocol v1,
declined unsupported work honestly and satisfied one more ECMA-backed scenario than
safe-docx on this particular corpus. It also gives a public regression point for two
tracked table-row operations and safe paragraph-mark merge semantics.

It does not prove that WordToolkit is globally better, more lossless, faster, safer,
cheaper in tokens, or closer to Word layout. The sample is too small and the capability
intersection too narrow. Any document claiming more from these 42 rows would be polished
nonsense. The next credible step is a producer-diverse hostile corpus, shared round-trip
metrics, Word-open and visual gates, token/latency traces and licensed-engine adapters.
