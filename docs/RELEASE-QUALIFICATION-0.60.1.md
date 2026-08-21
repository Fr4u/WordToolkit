# WordToolkit 0.60.1 release qualification

Status: local qualified10 record. Git publication and PR checks are not included as passed evidence.

## Qualified10 candidate

- Version: `0.60.1+codex.20260821114902`.
- Native MCP surface: 15 public tools, 149 native actions; metadata and first-call guidance are 149/149.
- A/B trees: `dist/release-0.60.1-qualified10-a` and `dist/release-0.60.1-qualified10-b`; 199 files and 91,092,261 expanded bytes each, map SHA-256 `026B5FB44FD054EEC8F4A586AA0FDCCB96484271AAAE8E77884AC96EE0257175`.
- ZIPs: `dist/qualified10-a.zip` and `dist/qualified10-b.zip`; 37,867,659 bytes each, SHA-256 `7B656C516DD17F490DE8012038765E91551A199BD524A84BE122FBAB68CF6C77`.
- Native executable SHA-256: `2E6914CBEBD2B11787BD4584EA3C1D924F3999088C0B4D37AC4A3C6A5A976F9D`.
- Native DLL SHA-256: `79EDCB70F9B1D76403D25B1B2FEC220DAA712B10003FB9A7C845E6AB0DE19AFA`.
- Installed cache: `C:\Users\Admin\.codex\plugins\cache\personal\wordtoolkit\0.60.1+codex.20260821114902`; artifact/source/cache all have 199 files and relative-map SHA-256 `026b5fb...`.
- Installed `skills/wordtoolkit/SKILL.md` SHA-256: `348EA65375847E1A953DFF3C2D685951729839CADED574C66B621ADC9A54ECFE`.
- Test counts: Python 1341 passed / 16 intentional skips; Engine 779; Native 585; LibreOffice 12.
- Guidance artifact: 149/149, SHA-256 `D94287...` (full hash is retained in the current guidance evidence).

## Real qualified10 evidence

The prior real gate remains historical qualified9 evidence: `dist/acceptance-0.60.1-qualified9-final/acceptance-report-qualified9-final.json` records 59/59 positive live actions. The qualified10 production delta is the package-writer atomic hard-link no-clobber race fix, covered by package-level evidence. The exact qualified10 real-Word gate was not run because the pre-existing user Word document safety stop blocked it; q10 therefore makes no live-Word PASS claim. The documented live boundary remains 15/149, with the three retained fully-qualified-name proofs from q9; no claim is made that the 149-action catalog was fully live-exercised.

## Installation boundary

Installation **passed**: read-only inspection proved artifact/source/cache parity at 199 files, expanded-map SHA `026B5FB44FD054EEC8F4A586AA0FDCCB96484271AAAE8E77884AC96EE0257175`, cache version `0.60.1+codex.20260821114902`, runtime capabilities and guidance 149/149. Old personal-cache processes stopped: 0.

## Limits and history

ManualFix remains a 25-operation boundary. SmartArt and feature-behavior probes remain partial. Qualified6 and earlier rows remain historical evidence in `docs/STAGE-RESULTS.md`; they are not current qualification claims and are not rewritten here.

## Requirement to evidence

| Requirement | Evidence |
|---|---|
| Version/catalog | `plugin/wordtoolkit/.codex-plugin/plugin.json`; `schemas/mcp-tools-local.v1.json`; qualified10 runtime |
| Deterministic package | `dist/release-0.60.1-qualified10-a`, `dist/release-0.60.1-qualified10-b`, `dist/qualified10-a.zip`, `dist/qualified10-b.zip` |
| Guidance parity | current generated guidance artifact, 149/149, SHA prefix `D94287...` |
| Real Word gate | q9 historical report `dist/acceptance-0.60.1-qualified9-final/acceptance-report-qualified9-final.json` (59/59); q10 gate deferred by the pre-existing user-document safety stop |
| Installation | Passed: artifact/source/cache parity, 199-file map SHA `026B5FB4...`, exact q10 cache version/path, runtime capabilities and guidance 149/149 |

Additional local gates include Ruff/format, metadata and dispatcher 149/149, schema export, deterministic A/B ZIP parity, and the local Word interop workflow. GitHub execution after push is not claimed.
