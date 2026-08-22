# Changelog

## Unreleased

- Hardened file-backed `.wtpatch` reads against concurrent replacement and
  in-place rewriting. Native patch inspection/apply and the shared rollback
  engine now decode one bounded, encrypted stable snapshot captured with
  read/write/delete sharing instead of parsing a live `FileStream`; two
  inconsistent captures fail with retryable `SOURCE_CHANGED`, and inspection
  length/hash metadata comes from that same accepted snapshot. The snapshot
  cap now includes a saturating worst-case DEFLATE/ZIP bound, so large custom
  limits cannot make a codec-produced patch reject its own path round trip.
  Oversized patch snapshots now remain `PATCH_LIMIT`, while every package CLI
  that can emit retryable `SOURCE_CHANGED` returns temporary-failure exit code 75.
  Added deterministic bound/distribution and race regressions plus a Strict
  WordprocessingML review
  transaction case proving insertion/deletion decisions and exact inverse
  restoration under both Transitional and Strict namespaces.

- Closed two fail-closed diagnostic gaps. Saved-package plain-text plans now
  reject `w:t` or `w:delText` nodes inside tracked insertion, deletion and move
  wrappers, including Word 2010 conflict revisions, before producing a mutation
  plan; callers must use the dedicated review-decision workflow for revision
  markup. The maintained Python live
  backend now matches the native mixed-batch contract by returning the exact
  zero-based `failed_operation_index` for an attributable equation failure and
  explicit `failed_operation_index_available=false` plus `failure_scope=batch`
  when only aggregate equation-count drift is known. Regression tests prove the
  source DOCX remains byte-exact and failed live batches publish no text and do
  not advance the live version.

- Made native live-formatting contracts executable instead of aspirational. The
  inspected schemas now type every accepted value and reject unknown fields,
  alias conflicts, invalid colors, wrong JSON types and out-of-range numbers
  before dispatch. Native Word now applies and verifies double strikethrough,
  highlight color indexes `0..16` and distributed paragraph alignment through
  isolated staging and publication readback; both backends reject simultaneously
  enabled single and double strike because Word clears the first mode. The
  maintained Python COM backend now applies highlighting through Word's documented
  `Range.HighlightColorIndex` instead of attempting an undocumented `Font` member.
  Table-formula preflight also publishes its actual `1..200` item bound. Native `font_name` input is now
  capped at the same 128-character bound already used by the Python contract.
  This intentionally replaces the former native-only 256-character maximum;
  callers sending 129–256-character pseudo-family names must shorten them.

- Closed two deterministic package-workflow gaps. `inspect_ooxml_semantics` can
  now opt into at most 200 source-ordered text-node locators bound to paragraphs
  already present in its bounded outline, with shared preview privacy, explicit
  scope/count/truncation evidence and package-fingerprint guidance for guarded
  edits. Flat OPC import/export now captures input once and uses those exact
  bytes for conversion, validation and `InputSha256`, eliminating a path-based
  time-of-check/time-of-use race that could previously mix three file versions.

- Closed transaction-boundary gaps across both authoring surfaces. Remote draft image
  inputs are now ephemeral: invalid images, successful inserts and later batch failures
  remove their session upload instead of leaking storage quota after commit or rollback;
  an OS cleanup failure is a warning and never masks an already committed mutation.
  Native live batch staging projects the failing operation index across range construction
  and per-operation equation-count verification without inventing an index for an
  unlocalizable aggregate drift. Multi-integral equation readback validates differential
  placement per integral operand instead of accepting only an aggregate count. The Python
  live backend now implements the inline formatted runs already advertised by the local
  schema, counts every payload-relative Word range in UTF-16 code units, and keeps its
  documented formatting properties aligned with the fields accepted at runtime.

- Removed four contract dead ends found by an external AI workflow. Native captions now
  accept exactly one fresh selection or found-range token; table-formula schemas enumerate
  the real typed source contract instead of accepting an opaque object; live structure
  inspection omits unresolved COM wrappers instead of serializing `System.__ComObject`;
  and member-operation guidance states that `result_id` is required to publish a result.

- Hardened legacy text edits at structural and Unicode boundaries. Failed tracked
  edits that cross a hyperlink now reject before publication instead of removing
  an earlier run and then throwing; untracked replacements can cross the wrapper,
  inherit the first changed character's container and preserve the exact untouched
  hyperlink remainder. Paragraph mutations are prepared on an isolated copy and
  published without invalidating existing paragraph references, rich inline run
  content fails closed, and
  UAX #29 grapheme checks prevent edits from leaving an orphaned combining mark,
  variation selector, emoji component or regional-indicator half. Accepted-view
  search now also includes tracked insertions nested inside hyperlinks.

- Fixed case-insensitive tracked-edit anchoring for Unicode characters whose
  case-folded form expands to multiple code points, such as `ß` to `ss`. The
  normalized match now retains an exact position map back to the original Word
  text and rejects matches that cut through only part of an expansion. This
  prevents both `IndexError` and silent over-selection while preserving the
  document's original glyphs in tracked deletions and replacements.

- Repaired the pinned HTTP transport's typed `httpcore` adapter contract by
  forwarding the complete TCP timeout, local-address and socket-option inputs and
  by modeling the asynchronous response body explicitly. The maintained Python
  layer is now checked by mypy in CI instead of relying on stale prose claims.
  Python dependency updates retain the deliberate `mcp<2` compatibility boundary;
  Dependabot groups minor and patch updates while MCP 2 remains blocked until its
  removed `mcp.server.fastmcp` API is migrated deliberately. The exact
  `pydantic-settings==2.15.0` build is excluded because it reports FastMCP's
  unresolved `lifespan` field during server construction; later fixed builds remain
  eligible, and pytest now fails if that incomplete-field warning returns.

- Prepared the `0.60.7` live-equation COM-ownership line. Repeated native-equation
  inspection and point replacement now release their `OMaths`, equation, range,
  formatted-text, content and custom-Undo RCWs on success, rejection, rollback and
  cancellation paths. Native equation construction transfers the returned equation
  only after build-up, optional style rewrite and semantic readback succeed; failures
  release the partially built object, while rewritten equations use COM identity rather
  than managed-wrapper identity before releasing an obsolete proxy. Rollback snapshots
  release all eleven structural collections plus the explicit story-range enumerator,
  and independent Flat OPC restoration releases its transient `FormattedText` range.
  A gated real-Word regression performs 60 consecutive fresh-token inspect/update cycles,
  verifies one editable OfficeMath object and native readback on every cycle, and quits
  only the Word instance started by the test. Real Word passed the 60-cycle soak, the
  dedicated styled-equation batch, the point-update/drift gate and the 59/59 live-action
  acceptance over 153 MCP requests; the latter saved a 70-paragraph DOCX containing
  12 equations and exported a valid PDF before closing Word. Local qualification passed
  Python 1403/1403 with 16 explicit environment skips and 74.56% branch coverage,
  Engine 799/799, Native 647/647 and LibreOffice 12/12. Two 199-file,
  91,154,989-byte builds produced byte-identical 37,890,109-byte ZIPs with SHA-256
  `332b4885c583196f8ea7b3f9e5b0d69e2c5ac9e9461a220306042f0ba8fa3715`;
  the Engine assembly is
  `6e34b89911497c6b2d49f932fd2b1ef5c44049ff36b740089c89b36fe324e640`,
  the executable is
  `c805614371c63ed2381bbd7c4dddc3d627b00cb9867535e67deb6cdf4ed2aef3`,
  and the native runtime assembly is
  `31533cb96dddfb02218ace8985e3b9c33c1ee6630a3fde19fdc81b9051759c6a`.
  This line does not claim that
  every Word COM path in the runtime has been exhaustively leak-tested or that one Word
  build proves identical behavior across Office releases.

- Prepared the `0.60.6` stable-path and encrypted-spool line. Package reads
  from filesystem paths now capture a bounded, two-pass SHA-256-verified snapshot
  through a handle shared for read, write and delete, so analysis can inspect a
  DOCX that Word still has open without parsing bytes from two saves. The shared
  reader, document analysis, heading outline, semantic query, OCR, semantic-role
  projection and encryption inspection use the same snapshot boundary. OCR hashes
  and parses one captured byte sequence, then independently re-captures it after
  provider execution. Path snapshots and non-seekable inputs now use a bounded,
  seekable AES-256-GCM block spool: only ciphertext reaches its delete-on-close
  temporary file, while fixed-size plaintext/cipher working buffers, nonces and the
  per-stream key are zeroed on disposal. This avoids retaining the complete compressed
  package in RAM while parsing can still retain bounded uncompressed OPC entries.
  Oversized path-based encryption inspection now reports
  `ENCRYPTION_INSPECTION_LIMIT` instead of the misleading `IO_ERROR`. The
  structured-mutation gate requires its known-good control package to be structurally
  valid, Word-valid and error-free instead of merely avoiding an exception. Local
  qualification passed Engine 799/799, Native 646/646 and LibreOffice 12/12. Two
  199-file, 91,149,869-byte builds produced byte-identical 37,887,577-byte ZIPs
  with SHA-256
  `1909870e9cc1e3112e8312c976922746973c4a26c64d5d8e04898130d57f4637`;
  the Engine assembly is
  `385edafbae52b0a84e44cabf534a895c689d87e00feb7190db55af49730f0115`,
  the executable is
  `667679fdca5e9de199e7b66feaab2f7bf70d7e112b085bb89c919186b16edebd`,
  and the native runtime assembly is
  `3b324b6b43bcf9ca9aa86cb9f0c3a18e304598285707f88779211b9e48816fe6`.
  This line does not claim an operating-system atomic snapshot, protection against a
  process that can read WordToolkit memory, or full malformed-package fuzz coverage.

- Prepared the `0.60.5` stable-inspection and batch-diagnostics line. Path-based
  `inspect_ooxml_package` now copies the source to a bounded delete-on-close
  snapshot and requires two SHA-256/length-identical reads over one pinned file
  handle before parsing. A document that keeps changing during save fails with
  retryable `SOURCE_CHANGED`; CLI returns temporary-failure exit code 75 and
  action guidance tells agents to wait for the save and retry. Structured OPC
  mutation smoke retains a valid control package and no longer accepts
  `IO_ERROR`, preventing a blanket I/O regression from going green. Live mixed
  batch publication projects per-operation failures through
  `failed_operation_index` across COM range acquisition, verification and result
  construction, and releases transferred text/equation RCWs on both success and
  rollback paths; staging cleanup retains the original error code and failed
  operation index even when cleanup itself fails. Staging ranges, equations and
  documents now have explicit COM ownership and release points. A dormant real-Word
  acceptance fixture typo (`\night` instead of `\right`) was corrected after the
  licensed application rejected it. Local qualification passed Python 1403/1403
  with 16 explicit environment skips and 74.56% branch coverage, Engine 787/787,
  Native 645/645, LibreOffice 12/12, the dedicated real-Word regression gates and
  the 59/59 live-action acceptance over 153 MCP requests. Two 199-file,
  91,143,725-byte builds produced byte-identical 37,885,380-byte ZIPs with SHA-256
  `8543ED39...`; the executable is `C8FCD8B1...` and the native runtime assembly is
  `21081310...`. This line does not claim an operating-system atomic snapshot,
  complete malformed-DOCX fuzz coverage or a new Microsoft Word build matrix.

- Prepared the `0.60.4` reliability and authoring-contract line. The native
  LaTeX converter now accepts `\boxed{...}` and `\implies`; multiple-integral
  differential readback no longer applies an invalid one-to-one placement rule.
  Live formatting publishes its accepted fields, normalizes compatibility
  aliases `font_size` and `alignment` before COM, rejects alias/canonical
  conflicts and invalid JSON types during preflight, and supports bounded inline
  font runs inside one paragraph. Mixed-batch errors report
  `failed_operation_index` without echoing document content. Saved-package
  inspection opens DOCX files with `FileShare.ReadWrite`, including a real-Word
  acceptance case that inspects the file while Word still has it open.
  Remote downloads pin each validated DNS answer to the TCP connection across
  redirects, keep TLS hostname verification, disable proxy inheritance and
  reject local/mixed-private destinations; ZIP/XML/relationship boundaries now
  enforce cumulative extraction limits and fail-closed parsing. A scheduled and
  manual deterministic structured-mutation smoke workflow records TRX evidence;
  it is a bounded regression gate, not a fabricated full-fuzzing claim. Local
  qualification passed Python 1403/1403 with 16 explicit environment skips and
  74.56% branch coverage, Engine 785/785, Native 642/642, LibreOffice 12/12 and
  the dedicated real-Word acceptance. Two 199-file, 91,139,960-byte builds
  produced byte-identical 37,883,181-byte ZIPs with SHA-256 `4AA01911...`;
  the executable is `B295C4B4...` and the native runtime assembly is
  `F3944F1A...`.

- Added the `0.60.3` supply-chain and test-quality baseline. Every third-party GitHub
  Action is pinned to a full commit SHA with its reviewed release tag recorded inline;
  Dependabot owns future GitHub Actions, Python and NuGet updates. Pull requests receive
  high-severity dependency review and C#/Python CodeQL analysis, while a scheduled audit
  checks the complete locked Python environment and all transitive native/validator NuGet
  dependencies. The legacy Python suite now records branch coverage, rejects total
  coverage below 73%, enforces separate measured floors for security, document parsing,
  sessions/publication and rendering, and uploads its Cobertura evidence. A manual,
  deliberately non-blocking mutation pilot targets `security.py`; it is groundwork, not
  a fabricated mutation-score gate. The workflow toolchain itself pins uv 0.11.23.
  Local validation passed Python 1346/1346 with 16 explicit environment skips and 73.42%
  branch coverage, Engine 783/783, Native 620/620 and LibreOffice 12/12. Two independent
  199-file, 91,113,765-byte package builds produced identical 37,875,429-byte ZIPs with
  SHA-256 `E052E0A7...`; no new licensed-Word compatibility claim is made. The first
  locked audit exposed advisories in `cryptography 49.0.0`, `pypdf 6.14.2` and
  `starlette 0.52.1`; the safe floors are now 50.0.0, 6.15.0 and 1.3.1 respectively,
  and the audit must be green before this candidate can merge. Renderer coverage is
  platform-qualified: 69% on Linux CI and 83% on Windows, where the executable-discovery
  branches are reachable.

- Qualified local `0.60.2+codex.20260822001033` P0 hardening candidate: OCR trust publication now
  resolves the actual output filesystem and creates/renames immutable outputs relative
  to one verified Windows directory handle, rejecting ancestor swaps, reparse-point
  runtime/model inputs and invalid manifest windows. Recovery coordination lives outside
  read-only trust directories and preserves unowned publisher paths. Validation workers
  drain before snapshot cleanup; relationship SHA matching is case-insensitive; truncated
  mail-merge analysis is incomplete and mail-merge limit/projection failures keep their
  public error contracts. LibreOffice failure cleanup preserves a destination whose
  ownership cannot be proved atomically. Regression coverage addresses issues #4-#8.
  Python passed 1346 with 16 dependency/environment skips, Engine 783/783, Native
  620/620 and LibreOffice 12/12. Two 199-file, 91,113,765-byte package builds produced
  identical 37,875,434-byte ZIPs with SHA-256 `DF18FD36...`; the executable is
  `D52DD5CE...` and the native runtime assembly is `1560D4E8...`. The P0 delta does not
  claim a new real-Word qualification run.

- Qualified q24 `0.60.1+codex.20260821204944`: 199-file/91,106,085-byte parity,
  37,873,318-byte ZIP SHA `6C277387...`, executable `2767087D...`, DLL `5961396B...`.
  Windows file links are inspected through a handle opened with
  `FILE_FLAG_OPEN_REPARSE_POINT`, so hosted runners cannot substitute target attributes.
  The exact symlink regression and Native 613/613 pass. Q23 and older are historical.

- Qualified q23 `0.60.1+codex.20260821203509`: 199-file/91,105,573-byte parity,
  37,873,051-byte ZIP SHA `7E55CF1B...`, executable `441BC6A3...`, DLL `8CDE6C59...`.
  Windows trust paths now use native `GetFileAttributesW` to detect a file symlink before
  target attributes are followed. The exact symlink regression and Native 613/613 pass.
  Q22 and older are historical.

- Qualified q22 `0.60.1+codex.20260821202555`: 199-file/91,105,061-byte parity,
  37,872,932-byte ZIP SHA `9738480D...`, executable `4121E7A2...`, DLL `BDF97A13...`.
  Existing file symlinks are inspected through `FileInfo.LinkTarget` before target attributes,
  closing the Windows runner gap while retaining the dedicated exit-64 path contract.
  Native 613/613 and the exact symlink regression pass. Q21 and older are historical.

- Qualified q21 `0.60.1+codex.20260821200417`: 199-file/91,105,061-byte parity,
  37,872,962-byte ZIP SHA `FBCD5C3F...`, executable `DA82528D...`, DLL `069F19DB...`.
  Runtime-specific failures while inspecting OCR trust symlinks now fail closed through the
  dedicated path-validation contract (exit 64); journal and publisher failures remain exit 2.
  Native 613/613 and the Windows symlink regression pass. Q20 and older are historical.

- Qualified q20 `0.60.1+codex.20260821194458`: 199-file/91,105,061-byte parity,
  37,872,971-byte ZIP SHA `97AE85EE...`, executable `F331B345...`, DLL `96807477...`,
  Python 1343/16 skipped, Engine 780, Native 613 and LibreOffice 12. OCR trust locks
  now live outside read-only trust directories; recovery journals remain writer-owned and validate complete
  pairs cryptographically and never deletes partial public files. Reparse-path failures
  have a dedicated exit-64 contract, and remote validation snapshots are always cleaned.
  Q20 inherits the unchanged q17 Word gate; q19 and older are historical.

- Qualified q19 `0.60.1+codex.20260821184226`: 199-file/91,102,501-byte parity,
  37,872,609-byte ZIP SHA `54F73EF6...`, executable `F228262F...`, DLL `BF724C91...`,
  Python 1343/16 skipped, Engine 780, Native 613, LibreOffice 12 and OCR 17×3.
  Q19 inherits q17 live-full evidence (59/59, 15/149, guidance 149/149, combined 5/5,
  Word 0); its OCR-only dedicated-path exception and Windows symlink classification delta
  was not rerun through Word. q18 and older are historical.

- Qualified q18 `0.60.1+codex.20260821181314`: 199-file/91,102,501-byte parity,
  37,872,587-byte ZIP SHA `A63F7990...`, executable `BD4677F8...`, DLL `70C31959...`,
  Python 1343/16 skipped, Engine 780, Native 613, LibreOffice 12 and OCR 17×3.
  Q18 inherits q17 live-full evidence (59/59, 15/149, guidance 149/149, combined 5/5,
  Word 0); its OCR/security-only delta was not rerun through Word. Reparse, journal,
  lock and scoped-security regressions are covered; q17 and older are historical.

- Qualified q17 `0.60.1+codex.20260821171101`: 199-file/91,100,453-byte parity,
  37,872,116-byte ZIP SHA `7CD202F4...`, executable `C030E922...`, DLL `C60F6021...`,
  Python 1343/16 skipped, Engine 780, Native 609 and LibreOffice 12. The timed
  live-full-capabilities gate passed 59/59 (15/149, guidance 149/149); combined atomic
  checks passed 5/5. Word exited naturally after delayed shutdown with final count 0.
  Full atomic/OCR, shutdown and serialization wave is covered; q16 and older are historical.

- Qualified q14 `0.60.1+codex.20260821155232`: 199-file/91,098,917-byte parity,
  37,871,655-byte ZIP SHA `59B4B353...`, executable `D29D211A...`, DLL `4508EEE...`,
  Python 1343/16 skipped, Engine 780, Native 602 and LibreOffice 12. The timed
  live-full-capabilities Word gate passed 59/59 (15/149, guidance 149/149,
  OpenXML/save/reconnect, Word 0); FQN passed 3/3. Atomic no-clobber publisher/staging
  and OCR lock/journal/mapped-drive/scoped-resolver/conflict semantics are covered.

- Qualified q13 `0.60.1+codex.20260821135315`: external SHA fingerprints are now
  case-insensitive in DocumentAnalysis, HeadingOutline and SemanticRole; uppercase regressions 17/17.
  The 199-file/91,087,141-byte A/B package has map SHA `BC5B9B6...`, ZIPs are
  37,867,740 bytes with SHA `AD58F39B...`, executable `432D5618...` and native DLL
  `6D863495...`; installed artifact/source/cache parity matches. Engine 779, Native 587,
  Python 1341/16 skipped and LibreOffice 12. The timed live-full-capabilities Word gate
  passed 59/59 (15/149 coverage, guidance 149/149, OpenXML/save/reconnect, Word 0).
  Integral and Rollback passed. EquationStyle had an initial cleanup TimeoutException
  with final Word 0, then exact rerun 3/3 passed with Word 0; intermittent evidence only,
  no production or test change.

- Qualified q12 `0.60.1+codex.20260821123719`: 199-file, 91,087,141-byte A/B parity
  (map SHA `BC5B9B6...`), 37,867,739-byte ZIP (SHA `F66DB467...`), executable
  `A7A6BD83...`, native DLL `9E72259F...`, Engine 779, Native 587, Python 1341/16
  skipped and LibreOffice 12; installed artifact/source/cache parity matches.
  The timed live-full-capabilities Word gate passed 59/59 with OpenXML validation,
  save and reconnect; coverage remains 15/149 with guidance 149/149. Integral x2,
  Style x1 and Rollback x1 passed after a test-only owned-app fix; Word process count
  was 0. Integral fixture hygiene was test-corpus-only, not a production change.

- Qualified local 0.60.1 candidate `0.60.1+codex.20260821123012` as qualified11: additive
  recovery guidance, expected-version/live-version binding and corrected view placement;
  Python 1341/16 skipped, Native 587 and LibreOffice 12. Deterministic native A/B package
  evidence is recorded in `docs/RELEASE-QUALIFICATION-0.60.1.md`.

- Added generated first-call guidance for all 149/149 native actions (search, inspect,
  bind/acquire, example, execute, success and recovery). No public tools were added.
- Qualified local 0.60.1 candidate `0.60.1+codex.20260821114902` as qualified10: deterministic
  A/B package parity (199 files, 91,092,261 expanded bytes, 37,867,659-byte ZIP),
  149/149 guidance, Python 1341/16 skipped, Engine 779, Native 585 and LibreOffice 12.
  Real qualified9 evidence covers 59/59 positive actions plus the documented 15/149 live
  action boundary. Read-only inspection proves artifact/source/cache parity; the exact
  q10 real-Word gate was deferred by the pre-existing user-document safety stop, so q10
  makes no live-Word PASS claim. The production delta is the package-writer atomic
  hard-link no-clobber race fix.

## 0.60.1 — 2026-08-20

- Fixed n-ary UnicodeMath normalization so grouped operators preserve their
  operands and delimiters across the native conversion path.
- Locked the public MCP contract at 15 tools: 11 core actions plus 4 capability
  gateways (`11 + 4 = 15`), with the lazy native catalog remaining behind those
  gateways.
- Updated CI/repository policy, ownership and test-contract coverage to keep
  generated schemas, native packaging and release metadata synchronized.
- Added fail-closed rollback semantic stabilization for system note `w14:paraId`
  and covered the real footnote-to-endnote regression path.
- Added the atomic `application_owned_by_runtime` token and failure-path Word
  cleanup so ownership and teardown cannot be reported optimistically.
- Added fail-closed recovery for styled OMath after `InsertXML`, with a real
  regression covering the rollback path and native formatting preservation.
- Refreshed post-build publication evidence across a 10/10 real mixed batch and
  released RCWs on every failure path to prevent stale COM ownership.
- Completed explicit metadata coverage for all 149/149 native actions; the
  expanded 59-action real gate remains separately reported as evidence.
- Corrected Draft examples/encoding normalization and documented the honest
  ManualFix, SmartArt and feature-behavior-probe boundaries.

## 0.60.0 — 2026-07-27

- Added the cross-platform `wordtoolkit.inspect_ooxml_signatures/1.0` Engine operation,
  strict `inspect-signatures` CLI and lazy MCP action. They verify OPC signature topology,
  supported XMLDSIG values, signed package-part digests and OPC Relationship Transform
  subsets without opening Word or using the network.
- Kept integrity, identity and trust separate. Responses contain no document content, raw
  XML, certificate bytes, signer identity or local path; certificate hashes and OPC source
  URIs are independent opt-ins. Certificate-chain trust and revocation remain explicitly
  false, and signing/removal/re-signing are not implemented.
- Added fail-closed defenses for duplicate XML IDs, external or ambiguous `SignedInfo`
  references, unsigned manifest objects, wrong OPC signature-part content types,
  malformed manifest references, unsupported algorithms/transforms, weak SHA-1 reporting,
  bounded signature/certificate/reference/XML inputs and valid UTF-16 signature XML.
- Independently signed a real DOCX through WindowsBase. Both WindowsBase and WordToolkit
  accepted the untouched RSA-SHA256 package; both rejected a one-byte mutation in signed
  `word/document.xml`. WordToolkit preserved the useful distinction between a valid
  signature-object value and a failed signed-part digest.
- Expanded the native catalog to 149 actions and 60 complete explicit metadata contracts,
  with closed Draft 2020-12 schemas and a token-lean paged response.
- Pinned SDK 8.0.423 gates pass 778 Engine, 12 LibreOffice, 570 Native and 1,322
  Python tests with 16 intentional skips. Ruff, `dotnet format`, JSON parsing and the
  NuGet direct/transitive vulnerability audit are clean.
- Two independently named builds are byte-identical: 199 files / 90,801,955 expanded
  bytes, expanded-manifest SHA-256
  `faf3de578789a015755ba5882079055aa7a8b21f89cf5a3db4b9a1065ce07179`, and
  37,847,868-byte ZIPs at SHA-256
  `1ae44198b2bdf9324bf71576e821f1bfd5751f3abefd12aaead4136b6424756d`.
- Installed and enabled `0.60.0+codex.20260727022849`. Candidate, persistent marketplace
  source and active cache have identical 199-path length/hash maps. Full JSON-RPC execution
  from the exact cache accepts the WindowsBase-signed package, rejects its tampered copy
  and returns no source path, content, raw XML, certificate identity or network claim.
- Hosted CI run `30229211071` passed all six jobs for fix commit `d956065`. Downloaded
  Windows artifact `8639550433` contains the exact local 37,847,868-byte ZIP at SHA-256
  `1ae44198b2bdf9324bf71576e821f1bfd5751f3abefd12aaead4136b6424756d`;
  after removing its single `wordtoolkit/` wrapper, all 199 paths, lengths and hashes match.
  The preceding run correctly exposed a stale generated-catalog count; the exporter now
  derives the native action count from the local schema instead of hard-coding it.

## 0.59.0 — 2026-07-26

- Replaced path-only OCR trust with a strict ECDSA P-256 signed provider manifest and a
  host-owned publisher-key store. The manifest binds provider/publisher/key/interface
  identity, a maximum 366-day validity window, the exact Tesseract executable, every
  top-level runtime file and every allowed language model. Duplicate/unknown fields,
  noncanonical base64, wrong curves, expired manifests, untrusted keys, extra files and
  changed bytes fail closed.
- Closed the hash-to-loader race by opening every signed provider/model resource without
  write or delete sharing while hashing it. Up to four exact provider/model/language
  configurations remain pinned for the native-host session; provider updates require a
  restart. The parent also re-enumerates the exact signed top-level runtime set immediately
  before child launch and after its result, so a directory-set change cannot be reported as
  a successful call. The child receives only the parent-verified binding through closed IPC
  and still runs inside the capability-free AppContainer and bounded Job Object.
- Added the content-free local `ocr-provider-trust` CLI with strict `keygen`, `issue` and
  `verify` modes. It publishes new artifacts only, returns neither paths nor private key
  material and keeps all trust material outside MCP/AI requests.
- Added catalog policy `signed_manifest_session_pinned` and bound it into the catalog hash.
  The OCR action contract and token cost are unchanged; manifest, key, signature and
  expected runtime hashes are host configuration rather than repeated model input.
