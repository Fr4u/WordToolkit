# WordToolkit 0.60.1 release qualification

Status: local qualified9 record. Git publication, PR checks and installation mutation are not included as passed evidence.

## Qualified9 candidate

- Version: `0.60.1+codex.20260821002028`.
- Native MCP surface: 15 public tools, 149 native actions; metadata and first-call guidance are 149/149.
- A/B trees: `dist/release-0.60.1-qualified9-a` and `dist/release-0.60.1-qualified9-b`; 199 files and 91,092,261 expanded bytes each, with identical relative path/length/hash maps.
- ZIPs: `dist/qualified9-a.zip` and `dist/qualified9-b.zip`; 37,867,598 bytes each, SHA-256 `650E7D45A0C6A1ACC3B1D9F043F08D900CA8B1A701D52DC12C7B1A9FAFD9F87B`.
- Native executable SHA-256: `8A743CBBCDD846B0DEAA2042C5AF18E847461ABCFCC5F356A6F6C401D88148D4`.
- Native DLL SHA-256: `AAA1AF533960A3597D9B1C69E76FDDA9BDFD43B244DC697D1E4BF9C8BFB4D863`.
- Installed cache: `C:\Users\Admin\.codex\plugins\cache\personal\wordtoolkit\0.60.1+codex.20260821002028`; artifact/source/cache all have 199 files and relative-map SHA-256 `270e5b0...`.
- Installed `skills/wordtoolkit/SKILL.md` SHA-256: `348EA65375847E1A953DFF3C2D685951729839CADED574C66B621ADC9A54ECFE`.
- Test counts: Python 1341 passed / 16 intentional skips; Engine 778; Native 585; LibreOffice 12.
- Guidance artifact: 149/149, SHA-256 `D94287...` (full hash is retained in the current guidance evidence).

## Real qualified9 evidence

`dist/acceptance-0.60.1-qualified9-final/acceptance-report-qualified9-final.json` records 59/59 positive live actions, one safety-guard pass, 15/149 action coverage and guidance 149/149. Open XML validation, save, close and reconnect passed; the Word process was closed. Three FQN tests passed 1/1 each. No claim is made that the 149-action catalog was fully live-exercised.

## Installation boundary

Installation **passed**: read-only inspection proved artifact/source/cache parity at 199 files, map SHA `270e5b0...`, cache version `0.60.1+codex.20260821002028`, runtime capabilities and guidance 149/149. Old personal-cache processes stopped: 0.

## Limits and history

ManualFix remains a 25-operation boundary. SmartArt and feature-behavior probes remain partial. Qualified6 and earlier rows remain historical evidence in `docs/STAGE-RESULTS.md`; they are not current qualification claims and are not rewritten here.

## Requirement to evidence

| Requirement | Evidence |
|---|---|
| Version/catalog | `plugin/wordtoolkit/.codex-plugin/plugin.json`; `schemas/mcp-tools-local.v1.json`; qualified9 runtime |
| Deterministic package | `dist/release-0.60.1-qualified9-a`, `dist/release-0.60.1-qualified9-b`, `dist/qualified9-a.zip`, `dist/qualified9-b.zip` |
| Guidance parity | current generated guidance artifact, 149/149, SHA prefix `D94287...` |
| Real Word gate | `dist/acceptance-0.60.1-qualified9-final/acceptance-report-qualified9-final.json` |
| Installation | Passed: artifact/source/cache parity, 199-file map SHA `270e5b0...`, exact cache version/path, runtime capabilities and guidance 149/149 |

Additional local gates include Ruff/format, metadata and dispatcher 149/149, schema export, deterministic A/B ZIP parity, and the local Word interop workflow. GitHub execution after push is not claimed.
