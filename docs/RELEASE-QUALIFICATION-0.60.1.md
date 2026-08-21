# WordToolkit 0.60.1 release qualification

Current checkpoint: qualified19 `0.60.1+codex.20260821184226`; q18 and older are historical.
Q19 inherits q17's live-full-capabilities evidence (59/59, 15/149, guidance 149/149, combined 5/5, Word 0); q19's delta is OCR-only and was not rerun through Word.

Qualified11 checkpoint: `0.60.1+codex.20260821123012`, native 587, LibreOffice 12,
Python 1341/16 skipped; q10 atomic-writer evidence remains historical. Q11 adds
expected_version/live_version binding, additive recovery guidance and corrected view placement.

Status: local qualified19 record. Git publication and PR checks are not included as passed evidence.

## Qualified19 candidate

- Version: `0.60.1+codex.20260821184226`.
- Native MCP surface: 15 public tools, 149 native actions; guidance 149/149.
- A/B trees: `dist/release-0.60.1-qualified19-a` and `dist/release-0.60.1-qualified19-b`; 199 files and 91,102,501 expanded bytes each; artifact/source/cache parity passed.
- ZIPs: 37,872,609 bytes each, SHA-256 `54F73EF6E0BD607902E55AEF7802842F3FE9B81BA59655DF1B8AA52E099D40C3`.
- Native executable SHA-256: `F228262FAF21786578AE64B7CB8594263CF8EA6FA8825C892C04A03948EE58F1`.
- Native DLL SHA-256: `BF724C91C01336272D20ED39CFB4E5F49EE49B82616EFA5F712816C1FDFCA5AF`.
- Test counts: Python 1343 passed / 16 intentional skips; Engine 780; Native 613; LibreOffice 12; OCR 17×3.

## Real qualified19 evidence

Q19 inherits q17's timed live-full-capabilities evidence: 59/59, 15/149, guidance 149/149, combined atomic checks 5/5 and final Word count 0. Q19 was not rerun through Word because its delta is OCR-only: dedicated-path exception and Windows symlink classification; OCR 17×3 passed.

## Historical qualified18 checkpoint

q18 and older checkpoints remain historical and are not current qualification claims.

## Historical qualified10 candidate

- Version: `0.60.1+codex.20260821114902`.
- Native MCP surface: 15 public tools, 149 native actions; metadata and first-call guidance are 149/149.
- A/B trees: `dist/release-0.60.1-qualified10-a` and `dist/release-0.60.1-qualified10-b`; 199 files and 91,092,261 expanded bytes each, map SHA-256 `026B5FB44FD054EEC8F4A586AA0FDCCB96484271AAAE8E77884AC96EE0257175`.
- ZIPs: `dist/qualified10-a.zip` and `dist/qualified10-b.zip`; 37,867,659 bytes each, SHA-256 `7B656C516DD17F490DE8012038765E91551A199BD524A84BE122FBAB68CF6C77`.
- Native executable SHA-256: `2E6914CBEBD2B11787BD4584EA3C1D924F3999088C0B4D37AC4A3C6A5A976F9D`.
- Native DLL SHA-256: `79EDCB70F9B1D76403D25B1B2FEC220DAA712B10003FB9A7C845E6AB0DE19AFA`.
- Installed cache: `C:\Users\Admin\.codex\plugins\cache\personal\wordtoolkit\0.60.1+codex.20260821114902`; artifact/source/cache all have 199 files and relative-map SHA-256 `026b5fb...`.
- Installed `skills/wordtoolkit/SKILL.md` SHA-256: `348EA65375847E1A953DFF3C2D685951729839CADED574C66B621ADC9A54ECFE`.
- Test counts: Python 1341 passed / 16 intentional skips; Engine 779; Native 585; LibreOffice 12.
- Guidance artifact: 149/149, SHA-256 `6DD3C13A55FC1748F206F246A2797A1278F2DE5708BD9A84EA0FE737C601C090`.

## Historical qualified10 evidence

The prior real gate remains historical qualified9 evidence: `dist/acceptance-0.60.1-qualified9-final/acceptance-report-qualified9-final.json` records 59/59 positive live actions. The qualified10 production delta is the package-writer atomic hard-link no-clobber race fix, covered by package-level evidence. The exact qualified10 real-Word gate was not run because the pre-existing user Word document safety stop blocked it; q10 therefore makes no live-Word PASS claim. The documented live boundary remains 15/149, with the three retained fully-qualified-name proofs from q9; no claim is made that the 149-action catalog was fully live-exercised.

## Installation boundary

Installation **passed**: read-only inspection proved q13 artifact/source/cache parity at 199 files, map SHA `BC5B9B6B...`, exact q13 cache version/path, runtime capabilities and guidance 149/149.

## Limits and history

ManualFix remains a 25-operation boundary. SmartArt and feature-behavior probes remain partial. Qualified6 and earlier rows remain historical evidence in `docs/STAGE-RESULTS.md`; they are not current qualification claims and are not rewritten here.

## Requirement to evidence

| Requirement | Evidence |
|---|---|
| Version/catalog | `plugin/wordtoolkit/.codex-plugin/plugin.json`; `schemas/mcp-tools-local.v1.json`; qualified13 runtime |
| Deterministic package | q13 A/B package paths and hashes above |
| Guidance parity | current generated guidance artifact, 149/149 |
| Real Word gate | q13 timed live-full-capabilities gate: 59/59, OpenXML/save/reconnect passed; q10/q11/q12 historical |
| Installation | Passed: q13 artifact/source/cache parity, 199-file map SHA `BC5B9B6B...`, exact q13 cache version/path, runtime capabilities and guidance 149/149 |

Additional local gates include Ruff/format, metadata and dispatcher 149/149, schema export, deterministic A/B ZIP parity, and the local Word interop workflow. GitHub execution after push is not claimed.