- Added negative coverage for manifest/trust-store schema attacks, signature/key/expiry
  failure, executable/model/runtime drift, unlisted runtime files, write-sharing denial,
  malformed IPC bindings and keygen/issue/verify disclosure boundaries. Real Tesseract OCR
  passes through the complete signed AppContainer path with no Word invocation or source
  mutation.
- The exact self-contained release benchmark keeps one typed result hash across all 14
  calls. Direct median is 300.4712 ms; signed AppContainer median is 585.0561 ms, a
  disclosed +284.5849 ms / +94.71% cost. No recognized text, path or private key is stored.
- Fixed `build_native_plugin.ps1` so Windows PowerShell resolves the exact 8.0.423 SDK from
  `global.json` through an explicit pinned-host search instead of silently selecting an
  incompatible global 9.x/10.x installation.
- Two independent release builds are byte-identical: 197 files / 90,166,894 expanded
  bytes, 37,613,426-byte ZIP, SHA-256
  `c6817fbadfbebd701e88aac078f0e4894e496d142c81e3f7b0782f4d47f426a5`.
  Local gates pass 765 Engine, 12 LibreOffice, 567 Native and 1,318 Python tests with 16
  intentional skips; Ruff and `dotnet format` are clean.
- Installed and enabled `0.59.0+codex.20260727004624`. Candidate, persistent source and
  active cache have zero differences across all 197 files. Installed catalog SHA-256 is
  `d62313d487baf4717f54cd334956fe82bbc6cc812e840300ddf6514a1b936afb`, and the exact
  active-cache executable passes signed AppContainer OCR with the release typed-result hash.
- Hosted CI run `30225512026` passed all six jobs for commit `dc0ce44`. Windows artifact
  `8638467900` contains the exact local 37,613,426-byte ZIP at the same SHA-256; after
  removing its single `wordtoolkit/` wrapper, all 197 paths, lengths and file hashes match.

## 0.58.0 — 2026-07-26

- Moved the built-in OCR host from caller-token process containment into a real Windows
  AppContainer with no capability SIDs. The child is created suspended, attached to the
  existing 1 GiB/three-process/kill-on-close Job Object and only then resumed, eliminating
  the pre-assignment execution window.
- Added a package-SID filesystem broker. The trusted parent verifies absolute,
  reparse-free host/provider/model paths and grants read/execute access only to those
  directories. `TEMP`, `TMP` and `LOCALAPPDATA` point to the private writable AppContainer
  profile; no network capability is granted. Existing machine ACLs for all AppPackages
  remain an explicit limit rather than being hidden behind an empty-filesystem claim.
- Added the compact catalog profile
  `windows_app_container_no_network_brokered_filesystem`. Policy rejects that claim unless
  the capability is out of process, declares filesystem read/write, uses the hard process
  boundary and requests no network permission. The catalog hash binds the profile.
- Added a strict internal sandbox probe and executed it through the real launcher. It
  proved the AppContainer token, denied unbrokered user-file read/write, allowed one
  brokered read while denying its write, and failed to connect to a listening localhost
  socket. Real Tesseract OCR then passed through the same isolated process tree.
- Added reproducible, stripped benchmark input at
  `docs/benchmarks/ocr-provider-appcontainer-input.png`; two independent generations have
  SHA-256 `a8ff46458b7d6b4ec59557213d9280fd7e1f93de5c54f2834b0cbe854a4fb32c`.
  Seven alternating real-provider samples kept one exact typed-result hash across all 14
  calls through the exact self-contained release executable. Direct median was 324.6673 ms
  and AppContainer median 743.9256 ms: a disclosed +419.2583 ms / +129.13% security cost.
  No recognized text is stored in the benchmark.
- Pinned SDK 8.0.423 gates pass 765 Engine, 12 LibreOffice and 557 Native tests. Ruff is
  clean and the retained Python compatibility lane passes 1,318 tests with 16 intentional
  skips. Two independent 197-file, 90,070,384-byte package trees and their 37,582,800-byte
  ZIPs are byte-identical at SHA-256
  `9fae274cb015bb93cae3b02f3b3b7b91b47c796b620b168b05e581ab406fe56b`.
- Installed and enabled `0.58.0+codex.20260726223051`. Candidate, persistent source and
  active cache have zero path/length/hash differences. Installed full-response MCP reports
  catalog SHA-256 `334192c71d41b74a88ac4bd60e88d996feb12936504ea0bd4e9f3fe0fe441dff`,
  the AppContainer sandbox profile and no exact implementation/path properties. Installed
  real OCR preserves the benchmark result hash and reports network isolation/filesystem
  brokering without returning recognized text.
