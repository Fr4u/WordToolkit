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
- COM delegates are non-replayable by default and are never automatically repeated after
  an uncertain disconnect. Cancellation after execution begins retains an ever-started
  state even when completion races the cancellation continuation and returns non-
  retryable `WORD_OPERATION_OUTCOME_UNKNOWN`. Further non-replayable work remains blocked
  until runtime restart, reconnect and inspection. Only explicitly proven read-only or
  idempotent delegates may reconnect once, after a fresh cancellation check. Word's
  mutating `Document.Compare` is blocked rather than inferred as a `compare*` read. OLE
  busy-call retry stays within a hard 30-second elapsed-time budget.
- The native base version is centralized in `native/Directory.Build.props`; packaging
  fails if it disagrees with the plugin manifest. README distinguishes the 0.35
  development line from the immutable published 0.34 artifact.
- The Python schema exporter no longer overwrites the native MCP schema. CI rejects
  generated remote-schema drift; native schema coverage is enforced by .NET tests.
- Native builds pin SDK 8.0.423 through `global.json` and CI configuration. Repository
  text uses an explicit LF policy so Windows checkout settings cannot silently change
  packaged manifest/skill bytes. Compiler paths are mapped to stable virtual roots so
  an absolute local or hosted-runner PDB path cannot alter packaged assemblies.
- Release assemblies omit debug metadata, and packaging rejects a checkout path leaked
  into either WordToolkit assembly. A fresh local checkout and two independent hosted
  Windows builds produced the same final ZIP byte for byte.
- The 48-action Word gate is compatible with Windows PowerShell 5.1: its source is
  parser-safe ASCII, Unicode request values use JSON escapes, and MCP stdin is opened
  without a UTF-8 preamble.
- Repeatable graph/patch benchmarks and exact JSON evidence are checked in. On the
  measured workstation, 998,998 dependency nodes peaked at 4,173.1 MiB and a 400 MiB
  patch payload peaked at 2,158.1 MiB.
- Default patch limits were reduced to 128 MiB aggregate, 64 MiB per blob, a 4 MiB
  manifest and a 100:1 compression ratio. Higher values require explicit configuration.
- Raw patch confidentiality is documented. An optional engine envelope adds
  AES-256-GCM and ECDSA-SHA256 with caller-managed keys and signer identity pinning.
- GitHub branch protection now guards `main`: updates require a pull request, all five
  normal CI jobs, one approving review from someone other than the last pusher,
  dismissal of stale approvals and resolution of review conversations. Administrators
  are subject to the same policy; force pushes and branch deletion are disabled.
- `README.md`, `TESTING.md`, `SECURITY.md` and `KNOWN-LIMITATIONS.md` now agree on review
  mutation support, CI coverage, patch risk, MCP cancellation and scale evidence.

## Verification at this checkpoint

- `WordToolkit.Engine.Tests`: 245 passed.
- `WordToolkit.Native.Tests`: 185 passed.
- Python/OOXML: 1,273 passed, 16 intentionally skipped.
- Ruff: passed.
- Native Windows x64 package: 195 files, 35,886,733 bytes, SHA-256
  `e8f2e4b74fe65213197126c7aafb445452bd0e80bc05f7206d82672e4b09e59b`.
- The package hash matched across a fresh local checkout, GitHub run `29911798824`
  attempts 1 and 2, and the current-head run `29912380897`.
- Packaged runtime initialize smoke: `WordToolkit Native` 0.35.0, MCP 2025-06-18.
- Full real-Word gate: 122 MCP requests; all 48 installed live actions exercised; 47
  positive passes and one expected confirmation-guard pass; 12 editable equations;
  saved DOCX and PDF; zero Open XML validation errors; close/open/reconnect passed.
- The acceptance gate ran twice after its Windows PowerShell repair. Existing user Word
  process PID 5232, start time and active document title were unchanged before and after.

## Still open before a 0.35 release

- Merge the cumulative draft PR so `main` becomes the single repository truth. The old
  0.19 default branch remains a real defect until that merge completes.
- Obtain independent human review. Self-review, automated tests and CI are not a second
  maintainer.
- Register a licensed `[self-hosted, windows, word]` GitHub runner before relying on the
  tag workflow. The exact 0.35 package passed the full gate locally, but the repository
  currently has no registered self-hosted runner to execute that workflow on a release.
- Keep the new `main` protection policy intact and verify its required check names if a
  workflow job is renamed. Protection closes the bypass, but it does not supply the
  still-missing independent reviewer or licensed Word runner.
- The historical cumulative PR cannot honestly be made small after the fact. Merge it as
  a reviewed baseline or close it; all work after that baseline must use narrow PRs.
- The patch envelope exists at engine level. MCP secret-store provisioning remains
  intentionally absent until there is a real key custody policy.
- Graph memory was proven expensive here. WordToolkit 0.38.0 later added compact
  compressed-row adjacency plus a graph-local 128 MiB accounted-byte budget. Shared
  source storage and an operation-wide resource lease remain engineering work.
