# WordToolkit 0.61.3 release qualification

Date: 2026-08-29

Runtime: `0.61.3+codex.20260829085321`

## Scope

This release reduces the context and ceremony paid by an AI client without removing the
full typed contract or Word/OOXML safety gates. It also fixes direct-OMML readback when
Microsoft Word omits explicit properties that are identical to its documented defaults.

## Measured AI-facing payloads

The baseline is the previously installed personal plugin
`0.61.2+codex.20260828201723`. The candidate is the packaged 0.61.3 runtime.

| Payload | 0.61.2 | 0.61.3 | Reduction |
| --- | ---: | ---: | ---: |
| Always-loaded `SKILL.md` | 102,407 bytes | 8,784 bytes | 91.4% |
| Plugin manifest | 23,221 bytes | 3,585 bytes | 84.6% |
| Search: `equation preflight` | 15,886 chars | 11,340 chars | 28.6% |
| Search: `find replace` | 12,608 chars | 6,714 chars | 46.7% |
| Search: `insert dropdown` | 14,417 chars | 8,763 chars | 39.2% |

The short skill routes to four references totaling 19,851 bytes, but an agent loads only
the one or two references required by the current task. The manifest now exposes 12
meaningful presentation capabilities and three starter prompts instead of 133 and 49.

`tools/list` remains effectively unchanged at 21,035 characters versus 21,024 because all
17 public tools are still present. Search now returns three ranked candidates by default.
The top result retains its complete executable `inputSchema`, annotations, and first-call
guidance; it omits `outputSchema`, permissions, and reversibility. A full
`inspect_wordtoolkit_action` call still returns the complete contract.

## Automated qualification

- .NET SDK: pinned `8.0.423`.
- Engine Release tests: 939/939 passed.
- Native Release tests: 936/936 passed.
- LibreOffice Release tests: 12/12 passed.
- Python suite: 1,583 passed, 17 intentionally skipped, one Starlette deprecation warning.
- Focused metadata/plugin/contract tests: 27/27 passed.
- Ruff check and format: passed; 180 files formatted.
- mypy: passed for 31 source files.
- action-guidance and metadata generation checks: passed.
- plugin validator and skill validator: passed for both source and packaged trees.
- `git diff --check`: passed.

The heartbeat timing test produced one false failure only when three complete .NET suites
were deliberately run in parallel. The entire Native suite then passed sequentially, and
the focused heartbeat test passed five consecutive isolated runs.

## Real Microsoft Word qualification

Focused acceptance ran six tests against real Microsoft Word:

- inline formatted runs plus native `\boxed` equation publication;
- isolated valid equation preflight and worker cleanup;
- exact-index timeout and verified process termination;
- ordered valid-invalid-valid diagnostics;
- seven-equation vector/Maxwell corpus;
- cancelled 100-operation batch, status polling, and idempotent receipt replay without
  duplicate publication.

All six passed.

The packaged full-live gate then completed 153 MCP requests in 165.264 seconds:

- 17 public tools and 157 available native actions confirmed;
- 59 live Word actions exercised successfully plus one destructive confirmation guard;
- 70 paragraphs, one table, 12 native equations, one comment, one footnote, one endnote,
  one image, and three sections;
- close/open/reconnect passed;
- Microsoft Open XML validation passed;
- a 194,882-byte, three-page Word PDF was exported;
- the test document was closed and no `WINWORD` process remained.

The three PDF pages were rasterized at 144 DPI and visually inspected. Text, equations,
tables of contents/figures/authorities, index entries, notes, headers, and footers remained
inside page bounds with no clipping, overlap, blank page, or raw equation control syntax.
This is an acceptance fixture, not a claim that its utilitarian visual design is a polished
user document.

During qualification the gate exposed two stale assertions in its own script: compact
member summaries use `member_name`/`member_kind`, and direct OMML proves preservation with
its semantic hash rather than the separate style-rewriter flag. Both assertions were
corrected. The same run also exposed a real direct-OMML hash bug: Word omits explicit
`m:sty="i"` and `m:scr="roman"` defaults. The semantic contract now normalizes those exact
defaults while retaining every non-default style and script value. The previously failing
four-style fraction passed native preflight with equal expected and actual hashes.

## Reproducible artifact

Two independent package builds contained 204 files and produced byte-identical ZIPs:

- asset: `WordToolkit-0.61.3+codex.20260829085321-native-win-x64.zip`;
- size: 38,475,539 bytes;
- SHA-256: `8657d755516b8926090fe1dbe59d455cf90c75b6936878b02d89c0cfdd72e80d`;
- runtime assembly SHA-256:
  `1c880ae82338435da8fbe542b1adabe3fb9a3e64fa2e7c27b44c02f34c3a2841`.

The package contains the self-contained Windows x64 .NET runtime and no Python runtime.

## Remaining boundary

The optimized search response is smaller, not tiny: a complex top input schema and its
guidance can still require roughly 11,000 response characters. Removing that contract
would save tokens by forcing another inspect call and would make execution less reliable,
so 0.61.3 keeps the executable top contract and offers `include_top_schema=false` when a
caller wants the smallest discovery result.