- Mandatory hosted CI run [`30220272858`](https://github.com/Fr4u/WordToolkit/actions/runs/30220272858)
  passed all six jobs on implementation commit
  `d896a1b0737db9b58804bb73c1ea3250b1422ef7`. Its downloaded Windows artifact is
  byte-for-byte identical to both local archives: 37,582,800 bytes, SHA-256
  `9fae274cb015bb93cae3b02f3b3b7b91b47c796b620b168b05e581ab406fe56b`.

## 0.57.0 — 2026-07-26

- Activated the first real `OutOfProcess`/`ProcessBoundary` extension path. The registry
  now requires an explicit host-owned proxy, hard timeout mode and positive process-memory
  ceiling; ordinary or mislabeled in-process implementations fail closed.
- Moved the built-in Tesseract adapter behind one fresh native child and a closed,
  duplicate/unknown-field-rejecting JSON protocol. Random request identity, image bytes/
  hash, exact provider configuration and pre/post host executable/assembly hashes bind
  every call and typed response. Paths, raw stderr, implementation types and exception
  internals never cross the channel.
- Attached the waiting child to a Windows Job Object before publishing the request. The
  complete provider tree has a 1 GiB aggregate commit ceiling, three-process limit,
  kill-on-close and hard timeout/tree termination. A minimized environment reduces
  accidental inheritance. Restricted tokens, AppContainer, network isolation and
  filesystem brokering remain explicitly unimplemented; this is failure/resource
  containment, not a permission sandbox.
- Added hostile protocol, identity, real child-process, Job Object termination, catalog
  and content-free benchmark regressions. Pinned SDK 8.0.423 gates pass 764 Engine and
  554 Native tests; the existing 12-test LibreOffice lane remains part of the release
  gate. Real Tesseract 5.5.0 OCR passed end to end through the isolated host with no Word
  invocation or source-package drift.
- The seven-sample alternating benchmark preserved one exact typed-result SHA-256 across
  all fourteen direct/isolated calls. Direct median was 248.0910 ms; isolated median was
  482.1869 ms, a disclosed +234.0959 ms / +94.36% correctness cost. The benchmark returns
  no recognized text; raw evidence is checked in.
- Two independently named pinned-SDK builds contain identical 197-file,
  90,040,541-byte trees and byte-identical 37,573,355-byte ZIPs at SHA-256
  `6fc800ff906e055c2d40d1497afe5d66262c1154c34f4e5045188bd08fc60bb4`.
  Installed and enabled `0.57.0+codex.20260726211107`; candidate, persistent personal
  source and active cache have zero path/length/hash differences. Installed discovery
  reports 148 actions, 15 tools and 59 explicit contracts. A full-response MCP catalog
  smoke returned `out_of_process`, `process_boundary`, the 1 GiB ceiling and no
  implementation type/path, assembly loading, Word or network use by inspection.
- Mandatory hosted CI run [`30217194508`](https://github.com/Fr4u/WordToolkit/actions/runs/30217194508)
  passed all six jobs. Its downloaded Windows artifact is byte-for-byte identical to
  both local archives: 37,573,355 bytes, SHA-256
  `6fc800ff906e055c2d40d1497afe5d66262c1154c34f4e5045188bd08fc60bb4`.

## 0.56.0 — 2026-07-26

- Added typed record-control semantics to the mail-merge graph. `NEXT`, `MERGEREC` and
  `MERGESEQ` are complete no-column controls; `NEXTIF` and `SKIPIF` retain a source
  column, one of six comparison operators and a privacy-gated comparison literal.
  Their column reads now enter the shared reference graph.
- Added parent-chain detection for conditional `IF` fields containing nested merge
  fields. Word splits those instructions around child field objects, so the graph makes
  the dynamic/unsupported shape visible and the schema planner blocks it instead of
  falsely claiming complete coverage.
- The schema planner now binds valid `NEXTIF`/`SKIPIF` source columns, accepts valid
  `NEXT`/`MERGEREC`/`MERGESEQ` controls without inventing column dependencies, and fails
  closed on missing, dynamic, extra or unsupported operands. Control kind, parse status,
  operator and comparison fingerprints are available in paged inspector/plan detail;
  the comparison literal requires `include_sensitive=true`.
- Pinned SDK 8.0.423 gates pass 763 Engine, 12 LibreOffice and 547 Native tests. The
  10,000-recipient benchmark includes 30 merge fields and five complete record controls;
  its 35-binding schema plan has zero issues, takes 0.1596 ms median after graph
  construction and allocates 32,624 bytes median across seven samples.
- Two independently named self-contained builds have identical 197-file,
  89,970,692-byte trees and byte-identical 37,549,211-byte ZIPs at SHA-256
  `b01f27cb6c0e98232ebc0f53cc3518e83f1e7690bbc844e629b32b20042be392`.
  Installed and enabled `0.56.0+codex.20260726200738`; candidate, persistent personal
  source and active cache have zero path/length/hash differences. Installed discovery
  reports 148 actions, 15 tools and 59 explicit contracts. A full-response schema-plan
  smoke returned the fingerprint-bound plan and the explicit no-Word/no-source/no-query/
  no-external-target/no-execution policy fields.
- Hosted CI run `30214530318` passed all six jobs on implementation commit `367e23a`.
  Its downloaded clean-Windows ZIP is byte-identical to both local archives: 37,549,211
  bytes at SHA-256
  `b01f27cb6c0e98232ebc0f53cc3518e83f1e7690bbc844e629b32b20042be392`.

## 0.55.0 — 2026-07-26

- Added `WordMailMergeSchemaPlanner`, a deterministic saved-package plan joining every
  projected `MERGEFIELD`/`MERGEBARCODE` to a caller-supplied ordered source-column
  schema. Exact and unique case-insensitive matches are distinct; duplicate/case-only
  collisions, missing columns, ambiguous ODSO mappings, incomplete/deleted fields,
  unsupported control fields and graph errors fail closed.
- Added `plan_ooxml_mail_merge_schema_binding/1.0`, a fingerprint-bound, paged and
  privacy-redacted package action. Its closed input accepts names and primitive data-kind
  hints only, rejects record values and unknown members, never opens Word or a source,
  never runs a query or follows an external target, and always reports execution as
  unsupported. Default output contains no column names.
- Added a vendor-neutral Engine operation, strict JSON codec and
  `mail-merge-schema-package` CLI using the same package/graph/planner contract. Stable
  schema fingerprints and plan IDs are length-prefixed SHA-256 values that include
  source order and data kind.
- Added deterministic, privacy, stale-version, strict-schema, limit and CLI/MCP
  regressions. The 10,000-recipient benchmark's additional 30-column/30-field plan runs
  in 0.1113 ms median with 26,000 median allocated bytes after graph construction; its
  cold first sample remains disclosed.
- Pinned SDK 8.0.423 gates pass 761 Engine, 12 LibreOffice and 546 Native tests; Ruff is
  clean and Python passes 1,318 tests with 16 intentional skips. Two independently named
  self-contained builds have identical 197-file, 89,956,623-byte trees and
  byte-identical 37,544,483-byte ZIPs at SHA-256
  `795f256c2757e3d2338c28485e29c1521527823b81cb01b934578d27754b56fa`.
- Installed and enabled `0.55.0+codex.20260726193615`; candidate, persistent personal
  source and active cache have zero path/length/hash differences. Installed discovery
  reports 148 actions, 15 tools and 59 explicit contracts. An installed full-response
  schema-plan smoke returned the no-Word/no-source/no-query/no-execution policy fields.
- Hosted CI run `30213451513` passed all six jobs on implementation commit `f039b24`.
  Its downloaded clean-Windows ZIP is byte-identical to both local archives: 37,544,483
  bytes at SHA-256
  `795f256c2757e3d2338c28485e29c1521527823b81cb01b934578d27754b56fa`.

## 0.54.0 — 2026-07-26

- Replaced full lossless/XDocument materialization of the read-only mail-merge
  `recipients.xml` part with one forward-only bounded `XmlReader` projection. It keeps
  exact source element ordinals, stable recipient IDs, inclusion state, columns,
  uniqueTag/hash identity semantics, unmodeled-child evidence and existing diagnostics
  without retaining a second editable XML object graph that no operation can mutate.
- Added explicit recipient XML element/depth limits, DTD prohibition, character checks,
  cancellation checks and no-copy array-backed input. Malformed or unsafe XML still fails
  closed; relationship/content-type/cardinality validation is unchanged.
- Replaced the hot recipient-ID `params`/LINQ/material-string/UTF-8/hex/lowercase pipeline
  with bounded stack spans, direct SHA-256 and lowercase hex emission. Regression proof
  compares the optimized IDs with the preceding canonical algorithm.
- Checked-in before/after Windows x64 .NET 8.0.29 benchmarks retain 30/30 resolved fields,
  every recipient, zero issues and the package fingerprint. At 10,000 recipients median
  build time falls 87.73% and median allocation 95.62%. At 100,000 recipients median
  time falls from 2,151.7312 to 247.7046 ms, median allocation from 1,030,632,576 to
  41,004,024 bytes (-96.02%) and peak working set from 1,166,508,032 to 168,493,056
  bytes (-85.56%). Operation accounting deliberately remains conservative and unchanged.
- Pinned SDK 8.0.423 release gates pass 758 Engine, 12 LibreOffice and 540 Native tests;
  Ruff is clean and Python passes 1,318 tests with 16 intentional skips. Two independently
  named self-contained builds have identical 197-file, 89,868,899-byte trees and
  byte-identical 37,523,118-byte ZIPs at SHA-256
  `4451f221021c556ed790a127952ba6e2386176379b9acf8fcb7b437b20e524d4`.
- Installed and enabled `0.54.0+codex.20260726184900`; candidate, persistent personal
  source and active cache have zero path/length/hash differences. Installed discovery
  reports 147 actions, 15 tools and 58 explicit contracts. An installed full-response
  `inspect_ooxml_mail_merge` smoke remained read-only and opened neither Word nor a data
  source or external target.
- Hosted CI run `30211742920` passed all six jobs on implementation commit `4166a94`.
  Its downloaded clean-Windows ZIP is byte-identical to both local archives: 37,523,118
  bytes at SHA-256
  `4451f221021c556ed790a127952ba6e2386176379b9acf8fcb7b437b20e524d4`.

## 0.53.0 — 2026-07-26

- Added `WordMailMergeGraphBuilder`, a bounded typed saved-package graph for Word mail
  merge. It joins settings configuration, top-level and ODSO data-source roles,
  Transitional/Strict relationships, Word's positional 30-field predefined-address
  mapping behavior, recipient-data inclusion/identity records and cross-story
  `MERGEFIELD`/`MERGEBARCODE` bindings. Missing, ambiguous, mistyped and forbidden
  relationships remain source-linked diagnostics.
- Added mail-merge configuration, ODSO, mapping, recipient-data, recipient and field
  nodes plus source/binding edges to the unified dependency graph. The graph preserves
  unresolved and external evidence but never follows a target or executes a field.
- Added lazy `inspect_ooxml_mail_merge/1.0` with summary, configuration, relationship,
  mapping, recipient, field and issue views. Query/connection/UDL strings, table/column
  names, declared mapping/field names, recipient identities, relationship targets and
  source provenance are independently gated; defaults return only counts and
  process-HMAC equality fingerprints. One 65,536-character projected-response budget
  and the shared operation resource lease bound the action. Its declared permissions
  include no network and no Microsoft Word.
- Upgraded the shared high-level analysis contract to
  `wordtoolkit.analyze_ooxml_document/1.1`. It adds content-free mail-merge counts and a
  `MAIL_MERGE_EVIDENCE` exact next-action signal without returning queries, source names,
  identities or targets. External/sensitive sources or mail-merge errors block automatic
  mutation.
- Added Transitional, Strict, relationship-error, recipient-cardinality, ambiguity,
  privacy, no-COM, closed-schema, deterministic-budget and dependency-integration
  regressions. Pinned SDK 8.0.423 gates pass 757 Engine, 12 LibreOffice and 540 Native
  tests; Python compatibility passes 1,318 with 16 intentional skips.
- Extended the benchmark harness with `mail-merge`. The checked-in 10,000-recipient
  point builds in 314.3591 ms median and 367.0644 ms p95/max; the 100,000-recipient
  point builds in 2,151.7312 ms median and 2,770.7688 ms p95/max. Both resolve all 30
  fields, preserve the package fingerprint and report zero issues. The 100,000 point's
  1.03 GB median allocation and 1.17 GB peak working set are disclosed, not hidden.
- Capability discovery now exposes 147 native actions and 58 complete explicit metadata
  contracts. Research, architecture, skill guidance and known limits document the hard
  separation between saved evidence and effectful data-source/merge execution.
- Hosted CI run `30210608401` passed all six jobs on commit `dc29e15`. Its clean Windows
  artifact is byte-identical to the locally qualified 37,521,314-byte ZIP at SHA-256
  `9647b0fd5eb49333e00eb85e271601940e850dc373566e3b67216171d5956243`. The allocation-
  free adjacency regression now compiles its measured path before opening the CLR
  allocation window, excluding one-time tiered-JIT/OSR bookkeeping without adding a
  tolerance or weakening the zero-allocation assertion.

## 0.52.0 — 2026-07-26

- Added an operation-scoped, byte-exact cache for immutable `LosslessXmlDocument`
  instances. Cache ownership follows one `WordOperationResourceLease` through a weak
  key, never crosses operation boundaries and never retains document content globally.
  Exact backing-array identity is a fast path; SHA-256 plus full byte comparison safely
  deduplicates separate arrays with identical content.
- Every cache reuse rechecks the caller's current XML byte, character, element, depth
  and text limits. A stricter second caller therefore cannot inherit a parse admitted by
  a looser first caller. Mutable backing-array regression proof prevents identity reuse
  after bytes change.
- High-level analysis now shares parses across semantic, style, numbering, reference,
  theme, font, MCE, lint, outline and list-sequence consumers. Theme, font, MCE, lint and
  list-sequence parsing plus bounded result collections now participate in the common
  operation lease. Remaining transient list/lint allocations stay explicitly omitted;
  complete resource accounting is not claimed.
- `analyze_ooxml_document` now returns compact
  `operation_budget.xml_parse_cache` statistics: model, requests, unique parses, cache
  hits and avoided conservative parse-accounting bytes. The closed MCP schema and
  regressions prove `requests = unique_parses + cache_hits`, deterministic output and no
  cache reuse across operation leases.
- Fifteen alternating cold-process Release runs against installed 0.51.0 reduced median
  analysis latency from 465.041 ms to 451.977 ms (-2.81%) for a 5,310-byte equation
  fixture and from 928.300 ms to 706.852 ms (-23.86%) for a 52,292-byte mixed-domain
  torture fixture. Accounted budget fell 22.83% and 35.21%; the candidate reported 48
  and 272 cache hits. These are two local fixtures, not a universal performance claim.
- Hosted CI run 30207437839 passed all six jobs for implementation commit `d197329`,
  including Linux Engine, qualified LibreOffice/UNO rendering, Python compatibility and
  regenerated golden artifacts, Windows native tests and final ZIP, the standalone Open
  XML validator and the remote-service container.

## 0.50.0 — 2026-07-26

- Added `wordtoolkit.render_ooxml_libreoffice_artifacts/1.0` and the matching
  `libreoffice-render-package` CLI. The operation binds a saved Word package fingerprint,
  exact LibreOffice/Java/UNO archive hashes and an embedded reviewed UNO helper; requests
  hidden read-only Writer loading, explicit input/PDF filters, `NEVER_EXECUTE` macros and
  `NO_UPDATE` external updates; and publishes PDF, optional Poppler-derived PNG pages and
  a provenance manifest without opening Microsoft Word or silently falling back.
- Private profiles, helper materialization, source copies, PDF and raster staging are
  removed before the no-clobber public publication transaction. The source is rehashed
  independently immediately before publication. Failure tests prove stale fingerprints,
  source drift, unknown fields and existing outputs create no partial public bundle.
- The result explicitly declines Microsoft Word layout/pixel-equivalence claims and does
  not misreport requested macro/update policies as behavioral prevention proof. Capability
  discovery now exposes 145 actions and 56 complete explicit metadata contracts.
- Hosted run 30201432923 passed all six jobs. Its Ubuntu 24.04 lane ran seven public
  render-action tests and twelve provider tests against exact LibreOffice 24.2.7.2,
  Temurin JDK 17.0.16 and source-rebuilt helper hashes. Local pinned-SDK 8.0.423 gates
  pass 739 Engine, 12 LibreOffice and 532 Native tests.
- Two independent 197-file, 89,608,757-byte Windows plugin trees and their
  37,448,673-byte ZIPs are byte-identical at SHA-256
  `d6f1dea5a29022b41516acf7ff445e5849c5b46529c3f482591fed5bfddcd98f`.
- Installed and enabled `0.50.0+codex.20260726142219`. The canonical build,
  persistent personal source and active Codex cache have identical 197-path
  length/SHA-256 maps; installed discovery reports 145 actions, 15 tools and 56
  complete explicit metadata contracts.

## 0.49.0 — 2026-07-26

- Added the first honest LibreOffice backend slice as a neutral `net8.0` adapter shared
  by Engine, strict JSON CLI and lazy MCP. `inspect_libreoffice_backend` requires one
  explicit absolute local executable, never searches `PATH`, rejects UNC, device,
  mapped-network and reparse-point paths, optionally binds an expected SHA-256, runs only
  bounded `--version` with closed stdin and process-tree timeout termination, and rehashes
  the executable after exit.
- The closed result proves only a recognizable LibreOffice product/version identity. It
  returns no executable path or environment values and explicitly leaves UNO, Writer,
  PDF export, document-load policy, macro/update prevention, rendering and Word fidelity
  unverified. `network_requested=false` is not called network isolation; the child process
  remains outside a sandbox. A recognizable banner is not treated as vendor-signature
  proof, and stable pre/post hashes are not described as an atomic binding to the bytes
  loaded by the operating system.
- Added strict request parsing, deterministic fake-process regressions, path/hash/version/
  timeout/output failure taxonomy, CLI/MCP convergence, no-Word proof, a cross-platform
  adapter test project and a Linux CI lane that probes the exact installed LibreOffice
  binary. The native Windows package now verifies that the neutral adapter assembly is
  present and free of checkout-path leakage.
- Capability discovery now exposes 144 actions and 55 complete explicit metadata
  contracts. The isolated UNO document-load/export lane and shared Word-versus-
  LibreOffice visual corpus remain deliberately unfinished and are not hidden behind the
  successful version probe.
- Verified 739 Engine, 8 neutral LibreOffice-adapter, 522 Native and 1,318 Python passes
  with 16 intentional skips on pinned SDK 8.0.423; Ruff, six .NET format gates, schema
  export and the standalone validator are clean. Hosted CI run 30198749524 passed six
  jobs, including a real Ubuntu LibreOffice 24.2.7.2 binary at SHA-256
  `eef555c71025262c67274dc6e98d00168c2a2ce0fcd16473c38609ff3ce2ace9`
  bound as the adapter's expected executable hash.
- Two independent 197-file, 89,444,147-byte plugin trees and their 37,397,666-byte ZIPs
  are byte-identical at SHA-256
  `30a9c9ad4a4291969c0e723e9eaeb68be6234f266bc90161038af97635abb927`.
  Installed and enabled `0.49.0+codex.20260726122100`; canonical build, persistent source
  and active cache have zero path/length/hash differences and installed discovery reports
  144 actions, 15 tools and 55 complete explicit metadata contracts.

## 0.48.0 — 2026-07-26

- Added a source-linked semantic-role evidence graph for theorem, lemma, proposition,
  corollary, definition, proof, example, remark, axiom and assumption paragraphs. The
  closed Polish/English profile keeps exact enclosing SDT declarations, explicit and
  inherited paragraph-style conventions, and strict leading labels as separate evidence
  channels; conflicts choose no winner and semantic completeness is never claimed.
- Added one shared direct Engine operation, strict `semantic-role-package` JSON CLI and
  closed lazy `inspect_ooxml_semantic_roles` MCP action. The default returns usable
  main-story theorem candidates without paragraph text or evidence identities. Paging is
  fingerprint-bound; evidence, styles, content-control IDs, short hashes, source and text
  are separately gated; raw XML and Custom XML values have no response field.
- Hardened false-positive and failure boundaries: a run-level content control nested
  inside a paragraph cannot declare that paragraph's role; the default paragraph style,
  typography, numbering, fuzzy similarity and private Custom XML vocabulary names are not
  evidence; unresolved style chains reduce coverage; every text/evidence/issue budget
  fails closed instead of silently truncating classification.
- Added Engine, CLI, JSON-RPC/schema, privacy, stale-page, conflict, inheritance,
  ambiguity and hard-limit regressions. A gated Microsoft Word 16.0 build 16.0.20131
  acceptance test saved the fixture, retained all three valid evidence classes, rejected
  the inline-SDT false positive and passed Microsoft Open XML SDK validation after save.
- Verified 731 Engine, 513 Native and 1,318 Python passes with 16 intentional skips on
  pinned SDK 8.0.423; Ruff, four .NET format gates, schema export and the standalone
  validator are clean. Two independent 196-file, 89,352,581-byte plugin trees and their
  37,370,393-byte ZIPs are byte-identical at SHA-256
  `b50acb29b74e1f1d705ecd89f9be768cf289a2b7a144d0526befdb4f2963c3fa`.
- Installed and enabled `0.48.0+codex.20260726112849`. Canonical build, persistent source
  and active cache have zero path/length/hash differences. Installed discovery reports
  143 actions, 15 tools and 54 complete explicit metadata contracts.

## 0.47.0 — 2026-07-26

- Added semantic numbering reconstruction across direct Engine, strict
  `numbering-rebuild-package` CLI and closed lazy MCP actions for candidate inspection,
  planning and apply. A typed blueprint can create a missing numbering part or append
  independent single-level, multilevel and hybrid definitions, then bind only exact
  fingerprinted paragraphs without asking an AI to write XML.
- Reconstruction supports decimal, zero-padded decimal, upper/lower Roman, upper/lower
  Latin letter, bullet and none formats; explicit starts, level text, restart policy,
  legal numbering, suffix, justification and typed twip geometry. It allocates collision-
  free OPC relationships and numbering IDs, preserves every unselected definition and
  paragraph, validates counters and labels, requires zero new Microsoft schema errors and
  proves a byte-exact inverse before atomic publication with a recovery backup.
- Transitional and Strict regressions cover missing/existing numbering infrastructure,
  main/header story isolation, 205 targets across bounded inspection pages, style-inherited
  numbering, every supported format, XML escaping, revision/MCE/tracked-property/signature
  blocks, stale package/candidate/plan evidence, CLI/MCP convergence and strict JSON.
- A guarded Word 16.0 build 16.0.20131 acceptance proof produced native labels `1.`,
  `1.a)`, `1.b)`, `2.`, passed Microsoft Open XML SDK validation, left the source package
  hash unchanged and exported a clean one-page PDF reviewed from a Poppler raster. Picture
  bullets, locale/custom formats, revision-view choice, style-definition binding and list
  merging remain explicit boundaries.
- Verified 711 Engine, 509 Native and 1,318 Python passes with 16 intentional skips on
  pinned SDK 8.0.423; Ruff, four .NET format gates, schema export and the standalone
  validator are clean. Two independent 196-file, 89,248,326-byte plugin trees and their
  37,341,668-byte ZIPs are byte-identical at SHA-256
  `11bf921e29d9552d849b231d6a77b21d69666bfb58959fee674c4036c5a2016d`.
- Installed and enabled `0.47.0+codex.20260726102718`. Canonical build, persistent source
  and active cache have zero path/length/hash differences. Installed discovery reports
  142 actions, 15 tools and 53 complete explicit metadata contracts.

- Added a source-linked equation-paragraph rewrite object that models only ordinary
  `w:r`/`w:t` text slots before, between and after direct immutable `m:oMath` or
  `m:oMathPara` anchors. Fields, revisions, hyperlinks, controls, range markers,
  drawings, tabs, breaks and other rich structures fail closed instead of being
  flattened.
- Added shared Engine inspect/plan/apply contracts, strict
  `equation-paragraph-rewrite-package` CLI and three lazy MCP actions. Candidate,
  package and plan fingerprints bind the ordered slot intent; apply blocks signatures,
  requires Microsoft Open XML validation, retains an atomic backup by default and
  returns neither paragraph text nor OMML.
- Candidate materialization preserves exact OfficeMath bytes, paragraph/run structure,
  text-node ordinals and every unselected equation paragraph, then proves an exact
  inverse that reconstructs all original uncompressed OPC entry bytes. Transitional and
  Strict multi-equation/display-math tests plus a deterministic XML-escaping corpus
  cover the public boundary.

- Added a source-linked OCR candidate graph for embedded images actually referenced by
  Word figures. Repeated image parts deduplicate to one stable fingerprint-bound
  candidate; declared raster types are checked against payload signatures, external
  targets are never fetched and vector/oversized/unresolved inputs fail closed.
- Added versioned `IWordOcrProvider` extension contracts and a built-in local Tesseract
  CLI adapter. It requires explicit absolute local-filesystem executable/model paths,
  rejects UNC/mapped-network and reparse-point paths, hashes both before and after one
  end-to-end timeout, launches without a shell, streams images through stdin, bounds and
  validates TSV text/confidence/geometry and never returns raw diagnostics or paths.
- Added direct Engine, strict `ocr-package` CLI and lazy
  `inspect_ooxml_ocr_candidates` / `run_ooxml_ocr` actions. Recognition is package-
  fingerprint-bound, explicitly selected, `local_only` by default and independently
  gates text, geometry and hashes. A real embedded PNG passed local Tesseract recognition
  through MCP with schema-valid provenance, unchanged DOCX hash and no Word invocation.

- Replaced the misleading equation conversion preflight with a default native Word
  execution path that uses an invisible unsaved scratch document and the same OMath
  build-up, style rewrite and bounded OMML readback as live apply. The explicitly named
  `conversion_only` mode now returns `valid: null` instead of false green success.
- Added semantic LaTeX normalization for `\binom`, `\dots` and trigonometric function
  powers, plus correct OMML `noBar` fraction readback. Real Word acceptance now covers
  these constructs together with mathematical text and refuses altered ASTs.
- Reclassified prepublication drift by stable semantic package state, visible text,
  exact ranges and object counts instead of volatile raw Flat OPC hashes. Real staging
  failures now preserve their original error when the connected document did not change;
  proven concurrent semantic drift returns `STAGING_TARGET_DRIFT` before target mutation.
- Added token-bound `inspect_live_word_equations` and `update_live_word_equation`. A point
  update requires the current live version plus an exact equation/range/context identity,
  stages the replacement in isolation, publishes one OMath in one Undo record and rejects
  stale tokens.
- Added `publish_ooxml_package_to_live_word`, a whole-package hybrid handoff that binds to
  an inspected fingerprint, requires zero Microsoft Open XML SDK errors, disables macros
  and link updates, proves the source file unchanged and opens a new live document. It
  deliberately refuses fictional in-place atomic identity replacement.

- Added a typed source-linked heading-outline graph with one resolution record per
  projected paragraph, exact direct/default/style inheritance precedence, explicit
  body-text value `9`, per-story hierarchy, revision/MCE exclusion and bounded issues.
  Localized style names are never classification evidence, malformed higher-precedence
  declarations never fall through, and heading text is not stored in the public graph.
- Added direct Engine inspection, strict `heading-outline-package` CLI and lazy
  `inspect_ooxml_heading_outline` MCP under a closed 1.0 contract. Main-story hierarchy
  metadata is the token-lean default; text, styles and source locations are separately
  gated. The action never opens Word, follows external targets, mutates a package or
  returns raw XML. The linter now consumes the same graph instead of a private resolver.

- Added a bounded typed OPC relationship-usage graph and guarded relationship repair.
  It distinguishes referenced, implicit, unknown, duplicate-ID, invalid-owner and orphan
  states while scanning every retained Markup Compatibility branch. One reviewed atomic
  batch can remove only fingerprinted unreferenced explicit relationships or orphan
  `.rels` entries; it never deletes target parts and rejects any new unreachable part.
- Added direct Engine contracts, strict `relationship-repair-package` CLI and lazy
  `inspect_ooxml_relationships`, `plan_ooxml_relationship_repair` and
  `apply_ooxml_relationship_repair` MCP actions. Apply recomputes `wrrplan_`, blocks
  signatures, requires Microsoft Open XML validation with no new errors, requires an
  explicit Boolean for external relationship removal and keeps an atomic backup by
  default. Responses never return external targets or raw XML.
- Extended the linter from 21 to 25 rules with typed unused-explicit-relationship,
  orphan-relationship-part, heading-outline diagnostic and empty-heading findings. The
  catalog now contains 121 actions and 35 complete metadata contracts; the explicit
  metadata gap remains 86.
- Verified 621 Engine tests, 442 Native tests and 1,313 Python passes with 16 intentional
  skips on pinned SDK 8.0.423; Ruff, 29-module mypy, four .NET format gates, schema export
  and the standalone validator are clean. A forced Word 16.0 build 16.0.20131 oracle
  matched engine heading levels across main/header stories, opened read-only with repair
  disabled and left the file hash unchanged.
- Packaged and enabled `0.39.0+codex.20260725034031`. Two builds produced identical
  196-file, 87,958,762-byte trees and identical 36,987,385-byte ZIPs at SHA-256
  `8742e1ef0231d8830d87a148ea05e61fd460f99f68173d287973019276e2c6d7`.
  Build, persistent marketplace source and active cache have zero file differences;
  installed discovery reports 121 actions and 35 complete contracts. Installed lazy-MCP
  heading inspection returned 4 metadata-only headings with no text/XML/mutation/Word
  launch, and the Word process set was unchanged.

- Added transactional saved-package numbering restart through direct Engine,
  `numbering-repair-package` CLI and lazy `plan_ooxml_numbering_repair` /
  `apply_ooxml_numbering_repair` MCP. The exact selected list tail is reassigned to a
  cloned `w:num`; earlier and unrelated sequences, paragraph text and unplanned parts are
  proved unchanged. Apply recomputes the fingerprinted plan, requires Microsoft Open XML
  baseline/candidate validation, blocks signatures and writes atomically with a sibling
  backup by default. Responses expose counts/hashes, never text/XML, and explicitly flag
  the 200-item detail ceiling.
- Extended `WordDocumentLinter` from 18 to 21 rules by consuming the executable sequence
  graph. Sequence diagnostics, unresolved counters and malformed/overlong labels are now
  findings; revision/MCE view choice, picture bullets and locale/custom label rendering
  remain explicit coverage boundaries. A guarded real-Word repair oracle passed with
  values `1,7,8,9`, labels `1.,7.,8.,9.`, zero SDK errors and an unchanged post-oracle
  package hash.
- Added closed 1.0 schemas and operation metadata for both numbering-repair actions. The
  native catalog now contains 117 actions and 31 complete metadata contracts; the
  explicit gap remains 86. A real JSON-RPC MCP response is validated against the
  published compact output schema.
- Verified 592 Engine tests and 434 Native tests on pinned SDK 8.0.423; all four scoped
  `dotnet format --verify-no-changes` gates and deterministic schema export pass. Two
  independent package builds produced identical 196-file, 87,708,110-byte trees and
  identical 36,918,950-byte ZIPs at SHA-256
  `569c313de07e30ce2fe4614a2ace13bd99ad355013160908ef772f4870896da0`.
  Installed and enabled `0.39.0+codex.20260725013020`; build, personal source and cache
  have zero path/length/hash differences. Installed discovery returns 117 actions and 31
  complete metadata contracts, and the installed numbering-repair schema is closed and
  explicitly requires its detail-truncation flag.

- Added a bounded source-linked `WordListSequenceGraphBuilder` and the lazy
  `inspect_ooxml_numbering` `view=sequences`. The executor resolves default/style/direct
  paragraph numbering, isolates counters per story and `numId`, applies higher-level and
  section-break restarts, legal numbering and direct `numId=0` removal, and exposes stable
  `wdli_`/`wdls_` identities. Counter and label certainty are separate; locale/custom
  formats, picture bullets, invalid labels and ambiguous revision/MCE views are never
  fabricated.
- Qualified the conflict-heavy sequence against an Open XML SDK-valid package in real
  Word 16.0 build 16.0.20131. Word returned values `1,9,10,2,9,1,9` and labels
  `1.,1.i,1.j,2.,2.i,1.,1.i`, proving replacement-level start precedence, ignored
  replacement-level restart and `w15:restartNumberingAfterBreak`. The observed start
  result contradicts Microsoft's written interoperability note, so the action reports a
  fixed compatibility warning and scopes the rule to the qualified build.
- `inspect_ooxml_numbering` now has an explicit 1.0 operation version, read-only
  permissions/reversibility metadata, a closed sequence-item output contract, semantic
  story/paragraph filters, pre-read unknown-argument rejection and paragraph-text-free
  output. Catalog coverage rises to 29 complete metadata contracts; 86 actions remain.
- Verified 582 Engine tests, 428 Native tests and 1,313 Python/OOXML passes with 16
  intentional skips; Ruff, mypy over 29 modules, scoped .NET format, schema generation and
  the standalone Open XML validator are clean. Two SDK 8.0.423 builds produced identical
  196-file, 87,598,120-byte trees and identical 36,889,700-byte ZIPs at SHA-256
  `3ea9c02ce12dff2ce52fc885024554fc6f78d96eb436fac8dc99d4484c034add`.
  Installed and enabled `0.39.0+codex.20260725000036`; build, marketplace source and cache
  have zero file differences, and installed lazy MCP sequence inspection passed.
- Added lazy `probe_live_word_feature_behaviors` behind the closed
  `wordtoolkit.probe_live_word_feature_behaviors/1.0` contract. After explicit
  `confirm_scratch_documents=true`, it performs native OMath BuildUp, rich-text
  content-control creation, SmartArt insertion and one custom Undo transaction, each in
  its own invisible unsaved scratch document. No connected-document content, style or
  object mutation is issued and `live_version` remains unchanged. Because Word may refresh
  volatile view/session metadata during activation, whole-package identity is not claimed.
- A success response is impossible until every created scratch document was closed with
  `wdDoNotSaveChanges`, the exact previous active document and window were restored, and
  the Word document count returned to baseline. Close, restore, verification or
  `EndCustomRecord` uncertainty returns `TEMPORARY_DOCUMENT_CLEANUP_FAILED`, quarantines
  the live handle and requires explicit disconnect. Ordinary behavior failures return
  only fixed codes after cleanup proof; exception text is never exposed.
- Six native regressions cover the non-read-only explicit-confirm contract, four passing
  behaviors in four documents, isolated failure plus unavailable SmartArt layouts,
  mandatory close/restoration, cleanup quarantine, Undo-record closure poisoning and
  rejection before COM dispatch. The native catalog now has 115 actions and 29 complete
  metadata contracts; the explicit gap is 86.
- Verified 574 Engine tests, 426 Native tests, 1,313 Python/OOXML tests with 16 intentional
  skips, the exact CI Ruff lane, mypy across 29 maintained modules, scoped .NET format,
  deterministic schema export and the standalone Open XML validator. The real-Word
  acceptance lane permits only Word's volatile semantic package projection to drift;
  every rollback snapshot content/range/count/Saved invariant remains exact.
- Packaged and enabled `0.39.0+codex.20260724230914`. Two SDK 8.0.423 builds produced
  identical 196-file, 87,523,060-byte trees and identical 36,870,505-byte ZIPs at SHA-256
  `6b942f24de52bd82c41dd1cae69dc12c929ea9472d9731cde7babf7763dee1d9`.
  Executable, runtime, Engine and Open XML SDK adapter hashes are
  `b38cc86a57ee18799621af58e631b9afbb45b392113ba3e8944c745913f6675b`,
  `94aa2e34eabd3b19254bb6585b1ecbda234a21dc1386b646bad92e8b0ef22b0a`,
  `12158da26fda58d40f5600159cc01c2e92bceff5b4c555e95ec2ce6154986f1c` and
  `bb975458d8d71a9a6b1d0ace1193b3efaeac3111677b45bb337bdacd17e6c0ff`.
  Build, personal source and enabled cache have zero path/length/hash differences and no
  Python files.
- The installed lazy MCP attached to Word 16.0 build 16.0.20131 and passed all four
  behaviors. It created and closed four scratch documents, restored the previous active
  document/window and document count, kept `live_version=0`, passed its installed output
  schema, returned no forbidden content/path/identity/COM field and disconnected cleanly.

- Added lazy `inspect_live_word_version_profile` behind the closed read-only
  `wordtoolkit.inspect_live_word_version_profile/1.0` contract. It returns raw
  `Application.Version`/`Build`, document `CompatibilityMode`/`SaveFormat`, a conservative
  version family and independent property-access probes for UndoRecord, native OMath,
  SmartArt and content controls. It reads no document content or path, returns no user or
  licence identity, never starts Word, and never infers a product edition from Word 16.0.
- Probe failures are isolated and reduced to eight fixed codes; explicit unknown values
  remain JSON `null`, while member availability is expressly not called a behavioural
  guarantee. The native catalog now has 114 actions and 27 complete metadata contracts;
  the explicit gap remains 87.
- Four new native regressions cover the closed contract, successful projection, all three
  probe states, partial COM failure, explicit-null preservation, content/path privacy and
  pre-COM rejection of unknown fields. A fifth regression covers every documented mapping
  plus unknown future values. The native checkpoint is 419/419 on the pinned
  SDK 8.0.423.
- Packaged and enabled `0.39.0+codex.20260724220014`. Two pinned-SDK builds produced
  byte-identical 196-file, 87,475,140-byte trees and byte-identical 36,857,975-byte ZIPs
  at SHA-256 `746201dcc9b7ea1b4147a8388df539d212589bce58dbb015da978d4db568d3b2`;
  neither tree contains Python. Build, personal source and enabled cache have zero
  path/length/hash differences, and installed discovery reports 114 actions, 15 tools
  and 27 complete contracts.
- The installed lazy MCP attached to a real Word 16.0 build 16.0.20131 document, returned
  compatibility mode 15, save format 12, four available probes and zero issues without
  advancing `live_version`. Its full response passed the installed output schema and
  exposed no document-content, path, user-identity or licence-identity field.
- Hosted CI run `30123410969` on code head `9e96323` passed all five jobs. Its clean
  Windows artifact reproduced the local 36,857,975-byte ZIP and SHA-256
  `746201dcc9b7ea1b4147a8388df539d212589bce58dbb015da978d4db568d3b2` exactly.

- Added bounded encrypted-OOXML detection across direct Engine, strict
  `inspect-encryption` CLI and lazy `inspect_ooxml_encryption` MCP under one closed
  `wordtoolkit.inspect_ooxml_encryption/1.0` contract. The cross-platform parser validates
  Compound File Binary header, DIFAT/FAT, directory, regular and mini-stream chains before
  recognizing root `EncryptionInfo`, `EncryptedPackage` and DataSpaces markers; it
  classifies Standard, Agile and Extensible version prefixes without opening Word.
- Encryption inspection accepts no password, decrypts no content, reads at most eight
  `EncryptionInfo` bytes, uses no network and returns no path, stream name or document
  content. Complete DataSpaces validation, universal package-boundary errors and authorized
  decrypt/encrypt adapters remain open. The native catalog now has 113 actions and 26
  complete metadata contracts, leaving the explicit coverage gap unchanged at 87.
- The checkpoint passes 574 Engine, 414 Native and 1313 Python tests with 16 intentional
  skips. All .NET build and test evidence uses the pinned local SDK 8.0.423 executable.
- A real Word smoke exposed legitimate preallocated surplus FAT sectors; the bounded parser
  now accepts a conservative surplus ceiling of 109 sectors while still requiring the
  mathematical minimum and rejecting physical/count overflow. The corrected Release engine
  classified a fresh 19,456-byte Word 16.0 password-protected DOCX as Agile 4.4 with zero
  issues and the general inspector returned `DOCUMENT_ENCRYPTED` rather than ZIP corruption.
- Packaged and enabled `0.39.0+codex.20260724210114`. Two pinned SDK 8.0.423 builds
  produced identical 196-file, 87,452,589-byte trees and identical 36,851,090-byte ZIPs
  at SHA-256 `9ac60e38c5263ddae3ca6b0202f77e620223b2bd68a8e05b15b5ae94ec67867e`.
  Build, personal source and cache are byte-identical; the packaged and installed-cache
  Engine assemblies both have SHA-256
  `d4e341d892cdaac0b4ba1fed1582bffba522ce2691845d52a7908d17ccaf7776`.
  Installed discovery reports 113 actions, 15 tools and 26 complete contracts. The same
  source revision passed the real Word probe above; a real lazy MCP call from the installed
  runtime validated against its inspected output schema and returned no path.
  Hosted CI run `30121281344` passed all five jobs; its clean Windows artifact is the same
  36,851,090 bytes with the same SHA-256 as both local archives.

- Added a privacy-minimizing observability spine to `WordToolkit.Engine`. Versioned
  `ActivitySource` and `Meter` producers are opt-in and use only registered operation name,
  normalized outcome, operation version and fixed effect flags. Audit independently supports
  `off`, bounded memory or local JSONL. Arguments, document text, XML, paths, relationship
  targets and package fingerprints have no telemetry or audit field, and no network exporter
  ships with the runtime.
- Sink I/O now runs behind a bounded nonblocking channel, so a slow or throwing sink cannot
  hold or replace a document operation. Capacity drops, retention drops, queue overflow and
  sink failures are explicit counters. Throwing host activity/metric listeners are also
  contained and counted. Audit events form a source-ordered unkeyed SHA-256
  append chain that is honestly marked unauthenticated rather than dressed up as compliance.
- Added lazy `inspect_wordtoolkit_observability` behind the closed
  `wordtoolkit.inspect_observability/1.0` contract and strict local
  `audit-log verify`. The inspector returns bounded content-free health data, requires
  separate opt-ins for correlations and hashes, opens no Word instance and reads no
  document. The verifier rejects malformed/unknown/duplicate fields, bounds bytes/events/
  line length and omits both the source path and event bodies.
- The native catalog now contains 112 actions, 15 exposed tools and 25 complete metadata
  contracts; 87 actions remain explicitly uncovered. New Engine and Native tests cover
  privacy, hostile dimensions, tampering, concurrency, retention, slow/throwing sinks,
  queue overflow, metrics/traces, environment configuration, real MCP dispatch and strict
  verifier bounds. Authenticated anchoring, transaction-durable evidence, remote export,
  legal hold, access audit, secure deletion and cross-segment manifests remain open.
- Packaged and enabled `0.39.0+codex.20260724201229`. Two pinned SDK 8.0.423 builds are
  byte-identical at 196 files and 87,396,612 bytes; both 36,831,975-byte ZIPs have SHA-256
  `2028f140497c272032e5fd24084602a8e6716998adf92f4b9049a74aae70084f`.
  Build output, personal source and enabled cache have zero path/length/hash differences.
  The clean hosted-Windows CI artifact is the same 36,831,975 bytes with the same SHA-256.
  The full checkpoint passes 553 Engine, 410 Native and 1309 Python tests with 16
  intentional skips. Installed CLI/MCP discovery reports 112 actions, 15 exposed tools,
  25 complete contracts and the exact stamped runtime version; installed observability
  smokes prove bounded memory and verified JSONL modes without content or path disclosure.

- Added the first fail-closed extension registry in `WordToolkit.Engine`. Hosts must
  explicitly allow extension IDs, trust/isolation modes, interface kinds and compatible
  engine/interface versions before registration. Capabilities declare exact permissions,
  input/output byte ceilings, concurrency, cooperative timeout, determinism and content
  behavior. Duplicate/conflicting registrations and use after freeze fail; the immutable,
  source-order-independent public catalog is bound to a deterministic SHA-256.
- Registered the Microsoft Open XML SDK candidate validator as the first real production
  module. Native style, comment-body, review, package-patch, merge, formatter and rollback
  paths now resolve it through `ExtensionWordPackageCandidateValidator` instead of
  constructing the SDK adapter beside the registry. A full parallel Native run exposed
  an undersized two-invocation profile; the stateless built-in validator now uses the
  host's 64-active-request ceiling while isolated registry tests still prove exact
  concurrency refusal at a limit of one.
- Added `InspectExtensionCatalogOperation`, strict `extensions` CLI and lazy
  `inspect_wordtoolkit_extensions` MCP behind one
  `wordtoolkit.inspect_extensions/1.0` contract. The bounded result reads no document,
  opens no Word instance, scans or loads no assembly, uses no network and returns no
  implementation type or path. The native catalog now contains 112 actions, 15 exposed
  tools and 25 complete metadata contracts; 87 actions remain explicitly uncovered.
- Added research and threat-model documentation stating that `AssemblyLoadContext` is not
  a security boundary. The current host accepts only trusted in-process modules with
  cooperative cancellation; untrusted/out-of-process loading remains rejected until a
  separate restricted process and closed IPC exist. The checkpoint passes 539 Engine,
  403 Native and 1309 Python tests with 16 intentional skips; Ruff, mypy over 29 modules,
  changed-project format checks and the real CLI output schema are clean.
- Packaged and enabled `0.39.0+codex.20260724191908`. Two pinned-SDK builds are
  byte-identical at 196 files and 87,285,844 bytes; both 36,798,079-byte ZIPs have
  SHA-256 `e298ebc80a64a6c5fb36ca579c041633963a383ff82a153cb8aa3c43bcf3c2d2`.
  Build output, personal source and enabled cache have zero path/length/hash differences.
  Installed CLI discovery reports 111 actions, 15 exposed tools and 24 explicit
  contracts; installed lazy MCP extension inspection returns the same catalog hash and
  confirms that it neither loads assemblies nor opens Word.

- Added a bounded Flat OPC transport owned by `WordToolkit.Engine`. The streaming codec
  prohibits DTDs and external resolution, enforces outer XML and decoded package budgets,
  rejects duplicate/case-colliding/traversal part names, validates XML payloads, preserves
  binary and XML-typed AltChunk payloads, rebuilds `[Content_Types].xml` and writes a
  deterministic OPC package. Signed packages fail closed because XML reserialization
  would invalidate their signatures.
- Added `FlatOpcWordPackageOperation`, the strict `flat-opc-package` CLI and lazy
  `convert_ooxml_flat_opc` MCP action behind one `wordtoolkit.convert_ooxml_flat_opc/1.0`
  contract. Every conversion is create-new, isolated behind a sibling temporary file and
  published only after Word-package validation plus exact part-name, content-type,
  relationship, binary-byte and XML-tree parity. Responses expose bounded hashes/counts,
  never document XML, and never open Word.
- Published a 13-case hostile Flat OPC corpus and added Microsoft Open XML SDK
  interoperability, direct/CLI/MCP parity and bundled real-DOCX round-trip regressions.
  A real-corpus false `RESULT_MISMATCH` exposed declaration-sensitive `XDocument`
  comparison; the guard now ignores only the XML declaration while retaining every node
  inside the part. The full checkpoint passes 531 Engine, 399 Native and 1309 Python
  tests with 16 intentional skips; Ruff, maintained-source mypy and all changed .NET
  projects are clean.
- Replaced renderer publication through `File.Move(overwrite:false)` after hosted Linux
  proved that two concurrent create-new writers could both succeed and the later Unix
  rename could replace the first artifact. `SemanticRenderArtifactPublisher` now creates
  the public name as an atomic same-filesystem hard link to the closed, flushed temporary
  file, so a pre-existing or concurrently won destination fails without clobbering. The
  race regression passes 20 repeated local runs and the full suites.
- Packaged and enabled `0.39.0+codex.20260724184043`. Two pinned-SDK builds, the personal
  marketplace source and enabled cache are identical at 196 files and 87,203,265 bytes.
  Both 36,776,448-byte ZIPs are byte-identical at SHA-256
  `58e65922b03e6c81240cfe128827b8d74f66646b1781bcb247e7716cb151b4ef`.
  Installed discovery reports 110 actions, 15 exposed MCP tools and 23 explicit
  contracts; the installed runtime exported and re-imported the bundled advanced DOCX
  with semantic parity, zero package errors and zero orphan parts.

- Moved saved-package patch rollback behind the public transport-neutral
  `PatchRollbackWordPackageOperation`. Direct .NET, the new strict
  `patch-rollback-package --mode plan|apply --request <json|->` CLI and both lazy MCP
  actions now share one closed JSON parser, one destination-bound `wtrollback_` identity,
  one semantic/risk/type/schema proof, one authorization decision and one atomic writer.
  The old Native-only reverse branch was removed instead of retained as a second source
  of truth. Changed rollback without an injected schema validator fails closed; exact
  no-op rollback remains non-mutating.
- Added six direct Engine regressions plus one SDK/CLI/MCP parity regression covering
  exact restoration and redo backup, stale fingerprints, cross-path plan rejection,
  active-content authorization, validator absence, no-op behavior, closed JSON and
  canonical result equality. A seventh Engine regression keeps destination binding
  case-sensitive on case-sensitive filesystems. The full checkpoint passes 511 Engine,
  395 Native and 1309
  Python tests with 16 intentional skips.
- Packaged and enabled `0.39.0+codex.20260724165517`. Two pinned-SDK builds, the personal
  marketplace source and the enabled cache are identical at 196 files and 87,150,234
  bytes. Both 36,755,923-byte ZIPs are byte-identical at SHA-256
  `c9216579cede83aec95cab4b895a30008d9c33fcfb28ba0ea3163566e937648d`.
  Installed discovery reports 109 actions, 15 exposed MCP tools and 22 explicit
  contracts; the installed executable reports the exact version and the new CLI help.

- Added public saved-package rollback as the separate lazy
  `plan_ooxml_patch_rollback` and `apply_ooxml_patch_rollback` actions. They derive the
  exact reverse from the reviewed original `.wtpatch`, require the current result
  fingerprint and original patch ID, bind a distinct `wtrollback_` plan to the normalized
  destination path, and re-run semantic, risk, package-type and baseline-aware Open XML
  validation before atomic publication. Risk authorizations remain independent and false
  by default; the success backup retains the pre-rollback state as redo evidence.
- Added closed versioned request/result, permission and reversibility metadata for both
  rollback actions. The native catalogue now contains 109 actions, 15 exposed MCP tools
  and 22 explicit metadata contracts; the remaining metadata gap stays visible at 87.
- Added rollback regressions for exact fingerprint restoration and backup contents,
  stale current state, destination-bound plan mismatch, macro authorization, no-op
  non-mutation, response privacy/size and the public contract surface. These operations
  never open Word and never return patch payloads or raw XML. The full checkpoint passes
  504 Engine, 394 Native and 1309 Python tests with 16 intentional skips; both changed
  .NET projects are format-clean and both new output schemas pass Draft 2020-12
  meta-validation.
- Packaged and enabled `0.39.0+codex.20260724161042`. Installed discovery reports 109
  actions, 15 exposed tools and 22 explicit metadata contracts. Build, personal source
  and enabled cache are identical at 196 files and 87,093,417 bytes. A second
  same-checkout package build produced an identical tree and identical 36,739,798-byte
  ZIP at SHA-256 `77b8216c597e2462aafbffdaf0f60110841b171385ba35c0b505cdd4fc298609`;
  clean-checkout reproducibility is still not claimed.
- Updated the remote-schema documentation generator's native action count to 109. The
  generated catalogue now remains byte-clean after export instead of silently rewriting
  the reviewed native count back to 107 in CI.

- Added an independent baseline-restoration path for failed
  `apply_live_word_operations` publication. The staged batch now retains its original Flat
  OPC snapshot in process; after a failed or unproven Undo, WordToolkit opens that snapshot
  as a separate hidden, read-only recovery document and copies its main story back through
  cross-document `Range.FormattedText` before deciding whether the target is safe.
- Recovery acceptance now requires two stable reads of a namespace-aware semantic Flat OPC
  hash, in addition to exact target/context boundaries, text fingerprints and structural
  counts. The semantic profile ignores only WordprocessingML `w:rsid*` session metadata;
  paragraph identities, content, fields, bookmarks, equations and every other element and
  attribute still participate in the proof.
- The original operation error remains authoritative only when Undo or the independent
  restore proves the complete checkpoint. `Document.Saved` is restored only after every
  other recovery comparison passes and is then rechecked. Any unstable snapshot, cleanup
  exception or residual mismatch returns `ROLLBACK_FAILED`, invalidates the handle and
  quarantines the document identity. Diagnostics expose only mismatch names and recovery
  status flags, never source content, OOXML or fingerprints.
- Added fake-COM proof that an `Undo=false` batch can be recovered independently with zero
  residual equations and an unchanged version. A forced real Word 16.0 test proves the
  harder boundary: the same recovery removes contaminated text and native OMath, but Word
  still normalizes the package beyond `w:rsid*`; WordToolkit therefore keeps returning
  `ROLLBACK_FAILED` and quarantining that state instead of declaring a false exact rollback.

- Replaced equation-only preflight with complete heterogeneous batch staging for
  `apply_live_word_operations`. WordToolkit now writes the current document's read-only
  Flat OPC snapshot to an isolated temporary clone, clears only the clone's main story,
  applies every requested text value, paragraph boundary, style, formatting property and
  native OMath there, and verifies the complete candidate before touching the target.
- Target publication is now one cross-document `Range.FormattedText` assignment instead
  of one text replacement followed by target-side formatting and OMath construction.
  Post-publication proof checks the exact published length/text fingerprint, global and
  range-local equation deltas, every operation boundary, every requested text-formatting
  value, equation display type, scoped math styling and optional semantic equation
  readback before the live version may advance.
- Added fail-closed staging hygiene: macros and link updates are disabled while the Flat
  OPC clone opens, the clone must close and its temporary file must be deleted, and the
  full target Flat OPC/text/structure must remain unchanged before publication. Unproven
  pre-publication drift returns `ROLLBACK_FAILED` and quarantines the handle without
  risking an unrelated Undo history entry.
- Added injected proof for one-call mixed publication, zero target-side OMath builds,
  failure before publication mutation, failure after partial publication, hidden target
  drift during staging, isolated equation rejection and every existing false/throwing/
  partial/visible-only Undo path. The real Word 16.0 acceptance now publishes two
  independently formatted paragraphs plus the eight-line complex integral derivation in
  one batch and re-verifies all six integrals and six correctly placed differentials.
- Full release gates pass 504 Engine, 389 Native and 1,309 Python tests with 16
  intentional skips; Ruff passes. Independent recovery is now attempted after a partially
  applied `FormattedText` assignment, but acceptance still requires full semantic package
  equivalence. Visible cleanup without that proof is reported as `ROLLBACK_FAILED` and
  quarantined rather than misrepresented as atomic.
- Enabled runtime `0.39.0+codex.20260724154131` reports 107 actions, 15 exposed tools and
  20 explicit metadata contracts. Build, personal marketplace source and enabled cache
  are identical at 196 files and 87,050,540 bytes with zero path, length or hash
  differences. The 36,731,827-byte ZIP SHA-256 is
  `88fe233ed24107866d757677f809f80732427acf140859576dec0f3f74642c55`.

- Removed the legacy live rollback helper that swallowed failed custom-record closure and
  `Document.Undo(1)` exceptions. Every current custom-Undo mutation family now uses one
  exact verifier and returns `ROLLBACK_FAILED` with handle/document quarantine when
  restoration cannot be proved.
- Expanded the rollback checkpoint to whole-document Flat OPC, main-story and linked-story
  OOXML, target/context ranges, save state and structural counts. SmartArt and review
  properties add supplemental fingerprints. Responses expose only mismatch names and
  structural summaries, never hashes, OOXML or document content.
- Added adversarial coverage for visible-state-only restoration with hidden OOXML residue,
  supplemental-state mismatch and removal of the legacy silent entry point. A forced Word
  16.0 probe proved `Undo=true` can restore visible text/counts while Flat OPC, range OOXML
  and story hashes still drift; WordToolkit failed closed and quarantined the identity.
- The native suite now passes 382 tests. Publication independent of Word Undo and complete
  heterogeneous batch staging remain explicit P0 work rather than being misrepresented as
  solved.
- Full release gates pass 504 Engine, 382 Native and 1,309 Python tests with 16
  intentional skips; Ruff and all four C# format verifiers pass. Enabled runtime
  `0.39.0+codex.20260724142108` reports 107 actions and 15 tools. Build, marketplace
  source and cache match at 196 files and 86,989,407 bytes; the 36,718,529-byte ZIP
  SHA-256 is `4df5585249c0e5cfbad23b0273ce97170fdc2598da34dda7d0ff09e01be3ede0`.

- Fixed the P0 atomicity defect in the native mixed text/equation transaction. The
  runtime no longer treats a single attempted `Document.Undo(1)` as proof of rollback.
  It snapshots live version/save state, main-story text, exact target and bounded-context
  OOXML, range boundaries and structural counts before mutation, then requires a true
  Undo result and an exact post-Undo match. Any unclosed custom record, false/throwing
  Undo, unreadable state or mismatch returns `ROLLBACK_FAILED`, invalidates the handle
  and blocks reconnection to the quarantined document identity until explicit disconnect.
- Every requested native equation is now built, styled and read back in an unsaved hidden
  Word staging document before target publication. A staging failure closes the temporary
  document and leaves target content, counts, version and handle unchanged; target-side
  rollback verification remains mandatory because publication can still fail.
- Added adversarial fake-COM coverage for exact rollback, content/OMath residue, partial
  rollback, `Undo=false`, thrown Undo, failed custom-record closure, handle invalidation,
  reconnect blocking and explicit quarantine release. Failure diagnostics return only
  codes, mismatch names and structural summaries; document text, OOXML and hashes stay
  private.
- Real Word 16.0 reproduced the underlying failure: `Undo(1)` returned `false` and a
  nominally empty target retained 33 paragraphs. The runtime returned `ROLLBACK_FAILED`,
  quarantined the identity and refused further inspection until explicit release. The
  final installed `0.39.0+codex.20260724132826` build subsequently staged, published,
  saved, SDK-validated and PDF-rendered the eight-line complex integral derivation with
  six native integrals, six correctly placed differentials and equal expected/readback
  contract hashes.
- Full gates pass 504 Engine, 378 Native and 1,309 Python tests with 16 intentional
  skips; Ruff lint and all four C# format verifiers pass. The enabled package exposes
  107 actions, 15 tools and 20 explicit metadata contracts. Build/source/cache trees are
  identical at 196 files and 86,962,059 bytes; the 36,709,200-byte ZIP SHA-256 is
  `bf6b236f7f9191559d4cb3a16ff49b8013d2daf527187a728dbb202c6861ebfc`.

- Added `wordtoolkit.mark_live_word_index_entry/1.0` and
  `wordtoolkit.insert_live_word_index/1.0` as native actions 106 and 107. They create
  token-bound native `XE` marks and one editable `INDEX` through Word, expose hierarchy,
  cross-reference/bookmark-page-range and semantic layout options, verify exact native
  readback in one Undo record and return no entry, bookmark, cross-reference, generated
  index or field-code text.
- Extended `wordtoolkit.update_live_word_reference_tables` to contract 1.1 so indexes
  participate in all-kind and exact-kind updates. Saved-package reference/dependency
  graphs now resolve complete, non-deleted `XE` entries to concrete complete `INDEX`
  field nodes.
- Added `wordtoolkit.mark_live_word_authority_citation/1.0` and
  `wordtoolkit.insert_live_word_table_of_authorities/1.1` as native actions 104 and 105.
  They use fresh token-bound targets, categories 1–16 or all-category insertion,
  native `TA`/`TOA` fields, one custom Undo record and exact post-mutation readback.
  Generated citation/table text, separator values, field instructions and COM objects
  are not returned.
- Fixed two defects found in Word before release: `IncludeSequenceName` is genuinely
  omitted with `Type.Missing`, preventing false `0-N` page ranges, and a real tab plus
  dotted leader now separates entries from page numbers. The action verifies all native
  separator/display/leader options and rolls back when Word does not preserve them.
- Resolved complete category-compatible `TA` references to concrete `TOA` field nodes in
  the saved-package reference and dependency graphs, including category-zero tables.
  Malformed, incomplete, deleted and ambiguous evidence still fails closed.
- Full gates pass 500 Engine, 362 Native and 1,309 Python tests with 16 intentional
  skips; Ruff lint and all four C# format verifiers pass. Exact installed Word 16.0 proof
  created three marks and one all-category table, passed Microsoft SDK validation with
  zero errors and produced a clean three-page PDF with pages `2` and `1, 3`.
- The installed `0.39.0+codex.20260724113419` package exposes 105 actions and 18 explicit
  metadata contracts. Build/source/cache trees match at 196 files and 86,856,508 bytes.
  The 36,685,680-byte ZIP SHA-256 is
  `c90c7dcf4b8ee3f2a341ddb2e2254e7fc3b942be040c65b8732744699e50460d`;
  the exact DOCX proof is
  `28515ac5afbbffd489bae3e6ed62e68b6c7c38d33230b50d52ed28db0a4e3562`
  and its PDF is
  `a824863c425a598dc79be2ec764f34e31a9e122ca1eb24ae2bb7f5b4f6a9d82b`.

- Added `wordtoolkit.insert_live_word_table_of_contents/1.0` as native action 103.
  It accepts semantic heading levels and heading/outline source flags instead of raw
  field instructions, inserts at the document start/end or a fresh collapsed cursor,
  optionally repaginates and updates, and verifies one exact native collection/range/
  field delta inside one custom Undo record with rollback on mismatch.
- Added focused fake-COM regressions for successful private readback, invalid source
  settings before Undo and rollback when Word creates no readable field. Catalogue
  coverage is now 103 input/effect contracts and 16 explicit output/permission/
  reversibility/version contracts; the uncovered explicit-metadata count remains 87.
- Full gates pass 498 Engine, 355 Native and 1,309 Python tests with 16 intentional
  skips; Ruff and C# formatting checks pass. The installed
  `0.39.0+codex.20260724102603` build is enabled and its 196-file build/source/cache
  trees have zero path, length or hash differences.
- The exact installed runtime created a two-level native contents table in Word 16.0.
  Microsoft SDK validation returned zero errors; independent inspection found one
  complete `TOC`, five complete `PAGEREF` fields, five resolved dependencies and zero
  issues/external/application-invoking fields. All three pages of the Word PDF were
  inspected and show correct leaders/page numbers without clipping or raw field syntax.
- The 14,992-byte unlocked DOCX proof has SHA-256
  `718627cd5f91b126aced63f5f1cc3890cc15fefa3fb9cd99567a4c2d63ff0982`; the
  40,742-byte PDF is
  `bafdd436de71c37e8cc481948b16fed4b839818e9383907d9d10e94af472f221` and the
  36,669,666-byte ZIP is
  `6a5761da11cd6cf7769b9e669d636474a55053b5799887760d6912570604190d`.

- Added `wordtoolkit.update_live_word_reference_tables/1.0` as native action 102.
  It updates existing Word `TablesOfContents`, `TablesOfFigures` and
  `TablesOfAuthorities` together or by exact kind/index, touches at most 128 objects,
  optionally repaginates, requires the live version and uses one non-replayable custom
  Undo record. Stable collection counts plus every reacquired field range are mandatory;
  mismatch requests one rollback. The response returns no field instruction, generated
  table text or raw COM object.
- Added five focused fake-COM regressions for all-kind refresh, exact index selection,
  no-target rejection, the 128-object ceiling, privacy and rollback after invalid native
  readback. The closed versioned action contract raises catalogue coverage to 102 input
  schemas/effect records and 15 explicit output/permission/reversibility/version records.
- Fixed a real saved-package reference-graph false positive: Word-generated `TA` fields
  identify their long citation through the `\l` switch rather than a positional operand.
  The new regression preserves a typed `IndexEntry` edge without emitting
  `FIELD_TARGET_MISSING` for valid Word syntax.
- At the preceding reference-table checkpoint, local gates passed 498 Engine, 351 Native
  and 1,309 Python tests with 16 intentional skips. Its installed build
  `0.39.0+codex.20260724100603` reports 102 actions, 15 exposed MCP tools and 15 explicit
  metadata contracts. Build output, personal source and enabled cache each contain the
  same 196 files with zero path/length/hash differences.
- The final installed runtime updated one native contents table, one table of figures and
  one table of authorities together in Word 16.0. Counts stayed 1/1/1, repagination and
  native range/field verification succeeded, the live version advanced once and the
  Microsoft Open XML SDK reported zero errors. Saved-package inspection found 15
  complete fields (`TOC` 2, `PAGEREF` 9, `SEQ` 2, `TA` 1, `TOA` 1), zero issues and no
  external or application-invoking fields. The four-page Word PDF shows populated
  contents, table and authorities lists with legible leaders/page numbers and no clipping
  or overlap.
- The 16,891-byte DOCX has SHA-256
  `d8e2bb820e821bcd77bbd9d800e786749260316face98259a51fc05aa5f83263`; the
  80,453-byte PDF has SHA-256
  `8bef6305acb5d5d4d51d8fb2e5b1381521ff86c38d4109a453ce0afb062ee141`. The
  36,663,305-byte self-contained ZIP has SHA-256
  `8a8fcdaf462a07f6bf5de1f4a2512d70ba522a002b822e5686a9813cf6ef9466`.

- Added `wordtoolkit.insert_live_word_caption/1.0` and
  `wordtoolkit.insert_live_word_table_of_figures/1.0` as native actions 100 and 101.
  Both require optimistic live-version control, use one non-replayable custom Word Undo
  record, verify native field/collection counts and automatically roll back on mismatch.
  Built-in caption labels are resolved by the installed Word language; custom labels
  must already exist. Neither action accepts raw field code or returns caption text.
- Seven focused fake-COM regressions pass for closed contracts, localized and custom
  native caption insertion, bounded label scans, response privacy, rollback and
  table-of-figures generation. Full local gates pass 497 Engine, 346 Native and 1,309
  Python tests with 16 intentional skips;
  Ruff and C# formatting checks pass.
- Installed build `0.39.0+codex.20260724093257` reports 101 actions, 15 exposed MCP
  tools and 14 explicit metadata contracts. Its build tree, personal source and enabled
  cache each contain the same 196 files with zero path/length/hash differences.
- A real Word 16.0.20131 proof created two native table captions and one updated native
  table of figures. Live and Microsoft Open XML SDK validation returned zero errors;
  saved-package inspection found two complete `SEQ`, one complete `TOC` and two nested
  `PAGEREF` fields with no issues. The one-page PDF has no clipping or overlap.
- The 36,657,762-byte ZIP has SHA-256
  `0f75b9cf43413080b2a9cc4765b52c3fbaf07ce24338d8044c2566c71c43943c`.

- Added guarded live SmartArt text preparation and apply as native actions 98 and 99.
  Preparation binds one-time node tokens to the exact Word story/collection locator,
  shape/range identity, layout/style/color IDs, complete bounded node structure and every
  node text hash. Apply requires the live version, consumes unique tokens, writes through
  Word in one custom Undo record and rolls back unless exact target readback, unchanged
  structure and unchanged untargeted text all hold. Exact no-ops create no Undo entry,
  repagination or version increment.
- Kept the claim narrow: single-line node text up to 4,096 characters is supported;
  node creation/deletion/reordering/hierarchy and layout/style/color mutation remain
  unsupported. The saved-package SmartArt graph remains read-only.
- Proved the installed path on a real five-node SmartArt document. Word changed both
  `word/diagrams/data1.xml` and the persisted `word/diagrams/drawing1.xml`; each moved
  from exactly one old-text occurrence to exactly one new-text occurrence. Microsoft
  Open XML SDK validation reports zero errors, and before/after Word PDFs retain the same
  five visual boxes with the new text unclipped.
- Two pinned .NET SDK 8.0.423 builds produced identical 196-file, 86,690,088-byte trees
  and 36,645,859-byte ZIPs at SHA-256
  `bb3ccf021e2135a1ca89c83920fcdd3c7aa73713936ac65db22febbe90096cf4`.
  Personal source and enabled cache are path/length/hash-identical; installed discovery
  reports 99 actions, 15 exposed tools and 12 explicit metadata contracts.
- Full gates pass 497 Engine, 339 Native and 1,309 Python tests, with 16 intentional
  Python skips. Ruff passes.

- Added a fail-closed saved-package formatter as native actions 96 and 97.
  `plan_ooxml_format` previews one explicit `remove_redundant_direct_formatting`
  policy; `apply_ooxml_format` rebuilds the exact output-bound plan and creates only a
  new same-extension package. Neither action opens Word or returns document text/XML,
  signed packages are blocked, and a stable no-op creates no file.
- Formatter candidates remove fully modeled scalar paragraph/run property elements
  whose direct cascade contribution equals the preceding resolved value. The same policy
  now covers `rFonts`, `color`, `u` and paragraph/run `shd` only after a bounded
  candidate-by-candidate package reparse proves complete group equivalence. Missing
  inherited theme/fallback members are retained; conditional table, revision and
  unmodeled cascade layers are skipped, and more than 64 composite proofs fail closed.
  Structural properties remain untouched. Every changed candidate must preserve OPC
  structure, semantic content, effective formatting on all affected nodes, exact changed
  parts and predicted fingerprint, then pass baseline-aware Open XML SDK validation.
  The engine retains an exact byte inverse and all scans/response pages are bounded.
- Final build `0.39.0+codex.20260724080018` passes 497 Engine, 334 Native and 1,309
  Python/OOXML tests with 16 intentional optional-environment skips. Two pinned .NET SDK
  8.0.423 builds produced identical 196-file, 86,623,437-byte trees and identical
  36,627,205-byte ZIPs at SHA-256
  `6bb2fce0a85bf61f03aeab320c68af985061bbcbff02e09b55299872f759a66f`.
  The enabled personal source/cache match the release tree exactly. The installed
  formatter removed 11 elements/330 bytes after five composite proofs, stabilized to a
  no-op, passed Microsoft SDK validation in Word and produced source/result 144-DPI page
  rasters that are byte-identical at SHA-256
  `2a882af2560fb684e55664c647e964ae3eebd98403292eaf07def3463895c966`.

- Added lazy `inspect_live_word_drawing_layout` as native action 95. The connected
  Microsoft Word build can repaginate and project bounded floating/inline objects,
  anchors, page/section placement, reference-aware positions, wrapping, group members
  and optional SmartArt semantic nodes plus their associated runtime shapes without
  returning COM or XML.
- Kept coordinate claims fail-closed. Alignment constants are distinct from point
  offsets; a page-relative box requires page/page references and numeric positions;
  group members use group-local coordinates; visible range positions and optional
  `Window.GetPoint` pixels are explicitly viewport-dependent and never page geometry.
  Root, group, SmartArt and diagnostic scans have independent hard ceilings.
- Drawing text is private by construction: names, titles, alternative text and SmartArt
  node text are not accessed unless `include_text=true`, then share one 4,096-character
  response budget. Three fake-COM regressions cover group/SmartArt/inline/floating
  projection, the viewport cost gate and zero sensitive getter access by default. A real
  Word proof over `lo_groupshape_sdt.docx` preserved the source SHA-256 and exposed the
  honest normalization boundary between two declared VML group nodes and one runtime
  group plus one `msoAutoShape`.

- Added a bounded declared shape model inside the existing figure graph. Stable `wdsh_`
  nodes preserve Wordprocessing group/canvas, DrawingML shape and VML group/shape
  topology; typed data covers transforms, recognized presets, custom path commands and
  formula points, fills, lines, known effects and text-flow declarations without
  executing geometry or Word layout.
- Added `FigureShape` dependency nodes with explicit representation-to-root and
  parent-to-child edges. Shape nodes, paths, commands, points and effects have hard
  source limits plus operation-wide resource charges; unknown enum-like tokens are
  diagnosed rather than trusted.
- Kept shape inspection token-lean. Declared representation output exposes only shape
  counters by default. `include_shape_details=true` requires a two-item declared
  representation page and caps output at 64 nodes; path commands/formula points still
  require the independent `include_geometry=true` opt-in and have separate response
  budgets. Shape names/text remain behind `include_text=true`.

- Extended the existing figure representation model instead of adding a duplicate
  layout graph. DrawingML anchors now type simple position, reference-frame alignment
  or offset, effect extents, relative size, wrapping side/distances and bounded tight/
  through polygons. Known VML position, size, z-order, percent, wrapping and visibility
  declarations and bounded VML `wrapcoords` polygons are normalized, including physical
  lengths to EMU.
- Extended lazy `inspect_ooxml_figures` with a declared-placement projection and a
  separate `include_geometry` opt-in capped at two response items and 128 polygon line
  points per item. Null placement fields are omitted. The response states that these values are declarations, never rendered page
  coordinates, and the package-only path still does not open Word or execute layout.
- Replaced the broad `drawingml_vml_advanced_layout` dependency-coverage omission with
  the precise `drawingml_vml_rendered_geometry_and_layout_execution` boundary. Valid
  Microsoft 365 anchor fixtures, malformed-coordinate diagnostics, polygon safety limits
  and compact native response gates cover the new contract.

- Added `WordDiagramGraph`, a bounded native SmartArt model for Transitional and Strict
  diagram data, points, connections, layout/style/color relationships and persisted
  drawings. Point text is counted and discarded; unsafe XML, malformed cardinality,
  duplicate IDs, missing endpoints and invalid orders fail closed or remain diagnosed.
- Added lazy `inspect_ooxml_diagrams` as native action 94 with compact paged views,
  exact diagram/point-type filters, independent key/hash/source opt-ins, one shared
  `wop1` budget and a bounded response projection. It never opens Word, executes Office
  layout, mutates the package, returns point text or exposes raw XML.
- Integrated SmartArt diagram/point nodes and definition/containment/connection/part
  edges into the unified dependency graph. The `smartart_diagrams` omission is removed;
  Office layout execution, rendering and mutation remain explicit boundaries.

- Preserve the plugin manifest build metadata in the packaged native runtime and expose
  the exact identity through `--version`, MCP initialization and capabilities.
- Require the OPC `dcterms:created` and `dcterms:modified` nodes to declare an
  `xsi:type` QName resolving to `dcterms:W3CDTF`; missing or rebound annotations now
  fail closed instead of entering the field-property index.

- Fixed native Word build-up for adjacent LaTeX factors after a structured base.
  The converter now emits an inert parsing boundary between forms such as
  `x^3e^{2x}`, `e^{2x}\sin(3x)`, a fraction followed by a variable, or a scripted
  factor followed by a delimited factor. Without that boundary Word could absorb the
  factors into one malformed function-name or superscript tree; the immediate OMML
  verifier then correctly rejected the changed structure.
- OMML readback no longer adds a second redundant wrapper around an already delimited
  superscript or subscript base. A gated real-Word regression now builds and reads back
  the complete eight-row complex-method derivation of
  `\int x^3e^{2x}\sin(3x)\,dx`: all six native n-ary integrals and all six integral-
  owned differentials survive with identical canonical contracts. The differential,
  symbol and structural gates remain enabled; the fix does not weaken verification.

- Added `WordDocumentPropertyGraph`, a bounded source-linked model for OPC core,
  Office extended and typed custom properties. Exact relationships/content types,
  Transitional/Strict namespaces, custom `pid`/`fmtid`, duplicate names/IDs and scalar
  lexical forms are validated. Complex/binary values are classified without decoding,
  and malformed or ambiguous properties cannot enter the field-resolution index.
- Added lazy `inspect_ooxml_properties` as native action 93 with summary/property/part/
  issue views, exact filters and independent custom-name/value/hash/source opt-ins. Raw
  XML, complex values and field results are unavailable; Word is never opened and the
  package is never mutated. One shared `wop1` lease and a 32 KiB projected-item ceiling
  bound the operation.
- Added document-property and persistent-document-variable nodes to the unified
  dependency graph. Valid unique `DOCPROPERTY` and `DOCVARIABLE` reads resolve to their
  concrete sources; `SET`/`ASK`, duplicate sources and invalid lexical values remain
  unresolved. The nine-producer semantic golden corpus now records the new typed
  definition edges.

- Added a bounded, read-only active-content metadata graph for legacy/ISO OLE
  declarations, linked/embedded targets, ActiveX XML/binary bindings, embedded-package
  payloads, VBA/support/customization parts, VBA project signatures and OPC package
  signature topology. Exact relationship namespaces prevent suffix-spoofed types from
  entering the graph; orphan declarations, duplicate IDs, target contradictions and
  malformed ActiveX XML fail closed.
- Added lazy `inspect_ooxml_active_content` as native action 92 with compact paged views,
  exact filters, independent name/target/hash/source opt-ins and a shared `wop1` resource
  lease. Raw XML, field codes, binary values, ActiveX licenses and property values are
  never returned. Word, embedded packages and external targets are never opened; no
  macro/control execution or cryptographic signature-validation claim is possible.
- Added active-content payload/declaration/ActiveX nodes and typed edges to the unified
  dependency graph. Binary internals/execution, signature validation/resigning and
  encrypted packages remain explicit coverage gaps rather than being hidden behind a
  generic "macros/signatures" label.
- Verified the active-content checkpoint with 457 Engine tests, 313 Native tests and
  1309 Python/OOXML tests with 16 intentional environment/model skips. Release builds
  have zero warnings, Ruff is clean and mypy passes all 29 maintained Python modules.
  Complete dependency JSON-RPC output remains below its 8,000-character gate by
  omitting only zero per-edge-kind counters; nonzero counters and all fixed execution-
  safety assertions remain explicit.

- Removed automatic replay of mutating Word COM delegates after disconnect. COM work is
  now non-replayable by default: only explicitly proven read-only or idempotent calls
  may reconnect once. A disconnected non-replayable operation returns non-retryable
  `WORD_OPERATION_OUTCOME_UNKNOWN`, and further non-replayable work remains blocked
  until runtime restart, reconnect and inspection instead of risking a duplicate edit
  against a stale live version.
- A cancellation observed after a mutating COM call begins now reports the same unknown-
  outcome state rather than claiming that Word cancelled the mutation. Recovery gating
  remains active until every abandoned synchronous call returns. A queued request's
  cancellation can no longer clear recovery owned by an earlier executing call, and
  late COM completion cannot strand the host in recovery. Cancellation is registered
  at submission time, so an already queued mutation cannot slip through while the
  caller's cancellation continuation is still pending. The STA queue also waits for the
  client-side result/error/cancellation decision before starting its next item, closing
  callback scheduling races rather than merely making them less likely.
- Bounded `IOleMessageFilter` busy-call retry to 30 seconds at 100 ms intervals. Added
  twelve Windows host regressions covering no mutation replay, one replay-safe reconnect,
  cancellation/completion races, cancellation before replay, sticky unknown outcome,
  recovery ownership, `Document.Compare` policy and the hard OLE retry budget.
- Removed `compare*` from the generic object-model read-prefix policy. Word's mutating
  `Document.Compare` is now blocked instead of being mislabeled as a read. Generic
  object-model execution remains non-replayable even when its current batch contains
  only policy-approved reads.

- Added a bounded, read-only bibliography graph for the Open XML 2006 and legacy Word
  2004/10 `Sources` formats. Collections, stable sources, typed identity/type/locale
  fields, scalar metadata, people and corporate contributors retain package provenance;
  duplicate tags remain ambiguous. Unique case-insensitive `CITATION` tags now terminate
  at concrete bibliography source nodes in the unified dependency graph.
- Added lazy `inspect_ooxml_bibliography` as the 91st native action with paged
  summary/collection/source/field/contributor/citation/issue views. Sensitive values and
  source locations are opt-in; the operation never opens Word, evaluates fields,
  executes bibliography XSLT or follows external targets. Its OPC, semantic, reference
  and bibliography phases share the 640 MiB `wop1` budget. People and corporate-name
  ceilings are enforced across all roles in one source. A separate 64 KiB projected-
  payload budget bounds paged results and diagnostics; truncation returns a continuation
  offset. Redacted value fingerprints are process-keyed HMAC tokens rather than public
  unsalted hashes. Unique unmodeled element names are capped at 256 per source and
  charged to the aggregate metadata budget before display-string materialization.
- Duplicate singleton Tag, Guid and SourceType fields now fail closed instead of using
  the first value for identity or citation resolution. Runtime `source_id` validation
  now matches the published schema before OPC parsing.
- Added ten Engine and seven Native bibliography regressions, golden dependency-corpus
  updates and a reproducible 10,000-source Release benchmark. The benchmark resolves all
  10,000 citations with zero issues and preserves the package fingerprint; seven graph
  samples measure 642.5681 ms median, 811.9211 ms p95/max and 245,631,072 median
  thread-allocated bytes on the disclosed local Windows x64/.NET 8.0.29 host. The graph
  accounts 81,230,184/671,088,640 operation bytes; the raw result is checked in. No
  before/after speedup is claimed because the prior HEAD had no bibliography graph.

- Added remote `apply_document_operations`, a provider-neutral
  `wordtoolkit.apply_document_operations/1.0` command for 1-16 ordered operations from
  the complete 33-tool ordinary draft-mutation surface. The server validates every
  nested argument against the corresponding standalone Pydantic contract, rejects
  read-only hybrid actions, enforces a 1 MiB aggregate argument ceiling, runs the whole
  list on one isolated engine clone, validates the final candidate once and advances
  `draft_version` once. Its generated Draft 2020-12 schema is a closed 33-variant
  `oneOf`; arbitrary engine method dispatch and result-reference syntax are absent.
  The same exporter now emits `schemas/draft-operations.v1.json` with the input,
  success-data and error schemas, permissions, limits, side effects and validated
  examples so non-MCP clients do not need to reverse-engineer the adapter.
- Batch failures carry a bounded operation index/name/cause and discard every earlier
  clone mutation. Results preserve order but project only compact IDs/counts/status so
  inserted document content is not echoed back into the model context. Caller
  cancellation drains image staging and the transaction before releasing the document
  lock. Because Apps SDK file parameters support only top-level fields, image calls use
  the top-level `files` array declared through `openai/fileParams` and a bounded
  `file_index` inside `insert_image`; nested file objects are rejected. Downloads remove
  partial files on error or cancellation. An optional missing `file_name` is handled
  through an allowlisted extension from the final URL, declared MIME type or response
  content type rather than silently treating the payload as `.bin`. A fully staged
  upload can still remain when a later operation fails, so that non-document
  session-quota side effect is stated in the
  public tool description and tested. The compact success payload no longer repeats the
  operation contract, backend, atomicity, count or requested operation names; a
  representative one-step envelope fell from 290 to 102 compact JSON characters.
- Recorded 15 Windows x64 in-process FastMCP samples for
  `format_paragraph -> enable_track_changes -> insert_paragraph`: three standalone COW
  calls measured 189.479 ms median versus 70.901 ms for one batch (-62.58%, -118.578
  ms). The batch also reduced the measured compact request JSON from 480 to 427
  characters (-11.04%), removed two MCP round trips and used one commit/version advance
  instead of three. Document creation/close and the optional SDK validator were outside
  this measurement. The closed 33-variant schema is not free: after removing redundant
  nested titles and envelopes it occupies 19,088 compact JSON characters and raises the
  complete 66-tool catalog from 57,566 to 77,439 characters (+34.52%). A contract test
  caps that schema below 20,000; no whole-catalog token reduction is claimed.
- Made all 33 ordinary remote draft mutators copy-on-write transactions. Each request
  now snapshots the locked active engine, applies the complete operation to an isolated
  clone, serializes and structurally validates the candidate, and swaps the active
  engine plus `draft_version` only after every gate succeeds. An operation that mutates
  and then raises, or a candidate rejected by validation, leaves the active engine,
  package bytes and version unchanged. Validation failures expose only bounded counts
  and issue codes, never validator messages, part names or document content.
- Preserved cancellation truth at the stronger boundary: a cancelled caller cannot
  release the document lock while the clone worker is alive; a fully successful and
  validated post-cancellation mutation is committed and advances the version, while a
  failed post-cancellation attempt is discarded. Transaction snapshots and abandoned
  clones are drained and removed before the lock is released.
- Added partial-mutation rollback, candidate-validation rejection, redaction and engine
  identity regressions. Sequential mutation -> mutation -> save -> close coverage proves
  that the one active clone workspace survives publication and replaced workspaces are
  removed. Replaced-engine cleanup runs in a drained background worker, so a large
  workspace cannot block the event loop or escape cleanup when the caller cancels.
- Fixed a Windows snapshot defect that compared backslash filesystem paths with canonical
  POSIX OPC part names. The broken comparison could omit modified XML while still
  producing a structurally valid, stale package; snapshots now serialize every marked
  part with canonical ZIP names.
- Recorded a 15-sample local Windows x64 cost measurement for one paragraph insertion:
  direct in-memory mutation measured 0.160 ms median, while snapshot + clone open +
  mutation + candidate snapshot + structural validation measured 56.319 ms median. The
  absolute 56.159 ms / 351.99x median cost is an explicit correctness tradeoff. This
  candidate-preparation point excludes engine creation, commit swap, replaced-workspace
  cleanup and MCP dispatch; the optional Microsoft SDK validator was unavailable.

- Raised the legacy remote Python service to the unified 0.40.0 development line and
  published remote MCP schema v2.
  Every edit, save, repair, render, preview and close of an existing draft now requires a
  non-negative `expected_version`; stale calls fail under the same document lock before
  document-engine mutation or output/artifact publication. DOCX export enforces the
  version conditionally while
  read-only Markdown export remains version-neutral.
- Remote save, repair and render now execute on an isolated copy-on-write engine. The
  active engine, `draft_version`, current path and artifact inventory change only after
  validation and all-or-nothing artifact registration succeed. Failed saves/renders
  discard their clone and attempt outputs. Document/session close races recheck identity
  after lock acquisition, and session shutdown cannot close an engine in active use.
  Cancelled background engine calls are drained before the document lock is released; a
  mutation that completes after caller cancellation still advances the draft version.
- Added the v1-to-v2 migration, required-field schema tests, stale writer/save tests,
  failed save/render rollback tests, close-race coverage and atomic multi-artifact failure
  coverage, repeated-cancellation tests and version-stable concurrent snapshot tests.
  Remote example clients now carry the returned version through publication and close.
- Recorded a nine-sample copy-on-write DOCX publication benchmark: median direct save
  17.935 ms versus 63.224 ms for snapshot, clone open and validated save. The measured
  45.289 ms cost is reported as a correctness tradeoff, not hidden as a speed win.

- Added the vendor-neutral `word_operation_accounted_v1` lease and wired one instance
  through the complete saved-package dependency-inspection pipeline: OPC retention,
  lossless XML parsing, semantic nodes/fingerprint caches, styles, numbering, references,
  sections/settings, charts, figures/captions, content controls, tables and final graph.
  The calibrated production ceiling is 640 MiB; MCP exposes only
  `operation_budget: {model, used, maximum}` under compact alias `wop1`.
- ZIP central-directory count/size preflight now runs before `ZipArchive.Entries` is
  materialized. Non-seekable public streams are copied through a 576 MiB bounded,
  delete-on-close temporary spool so they cannot bypass that preflight. Package-entry
  and XML charges reject before guarded byte copies; OPC
  content-type, part, relationship and diagnostic records are count-bounded and charged.
  Semantic hash recursion and metadata XML loading observe cancellation, while
  table/figure/content-control aggregate byte ceilings reject the next part before
  parsing it. Operation exhaustion maps to a typed, bounded `PACKAGE_LIMIT`
  stage/attempted-charge response without paths or document data.
- Case-insensitive ZIP-name collision diagnostics now return only a bounded spelling
  count instead of joining every attacker-controlled name into one large string.
- Kept the existing `wdg1` graph budget and dependency input schema byte-for-byte
  compatible. Chart/table limit and projection failures now map to `PACKAGE_LIMIT` and
  `INVALID_WORD_PACKAGE` instead of falling through to `IO_ERROR`. Empty
  `issues_truncated=false` noise is omitted from compact responses; the complete default
  dependency JSON-RPC envelope remains below 8,000 characters.
- Calibrated the previous 99,997-node graph point at 539,282,576 operation-accounted
  bytes. A 512 MiB candidate failed at 536,870,624 bytes, while 640 MiB preserves the
  admitted boundary with 19.7% accounted headroom. The model remains a cumulative stable
  proxy, not an exact CLR heap, live-set or resident-memory claim.
- Raised the development runtime/plugin line to 0.39.0 without increasing the 90-action
  catalogue.
- Two pinned .NET SDK 8.0.423 builds produced byte-identical 196-file,
  85,759,136-byte trees and 36,380,971-byte ZIPs at SHA-256
  `2875209550cca57b36d0e99873d443c337f89950e5230d3083110398da0d7468`.
  The package contains no Python files. The enabled personal-plugin cache at
  `0.39.0+codex.20260723142922` is an exact path/length/hash copy; its runtime reports
  0.39.0 and 90 actions. A packaged dependency smoke call returned both `wop1` and
  `wdg1` while leaving the checked-in DOCX byte-identical.

- Replaced the unified dependency graph's two eager per-node adjacency dictionaries
  with compact incoming/outgoing offset and edge-index arrays. Direct adjacency views
  retain ordering without allocating one list per node, endpoint validation stays
  fail-closed and construction now observes cancellation through every large index pass.
- Added the deterministic `dependency_graph_accounted_v1` resource model with a 128 MiB
  production default, 65,536-character per-key/per-metadata ceilings and pre-retention
  rejection for nodes, edges and issues. Engine callers receive the complete resource
  record; the default MCP response pays only for `byte_budget: {model, used, maximum}`.
  The budget covers this graph, not upstream semantic and typed projections.
- Added exact one-byte-below-budget rejection, compact-adjacency ordering, option,
  cancellation-compatible and MCP token-envelope regression coverage. Five cold-process
  99,997-node Windows x64 samples per version reduced median retained managed memory from
  234,933,120 to 195,381,520 bytes (16.8%), median managed allocations by 4.2% and median
  dependency build time by 9.0%. Median peak working set was effectively flat (+0.1%),
  so no peak-memory win is claimed. Raw before/after series and primary .NET design
  evidence are checked in.
- Closed a typed-client contract gap by requiring the dependency action schema to cover
  every Engine node and edge kind. Dependency diagnostics are now an explicit
  `include_issues=true` opt-in outside `view=issues`; the default compact response no
  longer spends tokens on a diagnostic array.
- Raised the development runtime/plugin line to 0.38.0. Capability discovery remains at
  90 actions; this release hardens the existing dependency action rather than inflating
  the action count.
- Two pinned-SDK 8.0.423 package builds produced byte-identical 196-file,
  85,730,528-byte trees and 36,372,963-byte ZIPs at SHA-256
  `69c3406b238590dae096370a550ff6902352972cbe942b2344e2d80dca1e0541`.
  The personal marketplace and enabled cache match that final tree exactly. The installed
  runtime reports 0.38.0/90 actions and returns the dependency byte budget without
  opening Word or following external targets.

- Added the bounded, source-linked `WordFigureCaptionGraph` and lazy
  `inspect_ooxml_figures` action. Transitional/Strict inline and anchored DrawingML,
  VML and legacy-object representations now form stable logical figures with typed
  placement, accessibility metadata and inert image/chart/diagram/content-part/OLE
  resource relationships. `mc:AlternateContent` branches collapse into one logical
  object while the public selection basis states that Choice is present but not
  MCE-evaluated; no active or primary representation is invented. External targets and
  embedded resources are never opened.
- Added source-linked caption-style/`SEQ` candidates and a documented association
  policy that selects only mutual unique-best evidence within the same story/container.
  Ties stay ambiguous, weaker alternatives stay candidates and deleted/move-from
  evidence is never selected. Figure, representation, resource, caption and association
  objects now enter the shared dependency graph without upgrading non-selected
  candidates to resolved edges.
- Added independent text/source/relationship-target opt-ins, paging and default/gateway
  payload caps for figure inspection, plus hostile relationship, metadata, revision,
  ambiguity, Strict OOXML and fingerprint tests. A repeated 10,000-figure benchmark
  exposed and removed quadratic caption, ambiguity and field scans; the checked-in
  seven-sample 0.37.0 run with 10,000 distinct relationship IDs reports a 1,897.6 ms
  median and 2,084.6 ms p95 figure-graph build on its recorded host.
- Raised the development runtime/plugin line to 0.37.0 and capability discovery to 90
  lazy/core actions. Figure/caption behavior, research sources, competitor differentials,
  limits and honest exclusions are documented separately.
- Two pinned-SDK 8.0.423 package builds produced byte-identical 196-file,
  85,719,250-byte trees and 36,369,889-byte ZIPs at SHA-256
  `addb0ac796b9c5e41e6174f2bd1937d9fe996ae88015a81af5b000968caa63c7`.
  The enabled personal-plugin cache is an exact file/hash copy of that 0.37.0 package;
  its executable reports 90 actions and the Figure/Caption action through the CLI.
- Added `wordtoolkit.render_ooxml_semantic_svg/1.0` as the seventh public
  transport-neutral Engine/CLI/MCP operation and the second implementation of a shared
  native semantic-rendering backend contract. It requires both an exact semantic node
  ID and the inspected package fingerprint, creates only a new self-contained `.svg`,
  emits real selectable SVG text with `title`, `desc` and ARIA structure, estimates
  non-paginated flow/table geometry, and reports target subtree identity, backend,
  media type, layout basis, text mode, canvas, fidelity and degradation metadata. The
  static profile has no script, event handlers, `foreignObject`, active links, external
  resources or font loading; field instructions remain suppressed. Standalone drawing,
  marker and extension roots fail closed. Rendering is bounded before XML materialization
  by 40,000 text lines, 100,000 generated SVG elements, a 1,000,000-pixel canvas dimension
  and a 256 MiB artifact ceiling. The shared render path policy rejects UNC and Windows
  device namespaces before any filesystem probe, preserving the declared no-network
  boundary instead of risking implicit SMB access. The contract explicitly fixes `paginated`,
  `exact_text_metrics` and `pixel_equivalence_claimed` to false rather than laundering
  estimated geometry into a Word-fidelity claim. `render-package --backend semantic-svg`
  and the lazy MCP action call the same Engine implementation; omitting `--backend`
  preserves the historical HTML CLI path.
- Verified the final SVG slice with 408 Engine tests, 272 Native tests and the complete
  1,279-test Python/OOXML suite with 16 intentional skips; Ruff and the maintained
  28-module mypy lane are clean. Two supported Windows builds produced identical
  196-file, 85,514,480-byte trees and identical 36,306,172-byte ZIPs at SHA-256
  `5076fc4b41670ae05752ab36c0d23ff66fac7a2b8d753706b15473a478e4682a`; the expanded
  manifest and enabled personal-plugin cache matched at SHA-256
  `f9bfd22a28d04e509968f3fb799e2e283e9ff8e3ccabdb14fdbc7503c1bc7207`. Hosted run
  `29992366785` passed all five jobs for commit `d6b663922a45f8e271872f61a63dffa86bf21256`;
  artifact `8557592140` matched both pinned-SDK local ZIPs and expanded trees exactly. An
  initial .NET SDK 10 package was rejected as release evidence after its self-contained
  runtime differed from the repository-pinned .NET SDK 8.0.423 output.
- Added an explicit `workflow_dispatch` trigger to the existing five-job CI workflow so a
  named branch SHA can be verified when a pull-request webhook is not scheduled. Normal
  pull-request and release triggers remain unchanged.
- Added `wordtoolkit.render_ooxml_semantic_html/1.0` as the sixth public
  transport-neutral Engine/CLI/MCP operation. The dependency-free Engine creates a
  deterministic, self-contained HTML artifact from a saved DOCX/DOCM/DOTX/DOTM without
  Word, LibreOffice, Python, network access, external-resource loading or active-content
  execution. It renders the main body or all projected text stories, infers headings from
  modeled outline styles, preserves tables and cached field results, keeps hyperlinks
  inert, annotates tracked insertions/deletions/moves, emits bounded linear-text equation
  fallbacks and makes unsupported drawings/extensions visible as placeholders. The
  result contract calls the artifact `semantic_preview_non_paginated`; it makes no claim
  of Word layout, typography, pagination or print fidelity.
- Extended the same 1.0 renderer additively with fingerprint-bound `target_node_id`
  selection. One semantic table, row, cell, paragraph, equation, drawing, revision or
  other renderable subtree can now be emitted without its siblings. Missing, stale,
  out-of-scope and non-renderable targets fail closed before output creation. Selected
  rows, cells and semantic wrappers receive valid synthetic `table`/`tbody`/`tr`
  context, reported separately as `fragment_wrapper`. The selection-aware traversal
  applies the same normalization to nested tables, nested row/cell SDTs and row-level
  revisions anywhere below the selected target; pure wrapper chains are flattened with
  a warning and ambiguous mixed chains fail closed. Response metadata still returns no
  document text, raw XML, properties or source paths.
- Added the strict `render-package --request <json|->` CLI and lazy MCP adapter over the
  same renderer. The write is create-new and sibling-temporary, flushed before an atomic
  move, never overwrites an output, can bind an inspected package fingerprint, leaves the
  source byte-identical, and returns artifact hashes/counts rather than document text or
  XML in the response. The result explicitly marks that the local HTML artifact itself
  contains document content. Capability discovery now covers 89 lazy/core actions and reports seven actions with
  complete operation-version, permission, reversibility and output-schema metadata.
- Hardened the HTML boundary with output-name and 256 MiB artifact limits, a restrictive
  inline Content Security Policy, HTML escaping of every package-derived value, adaptive
  inline/block/table-row containers, inert hyperlinks, suppressed field instructions and
  an atomic create-new race test. Compact MCP responses and full gateway responses now
  both conform to the same closed output schema; telemetry is optional because the
  compact gateway intentionally removes it. Package-derived exception messages are never
  exposed as public reasons, so hostile ZIP entry names cannot escape through CLI or MCP
  errors; Engine and adapter regressions bind this privacy boundary.
- Verified the fingerprint-bound selection checkpoint with 396 Engine tests, 269 Native
  tests and the complete 1,276-test Python/OOXML suite with 16 intentional skips; Ruff and the
  maintained 28-module mypy lane are clean. Two supported Windows builds produced
  identical 196-file, 85,442,968-byte trees and identical 36,285,575-byte ZIPs at
  SHA-256 `a0737809d981cd739cade8f4036818408877afd903ff1049d538f29b4b99fb41`;
  expanded manifests matched at SHA-256
  `a45a778680bf0c9773089556e8a88104d155ebbf044bd852a24f20155d0596e4`.
  The manifest is the UTF-8/LF, no-final-newline serialization of relative path,
  byte length and lowercase SHA-256 fields separated by tabs and sorted by path.
  The packaged executable discovered the lazy action and rendered the checked-in Mammoth
  fixture to one deterministic 3,628-byte artifact across 15 cold processes at 254.66 ms
  median and 290.44 ms p95/max. Static HTML-tree, CSP and external-resource checks passed;
  the in-app browser blocked the local `file://` URL, so no visual-browser result is
  claimed and no policy bypass was attempted. The new packaged selection smoke rendered
  the fingerprint-bound Mammoth table to a 3,742-byte artifact with one `table`, one
  `tbody`, two `tr` and four cells under an HTML5 parser, zero invalid table ancestors,
  no source mutation, no Word process and no fixture cell text in the JSON response.
  Hosted CI run `29988472344` passed all five jobs for commit
  `dc72a8e811217bb9515b0ad30810c8925b3081cb`; downloaded Windows artifact
  `8556049406` matched both local ZIPs byte for byte and its normalized 196-file tree had
  zero length/hash differences at the recorded manifest SHA-256.

- Added the public comment-body operation pair
  `wordtoolkit.plan_ooxml_comment_body_edits/1.0` and
  `wordtoolkit.apply_ooxml_comment_body_edits/1.0`. The dependency-free Engine selects
  comments by stable semantic ID, matches exact bounded text across Word runs, binds match
  counts and optional body hashes into a deterministic reviewed plan, validates the exact
  candidate and atomically applies it through direct .NET, `comment-body-package` JSON CLI
  and lazy MCP without opening Word. Responses expose hashes and counts rather than comment
  text or XML, and candidate reprojection proves that anchors, authors, thread topology,
  durable IDs, reactions, revisions, permissions, unselected comments and unrelated parts
  remain invariant. Signatures, plan drift, unexpected matches and new schema errors fail
  closed; a recovery backup is retained by default.
- Comment matching is boundary-aware rather than a flattened descendant-text search.
  Ordinary `w:t` leaves may compose one match across adjacent runs in the same direct
  comment paragraph, including runs with formatting, but paragraph/table-cell boundaries,
  tabs, breaks, fields, content controls and other rich structures split or exclude the
  editable segment. Regression fixtures prove that none of those rendered or structural
  boundaries can be silently crossed.

- Verified the comment-body checkpoint with 388 Engine tests, 263 Native tests and the
  complete 1,273-test Python/OOXML suite with 16 intentional skips; Ruff and the
  maintained 28-module mypy lane are clean. Packaged plan/apply on the checked-in
  `mammoth_comments.docx` fixture reproduced the reviewed plan ID and predicted result
  fingerprint, changed only `word/comments.xml`, passed the bounded Microsoft schema
  comparison, retained a byte-exact backup and left the source fixture and Word-process
  set unchanged. Neither response returned comment text or raw XML.
- Built the checkpoint twice through the supported Windows PowerShell path. Both builds
  produced identical 196-file, 85,366,594-byte trees and identical 36,263,241-byte ZIPs
  at SHA-256 `267ace7127d82c8f54c32f56682830770d6209f1880b4f00d38a31dc45aa5503`;
  expanded manifests matched at SHA-256
  `25fd2c4afcfef292c95c5925d849cee55b5de1297dbbf0b05734e9edd9231373`.
  Fifteen cold packaged planner processes returned the same 1,027-character response at
  669.53 ms median and 688.29 ms p95/max without mutating the source or starting Word.
  No historical before-change comment-body CLI latency baseline exists. Hosted CI run
  `29980881560` passed all five jobs; its downloaded Windows ZIP matched both local
  archives byte for byte and its expanded 196-file tree had zero differences.

- Added the public semantic-style operation pair
  `wordtoolkit.plan_ooxml_semantic_edits/1.0` and
  `wordtoolkit.apply_ooxml_semantic_edits/1.0`. The dependency-free Engine now owns the
  seven-command typed contract, strict variant-aware JSON parser, bounded selector
  resolution, deterministic intent-bound plan ID, exact candidate projection and atomic
  apply used by direct .NET callers, `style-package` JSON CLI and the existing lazy MCP
  actions. Apply rebuilds the plan against the current package, rejects stale fingerprints,
  plan drift and signed packages, keeps a recovery backup by default and never opens Word.
  Requests are capped at 256 Ki characters, resolved edits and changed parts at 200 each,
  and schema issue details are returned only when explicitly requested.
- Added the neutral `IWordPackageCandidateValidator` boundary and the separate
  `WordToolkit.OpenXmlSdk` adapter. The Engine remains independent of Microsoft and every
  AI provider; plan reports an absent validator and apply fails closed with
  `VALIDATOR_REQUIRED`. The standard adapter preserves the previous bounded
  baseline-versus-candidate Microsoft 365 schema check and is now also reused by the
  older Native package mutation paths instead of duplicating validation logic.
- Hardened `OpcAtomicPackageWriter` against a non-cooperative last-millisecond writer.
  After atomic replacement it verifies that the displaced package is the reviewed
  fingerprint; on mismatch it restores those exact displaced bytes and returns
  `VERSION_CONFLICT`. A second destination change during compensation is never silently
  deleted: it is retained as an opaque sibling `.conflict` artifact and produces
  `RECOVERY_REQUIRED`. Failed compensation reports only names of artifacts that still
  exist, never an absolute path or document content, and claims no artifact when none was
  retained.
  Validator exceptions are now treated as untrusted and never copied into public error
  text, while `OOXML_SCHEMA_INVALID` retains its bounded count/issue diagnostics for
  existing MCP clients.
- Added operation versions, explicit filesystem/network/Word permissions, reversibility
  records and closed successful MCP output schemas for semantic-style plan/apply. The
  actual compact plan and apply envelopes validate against Draft 2020-12, while capability
  v1 keeps its existing closed summary shape and reports five metadata-complete actions.
- Verified this semantic-style interoperability checkpoint with 385 Engine tests, 260
  Native tests and the full 1,273-test Python/OOXML suite with 16 intentional skips;
  Ruff and the maintained 28-module mypy lane are clean. The packaged executable planned
  the checked-in real-DOCX request in a 1,811-character response, left the source bytes
  and zero Word-process set unchanged, and completed 15 cold process runs at 747.58 ms
  median and 779.22 ms p95. No historical before-change public style-CLI latency baseline
  exists.
- Ran packaged plan/apply against a disposable copy of the checked-in LibreOffice DOCX.
  Apply reproduced the reviewed plan ID and predicted semantic fingerprint, changed only
  `word/styles.xml`, passed Microsoft schema comparison, retained a byte-exact backup,
  returned no XML, left the original fixture byte-identical and did not start Word.
- Built the checkpoint twice through the supported Windows PowerShell path. Both builds
  produced identical 196-file, 85,271,141-byte trees and identical 36,238,851-byte ZIPs
  at SHA-256 `9a3fc1ae866ca9d5e1f9c1f40b21683491175b8e63125511f4dd6529bff6596e`;
  expanded manifests also matched exactly at SHA-256
  `cd728f01ea4da97e303a1ad488ebd79d64b3b21113bc80aee8438cc29d074104`.
  `WordToolkit.OpenXmlSdk.dll` is present and the package contains zero Python files.
  Mandatory CI run `29977852683` passed all five jobs. Its downloaded 36,238,851-byte
  Windows distributable matched both local ZIPs byte for byte at the same SHA-256.
- Added `wordtoolkit.query_ooxml_semantics/1.0` as the third public
  transport-neutral Engine/CLI/MCP operation. Direct saved-package and process-memory
  indexed queries now share one typed result builder and canonical JSON instead of an
  adapter-owned anonymous response. Stable node IDs are joined by high-level object
  category, story, child count and identity fields; local reads can require the exact
  package fingerprint. Properties remain optional and author/name/date/GUID/field
  instruction/anchor values require a second explicit sensitive-data flag. The operation
  also suppresses complex-field instructions from text previews until that second flag
  is present, reports property shortening explicitly, never returns raw XML, follows
  external relationships or opens Word, and a new
  `query-package --request <json|->` CLI accepts the same closed flat query shape.
- Made semantic query the first action with an explicit operation version, closed MCP
  lazy-action successful structured-content output schema, normalized
  filesystem/network/Word permissions and reversibility record. Capability v1 pages keep
  their existing closed operation-summary shape and count this metadata without adding
  incompatible fields; the exact values and full output schema remain available through
  `inspect_wordtoolkit_action`, while the lazy action stays under a 10,000-character
  contract budget.
  The public JSON codec now writes enums as non-numeric `snake_case` strings; request
  boundaries reject unknown members while result decoding retains additive v1 forward
  compatibility.
- Verified the semantic-query slice with 375 engine tests, 257 native-host tests and the
  complete 1,273 Python/OOXML tests with 16 intentional skips. The checked-in request
  produced five equation-containing paragraphs, a 3,195-byte compact result conforming
  to the published Draft 2020-12 output schema; the complete action contract is 9,461
  bytes, below its 10,000-byte budget. No raw XML or Word process appeared. Fifteen cold
  CLI runs measured 197.76 ms median and 206.68 ms p95 on the development machine; no
  historical before-change latency baseline exists.
- Built the semantic-query checkpoint twice through the supported Windows PowerShell
  package path. Both builds produced identical 195-file, 85,195,358-byte trees and
  36,214,740-byte ZIPs at SHA-256
  `04af23655f262389c16a9b8eb94cc08d39173c5a9d80e74606929d26f9beb9e9`.
  The packaged self-contained executable returned the same five schema-valid matches,
  discovered the query action, wrote nothing to stderr and left the Word-process count
  at zero. Mandatory CI run `29973644971` passed all five jobs; its downloaded Windows
  ZIP matched both local archives byte for byte at the same size and SHA-256.
- Added `wordtoolkit.transform_ooxml_package/1.0` as the second public
  transport-neutral Engine/CLI/MCP operation. Its typed core can replace the first
  ordinary text occurrence across run boundaries, accept all supported tracked changes
  or reject them without opening Word. It preserves untouched entries and opaque bytes,
  excludes OfficeMath from text matching, refuses MCE/revision ambiguity, blocks signed
  packages and output collisions, validates the complete candidate before atomic write
  and returns one canonical result through SDK, `transform-package` CLI and MCP. Deleted
  paragraph-mark acceptance and inserted paragraph-mark rejection now merge only a
  proven-safe immediate following paragraph; paragraph properties or following revision
  content make the shape unsupported rather than guessed.
- Added a direct protocol-v1 `docx-platform-tests` adapter and pinned the neutral harness
  at `fe0ee996...` against safe-docx `3615e213...`. Across the same 42 hidden-assertion
  scenarios, WordToolkit recorded 19 pass, 2 invariant-pass and 21 honest unsupported;
  safe-docx recorded 18, 2 and 22. Both produced zero failures, errors, divergent passes
  or protocol mismatches. Exact commits, environment, commands, caveats and the raw
  74,657-byte result at SHA-256
  `e0103e86940d285027494fd86a7916007943cda31ccad68a52fcde858df324dd` are checked in.
- Expanded the research matrix from eight to twelve pinned AI/Word repositories, split
  Microsoft Copilot's public service APIs from the preview host-bound Office API plugin,
  and added DevExpress, Telerik, TX Text Control, GroupDocs, Google Docs and Adobe PDF
  Services. The audit still refuses a global leadership claim: the neutral corpus is
  narrow and does not yet measure Word layout, preservation, tokens, latency or hostile
  package security.
- Verified the preceding transform/benchmark slice with 366 engine tests, 247 native-host
  tests and the complete 1,273 Python/OOXML tests with 16 intentional skips. Focused
  transform/protocol parity tests
  cover cross-run replacement, first-only behavior, OfficeMath exclusion, MCE rejection,
  accept/reject-all, clean no-op clone, signatures, output collisions, unsafe-input
  decline, exact revision inverse and no Word invocation. Two local self-contained builds
  produced identical 195-file, 85,150,302-byte trees and 36,200,235-byte ZIPs at SHA-256
  `e1ef21c763cae801f48bcfa43d1513ff279936a8fd026e00b28a04133755afe8`.
  The package contains zero Python files; its CLI reports 85 actions and 15 exposed MCP
  tools, finds `transform_ooxml_package`, and changes neither the zero Word-process count
  nor document state during discovery/help probes. Mandatory CI run `29970012359`
  passed all five jobs; its downloaded Windows ZIP matched both local archives byte for
  byte at the same size and SHA-256.
- Added the first public transport-neutral operation,
  `wordtoolkit.inspect_ooxml_package/1.0`, to `WordToolkit.Engine`. Typed file and
  seekable-stream requests, results and stable errors now feed the .NET SDK surface,
  `wordtoolkit-native inspect-package` and the existing MCP action through one bounded
  implementation. A public canonical JSON codec gives SDK, CLI and compact MCP data the
  same `snake_case` shape and null policy; legacy MCP runtime/timing fields remain only
  at the adapter edge. The CLI emits success JSON on stdout, failure JSON on stderr and
  stable sysexits-style codes without constructing the Word COM host.
- Tightened Word-package identity from an unsafe `/officeDocument` suffix test to exact
  Transitional/Strict relationship URIs, one internal resolved main part, one of four
  extension-compatible Word main content types and a Transitional or Strict
  `w:document` root with exactly one direct `w:body`. Inspection and semantic
  projection share these rules, so a valid OPC ZIP with
  `urn:evil/officeDocument` is no longer reported as a valid Word document. New
  regression tests prove read-only file hashes, stream-position restoration, external
  target redaction, default diagnostic-location redaction, false-Word rejection, ZIP
  limits, million-character/path-like stream labels, closed MCP arguments, canonical
  SDK/CLI/MCP parity and stable error codes without invoking or launching Word.
- Verified this operation slice with 357 engine tests, 243 native-host tests, 1,273
  Python/OOXML tests with 16 intentional skips, Ruff, active-service mypy, schema export,
  JSON/PowerShell parsing and an independent red-team. The review found and closed two
  initial P1 defects (false Word identity and MCP response compatibility), then two
  adversarial P1 defects (missing `w:body`/extension checks and an unbounded stream
  filename); its final rerun reported no unresolved P0/P1. Two local self-contained
  builds produced identical 195-file, 85,107,806-byte trees and 36,189,483-byte ZIPs at
  SHA-256 `ff910c0c314ccb98b7f716f40cfa6e4580659b763ee3d657e138c1d6732b4632`.
  Mandatory CI run `29967113037` passed all five jobs, and its hosted Windows ZIP
  matched both local archives byte for byte at the same size and SHA-256.
  Through that packaged executable, CLI and MCP returned identical 6,827-character
  canonical data for the same real DOCX, global help returned zero, and the Word-process
  count stayed 0. Across 20 cold runs, inspection measured 265.965 ms p50 / 286.932 ms
  p95 through CLI and 321.721 / 348.233 ms through MCP on the local Windows x64 host.
- Added a vendor-neutral, schema-versioned capability manifest shared by the native MCP
  `get_wordtoolkit_capabilities` gateway and `wordtoolkit-native capabilities` CLI.
  It preserves the embedded schema/MCP/compatibility header, publishes deterministic
  source, action-contract and capability-schema SHA-256 values, pages sorted summaries
  for all 84 actions, reports effect hints, hard limits and explicit metadata-coverage
  gaps, and exposes operation-specific format support without opening Word, reading a
  document or using the network. Native/core membership now comes from the embedded
  schema's `native_runtime` registry instead of duplicate C# lists. The normative JSON
  Schema is checked in, embedded and retrievable byte-for-byte through MCP
  `view=schema` or CLI `--schema`; its advertised hash is therefore independently
  verifiable. Fixed schema compaction so legitimate input properties named `title`
  survive while presentation annotations are still removed;
  malformed, unbounded and unknown input fails closed. The public MCP surface is now 15
  tools while full action schemas remain lazy.
- Verified the capability-contract slice with 340 document-engine tests, 239 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, schema-export drift
  checks, Draft 2020-12 validation and an independent red-team. The red-team found and
  closed two P1 defects: schema compaction had removed the real image `title` input, and
  an installed client could not retrieve the schema whose hash it was asked to trust.
  Two local self-contained builds produced identical 195-file, 85,083,230-byte trees
  and 36,179,845-byte ZIPs at SHA-256
  `1ebf765215f58ca22de6204a382dcedd83ec9e51dae15a9ba1c47509b237627f`.
  Through the exact packaged executable, CLI and MCP returned byte-equivalent canonical
  manifest/schema data; the schema hash validated, the image schema retained all 13
  properties, no Word process appeared and the package contained zero Python files.
  The 15-tool catalogue is 8,885 compact characters versus 8,172 without the capability
  gateway; the default 12-operation manifest is 5,359 and the opt-in schema view 6,769.
  Across 20 cold process runs, CLI manifest discovery measured 107.350 ms p50 / 121.159
  ms p95, MCP discovery 157.168 / 176.796 ms and CLI schema retrieval 86.148 / 94.272
  ms. The public release remains 0.34.0.

- Added a bounded, source-linked logical table graph and lazy
  `inspect_ooxml_tables` action. Transitional and Strict WordprocessingML tables now
  expose declared grids, physical-to-logical cell placement, row skips, `gridSpan`,
  exact-span vertical merge chains, separate legacy `hMerge` state, nested tables,
  contiguous repeating headers, row table-property exceptions, widths, fixed/autofit
  layout and Word-effective floating positioning. The dependency graph links nesting and
  merge continuations; the accessibility linter consumes the same typed header result.
  Cell text and raw XML have no response field, while names, layout and source require
  independent opt-ins.
- Verified the local table checkpoint with 340 document-engine tests, 228 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, schema validation,
  generated-document structural/visual checks and a warning-free Open XML validator
  build. Two local self-contained builds produced identical 195-file, 85,056,480-byte
  trees and 36,172,619-byte ZIPs at SHA-256
  `910a1cece37397f61568ea0a230fe663fdf54d834d8a8d367fa56b37ddfe1c13`.
  The packaged MCP returned 2 tables, 34 rows and 251 cells from the advanced torture
  DOCX in a 2,308-character compact response with zero issues, no Word process change,
  no source path, no cell text and no raw XML. Checked-in 10,000/100,000-cell scale
  points expose both throughput and allocation cost. Mandatory CI run `29959678412`
  passed all five jobs with zero annotations; its downloaded Windows artifact matched
  both local ZIPs and the 195-file expanded tree byte for byte. The public release
  remains 0.34.0.

- Added a bounded, read-only ECMA-376 Part 3 Markup Compatibility graph and the lazy
  `inspect_ooxml_markup_compatibility` action. The engine now models inherited
  `mc:Ignorable`, `mc:ProcessContent`, `mc:MustUnderstand` and
  `mc:AlternateContent`, separates branch selection from effective output, records
  legacy preservation hints without executing them, and keeps explicitly configured
  application-defined extension subtrees opaque. Namespace/source details are redacted
  by default; the action does not preprocess, mutate, open Word or claim a Word-version
  compatibility profile. Corrected scale fixtures remain below their declared ceilings
  at 99,999, 499,999 and 998,998 XML elements; the largest retains about 1.03 GB of
  managed memory, so the million-element boundary is documented as a hard limit rather
  than an ordinary workload.
- Verified the local MCE checkpoint with 316 document-engine tests, 218 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET
  formatting, schema drift checks, generated-document structural/visual checks and a
  warning-free Open XML validator build. Two local self-contained builds produced
  identical 195-file, 84,713,190-byte trees and 36,077,499-byte ZIPs at SHA-256
  `5312432d0ff5e8ea2c0c4ca664011225be7ea7ad49a0b7b091aa9db4efba2ea3`, with zero
  Python files. The exact packaged MCP inspected a real LibreOffice document in a
  3,054-character response without opening Word, following external targets, mutating
  the package or returning namespace URIs, source-part names, formulas or raw XML.
  All five jobs in mandatory CI run `29951495361` passed with zero annotations. Its
  hosted Windows artifact matched both local ZIPs and expanded trees byte for byte at
  the same size and SHA-256.
- Added fail-closed primary-name rename to the typed saved-package semantic edit actions.
  `rename_style` changes only `w:name` for an explicitly selected custom, non-default
  style; it never changes the stable `w:styleId`, aliases, formatting or ID-based
  references. Missing primary names are inserted losslessly. Existing ID/name/alias
  collisions, latent-style name consumers, risky `STYLEREF`, macros, `altChunk`, linked
  templates, unmodeled field consumers and `stylesWithEffects` block before mutation.
  Rename composes with assignment in the same deterministic transaction, reports a
  bounded rename count and retains an exact byte inverse without returning document
  content.
- Verified the rename slice with 283 document-engine tests, 208 native-host tests,
  1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET formatting,
  schema-export drift checks and the standalone Open XML validator build. Two local
  self-contained builds produced identical 195-file, 84,409,303-byte trees and
  35,998,031-byte ZIPs with SHA-256
  `85919959a2176627a6a973ad61ccabe189eaf36fabc44c83c5eb53e7479a59f2`.
  Through that packaged MCP, an 83-character `rename_style` command produced a
  946-character compact plan stable under JSON property order, with one positive byte
  delta and no new SDK errors. Apply matched the predicted fingerprint, changed only
  `word/styles.xml`, retained a byte-exact pre-apply backup, preserved the stable style
  ID and every original non-style ZIP-entry payload, returned no XML, used no Python and
  did not open Word. A packaged attempt to rename the default `Standard` style failed
  closed and left the file byte-identical. All five jobs in mandatory CI run
  `29942107590` passed, and its hosted Windows artifact matched the local ZIP and
  expanded tree byte for byte at the same counts, sizes and SHA-256.
- Added fail-closed unused-style deletion to the typed saved-package semantic edit
  actions. `delete_unused_style` removes only an explicitly selected custom, non-default
  definition after proving that no surviving semantic, revision, style, numbering,
  glossary, latent-style, `STYLEREF` or unmodeled XML consumer refers to it. One explicit
  batch may remove a closed graph of mutually dependent unused styles. The operation
  removes only exact `styles.xml` byte spans, participates in the same deterministic
  fingerprint-bound create/consolidate/delete/assign transaction, retains an exact
  inverse and reports a bounded deletion count without returning document content.
  Built-in/default styles, retained references, graph damage, macros, `altChunk`, linked
  templates, `stylesWithEffects`, signatures and schema regressions fail before mutation.
- Verified the deletion slice with 280 document-engine tests, 207 native-host tests,
  1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET formatting,
  schema-export drift checks and the standalone Open XML validator build. Two local
  self-contained builds produced identical 195-file, 84,397,476-byte trees and
  35,995,361-byte ZIPs with SHA-256
  `417af08694ba33cd7c6fedc6ad06a6e2baf00b51f0f2af76c34d703b3976f9b6`.
  Through that packaged MCP, a clone first created one unused custom style. Its
  55-character deletion command produced a 934-character compact plan stable under JSON
  property order, with one negative byte delta and no new SDK errors. Apply matched the
  predicted fingerprint, changed only `word/styles.xml`, retained an exact pre-apply
  backup, removed the style, preserved every original ZIP-entry payload, returned no XML
  and did not change the running Word process set. A packaged attempt to delete the
  default `Standard` style failed closed and left the file byte-identical. All five jobs
  in mandatory CI run `29938834101` passed, and its hosted Windows artifact matched the
  local ZIP byte for byte at the same size and SHA-256.
- Added fail-closed exact style consolidation to the typed saved-package semantic edit
  actions. `consolidate_style` rewrites type-checked paragraph/run/table, glossary,
  style-graph and numbering references, removes an explicitly selected
  canonical-equivalent custom source definition and retains one predicted fingerprint
  plus exact inverse. Linked
  paragraph/character pairs can be consolidated in one explicit batch; create/clone,
  consolidation and assignment stages compose only through an exact byte chain. Built-in
  or non-equivalent sources, chained targets, graph damage, matching latent-style
  exceptions, unsafe `STYLEREF`, unmodeled XML consumers, macros, `altChunk`,
  linked-template updates and `stylesWithEffects` fail
  before mutation. Plan responses expose bounded consolidation/reference counts, accept
  property-order-stable intent, return no XML/text and remain below the 4,500-character
  lazy-schema ceiling.
- Verified the consolidation slice with 278 document-engine tests, 206 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET formatting
  and the standalone Open XML validator build. Two local self-contained builds produced
  identical 195-file, 84,386,430-byte trees and 35,992,947-byte ZIPs with SHA-256
  `c7f68a994a185b80e211a751a05881537f0d836c53e195107cd4ae2fd4d11a76`.
  Through the packaged MCP, two clones and one server selector first created an exact
  duplicate and assigned it to six Apache POI paragraphs. A 108-character consolidation
  command then produced a 902-character compact plan, stable under JSON property order,
  with six reference rewrites and no new SDK errors. Apply matched the predicted
  fingerprint, changed only `word/document.xml` and `word/styles.xml`, retained a backup,
  removed the source definition, left six target uses and zero source uses, returned no
  XML and did not change the running Word process set. All five jobs in mandatory CI run
  `29936061264` passed, and its hosted Windows artifact matched the local ZIP byte for byte
  at the same size and SHA-256.
- Added atomic typed style-definition creation and cloning to the existing semantic edit
  actions. `create_style` emits a minimal custom paragraph, character, table or numbering
  definition with bounded inheritance/UI metadata; `clone_style` preserves an existing
  definition's modeled and opaque formatting while stripping unsafe default/link identity.
  The same stateless plan can server-select nodes and assign a newly created style, joining
  `styles.xml` and document-part changes under one predicted fingerprint, exact inverse,
  Microsoft Open XML validation and atomic backup-capable apply. Duplicate IDs, missing or
  wrong-type references, cycles, changed intent, signatures and `stylesWithEffects` mirror
  drift fail closed. No command accepts or returns raw XML, and the lazy schemas remain
  below 4,500 serialized characters.
- Verified the style-definition slice with 274 document-engine tests, 205 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET
  formatting and the standalone Open XML validator build. Two local self-contained
  builds produced identical 195-file, 84,359,203-byte trees and 35,982,900-byte ZIPs
  with SHA-256
  `ca713cbec08fa31dc67de8ef503bbf33c28a08e187980cc95d18093144c53f2f`.
  Through the packaged MCP, a 218-character two-command batch cloned `Standard` as
  `CodexDefinition` and server-assigned it to six paragraphs in an Apache POI DOCX.
  The compact seven-operation plan response was 2,146 characters. Candidate validation
  found no new errors; apply matched the predicted fingerprint, changed only
  `word/styles.xml` and `word/document.xml`, retained a backup, returned no XML, left
  the source fixture unchanged and did not alter the running Word process set. All five
  jobs in mandatory CI run `29932018346` passed, and its Windows artifact matched the
  local ZIP byte for byte at the same size and SHA-256.
- Added token-lean bulk `set_style_where` commands to the typed saved-package semantic
  edit actions. One compact selector now resolves paragraph, run, or table targets from
  bounded text, exact properties, ancestor/descendant, subtree and source-part evidence
  without making the model echo every node ID. The caller must declare `max_matches`;
  empty, excessive, overlapping, invalid-kind and over-200 selections fail closed, with
  at most 16 selectors per transaction. Canonical typed intent makes plan IDs stable
  across harmless JSON property order while preventing changed selector replay. Compact
  responses expose submitted/selector/resolved counts and no document text or XML.
- Verified the bulk-selector slice with 270 document-engine tests, 203 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET
  formatting and the standalone Open XML validator build. Two local self-contained
  builds produced identical 195-file, 84,327,130-byte trees and 35,973,436-byte ZIPs
  with SHA-256
  `65ccaed09b608be6fb851a4ae38e54b4fc4ff9d1b7ac8b6f1e5aaa9d0dcbaeac`.
  Through the packaged MCP, one bounded selector resolved six paragraph style changes
  in an Apache POI DOCX; candidate validation found no new errors, apply matched the
  predicted fingerprint, changed only `word/document.xml`, retained a recovery backup,
  returned no XML, left the source unchanged and did not alter the running Word process
  set. The selector used 77.53% fewer serialized command-input characters than the six
  exact commands on that fixture; the 200-target regression fixture reduced command
  input from 19,014 to 186 characters (99.02%). All five jobs in mandatory CI run
  `29928388511` passed, and its downloaded Windows ZIP matched both local archives
  exactly in length and SHA-256.
- Added lazy `plan_ooxml_semantic_edits` and `apply_ooxml_semantic_edits` as the first
  extensible high-level semantic mutation surface. The initial `set_style` command
  assigns existing compatible paragraph, character, or table styles to exact stable
  paragraph/run/table IDs under package, plan, and explicit-style preconditions. The
  lossless planner preserves unrelated bytes, avoids namespace-prefix rebinding,
  predicts the result fingerprint, retains an exact inverse, and validates the exact
  candidate against the baseline with Microsoft Open XML SDK. Apply is atomic, keeps a
  recovery backup by default, rejects signed/stale/drifting/invalid candidates, never
  opens Word, and returns neither XML nor document text. The public catalog remains 14
  tools and grows to 80 lazy actions.
- Verified the semantic-style slice with 270 document-engine tests, 200 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET
  formatting and the standalone Open XML validator build. Two local self-contained
  builds produced identical 195-file, 84,302,924-byte trees and 35,963,647-byte ZIPs
  with SHA-256
  `fc92ec8ae48273f972426934497b1217bd0d660fd55f6fa7f001b36215eb06e4`.
  The packaged MCP changed an exact source-linked paragraph in an Apache POI DOCX from
  `berschrift1` to the existing `Standard` style, validated the candidate, matched the
  predicted fingerprint, changed only `word/document.xml`, retained a backup, returned
  no XML, kept every complete response below 2,900 characters, left the source fixture
  unchanged and did not alter the running Word process set. All five jobs in mandatory
  CI run `29925881340` passed, and its downloaded Windows ZIP matched the local archive
  exactly.
- Added strict `ancestor` and `descendant` predicates to saved-package semantic
  queries. A related-node predicate combines semantic kinds and exact properties on
  one node, excludes self, and is propagated through the tree in linear time. Indexed
  queries resolve related matches from existing postings, add relationship positions
  to the smallest-candidate plan, and still recheck every predicate. This selects such
  objects as paragraphs containing equations and equations inside table cells without
  returning raw XML or spending model tokens on tree traversal. The public and lazy
  action counts remain unchanged.
- Verified the structural-query slice with 262 document-engine tests, 196 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET
  formatting and the standalone Open XML validator build. Two local self-contained
  builds produced identical 195-file, 84,250,176-byte trees and 35,951,915-byte ZIPs
  with SHA-256
  `f416bfbd8e37adcce2a3a88a97ad4f7e5b698001d9d9f2c4784b9e15715899fe`.
  The packaged MCP queried a real 194-node equation DOCX, selected 5 equation-bearing
  paragraphs after scanning 11 candidates, released the index, kept every complete
  JSON-RPC response below 3,200 characters, left the source unchanged and did not
  alter the running Word process set. The downloaded artifact from mandatory CI run
  `29923044649` matched the local ZIP exactly, and all five jobs passed.
- Added a bounded native semantic index for repeated AI queries. `WordSemanticIndex`
  precomputes source-ordered postings for node kind, source part and exact property
  values, then chooses the smallest posting as the candidate seed while rechecking every
  predicate. Lazy `manage_ooxml_semantic_index` creates/reuses, inspects, lists and
  releases package-fingerprint-bound handles; `query_ooxml_semantics` can consume one
  only with the exact package fingerprint and reports index use, candidate seed and scan
  counts. Indexes stay only in process memory, return no raw text, expire within 30
  minutes and are capped at four handles, 100,000 nodes each and 250,000 cached nodes.
  The public catalog remains 14 tools and grows to 78 lazy actions.
- Verified the semantic-index slice with 260 document-engine tests, 194 native-host
  tests, 1,273 Python/OOXML tests with 16 intentional skips, Ruff, scoped .NET formatting
  and the standalone Open XML validator build. Two local self-contained builds produced
  identical 195-file, 84,237,559-byte trees and 35,947,755-byte ZIPs with SHA-256
  `5c4b3ef4d420259463d2cca0e7ebef8d647781c541be761025861c1a73db004a`.
  The downloaded artifact from mandatory CI run `29920866791` matched that ZIP exactly.
  The packaged MCP indexed the 142-node LibreOffice TOC fixture, reduced a paragraph
  query to 13 candidates, explicitly released the handle, returned no raw index text,
  left the source unchanged and did not change the running Word process set.

- Verification checkpoint: 245 document-engine tests, 185 native-host tests, 1,273
  Python/OOXML tests with 16 intentional skips, Ruff and every mandatory GitHub job pass.
  A fresh local checkout and two hosted Windows builds produced the same 35,886,733-byte
  ZIP with SHA-256 `e8f2e4b74fe65213197126c7aafb445452bd0e80bc05f7206d82672e4b09e59b`.
  The exact package passed all 48 real-Word actions and Open XML validation while the
  pre-existing user-owned Word process and document remained open and unchanged.

- Added the first native saved-package linter. Eighteen deterministic core, styles,
  accessibility and security rules consume one fingerprint-bound set of typed graphs
  and report stable finding/rule IDs, severity, confidence, privacy-safe subject
  fingerprints, bounded evidence, optional exact source byte spans, validated
  suppressions and explicit fix safety. It detects graph/package corruption, unused and
  formatting-equivalent styles, direct formatting, external relationships, hidden text,
  heading-order gaps, absent drawing alt text, unmarked table headers and a missing
  document title. Coverage omissions and unmodeled domains prevent a false complete
  verdict; at that read-only checkpoint every fix remained marked unimplemented.
- Added lazy `lint_ooxml_document` with compact summary and paged finding/rule views.
  The action never opens Word, follows an external target or mutates a package. Source
  data is off by default, suppression arrays and pages are bounded, the default result
  stays below 5000 characters and the complete mirrored JSON-RPC response below 10000.
  The token-lean public surface remained 14 tools while the linter-only action catalog
  grew to 75; the repair slice below raises it again.
- Verified the linter checkpoint with 250 document-engine tests and 189 native-host
  tests. Two independent local package builds produced identical 195-file,
  35,918,887-byte archives with SHA-256
  `e0d162feac71679efedfeac0de6982447f4856298d9b7334a0195a04c27f7400` and no Python
  files. The packaged MCP inspected the LibreOffice TOC fixture with all 18 rules,
  returned 35 visible findings in a 3700-character complete response, reported
  incomplete document-domain coverage, and proved Word remained unopened and the
  package unmodified. The last complete 48-action live-Word gate remains the preceding
  exact 0.35 package checkpoint because this slice changes only saved-package analysis.
- Canonicalized copied plugin JSON/Markdown and the embedded native MCP schema to
  BOM-less UTF-8 with LF during packaging. A stale pre-`.gitattributes` checkout had
  silently produced different manifest and assembly bytes from a clean checkout. After
  this guard, the same exact ZIP hash is produced by that stale working tree, a clean
  detached worktree and the hosted Windows CI artifact.
- Added the first fail-closed native lint repair. `WordLintRepairPlanner` accepts only a
  package-bound document-title finding backed by exactly one existing, empty, leaf
  `dc:title`; it losslessly replaces that element, changes only the core-properties
  part, predicts the result fingerprint, reparses the candidate, proves the finding is
  gone and retains an exact byte inverse. Missing, duplicate, nonempty or mixed-markup
  titles are refused instead of synthesized or guessed.
- Added lazy `plan_ooxml_lint_repair` and `apply_ooxml_lint_repair`. The plan binds the
  package, finding, replacement and new same-extension output path, hashes rather than
  echoes the title, and compares baseline/candidate Open XML validation. Apply rebuilds
  the exact plan, blocks signatures and new validation errors, creates a new file
  atomically, never overwrites the source or output and never opens Word. The public
  catalog remains 14 tools and grows to 77 lazy actions.
- Verified the repair slice with 254 document-engine tests, 192 native-host tests,
  1,273 Python/OOXML tests with 16 intentional skips, Ruff and the standalone Open XML
  validator build. Two local self-contained builds produced the same 195-file,
  84,193,862-byte tree and 35,935,327-byte ZIP with SHA-256
  `49aae752c5d2457d4474f63ff1142fe5909bac20cfdf7b534e97044dd3e29ca8`.
  The packaged MCP repaired the empty title in the LibreOffice TOC fixture, changed only
  `docProps/core.xml`, matched the predicted package fingerprint, returned no raw title
  or XML, removed the exact lint finding and kept Word unopened and the source unchanged.
- Normalized Windows packaging onto Windows PowerShell 5.1 even when the caller starts
  the build through `pwsh`. The two hosts use different `System.IO.Compression`
  implementations and previously produced different ZIP bytes from the same 195-file
  tree. Local `pwsh`, direct Windows PowerShell and the hosted Windows artifact now
  produce the exact same distributable hash above.

- Made the document-engine and native .NET test suites mandatory CI inputs and added a
  clean Windows job that builds the exact distributable plugin ZIP. Tag builds on the
  licensed self-hosted Word runner now execute the full 48-action live acceptance gate.
- Repaired that gate for Windows PowerShell 5.1 by removing parser-unsafe source text,
  avoiding unsupported JSON parameters, escaping Unicode MCP values and preventing a
  UTF-8 preamble from corrupting the first request.
- Bounded line-delimited MCP input at 8 MiB, added per-request cancellation tokens,
  active request IDs, synchronized concurrent responses and MCP cancellation
  notifications. A cancelled in-flight COM call now blocks new Word work until it
  returns and directs supervisors to restart only the WordToolkit runtime if it hangs.
- Centralized the native base version in `native/Directory.Build.props`; package builds
  fail when it drifts from the plugin manifest. Corrected stale 0.30/0.33 README claims.
- Stopped the legacy Python schema exporter from overwriting the native MCP catalog.
  CI now rejects generated remote-schema/documentation drift, while native catalog
  coverage remains enforced by `WordToolkit.Native.Tests`.
- Pinned native builds to .NET SDK 8.0.423, enforced LF for repository text and mapped
  checkout-dependent compiler paths to stable virtual roots. This removes SDK/runtime-
  pack, Windows checkout conversion and absolute PDB paths from cross-host ZIP hashes;
  build reports now include the exact SDK version.
- Documented that version-1 `.wtpatch` files contain full confidential payloads, are
  materialized in memory and currently have no trusted signature or encryption envelope.
- Added a reproducible engine benchmark harness, scheduled/manual benchmark workflow and
  checked-in Windows x64 baseline. Roughly one million dependency nodes peaked at
  4,173.1 MiB in 38.56 seconds; a 400 MiB patch payload peaked at 2,158.1 MiB in
  15.61 seconds. These expose the current allocation debt instead of hiding it behind
  permissive safety ceilings.
- Reduced default `.wtpatch` limits after measurement: 128 MiB aggregate payload,
  64 MiB per blob, 4 MiB manifest and 100:1 compression ratio. Higher limits now require
  an explicit caller configuration rather than silently consuming multi-gigabyte memory.
- Added an optional authenticated patch envelope in the engine. AES-256-GCM uses a fresh
  nonce and binds canonical metadata as associated data; ECDSA-SHA256 signs metadata,
  tag and payload and binds a restricted signer key ID. Wrong keys, tampering, missing
  verifiers and unexpected signer identities fail closed. Raw `.wtpatch` remains
  unencrypted by default, and MCP key provisioning is still deliberately absent.

## 0.34.0 — 2026-07-22

- Added the initial unified `WordDependencyGraph`. Deterministic `wddn_` nodes and
  `wdde_` edges join OPC reachability, source-linked semantic containment, explicit
  paragraph/run/table style usage, style defaults and inheritance/link chains,
  numbering definitions and uses, field/bookmark targets, nested fields and section
  header/footer bindings. Missing and external targets remain explicit, every endpoint
  and input fingerprint is checked, stable-ID collision detection is constant-time,
  and node/edge/key/issue budgets fail closed.
- Added lazy `inspect_ooxml_dependencies` with compact summary, paged node/edge,
  unresolved-target, issue and bounded one-to-four-hop impact views. Keys and source
  provenance are redacted by default, external relationships are never followed, field
  codes are never executed and the response carries an explicit unmodeled-domain list.
  Page selection is a cancellable single pass that retains only the requested page;
  summary aggregation is bounded by the fixed edge-kind vocabulary. The public MCP
  surface remains 14 tools while the lazy action catalog grows to 74.
- Projected explicit table style, paragraph numbering instance and numbering-level
  references into the source-linked semantic properties so the dependency spine does
  not infer those relationships from rendered appearance.
- Refreshed all eight pinned AI/Word repository heads on 2026-07-22. Seven were
  unchanged; OfficeCLI advanced 28 commits to `e7916a2...` (`1.0.140`). No Word handler
  changed in that range, while its missing-destination atomic-write fallback was
  recorded as a persistence-semantic change rather than ignored as release noise.
- Verified 238 document-engine tests and 183 native-host tests. A field-heavy corpus
  document keeps the default dependency result data below 5000 serialized characters,
  and tests prove deterministic identities, complete edge endpoints, redaction,
  unresolved/orphan evidence, traversal limits and zero Word COM invocation.
- Built the self-contained Windows x64 plugin twice from independent output
  directories. Both 195-file archives are byte-identical at 35,867,498 bytes with
  SHA-256 `f4625c2c15827e78c9b5c54eaa50adf6aeeb64644235cafc46aa8374812b3944`;
  the native runtime assembly SHA-256 is
  `d93d12ab573a72547bc4db1992c997c1cb44c6b235439ca72ec1abd16d45840f`.
- Corrected the package report to hash the native runtime assembly separately from the
  generic .NET apphost executable, whose unchanged stub hash is not proof that the
  WordToolkit implementation stayed unchanged.

## 0.33.0 — 2026-07-22

- Added loss-aware native style preservation for Presentation MathML and OMML. MathML
  resolves inherited `mathvariant` values on `math`/`mstyle`, token overrides, all four
  weight/slant styles and ten representable mathematical alphabets. OMML preserves all
  four `m:sty` values (`p`, `b`, `i`, `bi`) separately from `m:ctrlPr/w:rPr` bold and
  italic properties for recognized structural controls. The full 19-name control
  property vocabulary is known; unsupported OMML structures still fail instead of
  being flattened under a broad support claim.
- Expanded the private build protocol from four to 24 stable, reserved sentinels so
  run-only, run-and-control and first-control scopes remain distinct. Marker injection,
  imbalance, unsupported placement and contextual Arabic MathML variants that cannot
  survive the linear Word path fail before mutation instead of silently flattening.
- Made native style readback semantic instead of byte-fragile. The verifier normalizes
  Word's documented default `m:sty="i"` and `m:scr="roman"`, coalesces only adjacent
  sibling runs with identical effective properties, and still hashes text, normal/literal
  flags, mathematical script, run style and structural-control placement. Diagnostics
  expose bounded property traces without formula text or raw OMML.
- Extended the complete real-Word gate with normal, bold, italic and bold-italic MathML
  tokens, all ten representable mathematical alphabets and separate OMML run/control
  scopes. The packaged Release runtime completed
  122 MCP requests, all 48 live actions, 12 editable equations, save/reopen/reconnect,
  Open XML validation and a 166,347-byte PDF while preserving the pre-existing Word
  process and closing only its own acceptance document.
- Verified 234 document-engine tests, 179 native-host tests, Ruff, mypy and 1273
  Python/OOXML tests with 16 intentional skips.
- Built the self-contained Windows x64 plugin twice from independent output
  directories. Both 195-file archives were byte-identical at 36,989,699 bytes with
  SHA-256 `8a64ed4f9b69b80f338de5c30bc687852bf29d24bddcb9b99eb078d15e06d1b1`;
  the runtime executable SHA-256 is
  `dd5bf30493826db37d43b027928a7f9b9a881b070264b18b6824c80ad15440db`.

## 0.32.0 — 2026-07-22

- Added native editable `\mathbf{...}` and `\boldsymbol{...}` authoring without
  per-character COM formatting. Balanced private build sentinels survive Word's
  `BuildUp()`, are removed by a bounded internal OMML rewrite, and become native
  `m:sty="b"` / `m:sty="bi"` runs plus matching `m:ctrlPr/w:rPr` weight on
  fraction, radical, delimiter and n-ary controls.
- Added independent semantic and style-placement contracts after reinsertion.
  Marker loss, style drift, control-placement drift, malformed XML, an extra
  equation or a changed formula now rolls back the complete Word Undo record.
  Strict OfficeMath creates Strict wordprocessing control properties, and generated
  `w:b` / `w:i` properties follow schema order.
- Repaired visible text spacing in LaTeX `cases`. Word-discarded ordinary spaces are
  replaced by U+2003 case-column and U+2005 text-boundary spacing; both survive
  build-up, save/reopen and PDF rendering and are now significant in native readback.
  The full-live UnicodeMath sample now uses a real canonical `∑` operator instead of
  the literal word `sum`.
- Kept AI responses bounded. Full preflight returns clean Word linear math without
  internal sentinels; compact preflight and mutation responses expose only aggregate
  style counts and verification facts. The complete packaged live gate remains at
  339 serialized characters for compact equation preflight.
- Verified 234 document-engine tests, 161 native-host tests and 1273 Python/OOXML
  tests, plus the 48-action real-Word acceptance with 12 native equations, SDK
  validation, save/reopen/reconnect and native PDF visual inspection. Two independent
  package builds produced the same 36,975,454-byte ZIP with SHA-256
  `dbe6a4553bcbcda1aeafcd366a240dc3c8d19b33f6ecd93bb13544c28bf6f71d`.

## 0.31.0 — 2026-07-22

- Corrected native Word round trips for named functions and limit operators. OMML
  function application no longer invents an extra argument delimiter, while `lim`,
  `min` and `max` now receive an explicit Word boundary after their lower limit so the
  following operand cannot be swallowed into that limit.
- Added LaTeX `\left\|...\right\|` and `\|...\|` norm delimiters, and canonicalized
  the edge whitespace that Word removes from mathematical text inside `cases`.
- Scoped differential-placement verification to differentials owned by integral
  operands. Ordinary derivatives such as `\frac{\mathrm{d}y}{\mathrm{d}x}` retain the
  same U+2146 contract without being falsely rejected for living outside `m:nary`.
- Preserved Word's mathematical-alphabet run properties for script, Fraktur,
  double-struck, sans-serif and monospace Latin letters and supported digits. LaTeX
  `\mathcal`, `\mathfrak`, `\mathbb`, `\mathsf`, `\mathtt` and simple `\mathrm`
  now create the intended native glyph family and survive readback. `\mathbf` and
  `\boldsymbol` fail closed because this linear OMath path does not preserve their
  weight; silently emitting an ordinary letter is no longer treated as success.
- Forced real-Word readback over the 48-family equation atlas, all 112 registered
  symbol commands, all 20 named functions, ten delimiter forms, mathematical
  alphabets and an ordinary derivative. Every accepted case matched its canonical
  contract and returned no raw OMML.
- Repaired the packaged full-live acceptance harness for the token-lean public
  surface: 14 exposed tools now discover and execute the 73-action catalog through
  the lazy gateways, detailed assertions explicitly request full responses, and a
  separate compact equation preflight remains capped at 339 serialized characters.
  The gate exercised all 48 live actions, reopened the saved DOCX, validated it,
  exported PDF and closed only its own test document. The older expanded acceptance,
  capability demo and Word atlas harnesses now use the same lazy public contract instead
  of demanding the retired 48-tool catalogue.
- Verified 234 document-engine tests, 143 native-host tests and 1273 Python/OOXML
  tests. Two independent Windows PowerShell 5.1 builds produced the same
  35,814,374-byte self-contained ZIP with SHA-256
  `17ef223ddac5b9b8ba02c7b86c29089b2e76d8cc730cbee58f9aa0225d088f25`.

## 0.30.0 — 2026-07-22

- Rebuilt live integral conversion around Word's actual UnicodeMath grammar. LaTeX
  `\,d x`, `\mathrm{d}x`, `\operatorname{d}x` and `\dd x`, normal-variant MathML
  `d`, native OMML normal `d`, and direct UnicodeMath `ⅆ` now converge on U+2146.
  Integral operands containing differentials are wrapped in Word's invisible `〖…〗`
  group, including nested `∫`, `∬` and `∭` forms, so `OMath.BuildUp()` cannot lift the
  differential into an exponent or leave it outside `m:nary/m:e`.
- Added a bounded native equation readback verifier. Structurally sensitive n-ary
  operators, differentials, matrices, cases, equation arrays, accents, hbar and dagger
  notation automatically read back the exact new OMath through `Range.WordOpenXML`.
  The verifier securely reparses one equation, compares canonical SHA-256 contracts,
  checks symbol counts and differential ancestry, returns no raw OMML, and rolls back
  the complete Word Undo transaction on drift. `verify_readback=true` can extend the
  same gate to otherwise low-risk equations.
- Hardened secure MathML/OMML conversion for Strict OfficeMath namespaces, real Word
  run/control formatting, omitted default integral characters and normal differential
  runs. Word's matrix/cases marker expansion and combining accent characters now map to
  the same canonical contract used by LaTeX input.
- Made equation preflight token-lean by default. Compact responses omit converted
  linear math, rule arrays and source markup, returning only bounded counts, flags and a
  short fingerprint; exact linear output requires `response_mode="full"`. Raw OMML is
  never returned by live verification.
- Added precise lazy equation-item schemas and explicit failure for misleading format
  aliases such as `source_format`; callers must use `input_format`. Fixed the direct
  single-equation action so its advertised token-verified `cursor`, `selection` and
  `document_end` targets are actually honored instead of always appending.
- Verified the release checkpoint with 234 native engine tests, 120 native host tests
  and 1273 Python/OOXML tests. Real Word MCP regression covered Gaussian, nested and
  double integrals, Presentation MathML, OMML, a parenthesized matrix, cases and
  combining accents with transactional rollback on the deliberately isolated failure.

## 0.29.0 — 2026-07-22

- Added `WordPackageThreeWayMergePlanner`, a bounded deterministic merge over an
  explicit ancestor plus left and right saved Word packages. It automatically selects
  one-sided changes, coalesces identical changes and composes disjoint lossless
  source-linked `w:t`, `w:delText` and `m:t` edits inside the same part. A branch must
  reconstruct byte-exactly from the ancestor through those text commands before the
  semantic path is trusted; hidden markup drift therefore falls back to conflict rather
  than being discarded.
- Added stable `wtmc_` conflict records and `wtmerge_` plan identities. Divergent add,
  modify, delete/modify and same-text-node changes expose hashes, sizes and bounded
  privacy-off-by-default text evidence without payloads or raw XML. Every conflict must
  be resolved exactly once with `use_ancestor`, `use_left` or `use_right`; unknown,
  duplicate and stale IDs fail closed.
- Added lazy `plan_ooxml_merge` and `apply_ooxml_merge`. Planning is summary-first and
  pages conflicts, entry decisions, resulting patch operations, risks or schema errors
  only on demand. Apply requires exact fingerprints for all three inputs and a
  destination-bound `wtmergeapply_` identity, reprojects and revalidates the candidate,
  preserves the patch engine's separate signature/active-content/external-link/binary/
  error gates, enforces the Word main-part type against the output extension, creates a
  new file atomically and never overwrites an existing path.
- Extended the atomic writer with a race-safe new-destination mode. Added merge coverage
  for no-op, one-sided/identical/disjoint edits, same-node conflicts, unknown markup,
  hidden XML drift, add/delete/modify conflicts, deterministic resolution order, stale
  identities, output-path binding, no-overwrite, macro and opaque-binary gates, type
  mismatch, cancellation and no-Word-host operation. This is an honest initial merge
  slice: arbitrary structural OOXML and revision-aware node merges remain unresolved
  work, not a fabricated success.
- Hardened the Windows release builder so it also runs under the system Windows
  PowerShell 5.1 instead of depending on a .NET-only `Path.GetRelativePath` method.
  Plugin ZIP entries are emitted in sorted order with a fixed OPC-compatible timestamp
  and explicit stream copying, removing source-file timestamp drift from repeated builds.

## 0.28.0 — 2026-07-22

- Added `OpcPackagePatchBuilder` and a deterministic reversible OPC entry-payload patch
  model. Add, replace and delete operations bind exact base/result package fingerprints,
  content types, byte lengths and before/after hashes; deduplicated payloads support an
  exact guarded reverse without the original files. The guarantee is explicit: entry
  names and uncompressed payloads are exact, while ZIP container metadata and compression
  layout are deterministic serializer output rather than byte-identical source records.
- Added the strict `.wtpatch` codec. Its canonical manifest and content-addressed payload
  archive rejects duplicate/unknown/missing fields, unsafe or duplicate ZIP names,
  noncanonical operation order, ID/hash/length/count drift, unreferenced payloads,
  excessive expansion and compression bombs. Read never extracts archive entries to the
  filesystem; create writes a sibling temporary file, flushes and rereads it, then moves
  only to a new path and never overwrites.
- Added semantic package-patch planning and risk analysis. Plans recompute the native
  semantic diff, detect OPC signatures, VBA/macro, OLE/embedded package, ActiveX/control,
  external relationship, opaque binary, custom XML, infrastructure and newly introduced
  structural changes. Signature invalidation, active content, external relationships,
  opaque binaries and new errors have independent false-by-default authorizations; no
  blanket force flag exists.
- Added five lazy token-lean actions: `plan_ooxml_patch`, `create_ooxml_patch`,
  `inspect_ooxml_patch`, `plan_ooxml_patch_apply` and `apply_ooxml_patch`. Apply requires
  exact base, patch and path-bound deterministic apply-plan identities, rematerializes the
  candidate, enforces package-main-type compatibility with the in-place destination
  extension, compares baseline/candidate Microsoft Open XML SDK results, writes atomically
  and keeps a recovery backup by default. Validation truncation, inability to open the
  candidate or a result-type/extension mismatch is non-overridable. No action opens Word,
  returns payload bytes/raw XML or stores a server-side document cache.
- Added adversarial codec, inverse, corpus, signature, macro, OLE, ActiveX, external-link,
  opaque-binary, custom-XML, inherited/new-error, stale-plan, artifact-tamper, no-overwrite,
  path-bound approval, package-type, atomic-backup and no-Word-host tests. The native
  checkpoint passes 221 engine tests and 85 host tests.

## 0.27.0 — 2026-07-22

- Added native `WordSemanticDiffEngine`, a bounded two-layer comparison of saved Word
  packages. It separates exact OPC entry drift from source-linked semantic added,
  removed, moved, text, declared-property, structure and unmodeled-markup differences
  across the main body, headers, footers, notes, comments, glossary entries and text
  boxes.
- Matching prefers document/story roles, exact semantic IDs, unique durable anchors and
  unique exact subtrees, then uses bounded contextual sibling alignment. Duplicate
  identities, near-tied candidates and alignment-budget fallbacks remain explicit;
  insertion-driven index shifts are not mislabeled as moves.
- Added lazy `compare_ooxml_semantics` with compact summary-first output, exact package
  fingerprint preconditions, filters and paging. Document text and property values are
  redacted by default; text previews, source paths and hashes are independent bounded
  opt-ins. The action never opens Word, returns raw XML or mutates either package.
- Added node, change, diagnostic, alignment, processed-text and captured-text budgets,
  deterministic diff/change IDs, cancellation, option-policy tests, adversarial
  ambiguity tests and no-op coverage across the bundled multi-producer DOCX corpus.
- The native checkpoint passes 192 engine tests and 78 host tests.

## 0.26.0 — 2026-07-22

- Added `WordReviewMutationPlanner`, a typed lossless saved-package transaction for
  accepting or rejecting bounded tracked-revision selections. It handles insertion,
  deletion and conflict wrappers, complete move pairs, run/paragraph/table/row/cell/
  section/numbering and `tblPrEx` property snapshots, numbering-change acceptance,
  inserted rows, cell-insertion acceptance and cell-deletion rejection while preserving
  every unrelated byte and retaining an exact guarded inverse. These cell decisions
  remove only safe markers; grid-blind
  cell reconstruction, vertical-merge restoration and fake `numberingChange` rejection
  are deliberately blocked.
- Added fail-closed dependency planning for nested revisions and named moves. Cascading
  is explicit; conflicting decisions, deleted paragraph-mark merges, table-grid
  reconstruction, custom XML and unsupported structural combinations remain blocked
  instead of being guessed into a corrupt document.
- Added lazy `plan_ooxml_review_decisions` and `apply_ooxml_review_decisions`. Selection
  uses stable revision IDs or redacted author fingerprints, or deliberate
  `select_all=true`; an empty implicit all-selection is forbidden. Apply rebuilds the
  deterministic plan under the original package fingerprint and exact plan ID, rejects
  signed packages, persists atomically and keeps a recovery backup by default.
- Candidate review packages are reparsed and compared against the baseline with the
  Microsoft Open XML SDK validator. Existing unrelated errors remain visible, while any
  newly introduced error blocks apply. Responses omit author names, document text and
  raw XML. Runtime bounds count every selector item, including duplicates, instead of
  trusting a caller to enforce the published JSON schema.
- Added shared hash-preconditioned package transaction primitives and lossless element
  remove, unwrap, local-name rename and replacement patches. Engine coverage includes
  UTF-preserving inverse tests, adversarial nesting and real Word/Pandoc/Apache POI
  tracked-change and move fixtures; native coverage includes plan/apply, redaction,
  selection safety and baseline-aware validation.
- The native checkpoint passes 172 engine tests and 73 host tests.

## 0.25.0 — 2026-07-22

- Added `WordReviewGraphBuilder`, a bounded, source-linked read graph for saved-package
  comments, story anchors, threaded replies, people, tracked revisions, named moves,
  permission ranges and review settings. It joins modern `commentsExtended`,
  `commentsIds`, `commentsExtensible` and `people` parts without flattening them into
  anonymous text.
- Added stable review identities, durable comment IDs, reply/root links, resolved state,
  reaction inventory, revision nesting and status, property/cell/custom-XML revision
  kinds, source/destination move pairs, editor/group permissions and explicit corruption
  diagnostics. Independent limits cap parts, comments, anchors, revisions, ranges,
  people, text, thread depth and issues.
- Added lazy `inspect_ooxml_review` with summary, comments, anchors, threads, revisions,
  move-range, move, permission, people, settings and issue views. Text and personal
  values are fingerprinted/redacted by default, bounded previews require explicit
  sensitive opt-in, source metadata is optional, raw XML is never returned and the path
  cannot open or mutate Word.
- Added four engine tests and four native host tests. The current native totals are 154
  engine tests and 67 host tests; end-to-end corpus smoke covers WordToolkit, Mammoth,
  Pandoc and Apache POI comments, tracked changes and moves with compact default MCP
  responses.

## 0.24.0 — 2026-07-21

- Added `WordEquationGraphBuilder`, a bounded source-linked canonical read graph for
  native OfficeMath in saved Word packages. It models all 19 standard OMML object
  families, argument roles, matrix rows/cells, math runs/text, display paragraphs,
  main math defaults, Strict markup, Word story boundaries, deleted content and
  preserved extension or unknown nodes.
- Added stable equation and math-node IDs derived from semantic source identities,
  normalized typed properties, and bounded diagnostics for missing or duplicate
  arguments, child order, matrix shape, invalid property values, nested equations,
  invalid placement, empty math, adjacent objects Word merges and unsupported
  extensions.
- Added lazy `inspect_ooxml_equations` with compact summary, equation, flat-node,
  paragraph, settings and issue views. Default responses expose counts and short
  fingerprints only; raw OMML is never returned, text previews require explicit
  sensitive opt-in, and the parse-only path neither starts Word nor converts or
  follows external content.
- Added nine engine tests and six native host/corpus cases, bringing the native
  document-engine totals to 150 engine tests and 63 host tests. The test corpus covers
  every standard OMML object family, malformed input, Strict markup, source-ID
  stability, safety limits, redaction, token-bounded paging, 23 tracked native
  equations and Microsoft Open XML SDK validation of all three tracked equation
  documents.

## 0.23.0 — 2026-07-21

- Added `WordReferenceGraphBuilder`, a bounded source-linked read graph for
  fields, bookmarks and dependencies. It separates Word stories, pairs
  bookmark starts/ends across paragraphs, preserves table-column ranges,
  resolves names case-insensitively with duplicate-last provenance, parses
  nested complex fields and recursive `w:fldSimple`, and keeps parent/child
  field identities plus source ordinals.
- Added a bounded field-instruction tokenizer and broad field-family
  classification. Explicit and implicit REF, PAGEREF, NOTEREF, TOC bookmark
  restrictions, HYPERLINK anchors, SEQ, variables, merge fields, citations,
  index entries, styles and external-resource fields now produce typed
  dependency edges. Malformed quotes, orphan/missing field characters,
  excessive switches, deleted instruction text and unresolved targets remain
  diagnostics instead of guessed results.
- Added lazy `inspect_ooxml_references`. Its default summary returns only
  field-type counts and bounded diagnostics; bookmark names, instructions,
  cached result text and dependency keys are redacted unless explicitly
  requested. DDE/LINK/INCLUDE/IMPORT/DATABASE targets are never followed and
  no field is evaluated or allowed to launch an application.
- Extended the semantic projector with source-linked `bookmark_end` nodes and
  marker properties, so both endpoints can be addressed without raw XML.
- Added nine engine tests and two native integration tests, bringing the native
  document-engine totals to 141 engine tests and 57 host tests. Coverage
  includes every bundled DOCX fixture, cross-paragraph ranges, nested and
  simple fields, malformed structures, story isolation, external-field safety,
  privacy redaction and a sub-5000-character default response budget on a
  field-heavy TOC fixture.

## 0.22.0 — 2026-07-21

- Added the cross-platform `WordToolkit.Engine` .NET library with a bounded,
  immutable OPC package graph, content-type and relationship resolution,
  reachability diagnostics, opaque-part preservation and deterministic
  package fingerprints.
- Strengthened OPC relationship validation with XML-ID and RFC 3986 checks,
  fragment-preserving target resolution, reserved relationship-part rules,
  required relationship content types, and explicit rejection of targets that
  point at package infrastructure.
- Added hash-preconditioned package mutations, deterministic serialization and
  version-checked atomic file replacement with validation and optional backup.
- Added the token-bounded `inspect_ooxml_package` native MCP action. It inspects
  DOCX/DOCM/DOTX/DOTM files without opening Word or dereferencing external
  relationships.
- Added a source-linked semantic projection for paragraphs, runs, tables,
  fields, equations and every nested OfficeMath element, revisions, drawings,
  content controls, and unknown extension islands. Stable node IDs prefer
  durable Word anchors and do not depend on raw paragraph indices.
- Extended the projection across Word's primary text-bearing stories: headers,
  footers, footnotes, endnotes, comments, glossary building blocks and text
  boxes. Story roots, note/comment bodies and their references carry source
  part provenance and durable relationship or Word IDs; the existing main-body
  node IDs do not drift when a related story changes.
- Added a typed section graph and lazy `inspect_ooxml_sections` action. It
  resolves section boundaries, page geometry and all six header/footer slots,
  distinguishing explicit, inherited, blank and display-fallback bindings under
  `titlePg` and `evenAndOddHeaders`, and reports unbound story parts.
- Added a typed, source-linked style graph and lazy `inspect_ooxml_styles`
  action. It discovers transitional and strict styles relationships, validates
  the styles part and optional styles-with-effects part, models document
  defaults, latent-style exceptions, four style types, UI metadata and common
  paragraph/run properties, and reports missing, mismatched or circular
  `basedOn` chains without discarding the remaining style inventory.
- Added `WordEffectiveFormattingResolver` and lazy
  `resolve_ooxml_formatting`. One paragraph or run is rebound to its exact XML
  element and resolved through document defaults, base-first paragraph and
  character style chains, effective numbering levels, and direct formatting,
  with per-property provenance and standard toggle-property state transitions.
  The result explicitly lists unresolved application defaults, conditional
  table styles, revision views, unmodeled elements and Microsoft's documented
  compatibility divergences instead of claiming false visual certainty.
- Added a typed, source-linked numbering graph and lazy
  `inspect_ooxml_numbering` action. It discovers transitional and strict
  numbering relationships, validates the dedicated part, inventories picture
  bullets, abstract definitions, instances, levels and overrides, resolves
  `numStyleLink`/`styleLink` indirection and `startOverride`, and reports
  missing, circular, mismatched, recursive or out-of-range references without
  flattening the surviving graph. Level paragraph/run properties now enter the
  Word-specific formatting hierarchy after paragraph styles and before
  character styles and direct formatting.
- Added a typed, source-linked Office theme graph and lazy
  `inspect_ooxml_theme` action. It validates transitional and strict theme
  relationships, the theme content type and DrawingML root; models all twelve
  color slots, system-color fallbacks, major/minor primary and supplemental
  fonts, format-scheme inventories, unknown markup and bounded diagnostics; and
  exposes metadata-first paged color/font/format views without opening Word.
- Integrated theme values into effective formatting. Word font tokens such as
  `minorHAnsi` now resolve to concrete typefaces and theme colors resolve to
  derived RGB properties with theme-part provenance. Composite `rFonts`, color,
  underline and shading declarations now correctly cut off stale inherited
  attributes when a later style or direct-formatting element replaces them.
  `themeFontLang` now selects an explicit or likely ISO 15924 script for
  supplemental theme fonts, including region-sensitive Chinese and Punjabi
  behavior. Unmappable language, environmental colors, unsupported DrawingML
  transforms, and the measured difference between deterministic HSL math and
  Word's private color quantization stay explicit instead of being hidden behind
  fabricated values.
- Added a typed, source-linked settings graph and lazy
  `inspect_ooxml_settings` action. It models view/zoom defaults, theme font
  languages, compatibility settings and derived compatibility mode, legacy
  compatibility switches, document and write protection metadata, document
  variables, attached templates, mail-merge references and bounded root
  inventory. Sensitive values are redacted by default; protection hashes and
  salts never leave the engine.
- Added a typed, source-linked font-table graph and lazy `inspect_ooxml_fonts`
  action. It models declared font classification, PANOSE/signatures and all four
  embedded faces, validates exact font relationships and Word-readable content
  types, diagnoses duplicate names and orphan relationships, and exposes bounded
  metadata without returning font bytes. Effective formatting now cross-references
  concrete theme fonts against this graph and records declared/embedded/readable
  provenance.
- Added the first lossless XML source model with exact byte spans, original
  prefixes/attributes/quotes/BOM retention, bounded secure parsing, guarded
  non-overlapping splices, and validated UTF-8/UTF-16/UTF-32/single-byte edits.
- Added the first typed semantic mutation: hash- and fingerprint-preconditioned
  replacement of source-bound Word or OfficeMath text leaves, including XML
  escaping, `xml:space` handling and fail-closed mixed-markup behavior.
- Added bounded multi-text transaction planning. A plan parses each affected
  part once, rejects duplicate targets, predicts the result package fingerprint,
  creates one isolated forward mutation and retains an exact part-byte inverse
  guarded by the applied fingerprint. Compact plan metadata omits text content
  and per-text hashes.
- Added streaming semantic selectors and the lazy `query_ooxml_semantics`
  native action. Queries filter node kinds, exact properties, source parts and
  semantic subtrees; text predicates can cross run boundaries without
  flattening the document and results remain paged and preview-bounded.
- Added stateless lazy `plan_ooxml_text_edits` and
  `apply_ooxml_text_edits` actions. Apply must reproduce the reviewed plan ID
  and base fingerprint, writes through the atomic package transaction, retains
  a recovery backup by default, performs no write for a no-op, and fails closed
  on digitally signed packages.
- Added the lazy, token-bounded `inspect_ooxml_semantics` MCP action.
- Added 132 document-engine tests, including deterministic malformed-input,
  randomized relationship metadata and opaque-part round-trip fuzz smoke, plus
  300 randomized lexical text splices, multi-encoding/BOM preservation and
  semantic mutation provenance. Theme/settings/font tests cover strict and
  transitional packages, all twelve color slots, language/script font resolution,
  embedded faces, privacy boundaries, HSL tint/shade behavior, composite
  overrides, provenance, malformed/limited parts, and every matching part found
  in the bundled corpus from Word, LibreOffice, Pandoc, POI and Mammoth.
  The same corpus exercises every typed XML part and exact no-op source
  preservation, while native end-to-end package/semantic inspection retains
  all existing runtime tests.
- The native host now has 55 tests, including end-to-end semantic query,
  cross-story header query/apply, schema parity, recovery-backup, plan-mismatch,
  signed-package and no-op coverage plus theme/settings/font inspection and
  resolution that proves the saved-package path never invokes Word COM.
- Added a source-backed 2026 research matrix and the target lossless semantic
  document-engine architecture.

## 0.19.0 — 2026-07-20

- Replaced the eager 48-tool model surface with 10 common tools plus three
  lazy gateways for search, schema inspection and execution. All 48 native
  actions remain available without loading every schema into the AI context.
- Removed presentation-only JSON Schema titles from the exposed catalog.
- Added compact MCP responses that omit performance diagnostics, runtime
  boilerplate, repeated document metadata and echoed batch content. Full
  responses remain available explicitly through the execution gateway.
- Reduced the always-loaded tool catalog plus skill instructions from about
  15,065 to 2,489 estimated tokens, an 83.5% reduction.
- Added regression budgets for catalog size, lazy action coverage and compact
  mutation responses.

## 0.18.4 — 2026-07-20

- Fixed native Word Find and replace inside built-up OMath runs. Word may
  expand the next forward Find range back to the equation that contains the
  cursor; the bridge now skips that duplicate range and advances monotonically
  instead of rejecting the whole transaction as a backward range.
- Rebuilt the live equation atlas with proper function powers, preserved
  spacing between named operators and readable spacing in native cases.

## 0.18.3 — 2026-07-20

- Replaced Math AutoCorrect control words with their actual UnicodeMath
  structural characters before calling `OMath.BuildUp()`. Programmatic COM
  build-up does not expand keyboard-only commands such as `\matrix`, so the
  previous release could still display those commands as literal text.
- Matrices now use the native `■`, `⒨`, `ⓢ`, `⒱` and `⒩` enclosure markers;
  equation arrays use `█`, cases use `Ⓒ`, and accents use combining Unicode
  marks. These forms were verified visually in the real Word window.
- Limits now use the native below-script marker where Word requires it.
- MathML and OMML conversion now emit the same structural characters,
  including native function application, matrices, arrays, accents and boxes.

## 0.18.2 — 2026-07-20

- Fixed professional Word build-up for matrices, equation arrays, cases,
  accents, named functions and equation text. The converters now emit actual
  UnicodeMath control words such as `\matrix` and `\eqarray` instead of
  leaking internal strings such as `matrix(...)` into the document.
- Added correct delimiters for `pmatrix`, `bmatrix`, `vmatrix` and `Vmatrix`.
- Changed accent conversion to Word's postfix UnicodeMath form and mapped
  overbars, equation text and boxed expressions to native Word syntax.
- Applied the same structural rules to LaTeX, Presentation MathML and OMML
  input, with regression coverage for every repaired class.

## 0.18.1 — 2026-07-20

- Fixed native Word construction of integrals, sums, products and other
  n-ary equations. The LaTeX converter now emits Word's required `▒`
  boundary between operator limits and the equation body, so `OMath.BuildUp()`
  no longer absorbs the integrand into the upper limit or leaves an empty
  dotted operand box.
- Preserved LaTeX spacing commands such as `\,` as real Word linear-math
  boundaries, preventing a following differential such as `d x` from being
  absorbed into the preceding exponent.
- Applied the same n-ary boundary rule to Presentation MathML and OMML input.
- Added regression coverage for bounded and unbounded integrals, nested
  integrals, sums, MathML n-ary expressions and native OMML n-ary objects.
- Passed all 29 native runtime tests.

## 0.18.0 — 2026-07-20

- Expanded the self-contained native runtime from 29 to 48 Word Live tools.
  The restored native modules cover 17 story types and 23 structure
  collections, bounded item inspection, layout diagnosis, privacy-preserving
  aggregate learning, tokenized comments/revisions, typed table formulas,
  table-field recalculation, native bookmarks and allowlisted fields.
- Added a real installed-Word COM type-library scanner. On the release machine
  it cataloged 767 types and 12,167 members with zero scan errors and no
  truncation. The catalog is kept only in process memory and never stores
  document content, paths, help files or owner identifiers.
- Added one deterministic capability profile per installed member plus typed
  preflight and execution graphs. Execution accepts stable capability IDs,
  never raw names or dotted COM paths. Macros, DDE, print/mail/web effects,
  lifecycle actions, sensitive metadata, application-global mutations,
  events, restricted members, unknown writes and unsafe setters fail closed.
- Replaced the full-test `SendKeys` dependency with catalog-backed
  `Document.Range` plus `Range.Select`, two narrow non-content-changing
  operations. Live selection tests no longer depend on whichever window
  happened to own the keyboard focus.
- Fixed named C# tuple values crossing a `dynamic` COM boundary in locale-aware
  table formulas, bookmarks and field batches.
- Added native equation outcome counters and preflight regression tests for
  formulas, bookmarks and safe fields.
- Passed 25 native unit tests with zero build warnings.
- Passed a 48/48 real Microsoft Word acceptance: 71 MCP requests in 24.691 s,
  47 positive tools plus the guarded quit safety gate, valid Open XML,
  132,802-byte PDF, close/open/reconnect, and protection of one unrelated dirty
  document. The test document remained open and visible in Word.

## 0.17.0 — 2026-07-20

- Replaced the local Codex plugin runtime with a self-contained .NET 8
  Windows x64 MCP executable. The installed plugin launches
  `wordtoolkit-native.exe` directly and packages no Python, uv, pywin32,
  virtual environment, interpreter bootstrap or per-call helper process.
- Added a persistent COM STA host with Running Object Table attachment,
  bounded Word-busy retries through `IOleMessageFilter`, optimistic live
  versions, exact selection tokens, guarded top-entry Undo and transactional
  rollback.
- Added 29 native tools for the full explicit Word lifecycle, live document
  creation and connection, inspection,
  fast text and mixed batches, formatting, native Find and replacement,
  tables, lists, images, comments, footnotes/endnotes, headers/footers,
  LaTeX/UnicodeMath/MathML/OMML OMath, PDF export, save, close, quit,
  validation and disconnect.
  Removed unported local declarations instead of silently falling back to the
  Python runtime.
- Added direct COM Word activation and bounded existing-file opening for
  explicit absolute DOC, DOCX, DOCM, DOT, DOTX, DOTM, ODT, RTF, TXT, PDF,
  HTML/MHTML and XML paths. Macro execution is force-disabled and external
  links are not updated during open. Close and quit require explicit
  save/discard policy; untitled dirty documents fail before a blocking Save As
  prompt.
- Added content-bound native-Find range tokens so comments can be placed fully
  automatically without trusting stale raw coordinates or requiring the user
  to select text manually.
- Added secure Presentation MathML and OMML parsers with DTD/external-entity
  rejection, strict namespace/root checks, bounded depth and element counts,
  and conversion to editable native Word OMath.
- Added transactional native inline images, comments, notes, headers and
  footers plus native PDF export through a verified sibling temporary file.
- Added an in-process fail-closed LaTeX-to-UnicodeMath converter covering
  fractions, indexed radicals, scripts, n-ary operators, common symbols,
  functions, accents, text, matrices, aligned arrays and cases.
- Added a native self-contained build that rejects Python runtime files and
  verifies the direct MCP executable command before creating the ZIP.
- Measured a real 48,800-character, 100-operation Word mutation at
  259–268 ms versus 751.658 ms through the old Python bridge, about a 2.9×
  improvement. The packaged MCP initialized in 106.767 ms and spawned no
  Python or uv child process.
- Added real installed-cache acceptance tests covering the original insert,
  Find, replace, guarded Undo, table, list and LaTeX path with automatic
  restoration plus the expanded lifecycle and document-structure surface,
  saved-DOCX validation, PDF export, close and reopen.
- Created and validated
  `WordToolkit-Native-Mechanika-Kwantowa-2026-07-20.docx` through the packaged
  runtime: 16 paragraphs, four native equations, one native list and zero
  Microsoft Open XML SDK errors.
- Rejected and removed the NativeAOT experiment after real COM startup proved
  that dynamic COM requires a registered `ComWrappers` implementation.

## 0.16.2 — 2026-07-20

- Removed more than 1.2 GiB of historical release directories, smoke
  environments, old archives, test artifacts, caches, temporary storage and
  generated validator build output from the development workspace.
- Removed the obsolete embedded `src/docx_mcp/skill` copy and the unused
  `docx_mcp.cli` / `python -m docx_mcp` launcher that tried to auto-install a
  Claude-specific skill. WordToolkit now has one authoritative Codex skill and
  only its declared `wordtoolkit` and `wordtoolkit-stdio` entry points.
- Added `scripts/clean_workspace.py`. It is dry-run by default, constrains all
  targets to the repository root, collapses nested targets, handles Windows
  read-only files and requires `--apply` before deleting anything.
- Expanded ignore rules so build history, test artifacts, local schemas,
  caches, temporary Word Live storage and .NET build products do not grow back
  into the source tree.
- Added regression tests for the cleaner and a packaged-module import sweep.
  The tested `docx_mcp` compatibility package remains because it is the active
  OOXML engine, not dead code.

## 0.16.1 — 2026-07-20

- Fixed native numbered lists created by `manage_lists(action="apply")`.
  WordToolkit now writes an explicit `w:start` element and passes the bounded
  `start` argument through the local MCP layer. LibreOffice and Microsoft Word
  no longer get room to disagree about whether a fresh numbered list begins
  at zero or one.
- Added regression coverage for both the normal one-based start and an
  explicitly requested zero-based list.

## 0.16.0 — 2026-07-20

- Added `find_live_word_text` and `replace_live_word_text`. Native Word Find
  returns bounded ranges and context. Replacement discovers the complete
  bounded match set before mutation, edits ranges in reverse, restores Track
  Changes and groups the operation in one custom Undo record with rollback.
- Added token-safe live review. Comments can be added to a fresh selection,
  replied to, resolved or deleted; one inspected tracked revision can be
  accepted or rejected. Every existing-item mutation requires an HMAC token
  bound to its current range, metadata, content fingerprint, reply count and
  live version.
- Added explicit Track Changes control with read-back verification and manual
  rollback. It does not falsely claim that the property participates in Word
  custom Undo.
- Added bounded live layout diagnosis for keep-with-next chains, long headings,
  body page breaks, oversized keep-together paragraphs, disabled widow
  control, empty-paragraph runs, manual page breaks and heading-style overuse.
  It returns no paragraph text.
- Added guarded Undo. WordToolkit can undo only one current top entry labeled
  `WordToolkit:`, and only with a fresh HMAC token plus exact live version.
  Raw counts, unavailable history, stale stacks and intervening user actions
  fail closed.
- Added a disposable real-Word acceptance harness. Microsoft Word 16.0 passed
  Find, replacement, comment add/reply/resolve, Track Changes, tokenized
  revision acceptance, guarded Undo, same-path save and structural plus
  Microsoft Open XML SDK validation with zero errors.
- Audited `ykarapazar/word-mcp-live` at commit `c6c76179`. The comparison
  records both projects' strengths, the competitor's inconsistent tool counts
  and broken test bootstrap, and WordToolkit's remaining macOS and dedicated
  wrapper gaps without hiding them.

## 0.15.0 — 2026-07-20

- Expanded the installed-member registry from one policy profile per entry to
  one individually named virtual tool definition per entry. The refreshed Word
  8.7 catalog produces exactly 12,167 profiles, 12,167 unique virtual-tool
  names and 24,334 Draft 2020-12 input/output schemas.
- Added parameter-specific schemas for typed targets, positional arguments,
  result chaining, enum constants, optional COM omissions and member return
  values. A member-by-member audit validates every schema and hashes the full
  coverage surface so a missing or duplicated tool fails the release.
- Added typed `constant_id` arguments for all 3,756 installed Word enum values.
  Preflight resolves a constant only when its enum type matches the COM
  parameter; unchecked strings no longer pass as interface or enum arguments.
- Added an indexed-property adapter using the catalog DISPID and invocation
  kind. A real Word test proved that `Document.Compatibility(...)` accepts the
  adapter call but does not reliably participate in Word Undo. Therefore every
  indexed setter keeps its individual schema and adapter metadata but generic
  execution fails closed until that exact member has a proven reversible
  workflow.
- Corrected positional optional-argument handling. An omitted parameter before
  a later required value must use `{"missing": true}` and resolves to the
  native PyWin32 missing-argument sentinel only during local execution.

## 0.14.0 — 2026-07-20

- Added a deterministic capability registry with exactly one profile for every
  installed Word COM catalog member. On the release workstation the refreshed
  Word 8.7 catalog produced 12,167 profiles and 12,167 unique stable IDs with
  zero scan errors and no truncation.
- Added `inspect_live_word_member_capabilities`,
  `preflight_live_word_member_operations` and
  `execute_live_word_member_operations`. Together they provide a bounded
  browser, typed planning and one transactional executor instead of dumping
  12,167 near-duplicate MCP schemas into model context.
- Every profile records its target type, signature, accessor group, effect
  class and execution policy. Enum values remain constants, event callbacks
  remain non-invocable, and lifecycle, external, password, path, macro, DDE,
  print, mail, web and application-global actions fail closed.
- Catalog-backed execution accepts only stable capability IDs, document-rooted
  targets and typed results from earlier operations. It rejects arbitrary COM
  paths and unchecked member names, caps batches at 50 operations and 512 KiB,
  validates argument counts/types, and requires `expected_version` for writes.
- A mutating batch resolves the connected document inside one COM attachment,
  runs in one custom Word Undo record, rolls back on any native failure and
  advances the live version once only after the whole sequence succeeds.
- Corrected PyWin32 `FUNCDESC` decoding. Parameter count now comes from the
  parameter descriptor array, `cParamsOpt` remains the optional count, and the
  scanner retains invocation kind, call convention, vtable offset, variadic
  state, function flags, defaults and implemented-interface relationships.
- Real Word verification appended a marker through the catalog-backed
  `Range.InsertAfter` profile, observed it in the visible document, then
  removed it with the same custom Undo record. The document content and saved
  state were restored after the test.
- Reorganized the plugin skill into a short lifecycle router plus focused
  references for equations, structures, formatting, fields, tables, lists,
  learning and installed-member operations.

## 0.13.0 — 2026-07-20

- Added `inspect_live_word_object_model_types` and
  `inspect_live_word_object_model_members`, a paged read-only catalog built
  from the actual Microsoft Word COM type library installed on the local PC.
- The first explicit scan reads type, method, property, parameter, variable
  and enum metadata from already-running Word. The bounded atomic cache serves
  later queries without another COM attachment and can be refreshed after an
  Office update.
- The catalog stores no document content, document counts, paths, handles,
  owner identifiers, documentation strings or help-file paths. It is API
  discovery, not autonomous code mutation or fabricated edit support.
- Added `update_live_word_table_fields`, which refreshes up to 5,000 existing
  fields in one native table through one `Fields.Update()` call, one COM
  attachment and one Undo transaction.
- Field counts and numeric types are checked before and after refresh. A Word
  error or structural change rolls back the mutation; responses omit field
  codes, displayed results and document content.
- On the release workstation the installed Word 8.7 type library contained
  767 types and 12,167 callable/constant members. The 2.36 MB cache had zero
  scan errors; a cached query took 0.24 ms. A real two-formula table refresh
  completed in 233 ms with native return value zero.
- Plugin smoke and real-world tests now run `uv` in an isolated environment
  with bytecode writes disabled. Testing a release directory therefore no
  longer leaves a machine-bound `.venv`, editable-path references,
  `__pycache__` directories or `.pyc` files inside the package that is later
  installed.
- The final installed-cache test exposed 93 local tools, rebuilt its runtime
  against the installed cache path, indexed 767 Word types and 12,167 members,
  and refreshed two existing type-34 fields in 400.782 ms. Word returned zero,
  used one Undo transaction and left the open document saved.

## 0.12.0 — 2026-07-20

- Added `preflight_live_word_table_formulas` and
  `insert_live_word_table_formulas` for up to 200 typed calculations in one
  existing rectangular native Word table.
- Supports `SUM`, `AVERAGE`, `COUNT`, `MAX`, `MIN` and `PRODUCT` over one or
  two positional directions or a bounded source range generated from numeric
  row and column coordinates. Raw formulas and field codes remain unreachable.
- Destination cells must be empty unless `replace_existing=true` is explicit.
  The bridge validates all cells before mutation, uses one COM attachment and
  one Undo transaction, localizes separators once per batch, inserts native
  field type 34 directly at each cell range and checks calculated result
  ranges plus the final field count.
- Word calculates formula fields on insertion, so the default fast path avoids
  immediately recalculating every field a second time. `force_update=true`
  keeps an explicit verified recalculation path when it is genuinely needed.
- Responses return only coordinates, function classes, ranges, verification
  and performance metadata—never formula codes, source values or results.

## 0.11.0 — 2026-07-20

- Added `inspect_live_word_structure_items`, a read-only paged inspector for
  all 23 native collections already covered by the Word Live structure map.
- The inspector reads at most 200 items through one COM attachment and returns
  bounded semantic metadata such as native type, range, dimensions, style,
  lock state and structural identifiers. Optional text previews are capped at
  2,000 characters per item.
- Field codes and external hyperlink addresses are never returned. Property
  failures are isolated per item instead of aborting the entire collection.
- Structure learning now records only fixed property names, aggregate read
  successes/failures and timing. It never receives property values, text,
  document counts, paths, owners, handles or document-derived identifiers.
- Properties unavailable in the installed Word object model are retried at
  exponentially spaced inspection observations, while properties that have
  succeeded remain enabled for current-value reads.

## 0.10.0 — 2026-07-20

- Added `preflight_live_word_bookmarks` and
  `insert_live_word_bookmarks` for up to 200 native named ranges in one text
  assignment, one COM attachment and one Undo transaction.
- Bookmark names are bounded and case-insensitively unique. Existing-name
  collisions fail before mutation; every native name and exact range is
  verified before the live version advances.
- Added privacy-preserving structure learning and
  `inspect_live_word_structure_learning`. The store retains fixed collection
  names, native enum values, scan outcomes and timing, but never content,
  document counts, paths, handles or document-derived identifiers.
- `map_live_word_structures` now performs adaptive type scans at exponentially
  spaced presence observations. Dirty or truncated scans retry on the next
  presence, while an explicit histogram request still forces every scan.
- The live `reference` field workflow is now complete: create a verified native
  bookmark, then insert and update a native `REF` field targeting it.

## 0.9.0 — 2026-07-20

- Added `preflight_live_word_fields`, a read-only validation pass for up to 200
  allowlisted native Word fields without attaching to Word.
- Added `insert_live_word_fields`, which writes one marker payload and creates
  page, document-property, date/time, sequence, bookmark-reference and
  restricted numeric formula fields inside one COM attachment and one Undo
  transaction.
- Raw field codes, external-data field types, unbounded field switches,
  unknown formula names and bookmark references that do not exist are rejected
  before the document is mutated.
- Every inserted field is type-checked and updated by Word. A failed update,
  count mismatch or COM error rolls back the entire batch and leaves the live
  version unchanged.
- Formula expressions use a locale-neutral public syntax and are translated
  to Word's active list, decimal and thousands separators immediately before
  `Fields.Add`, so the same request works on English and Polish installations.
- Structured live equations now force native OMML readback. Normalized
  mathematical text and recursive child scope are compared with the prepared
  AST; Word build-up drift rolls the full transaction back instead of being
  reported as a native success.
- Equation learning classifies those failures as
  `NATIVE_FIDELITY_MISMATCH` without retaining formula text, document content,
  names or paths.

## 0.8.0 — 2026-07-19

- Added `insert_live_word_list` for up to 1,000 native bulleted or numbered
  paragraphs in an already-open Word document.
- The fast path assigns one validated paragraph payload and applies one
  `Range.ListFormat` operation, avoiding one COM write per list item.
- Added token-safe cursor/selection targets, optional style and formatting,
  optimistic versioning, screen-update suspension and full Undo rollback.
- Structure mapping now inventories Word `Lists` separately from
  `ListParagraphs` and optionally reports bounded `WdListType` histograms.

## 0.7.0 — 2026-07-19

- Added `insert_live_word_table` for native rectangular Word tables with up to
  200 rows, 50 columns and 5,000 cells.
- The fast path writes one validated TSV/paragraph payload and calls
  `Range.ConvertToTable`, avoiding one COM write per cell.
- Added style, repeating header row, fixed/content/window AutoFit and
  left/center/right table alignment controls.
- Added transactional rollback, optimistic versioning, token-safe cursor or
  selection targets, count verification and content-free results.

## 0.6.0 — 2026-07-19

- Added `format_live_word_selection`, which applies a style plus validated font
  and paragraph formatting to an exact non-empty Word selection protected by a
  fresh selection token, one COM attachment and one Undo record.
- Added the same formatting object to single live text insertion and mixed
  live batches, so generated text is inserted and formatted without a second
  connection or text round-trip.
- Added bounded support for fonts, point size, RGB color, emphasis, underline,
  highlighting, paragraph alignment, spacing, indentation, pagination and
  keep controls.
- Added numeric type histograms for OMath, fields, form fields, content
  controls, revisions, inline/floating shapes and styles. Inventories remain
  content-free; histograms are opt-in, default to 2,000 objects and hard-cap at
  10,000 objects per collection.

## 0.5.0 — 2026-07-19

- Added `map_live_word_structures`, a content-free inventory of all 17 Word
  story types plus more than 20 document collections including sections,
  styles, fields, bookmarks, revisions, content controls, shapes and notes.
- Added a privacy-preserving local equation outcome store. It records only
  structural feature classes, input formats, success/failure counts, readback
  outcomes and timings; formula text, document text, paths and owners never
  enter the store.
- Added adaptive equation policy: a structural class with real Word failures
  automatically forces native OMML readback on subsequent matching formulas.
- Added `inspect_live_word_equation_learning` so the learned aggregate policy
  is auditable instead of hidden.
- Added bounded, atomic learning persistence and corruption-safe fallback.

## 0.4.0 — 2026-07-19

- Added `apply_live_word_operations`, which preflights and appends up to 200
  interleaved text/equation operations through one COM attachment, one bulk
  text assignment and one Word Undo transaction.
- Added optional temporary `ScreenUpdating` suspension with exact restoration
  after success or failure.
- Added `preflight_live_word_equations`, a non-mutating syntax pass that
  returns canonical AST, Word linear math, rule hits, warnings and native
  readback requirements for up to 200 formulas.
- Added mandatory live symbol-preservation checks for hbar and dagger notation;
  Word now rolls back the mutation if its OMath parser drops either symbol.
- Added a durable equation-authoring rule reference and a live/isolated Word
  structure routing catalog to the plugin skill.

## 0.3.1 — 2026-07-19

- Added a local Word Live equation batch tool that inserts up to 100 native equations in one COM operation.
- Added an optional fast path that verifies native OMath counts without per-equation OMML AST readback; full readback remains opt-in.

## 0.3.0 — 2026-07-19

- Added nine Windows-only Word Live MCP tools to list, connect, inspect, edit,
  validate, save and disconnect documents already open in Microsoft Word.
- Added direct native `OMath` insertion through Word COM with explicit display
  or inline type, build-up, OOXML read-back verification and transactional Undo
  rollback on failure.
- Added cursor and selection tokens, explicit selection-replacement consent,
  optimistic live versions and read-only/protection/final-document checks.
- Added same-path `Document.Save()` plus temporary validation copies made only
  from an already-saved DOCX, without exporting a new user file.
- Kept Word Live out of the remote HTTP server. It is exposed only by the local
  STDIO plugin and never launches, closes or quits Microsoft Word.

## 0.2.0 — 2026-07-18

- Added a local MCP STDIO server for a one-install Codex workflow without a
  public domain, Docker daemon, OAuth tenant or background HTTP process.
- Added explicit local-path and `file://` inputs plus durable local artifact
  links for exported DOCX, PDF and PNG files.
- Preserved the authenticated Streamable HTTP deployment mode and made
  `local_stdio` impossible to use through the HTTP application.
- Added Windows discovery for LibreOffice and Poppler, including safe process
  environment handling and correct file-URI profile paths.
- Added a self-contained plugin build that bundles the Python runtime source,
  lockfile and optional Microsoft Open XML SDK validator.
- Fixed Windows ZIP part names in metadata sanitization, PII redaction, raw-part
  listing and upstream repacking. The old backslash paths could leave original
  XML beside sanitized or redacted XML inside a DOCX.

## 0.1.1 — 2026-07-18

- Added the advanced OPC/OOXML/OMML acceptance and visual torture test.
- Fixed root relationship security inspection and orphan-part detection.
- Fixed semantic LaTeX, UnicodeMath and MathML export/reparse edge cases.
- Fixed table geometry, inline DrawingML metadata ordering and section-aware
  layout-risk analysis.
- Made Poppler page rendering deterministic and resistant to stale or truncated
  preview files.
- Added nine-page validated DOCX/PDF evidence and CI release gates.

## 0.1.0 — 2026-07-18

- Initial remote Streamable HTTP MCP, document engine, native Office Math,
  security boundary, rendering pipeline, plugin manifest and deployment assets.
