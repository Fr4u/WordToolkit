# Audit remediation checkpoint — 2026-07-22

This checkpoint answers the critical repository/release audit performed after 0.34.0.
It records evidence and remaining blockers; it is not a claim that the full document-
engine objective is complete.

## Closed in the 0.35 development line

- CI now runs `WordToolkit.Engine.Tests` on Linux and builds/tests the complete native
  Windows plugin ZIP on `windows-2022`.
- Tag/manual runs on the licensed self-hosted Word runner build the package and execute
  the complete 48-action live acceptance instead of only opening/resaving samples.
- MCP lines are bounded to 8 MiB, active request IDs have independent cancellation
  tokens, cancellation notifications are handled while the serialized request executor
  remains responsive, and oversized lines cannot poison the next request.
- A cancelled active COM call blocks new Word work. Recovery explicitly restarts only
  `wordtoolkit-native.exe`, never the user's Word process.
- The native base version is centralized in `native/Directory.Build.props`; packaging
  fails if it disagrees with the plugin manifest. README distinguishes the 0.35
  development line from the immutable published 0.34 artifact.
- The Python schema exporter no longer overwrites the native MCP schema. CI rejects
  generated remote-schema drift; native schema coverage is enforced by .NET tests.
- Native builds pin SDK 8.0.423 through `global.json` and CI configuration. Repository
  text uses an explicit LF policy so Windows checkout settings cannot silently change
  packaged manifest/skill bytes. Compiler paths are mapped to stable virtual roots so
  an absolute local or hosted-runner PDB path cannot alter packaged assemblies.
- Repeatable graph/patch benchmarks and exact JSON evidence are checked in. On the
  measured workstation, 998,998 dependency nodes peaked at 4,173.1 MiB and a 400 MiB
  patch payload peaked at 2,158.1 MiB.
- Default patch limits were reduced to 128 MiB aggregate, 64 MiB per blob, a 4 MiB
  manifest and a 100:1 compression ratio. Higher values require explicit configuration.
- Raw patch confidentiality is documented. An optional engine envelope adds
  AES-256-GCM and ECDSA-SHA256 with caller-managed keys and signer identity pinning.
- `README.md`, `TESTING.md`, `SECURITY.md` and `KNOWN-LIMITATIONS.md` now agree on review
  mutation support, CI coverage, patch risk, MCP cancellation and scale evidence.

## Verification at this checkpoint

- `WordToolkit.Engine.Tests`: 245 passed.
- `WordToolkit.Native.Tests`: 185 passed.
- Python/OOXML: 1,273 passed, 16 intentionally skipped.
- Ruff: passed.
- Native Windows x64 package: 195 files, 35,882,398 bytes, SHA-256
  `00a9654ac87f6a82a7fee4017d181e97d27aa46558947aa8e0393e36e36264b0`.
- Packaged runtime initialize smoke: `WordToolkit Native` 0.35.0, MCP 2025-06-18.
- Existing user Word process PID 5232 remained open and responsive; no live Word gate
  was run from this checkout.

## Still open before a 0.35 release

- Merge the cumulative draft PR so `main` becomes the single repository truth. The old
  0.19 default branch remains a real defect until that merge completes.
- Obtain independent human review. Self-review, automated tests and CI are not a second
  maintainer.
- Let all new mandatory GitHub checks pass on the pushed commit.
- Re-run local/CI reproducibility comparison under the newly pinned SDK. The first
  comparison correctly failed because local SDK 10.0.300 and CI SDK 8.0.423 supplied
  different self-contained runtime packs and Windows checkout changed text line endings.
- Run the full licensed Word release gate on the exact 0.35 package. Do not reuse the
  0.34 live result as evidence for changed MCP/COM code.
- The historical cumulative PR cannot honestly be made small after the fact. Merge it as
  a reviewed baseline or close it; all work after that baseline must use narrow PRs.
- The patch envelope exists at engine level. MCP secret-store provisioning remains
  intentionally absent until there is a real key custody policy.
- Graph memory is proven expensive, not solved. Compact source storage and adjacency
  construction remain engineering work.
