# Stage results

# WordToolkit 0.51.0 high-level content-free document analysis - 2026-07-26

- Added `wordtoolkit.analyze_ooxml_document/1.0` once in the neutral Engine and exposed
  that same strict contract through `analyze-package` CLI and lazy
  `analyze_ooxml_document` MCP. One fingerprint-bound read joins OPC structure, semantic
  object counts, dependency diagnostics, all native lint packs, grouped repair
  opportunities, typed active-content/external-link presence and markup-compatibility
  evidence into at most 32 deterministic exact next-action signals.
- The response has no document-text, raw-XML, source-location, external-target or binary
  field and never opens Word, follows a relationship, executes active content or mutates
  the package. Execution completeness is separate from document coverage, semantic
  completeness and operation-budget coverage; rendered layout, active-content internals,
  signature cryptography, encryption, coauthoring and a complete target-application MCE
  profile remain explicit gaps.
- A real equation-heavy DOCX exposed a rotten classification in the first draft: four
  ordinary lint errors were called a critical structural/fatal failure even though OPC
  validity was true. The final code emits `STRUCTURAL_PACKAGE_INVALID` only for actual
  structural invalidity or fatal lint evidence and routes ordinary errors through the
  separate blocking `LINT_ERROR_FINDINGS` signal.
- The closed MCP regression proves zero COM invocations, rejects secret body-text leakage,
  validates the complete output schema and keeps the representative default compact result
  below 7,500 characters. Determinism, stale-fingerprint rejection, unknown-field
  rejection, bounded signal paging, active-content classification and stream-position
  restoration are also covered.
- Pinned SDK 8.0.423 gates pass 744 Engine, 12 LibreOffice and 535 Native tests. Ruff is
  clean; Python compatibility passes 1,318 with 16 intentional skips; four changed .NET
  projects pass format verification; schema export is clean; the standalone Open XML
  validator builds with zero warnings.
- Two complete package builds produced byte-identical 197-file, 89,695,110-byte trees and
  byte-identical 37,474,662-byte ZIPs at SHA-256
  `41f3a59f22341803fcaca22e3c339900b87ac532eaa54ec2710633e3857064d8`.
  Executable, runtime assembly, Engine, LibreOffice and Open XML SDK hashes are
  `fbf941ce9de529a0e3a8c278fa510459bd82a87f73f66aca83ecb45cbb1a467e`,
  `8690d1cd456832509c1de8151cb7ae26a6033844e314055dab3a4e428f287948`,
  `70a1504cdac5b258edb3ea4047605736930cbf7a2227f6495df1deec9abd2f37`,
  `41a1f0ed01d3dbaa3b13deee5748a7ab55924ab63bd616d645c5ed597d09127c`
  and `1adf0c66256e00b29a272275e7b09c4e70f4378a12b6565f6ced255f74f800a`.
- Installed and enabled `0.51.0+codex.20260726154529`. The canonical candidate,
  `C:\Users\Admin\plugins\wordtoolkit` and active cache contain the same 197 paths,
  lengths and SHA-256 values. Installed discovery reports 146 actions, 15 MCP tools and
  57 complete explicit metadata contracts. The previous source remains intact at
  `C:\Users\Admin\plugins\wordtoolkit.backup-0.50.1-codex.20260726150446`.
- Hosted CI qualification is not claimed until the exact committed branch passes.

# WordToolkit 0.50.1 LibreOffice four-package PDF/PNG qualification - 2026-07-26

- Expanded the gated real public render test from one DOCX/PDF observation to minimal
  DOCX, DOCM, DOTX and DOTM sources rendered as PDF plus PNG pages at 96 DPI. The test
  verifies exact artifact hashes and lengths, PDF page count and MediaBox-derived PNG
  geometry, manifest fields, unchanged source bytes, absence of Word invocation and
  deletion of both LibreOffice and Poppler private workspaces.
- The broader corpus found a real defect: DOTX was instantiated as a new editable
  document and the helper correctly failed closed with `READ_ONLY_NOT_VERIFIED`. The
  Java UNO load descriptor now sets `AsTemplate=false` independently of `ReadOnly=true`,
  preserving the source location and making read-only verification meaningful for
  DOTX/DOTM. The deterministic 9,026-byte embedded helper SHA-256 is
  `cc252d63ff7a0737d261bfc76a9d211b5d0e5303a2cb6ea245db6707eda9ce91`.
- Hosted run <https://github.com/Fr4u/WordToolkit/actions/runs/30203138387> passed all six
  jobs at `da207cda4696312e8db2da28c1ab99dff7966b6a`. Its Ubuntu 24.04 lane qualified
  LibreOffice 24.2.7.2, Temurin 17.0.16, `pdfinfo` 24.02.0 and `pdftoppm` 24.02.0;
  twelve provider tests and seven public-layer tests passed, including the real
  four-package PDF/PNG case in four seconds.
- The DOCM/DOTM fixtures contain no adversarial VBA payload. They prove package-type
  routing, not macro or external-update prevention; those flags remain false. A local
  Windows attempt using LibreOffice 26.2.4.2, Temurin 17.0.16 and Poppler 25.07.0 timed
  out while connecting to the backend, so that tuple is explicitly unqualified.
- The release bump exposed one stale hard-coded MCP test expectation (`0.50.0`); the
  runtime already reported `0.50.1`, so the test was corrected and rerun. Two complete
  package builds with pinned SDK 8.0.423 then passed 739 Engine, 12 LibreOffice and 532
  Native tests each. Both expanded trees contain 197 files and 89,608,923 bytes with
  zero path/length/hash differences. Both 37,448,758-byte ZIPs have SHA-256
  `9ccd63f2c8083de4ca89ad3a01a65a0f5024e734c5512333e77d4920b34b6e7c`.
- The release executable, runtime assembly, Engine, LibreOffice and Open XML SDK
  adapter hashes are
  `dfcf355207f19481ffc6875fa5fc84c40386138fbfe4a8ea6cd5361d1ba4453a`,
  `5617bde332e6450d7961f7c5467dbf42ba33b15c778438aebd74e346dcba8d44`,
  `7d4750def2bf6be6abba3a79d415cbcf1f76d94e657c2ee875869e08552484f3`,
  `dd7f8519c6a8daae16f7fc9c175a95368e566bfe95baf9605a09376d4877a06a`
  and `a3b3dd6347f416a0a91a571682869910ef7665667a247be4ac8f66acf1390d72`.
- Installed and enabled `0.50.1+codex.20260726150446`. The canonical candidate,
  `C:\Users\Admin\plugins\wordtoolkit` and active cache contain the same 197 paths,
  lengths and SHA-256 values. Installed discovery reports 145 actions, 15 MCP tools and
  56 complete explicit metadata contracts. The previous source remains intact at
  `C:\Users\Admin\plugins\wordtoolkit.backup-0.50.0-codex.20260726142219`.

# LibreOffice isolated Writer artifact transaction - 2026-07-26

- Added the public `wordtoolkit.render_ooxml_libreoffice_artifacts/1.0` action and strict
  `libreoffice-render-package` JSON CLI. Both require exact local paths and expected
  SHA-256 identities for LibreOffice, Java and the resolved LibreOffice Java archive;
  the reviewed WordToolkit helper is embedded and cannot be replaced by a caller.
- The action validates the OPC package and expected fingerprint before any process,
  rehashes the source independently before publication, deletes private process/profile/
  PDF/raster staging first, then publishes the PDF, optional PNG pages and manifest as one
  create-new transaction. It never opens Word and has no hidden backend or PATH fallback.
- Unit and closed-schema tests prove success, stale fingerprint rejection, source-drift
  rejection, unknown-field rejection and rollback when an output already exists. Hosted
  run <https://github.com/Fr4u/WordToolkit/actions/runs/30201432923> passed all six jobs;
  its Ubuntu 24.04 lane ran the exact public action through LibreOffice 24.2.7.2 and JDK
  17, passing seven public-layer and twelve provider tests.
- The result calls the output `libreoffice_writer_fixed_layout`, not Word-authoritative
  layout. Macro `NEVER_EXECUTE` and update `NO_UPDATE` are recorded as requests; behavioral
  prevention remains explicitly false until adversarial active-content probes exist.
- Two pinned-SDK 8.0.423 package builds produced byte-identical 197-file,
  89,608,757-byte trees and byte-identical 37,448,673-byte ZIPs at SHA-256
  `d6f1dea5a29022b41516acf7ff445e5849c5b46529c3f482591fed5bfddcd98f`.
  Runtime executable, runtime assembly, Engine, LibreOffice and Open XML SDK hashes are
  `3b157e2ab69ea5ed17e421c3023e0fd0ef7dfe9f430be1398785e03c1c10f439`,
  `9af5a21c51834e572e57cb6bcafc6cb6c9e501f13cb005d922d5e78fbddfaa16`,
  `3abbf6a090fb35abf7c9e12b790ab5a24650123c01a8027c3f7933ca0c450077`,
  `8b14d5c918b515543bc6f56e9a2ef936d3b079c64117d091c2c6ac4f5c349b9d`
  and `3faaa2a513fedfafa1be6397ee98931c1f662fc2b27d9c4cb3dd9d88fccd538b`.
- Installed and enabled `0.50.0+codex.20260726142219`. The canonical candidate,
  `C:\Users\Admin\plugins\wordtoolkit` and active cache contain the same 197 paths,
  lengths and SHA-256 values. The installed executable reports 145 actions, 15 MCP tools
  and 56 complete explicit metadata contracts; the retained source backup is
  `C:\Users\Admin\plugins\wordtoolkit.backup-0.49.0-codex.20260726122100`.

# WordToolkit 0.49.0 explicit LibreOffice backend identity - 2026-07-26

- Added neutral `WordToolkit.LibreOffice` (`net8.0`) and one shared strict operation,
  `wordtoolkit.inspect_libreoffice_backend/1.0`, exposed through direct Engine, the
  `libreoffice-backend` JSON CLI and lazy MCP. The request requires one absolute local
  executable, never searches `PATH`, rejects UNC/device/mapped-network/reparse paths,
  optionally enforces an expected SHA-256, runs only bounded `--version` with closed
  stdin and process-tree timeout termination, and rehashes after exit.
- The result returns a normalized reported product/version, executable filename/size/hash
  and coarse host architecture without returning the path, environment values or process
  diagnostics. It proves no UNO connection, Writer component, PDF export, document-load
  policy, macro prevention, external-update prevention, rendering or Word fidelity.
  Network is not requested but is not isolated. A reported banner is not a vendor-
  signature proof, and pre/post hashes are not an atomic OS-loader binding.
- Local pinned-SDK gates pass 739 Engine, 8 neutral LibreOffice-adapter and 522 Native
  tests. Python compatibility passes 1,318 with 16 intentional skips; Ruff, six .NET
  format gates, schema export, standalone OpenXmlValidator and `git diff --check` are
  clean.
- Hosted CI run
  <https://github.com/Fr4u/WordToolkit/actions/runs/30198749524> passed six jobs on
  `76ee60f`. The dedicated Linux lane recorded LibreOffice
  `24.2.7.2 420(Build:2)` with executable SHA-256
  `eef555c71025262c67274dc6e98d00168c2a2ce0fcd16473c38609ff3ce2ace9`,
  supplied that digest as the adapter's expected hash and passed the real probe.
- Two independent package builds contain 197 files and 89,444,147 bytes with zero
  path/length/hash differences. Their 37,397,666-byte ZIPs are byte-identical at SHA-256
  `30a9c9ad4a4291969c0e723e9eaeb68be6234f266bc90161038af97635abb927`.
  Runtime executable, runtime assembly, Engine, LibreOffice adapter and Open XML SDK
  adapter hashes are `3c3d568bcc01f4c747a1efee0c33cca1146d33d811f94e395857966e3288395b`,
  `fb85dbfc03c08cbcf9abdfdb56ed9cabbc294328c935f1c2a3941d55409c5356`,
  `aed2f4ca934bfec9b18e3d8f4ca0ae1fac75c80df23e8d874e6d7df49fcc167c`,
  `b924d9b42d73a1cc0328b7bac6255465cac5538b19c43d4ac4eaa7f5dbfab82c`
  and `43f5191d6fe622925087556727c681caac0b1db157f495527a0f08e259b90a46`.
- Installed and enabled `0.49.0+codex.20260726122100`. Canonical build, persistent
  personal source and active cache contain the same 197 paths/lengths/hashes. Installed
  capability discovery reports 144 actions, 15 MCP tools and 55 complete explicit
  metadata contracts. The previous persistent source is retained at
  `C:\Users\Admin\plugins\wordtoolkit.backup-0.48.0-codex.20260726112849`.
- This tranche is not a LibreOffice renderer. One-shot UNO loading with explicit
  `MacroExecutionMode`/`UpdateDocMode`, transactional PDF/PNG/manifest publication and the
  shared Word-versus-LibreOffice visual corpus remain missing.

## WordToolkit 0.48.0 semantic role evidence graph - 2026-07-26

- Added a separate source-linked role graph for theorem, lemma, proposition, corollary,
  definition, proof, example, remark, axiom and assumption paragraphs. Exact enclosing
  `wordtoolkit:role=<role>` SDT declarations, exact explicit/inherited paragraph-style
  conventions and strict Polish/English leading labels remain independent evidence
  channels. Conflicts choose no winner and semantic completeness is never claimed.
- Direct Engine, strict `semantic-role-package` CLI and lazy
  `inspect_ooxml_semantic_roles` MCP share one closed JSON contract. Default output is
  usable main-story theorem candidates without paragraph text or evidence identities;
  paging is fingerprint-bound and evidence, style/control identity, hashes, source and
  text are separately gated. Raw XML and Custom XML values have no response field.
- False-positive and failure boundaries are explicit: an inline run-level SDT cannot
  declare the enclosing paragraph, default style/typography/numbering/fuzzy similarity/
  private XML vocabulary names are not evidence, unresolved style chains reduce coverage
  and exhausted text/evidence/issue budgets fail closed.
- Full local gates pass **731 Engine**, **513 Native** and **1,318 Python tests**, with 16
  intentional Python skips. Ruff, four .NET format gates, deterministic schema export and
  the standalone Open XML SDK validator are clean. Every .NET command used pinned SDK
  `C:\Users\Admin\.dotnet8\dotnet.exe` 8.0.423.
- Two independent self-contained win-x64 builds produced byte-identical 196-file,
  89,352,581-byte trees and byte-identical 37,370,393-byte ZIPs at SHA-256
  `b50acb29b74e1f1d705ecd89f9be768cf289a2b7a144d0526befdb4f2963c3fa`.
  Executable, runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `39645d966ec73e9bb1211030b02328c24600f9353f6f8ab16a4296b95b9948ed`,
  `03bc1c8dd86e523d4056b09caadac4b4af638225de057d9b348c11f97b56eb6b`,
  `6709ea67cdda7b4b2176baa4a8d26041639b3225e8deb24c80497da17a23466b` and
  `5f30355da29e8992091a41a63825105c401eacfde161f29a92bb7571222481ea`.
- Installed and enabled `0.48.0+codex.20260726112849`. Canonical build, persistent
  personal source and enabled cache contain the same 196 paths, lengths and hashes with
  zero differences. Installed capability discovery reports 143 actions, 15 MCP tools and
  54 complete explicit metadata contracts and returns exactly
  `inspect_ooxml_semantic_roles` for a `semantic_role` query.
- A guarded Word 16.0 build 16.0.20131 acceptance test saved the fixture and then passed
  Microsoft Open XML SDK validation. The engine recovered lexical, explicit-style and
  enclosing-SDT theorem candidates after Word normalization and did not promote an
  inline SDT to paragraph evidence.
- Cross-paragraph theorem extent, proof-to-theorem linkage, caller-defined semantic
  profiles/dictionaries, broader languages, chosen revision/MCE views, mutation and a
  qualified cross-version corpus remain missing. This release closes one conservative
  read-only slice of `find every theorem`; it does not close the full document-engine
  objective.

## WordToolkit 0.47.0 semantic numbering reconstruction - 2026-07-26

- Added a separate reconstruction engine instead of widening the old list-tail restart.
  Exact fingerprinted paragraphs receive typed single-level, multilevel or hybrid
  blueprints; the engine allocates new abstract/instance IDs and can create the numbering
  OPC part, content-type override and main-part relationship when they are absent. Existing
  definitions and every unselected paragraph remain unchanged.
- The public vocabulary covers decimal, zero-padded decimal, upper/lower Roman,
  upper/lower Latin letter, bullet and none formats plus explicit starts, level text,
  restart policy, legal numbering, suffix, justification and bounded twip geometry. Raw
  XML, namespaces, relationship IDs and numbering IDs never cross the AI boundary.
- Direct Engine, strict `numbering-rebuild-package --mode inspect|plan|apply` CLI and lazy
  `inspect_ooxml_numbering_rebuild_candidates`, `plan_ooxml_numbering_rebuild` and
  `apply_ooxml_numbering_rebuild` MCP share one strict JSON parser and planner. Planning
  proves candidate freshness, exact counters/labels, unchanged unselected numbering and
  semantic topology, zero new Microsoft errors, predicted fingerprint and byte-exact
  inverse before apply. Apply recomputes the plan and retains a sibling backup by default.
- Full local gates pass **711 Engine**, **509 Native** and **1,318 Python tests**, with 16
  intentional Python skips. Ruff, four .NET format gates, deterministic schema export and
  the standalone Open XML SDK validator are clean. Every .NET command used pinned SDK
  `C:\Users\Admin\.dotnet8\dotnet.exe` 8.0.423.
- Two independent self-contained win-x64 builds produced byte-identical 196-file,
  89,248,326-byte trees and byte-identical 37,341,668-byte ZIPs at SHA-256
  `11bf921e29d9552d849b231d6a77b21d69666bfb58959fee674c4036c5a2016d`.
  Executable, runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `821541df95ee9590a9fbd711e3608fe5292179044ca822639b50e423caee3a67`,
  `9f74bac9f6e5a781fd5aa7063b61e164a20c4a99b7ac33765330ed1244e76334`,
  `0184b95c88d8b3bf097277456991e2a735b605c5e1757ef34d537f2d7031277e` and
  `9519100ec9b6c491306e50614017223a40425a607a649171b3ffc54ea4efe0a9`.
- Installed and enabled `0.47.0+codex.20260726102718`. Canonical build, persistent
  personal source and enabled cache contain the same 196 paths, lengths and hashes with
  zero differences. Installed capability discovery reports 142 actions, 15 MCP tools and
  53 complete explicit metadata contracts and returns exactly the three reconstruction
  actions for a `numbering_rebuild` query.
- The final guarded Word 16.0 build 16.0.20131 proof created a missing numbering part and
  returned engine and Word labels `1.`, `1.a)`, `1.b)`, `2.` exactly. Microsoft Open XML
  SDK validation is clean; Word opened read-only, exported PDF and left the source hash
  unchanged. The retained DOCX/PDF hashes are
  `d0cac3eb9b0d2a0ba07582394ea9ac00ae8ebbb3edf6a9254a7c79287167acec` and
  `88b691133ee266879279fddd678011142a928fffbfb154f583776c15bc388ac9`.
  A 160-DPI Poppler raster is one clean page with aligned native labels and no clipping,
  overlap, raw XML or broken glyphs.
- Picture bullets, locale/custom formats, bidirectional layout, revision-view selection,
  style-definition binding, field refresh and list merging remain explicit missing
  capabilities. This release closes one vertical slice of `rebuild numbering`; it does
  not close the full document-engine objective.

## WordToolkit 0.46.0 equation-paragraph text-slot rewrite - 2026-07-26

- Added a deliberately narrow semantic operation for rewriting only ordinary paragraph
  prose before, between and after direct native OfficeMath anchors. Paragraph/run objects
  and properties are retained; every direct `m:oMath` or `m:oMathPara` byte must remain
  identical. Fields, hyperlinks, revisions, bookmarks/range markers, content controls,
  drawings, tabs, breaks and other rich inline structures fail closed instead of being
  flattened.
- Added exact package/candidate/plan fingerprints, deterministic `wepr_` and `weprplan_`
  identities, selected-slot readback, unselected-candidate invariants, exact predicted
  result fingerprints and an inverse that reconstructs every original uncompressed OPC
  entry byte. Signed packages, missing SDK validation, plan drift, empty-gap insertion,
  structural drift and any equation-byte change block apply.
- One strict Engine JSON codec now drives direct .NET, the
  `equation-paragraph-rewrite-package --mode inspect|plan|apply` CLI and lazy
  `inspect_ooxml_equation_paragraph_rewrites`,
  `plan_ooxml_equation_paragraph_rewrites` and
  `apply_ooxml_equation_paragraph_rewrites` MCP actions. Ordinary plan/apply responses
  return neither paragraph text nor OMML and never open Word. The native catalogue now
  exposes 142 actions, 15 MCP tools and 53 complete explicit metadata contracts.
- Full local gates pass **692 Engine**, **505 Native** and **1,318 Python tests**, with
  16 intentional Python skips. Ruff and four .NET format gates are clean. Every .NET
  invocation used pinned SDK `C:\Users\Admin\.dotnet8\dotnet.exe` 8.0.423.
- Two independent and one canonical self-contained win-x64 builds produced identical
  196-file, 89,069,292-byte trees and identical 37,290,276-byte ZIPs at SHA-256
  `1277483e8682209dd3176739deb853816a87e7d5ec58b6644bfb36a1a744c672`.
  Executable, runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `4ed16c8da74a1f1fdbe8e11604b1ba11265757f31e9fa303d10e82c5a21f958f`,
  `39d7f66102b215c72b4681c44da6f71d357fdfa6a745e746ce8ca321ca63a5e1`,
  `a91b3f297d233c392f94932a5595739d400530e16426c08ad74f5f4e7d46b584` and
  `3d51488db5c070a1d2d1f4307033b635b9bc97e6a5af6c52ef860b71854a72f4`.
- Installed and enabled `0.46.0+codex.20260726083217`. Canonical build, persistent
  personal source and enabled Codex cache contain the same 196 paths, lengths and hashes
  with zero differences. Installed executable discovery reports the exact version and the
  three new actions; the complete 1,049-line installed skill was reread after installation.
- A real Word-created paragraph contained two prose slots around one editable inline
  equation `x^2+y^2=1`. The packaged runtime changed only the two prose slots, retained a
  sibling backup identical to the 13,492-byte original and produced the exact predicted
  target package. The complete 916-byte native `m:oMath` outer XML remained byte-identical
  at SHA-256 `0775ce309c40b1c20f6107985717f76eed6987019dce4b23809d5cd0dbdc992f`.
  Baseline and candidate have zero Microsoft Open XML SDK errors.
- Microsoft Word 16.0 build 16.0.20131 opened the result as a real read-only document with
  one paragraph and one native equation, then independently rendered original and result
  through its fixed-layout exporter. Both are one clean 1191x1684 PNG page at 144 DPI;
  visual inspection shows only the requested prose change, with the equation intact and no
  raw linear syntax, clipping or overlap. Word rechecked both source hashes after close.
- The checked-in token point records compact inspect/inspect-with-text/plan/apply requests
  of 310/355/546/617 characters and compact responses of 888/1,272/1,079/909 characters.
  Apply returns exact equation-byte, paragraph-structure, inverse and zero-error SDK proof
  without document prose or OMML.
- Mandatory hosted CI run `30192186347` passed all five jobs on implementation commit
  `505cb4154f4baa55240c3d77807633e511b1bdb5`: Linux Engine, Windows Native tests and
  distributable ZIP, Python compatibility/rendering, standalone Open XML validator and
  remote service container. The downloaded 37,290,276-byte Windows artifact is byte-for-
  byte identical to the local canonical ZIP at SHA-256
  `1277483e8682209dd3176739deb853816a87e7d5ec58b6644bfb36a1a744c672`.

## WordToolkit 0.45.0 deterministic template style alignment - 2026-07-26

- Added `WordTemplateStyleAlignmentPlanner` as a two-package, stable-ID operation rather
  than a `styles.xml` copy. One reviewed root expands through complete `basedOn`, `next`,
  linked-style, numbering-style-link, style-link and numbering-level paragraph-style
  dependencies. Existing target types must match. Localized visible names never define
  identity.
- Transitional and Strict Word namespaces are normalized only for the standard Word
  vocabulary. Extension namespaces, comments, processing instructions and opaque style
  children remain significant and are preserved. Target-only and unselected styles and
  every unrelated OPC entry retain their prior bytes. A symmetric `stylesWithEffects`
  part is mirrored for the same selected IDs; an asymmetric pair fails closed.
- Theme-dependent closures require equal canonical theme content and `themeFontLang`.
  Numbered closures require the exact target/template numbering instances and abstract
  definitions to be equivalent; picture bullets fail closed. The aligner does not attach
  or mutate the template, migrate themes/numbering, restyle content, infer roles from
  names or claim visual equivalence across Word versions.
- Added strict public inspect/plan/apply Engine contracts, a shared
  `template-style-alignment-package` CLI and three lazy MCP actions. Both package
  fingerprints, every candidate fingerprint and the deterministic `wtsaplan_` ID are
  reproduced at apply. Apply requires Microsoft Open XML SDK validation, blocks signed
  targets, re-reads the template immediately before atomic target publication and keeps
  a sibling recovery backup by default.
- Candidate validation reparses the exact package, reprojects semantics, verifies the
  complete selected closure against the template, re-proves theme and numbering context,
  rejects new style or numbering issues, checks unplanned entry hashes and constructs an
  exact inverse. Public responses return no document text or raw XML and never open Word.
- Full local gates pass **686 Engine**, **502 Native** and **1,313 Python tests**, with
  16 intentional Python skips. Ruff, both .NET format gates, remote schema export, sample
  generation, the 11-page/17-equation torture document and the standalone Open XML SDK
  validator are clean. Every .NET command used pinned SDK
  `C:\Users\Admin\.dotnet8\dotnet.exe` 8.0.423.
- Two self-contained win-x64 builds produced byte-identical 196-file,
  88,930,113-byte trees and byte-identical 37,257,187-byte ZIPs at SHA-256
  `6bea91fc4d577c13cd47b26be991da01d7a321efd7849ebef2ae013c81e98931`.
  Executable, runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `517f551469ddc62b7eb8946ff983b394b4457b76ddf666dc6d70e0882edd6758`,
  `40e24063ea9353319c8f3b42da7d3c3968ba19d088bac07801d017224caabe19`,
  `71a0d3786e6dc19499c8d4e72fb010577e4f835e86e311f5391ab4f10976c6a0` and
  `c9ba519f9cc857136bcab65a2b0db494a0340b26db11347aac27efa563349e05`.
- Installed and enabled `0.45.0+codex.20260726070205`. Canonical build, persistent
  personal source and enabled Codex cache contain the same 196 paths, lengths and hashes.
  Installed discovery reports 136 actions, 15 tools and 47 complete metadata contracts;
  a template-style-alignment query returns exactly the inspect/plan/apply trio. The full
  1,019-line installed skill was reread after installation.
- A real Word-created target cloned `Normal` into `AlignedStyle`; a separate Word-created
  template cloned `Title` into the same stable ID. The packaged 0.45 runtime inspected
  candidate `wtsa_9e36e30bc87e5607a5cfa622`, expanded dependency `Normal`, planned
  `wtsaplan_naZNVR0YLYKg3qDzMd-6j0zw`, replaced one style in one part and reproduced the
  exact predicted target fingerprint
  `685e22ac9eb6ddc657e7cbe8bea49e24b5d146780d211f8469c5a2ce74b55a52`.
  Template package fingerprint
  `7908eae79fa5430e0b3b46ce041ed3de7b0b8c740ff68df11ce04f5a792ebaf2`
  and file SHA-256 remained unchanged. Both final packages have zero Open XML SDK errors.
- Microsoft Word 16.0 build 16.0.20131 rendered the before and after packages read-only,
  rechecked their hashes after close and produced one 1191x1684 PNG page for each at
  144 DPI. Visual inspection shows the same paragraph changing from ordinary body text
  to the large `Title`-derived appearance, with no clipping, overlap, raw XML or content
  change. The first render attempt deliberately failed closed when a `.cmd` Poppler shim
  returned exit code 3; direct verified Poppler executables then completed the transaction.
- The exact installed executable repeated inspect/plan/apply against a fresh copy of the
  pre-alignment target. It reproduced the same candidate ID, plan ID and final fingerprint,
  retained an existing backup and passed the standalone Microsoft validator with zero
  errors. `codex plugin list --json` reports the personal plugin installed and enabled.
- Mandatory hosted CI run `30189985502` passed all five jobs on implementation commit
  `4f88eaf32d5fb860b99fbc02f22eadf89e85dcb1`: Linux Engine, Windows Native tests and
  distributable ZIP, Python compatibility/rendering, standalone Open XML validator and
  remote service container.

## WordToolkit 0.44.0 guarded saved-package OfficeMath duplicate repair — 2026-07-26

- Added a bounded `WordEquationRepairPlanner` for Transitional and Strict Word packages.
  It discovers a candidate only when the existing source-linked OfficeMath graph reports
  the matching duplicate diagnostic and every sibling in the group is canonically
  identical by expanded element/attribute names, sorted attributes, text, comments,
  processing instructions and descendants. Non-equivalent properties, missing arguments,
  child reordering, ragged matrices, empty equations and preserved extensions remain
  explicitly unsupported.
- Added exact package/candidate fingerprints, deterministic `werplan_` identities and
  lossless byte-span removals for two repair kinds: later duplicate OMML property
  containers and later duplicate scalar properties. Candidate validation requires a
  complete reparse, selected issue reduction, exact removed-subtree counts, no new
  issue code/severity identities, normalized affected-part equivalence, byte-identical
  unplanned entries and an exact inverse that reconstructs the original package
  fingerprint.
- Added one provider-neutral Engine operation shared by strict
  `equation-repair-package --mode inspect|plan|apply` CLI and lazy
  `inspect_ooxml_equation_repairs`, `plan_ooxml_equation_repair` and
  `apply_ooxml_equation_repair` MCP actions. Apply blocks signatures and any missing,
  non-improving or newly failing Microsoft Open XML SDK validation, writes atomically and
  retains a sibling backup by default. Responses return neither equation text nor raw
  OMML and never open Word.
- Full local gates pass **675 Engine**, **498 Native** and **1,313 Python tests**, with
  16 intentional Python skips. Ruff, both .NET format gates, generated schema validation,
  generated sample/equation documents, the 11-page/17-equation torture document and the
  standalone Open XML SDK adapter are clean. Every .NET command used pinned SDK
  `C:\Users\Admin\.dotnet8\dotnet.exe` 8.0.423.
- Two self-contained win-x64 builds and the canonical build produced byte-identical
  196-file, 88,761,160-byte trees and byte-identical 37,213,287-byte ZIPs at SHA-256
  `e2775ba33aed6b34d0efc30c184c9fa32e532826bcb2cd9c167220cf764a8b81`.
  Executable, runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `b98d0a9aaf9e076c582f1829fc1eeb1553f39b88a5a26ececd058b15e0ff8cad`,
  `6a22cd670fc13985f35a0daaa0f1de2390f752566c80101289e0478ec59badc4`,
  `6d9505db9048e7f027a8cea516b316fbdfd55937ef8adba7bfbd0cdf0b27c561` and
  `2d76117e010f1a5667be6d711e424acc44304d9c3a94caf5277f6d16bd7f5e21`.
- Installed and enabled `0.44.0+codex.20260726050451`. Canonical build, persistent
  personal source and enabled cache contain the same 196 paths, lengths and hashes.
  Installed discovery reports 133 actions, 15 tools and 44 complete metadata contracts;
  an equation-repair query returns exactly the three new actions.
- The exact installed executable inspected, planned and repaired a disposable DOCX with
  one duplicate `m:fPr` container and one duplicate `m:sty` property. Engine validation
  passed, Microsoft schema errors fell from 2 to 0, two duplicate group members and three
  XML elements were removed, the result matched the planned fingerprint, a second scan
  found zero candidates, and the backup reproduced the exact original package
  fingerprint. No sensitive equation marker or raw OMML was returned and the Word-process
  count stayed at zero.
- No mathematical- or visual-equivalence claim is made. The proof covers exact duplicate
  declarations whose normalized affected-part representation is invariant, not conflicting
  declarations, missing structures, notation conversion or rendered Word layout.
- Mandatory hosted CI run `30186198534` passed all five jobs on implementation commit
  `43d200aee7f1741b04bc1b4e04b33ef56a19d15b`: Linux Engine, Windows Native/package,
  Python/rendering, Open XML validator and remote service container.

## WordToolkit 0.43.0 saved-package note integrity and guarded repair — 2026-07-26

- Added a bounded `WordNoteGraphBuilder` for Transitional and Strict packages. It joins
  footnote/endnote definitions, ordinary references, document-wide special references
  and document/section placement, format, start and restart policies without hardcoding
  separator IDs. Invalid IDs, missing/ambiguous definitions, nested note references,
  invalid custom marks, missing reference marks, complex/contentful orphans and invalid
  policies remain explicit diagnostics.
- Added exact definition/package fingerprints and two deliberately narrow repair kinds:
  removal of an empty simple ordinary orphan, or of a later canonically identical
  redundant duplicate. Missing note content and missing special definitions are never
  synthesized. A candidate must preserve every untargeted definition, ordinary/special
  reference and numbering policy, add no note issues, preserve every unplanned entry and
  reconstruct the base fingerprint through an exact inverse.
- Added direct Engine, strict `note-package --mode inspect|plan|apply` CLI and lazy
  `inspect_ooxml_notes`, `plan_ooxml_note_repair` and `apply_ooxml_note_repair` MCP
  contracts. Plan/apply are stateless and bind the current package, exact definition and
  deterministic plan ID. Apply blocks signatures, requires baseline-aware Microsoft Open
  XML SDK validation, writes atomically and retains a sibling backup by default. Responses
  expose neither note prose nor raw XML and never open Word or use the network.
- Full local gates pass **664 Engine**, **493 Native** and **1,313 Python tests**, with
  16 intentional Python skips. The focused Release slice contributes 18 note graph/
  operation tests and four CLI/MCP cases, including Strict namespaces, negative and zero
  special IDs, duplicates, orphans, malformed references, bounded limits, exact inverse,
  validator absence, stale plans and published closed-schema conformance. Every .NET
  command used pinned SDK `C:\Users\Admin\.dotnet8\dotnet.exe` 8.0.423.
- Two self-contained win-x64 builds produced byte-identical 196-file,
  88,631,592-byte trees and byte-identical 37,182,081-byte ZIPs at SHA-256
  `9ca0e1428ff69dcbfec2a53782b842eee6b6b10e3cba56defbe3addd4ff27419`.
  Executable, runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `70ae9fe1210ed3311558cb7763538457d12047fa8f8e09758e5e9422da0456e6`,
  `13777134ba22a1db2b60a317737c74c5f46d90ca7dc29426a8b33db15e32fe9b`,
  `7164074569abf4f9adaa49ee668a01214188c76b224793218d42f46773f619c4` and
  `6bf91f1a2e5864967700b7feb046bc3ecd5e94eaed6ae611b98630608c45c075`.
- Installed and enabled `0.43.0+codex.20260726035032`. Build, persistent personal source
  and enabled cache contain the same 196 paths, lengths and hashes. Installed discovery
  reports 130 actions, 15 tools and 41 complete metadata contracts. The exact installed
  executable projected `mammoth_footnotes.docx` into six definitions, two ordinary
  references, four special references and two policies with complete coverage, zero
  issues, no mutation, no raw XML and no Word process.
- Mandatory hosted CI run `30183713009` passed all five jobs on implementation commit
  `c66fd6cfd1192f57c09d6215404c1c60fb126b28`: Linux Engine, Windows Native/package,
  Python/rendering, Open XML validator and remote service container.

## WordToolkit 0.42.0 source-linked local OCR — 2026-07-26

- Added a provider-neutral `wordtoolkit.ocr-provider/1.0` extension contract and a
  source-linked OCR candidate graph. Candidates come only from embedded images actually
  referenced by the typed figure graph, deduplicate repeated package parts and bind
  stable IDs to the exact package fingerprint, canonical part URI and image SHA-256.
  Raster signatures are verified; external relationships are never fetched and vector,
  unresolved, mismatched or oversized inputs remain explicit failures.
- Added the built-in `wordtoolkit.tesseract-cli` provider. It requires an explicit
  absolute executable and model directory or named environment bindings, refuses `PATH`
  search and every reparse-point path component, hashes the executable and selected
  models before and after recognition, starts without a shell, streams image bytes
  through stdin, applies one total timeout and validates bounded TSV text, confidence
  and geometry. Raw provider output, paths, XML and image bytes never enter the result.
- Added direct Engine, strict `ocr-package` CLI and lazy
  `inspect_ooxml_ocr_candidates` / `run_ooxml_ocr` MCP contracts. Recognition requires
  an exact package fingerprint and explicit selection of at most eight candidates.
  `local_only` rejects providers declaring network or credential access before
  invocation. Text, line/word geometry and image/document hashes are independent bounded
  opt-ins, and the source file hash is rechecked after every provider batch.
- Current primary-source comparison covers Windows OCR, Azure Document Intelligence,
  Google Cloud Vision, Amazon Textract and Tesseract. The resulting threat model and the
  unimplemented boundaries are recorded in `RESEARCH-OCR-PROVIDER-2026.md`; the trusted
  adapter plus child process is explicitly not called a sandbox.
- The gated real OCR test generated a 1,800×400 scan containing
  `WORDTOOLKIT OFFLINE OCR`, embedded it in DOCX and passed local Tesseract
  `v5.5.0.20241111` through the lazy MCP action with the expected three words, normalized
  confidence above `0.7`, schema-valid provider/model/image provenance, unchanged DOCX
  hash, zero COM calls and no new Word process.
- Full local gates pass **646 Engine**, **489 Native** and **1,313 Python tests**, with
  16 intentional Python skips. The Native suite includes the real OCR test. Ruff and
  mypy are clean, 230 local Draft 2020-12 input/output schemas validate, four .NET format
  gates pass, generated remote schemas have no drift, the standalone Open XML validator
  builds without warnings, and generated sample/torture DOCX/PDF artifacts validate.
  Every .NET command used pinned SDK `C:\Users\Admin\.dotnet8\dotnet.exe` 8.0.423.
- Two self-contained win-x64 builds produced byte-identical 196-file,
  88,450,913-byte trees and byte-identical 37,134,628-byte ZIPs at SHA-256
  `8a3094709762391764645028b5b52a60e036cd68973d62642d434adfdb891fcf`.
  Executable, runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `8207b96a1a802fa78a26fc7a83d23114842d354e9a071ff2b2ba8349273368a5`,
  `367753a6a83bd39dfc897038a490074216cc86c48cda0e8b501d59b3891d50bd`,
  `66b48552a987c52e2c4dff7484a248c2dca01ca9effe8ddd9f602fc5b44e4214` and
  `c3a806a52976e1981dd685a1370f20c53516192f0d97f41d876aaba406b992c8`.
- Installed and enabled `0.42.0+codex.20260726022546`. Build, persistent personal source
  and enabled cache contain the same 196 paths, lengths and hashes. Installed discovery
  reports 127 actions, 15 tools and 38 complete metadata contracts. The exact installed
  executable inspected two eligible images in the 11-page torture DOCX, recognized both
  locally into five lines/eight words while suppressing text, used no network or Word,
  returned valid provider/model hashes and left the source SHA-256 unchanged.

## WordToolkit 0.41.0 equation truthfulness, point update and hybrid publication — 2026-07-26

- Unified equation preflight with the live mutation path. The default mode now creates
  one invisible unsaved Word scratch document and runs the same native BuildUp, style
  rewrite, OMML readback and semantic verification used by
  `apply_live_word_operations`. Scratch cleanup and restoration of the previous Word
  state are mandatory. The explicitly cheaper `conversion_only` mode reports
  `valid: null`, `conversion_valid: true` and
  `native_execution_verified: false`; it can no longer pretend that conversion alone
  proves Word acceptance.
- Added semantics-preserving normalization for common LaTeX dialect edges:
  `\binom{n}{k}` becomes Word's native no-bar stack rather than a matrix, `\dots`
  becomes an ellipsis, and function powers such as `\sin^4 x` apply to the complete
  function value. Word's extra delimiter around a no-bar stack is normalized during
  OMML readback instead of becoming a false semantic failure.
- Replaced the bitwise prepublication drift gate with stable document evidence. Raw Flat
  OPC, story and range hashes remain diagnostics, while semantic package hash, visible
  text, exact range boundaries and structural object counts decide whether the target
  actually changed. Volatile-only Word rewrites keep the original staging failure and do
  not quarantine the handle; proven semantic or structural drift still returns
  `STAGING_TARGET_DRIFT` before publication.
- Added `inspect_live_word_equations` and `update_live_word_equation`. Inspection issues
  one-time version-bound tokens tied to the one-based OMath index, exact range, semantic
  OMML hash and surrounding context. Update stages and verifies exactly one replacement,
  publishes it in one custom Undo record, advances `live_version` once and rejects every
  stale token.
- Added fingerprint-bound `publish_ooxml_package_to_live_word`. It accepts only a valid
  Word package with zero Microsoft Open XML SDK errors, disables macros and link updates,
  rechecks the source hash after opening and returns a new live identity. Its only honest
  mode is `open_as_new_document`; it does not claim an atomic in-place identity swap that
  Word does not expose.
- Full local gates pass **636 Engine**, **482 Native** and **1,313 Python tests**, with
  16 intentional Python skips. Ruff and mypy are clean, four .NET format verification
  passes are clean, all local Draft 2020-12 schemas validate, generated remote schemas
  have no drift and the standalone Open XML SDK validator builds with zero warnings and
  errors. Every .NET command used pinned SDK
  `C:\Users\Admin\.dotnet8\dotnet.exe` 8.0.423.
- Two gated real-Word tests passed. The first natively preflighted binomial, ellipsis,
  trigonometric powers and mathematical text, inserted one equation, replaced it by token
  without changing the OMath count, advanced version `0 -> 1 -> 2` and rejected the stale
  token. The second opened an independently built, SDK-valid DOCX as a new live document
  and proved the source SHA-256 unchanged.
- Two self-contained win-x64 builds produced byte-identical 196-file, 88,268,183-byte
  trees and byte-identical 37,079,676-byte ZIPs at SHA-256
  `791e1ef1ea3390107fcf1c40a8e3c44793d784140442c1818312c1da2c6b6853`.
  The installed and enabled personal plugin is
  `0.41.0+codex.20260726004424`; its source and cache match the packaged tree by path,
  length and hash. Installed discovery reports 125 actions, 15 tools and 36 complete
  metadata contracts. The exact installed executable repeated the four-equation native
  preflight successfully and shut Word down explicitly.

## WordToolkit 0.40.0 presentation snapshot and authoritative fixed rendering — 2026-07-25

- Added immutable `WordPresentationSnapshot` as the shared input for semantic HTML and
  SVG. It binds one package fingerprint to semantic, style, review, equation, heading,
  section, numbering, table, reference, figure/caption and settings projections with
  explicit capability gaps. HTML no longer guesses headings from localized style names.
- Added provider-neutral render source/target/output/fidelity, backend capability,
  resolution, provenance and artifact-manifest contracts. Unresolved requirements and
  silent fallback fail before publication. The shared publisher stages and reads back the
  whole artifact batch, rejects aliases/reparse traversal, publishes with create-new hard
  links and returns `ROLLBACK_FAILED` when cleanup cannot be proved.
- Added `wordtoolkit.render_ooxml_fixed_artifacts/1.0` through strict Engine contracts,
  `fixed-render-package` CLI and lazy MCP. Exact fingerprint-bound saved Word packages
  open hidden/read-only with macros disabled, link updates off and no recent-file entry.
  Word exports exact whole-document or inclusive page-range PDF; an explicit Poppler
  backend may inspect per-page MediaBoxes and derive PNGs from that same PDF. Page counts,
  source-page mapping, PNG signatures/dimensions/hashes and `MediaBox × DPI / 72` geometry
  are verified before one PDF/PNG/manifest transaction publishes.
- Full local gates pass **636 Engine**, **466 Native** and **1,313 Python tests** with
  16 intentional Python skips. Ruff and mypy are clean, four .NET format passes completed,
  the Draft 2020-12 local schema validates at 122 actions, remote generated schemas have
  no drift and the standalone Microsoft Open XML SDK validator builds with zero warnings.
  Every .NET command used pinned SDK `C:\Users\Admin\.dotnet8\dotnet.exe` 8.0.423 or a
  subprocess with the same pinned `DOTNET_ROOT` and `PATH`.
- A gated real-Word oracle passed against Word 16.0 build 16.0.20131. A separate 49-page
  fixture exported pages 1–2 to one 107,500-byte PDF and two 1224×1584 PNGs at 144 DPI.
  Both PDF pages expose 612×792-point MediaBoxes, source SHA-256 remained unchanged and
  direct image inspection found readable title/table/footer/contents with no blank page
  or clipping. The final PNG SHA-256 values are
  `a61cdbb8318af6de414693711108a19f7bd5b5051cb7bba495afd17163c27911` and
  `c9005238753ae70e861a546664864826e5e2035984440cecd89df4789b441b81`.
- Two pinned SDK builds produced byte-identical 196-file, 88,182,143-byte trees and
  byte-identical 37,058,042-byte ZIPs at SHA-256
  `32aa48adba64bbff505155f8f0152f80e786697171b48c681d772836e2444e9d`.
  Executable, runtime assembly, Engine and Open XML SDK adapter hashes are
  `a0db869f90ff2b2eca2f5b46704854ff65999a3f1b1288c553367d1eed1a3d4b`,
  `ced41c4ff96473e4f91c666caf02f13d480039b9519d0daac0c2c350a6ea8111`,
  `3c69872c10cd18c2a1b77a9cdb4768975df1cb7e3e40f209acd494010fe9cce8` and
  `6fd5481d08663720aaa2ca6890256a9347d3ae487d49a08f0a701924a08ce66e`.
- Installed and enabled `0.40.0+codex.20260725043457`. Build, persistent personal source
  and active cache contain the same 196 paths, lengths and hashes. Installed discovery
  reports 122 actions, 15 exposed tools and 36 complete metadata contracts. Installed MCP
  inspection returned the closed render 1.0 contract; execution exported only source page
  5 to a 235,729-byte PDF at SHA-256
  `51cc8be8cd64c0e8170a17615c565102e6604e7e83f254a490499791b0f757a9`,
  resolved every declared requirement, used no fallback and did not mutate the source.
  A second installed MCP call published page 6 as a 125,183-byte 816×1056 PNG at
  96 DPI from a 612×792-point PDF page; SHA-256
  `7f81f00af3e47960981de9847f0a933855ac6e3973f211c34c73002c759cfd04`.
  The complete response passed the published closed output schema independently.

## Typed heading and outline graph — 2026-07-25

- Added one source-linked outline resolution for every projected paragraph. Direct
  `w:outlineLvl`, exact base-first paragraph-style inheritance and document defaults are
  resolved without localized-name heuristics. Stored values 0–8 map to heading levels
  1–9; value 9 and an absent effective declaration are body text, but only a declared
  source receives provenance. Invalid higher-precedence markup and broken style chains
  remain unresolved.
- Added per-story nearest-shallower heading hierarchy, stable semantic paragraph IDs,
  explicit skipped-level/empty-heading/revision-MCE diagnostics and independent privacy
  gates for title preview, style IDs and source locations. The graph caches inherited
  resolutions and never stores or hashes heading text in its public model.
- Added `outline_parent` and `outline_level_derived_from_style` edges to the unified
  dependency graph, two linter rules, direct Engine contract, strict
  `heading-outline-package` CLI and lazy `inspect_ooxml_heading_outline` MCP. The 1.0
  input/output schemas are closed, default responses contain no heading text or raw XML,
  and `stylesWithEffects` plus revision/MCE view selection remain named coverage gaps.
- The nine-document golden corpus changed only where the new edges exist: POI styles
  gained 3 style-authority and 2 hierarchy edges; the real hyperlink/footnote fixture
  gained 8 and 5. The other seven dependency snapshots remained byte-for-byte equal.
- Full verification passed 621/621 Engine, 442/442 Native and 1,313/1,329 Python tests
  with 16 intentional environment/model skips. Ruff is clean, mypy passes 29 maintained
  modules, four .NET format gates are clean, remote schema generation has no drift and
  the standalone Open XML validator builds with zero warnings. Every .NET command used
  pinned SDK `C:\Users\Admin\.dotnet8\dotnet.exe` 8.0.423 or a subprocess with the same
  pinned `DOTNET_ROOT` and `PATH`.
- A Microsoft 365 Open XML-valid fixture matched Word 16.0 build 16.0.20131 for levels
  1, 2, 9 and body-text 10 across main and primary-header stories. Word opened read-only
  with repair disabled and the file SHA-256 remained unchanged after close.
- Two independent builds produced identical 196-file, 87,958,762-byte trees and
  identical 36,987,385-byte ZIPs at SHA-256
  `8742e1ef0231d8830d87a148ea05e61fd460f99f68173d287973019276e2c6d7`.
  Executable, runtime assembly, Engine and Open XML SDK adapter hashes are
  `96455d4d20869893b47154b8c87120abd7f7c23fd4debb03d4a420e81416192a`,
  `4e7c7674db39f861267f97b479bb263963d8b66b62197e404861cb4a09d99f16`,
  `1cb65fd54728c31daf770f1d6c3a61816cf77be5a9e0097f0e861b939459b759` and
  `d936ba1760b8c03399227644bcf936d53c9cb04acbe75e376e10df72a89ade9d`.
- Installed and enabled `0.39.0+codex.20260725034031`. Build, persistent personal source
  and active cache each contain the same 196 files with zero path/length/hash
  differences. Installed discovery reports 121 actions, 15 exposed tools and 35 complete
  metadata contracts. Installed action inspection and execution returned the exact 1.0
  contract, four metadata-only headings, no text/XML/mutation/Word launch and no change
  to the existing Word process set.

## Guarded OPC relationship inspection and repair — 2026-07-25

- Added a bounded typed relationship-usage graph that parses each XML owner once, scans
  all retained Markup Compatibility branches and distinguishes package, referenced,
  implicit, unknown, duplicate-ID, missing-owner, binary-owner, unparseable-owner and
  proven-unreferenced explicit relationships. Orphan `.rels` entries are separate typed
  objects. Unknown or ambiguous evidence never becomes deletion authority.
- Added one atomic reviewed repair batch with exact relationship/entry fingerprints.
  It removes only a proven-unreferenced explicit relationship element or an orphan
  relationship part, never its target. Candidate proof requires unchanged semantic
  projection, byte-exact unplanned entries, exact relationship delta, no new OPC errors,
  no new unreachable part and an exact inverse back to the baseline fingerprint.
- Direct Engine, strict `relationship-repair-package` CLI and lazy
  `inspect_ooxml_relationships`, `plan_ooxml_relationship_repair` and
  `apply_ooxml_relationship_repair` MCP use the same parser, planner, SDK comparison and
  atomic writer. Apply reconstructs `wrrplan_`, blocks signatures, requires no new SDK
  errors, separately authorizes external relationship removal and keeps a backup by
  default. Responses never expose external targets, raw XML or document text.
- The linter now has 23 rules and reports both unused explicit relationships and orphan
  relationship parts without pretending that a finding authorizes mutation. Hostile
  regressions cover MCE branches, duplicate IDs, malformed/binary/missing owners, implicit
  and unknown types, stale hashes, duplicate/oversized batches, external authorization,
  target-orphan prevention, closed JSON, CLI/MCP parity and published output schemas.
- Full verification passed 606/606 Engine, 438/438 Native and 1,313/1,329 Python tests
  with 16 intentional environment/model skips. Ruff is clean, mypy passes 29 maintained
  modules, four .NET format gates are clean, the remote schema generator has no drift and
  the Open XML validator builds without warnings. Every .NET command used pinned SDK
  `C:\Users\Admin\.dotnet8\dotnet.exe` 8.0.423 or a subprocess whose `DOTNET_ROOT` and
  `PATH` were pinned to it.
- A forced Microsoft Word acceptance repaired a package containing one dead external
  hyperlink relationship and one orphan `.rels` entry. The result had zero Microsoft 365
  Open XML SDK errors; Word 16.0 build 16.0.20131 opened it read-only without repair,
  returned the exact text and zero hyperlinks, closed without save and left its file hash
  unchanged.
- Two independent builds produced identical 196-file, 87,857,714-byte trees and
  identical 36,958,852-byte ZIPs at SHA-256
  `5f770e0a61e6a4755a910a3eb587c425b5af888176cfa0e48386f05db90f390d`.
  Executable, runtime assembly, Engine and Open XML SDK adapter hashes are
  `88f759313b49ac2d0ef579e01f8fdbf2d48fd41fd3663f226b4e394b8bde5a64`,
  `b1a019ba2a408c6c67cd342914e18a1c3d488bf6b26c52ec42fa8aa09cc6e7f0`,
  `2eacd74c553660c1571487a4ba51d0dfeb497fdc0db3cd2325678706812dc781` and
  `a83c25fe3de9dbec046357d751f8a3d450de2dc1aa856611b0934ac8004e873b`.
- Installed and enabled `0.39.0+codex.20260725023344`. Build, persistent personal source
  and active cache each contain the same 196 files with zero path/length/hash differences.
  Installed discovery reports 120 actions and 34 complete metadata contracts. Installed
  MCP action inspection proved the closed read-only plan schema; installed execution on a
  real showcase DOCX returned the exact 1.0 relationship-inspection contract and confirmed
  zero target/XML disclosure, mutation or Word launch.

## Transactional numbering-sequence restart — 2026-07-25

- Added one deliberately narrow repair: `restart_numbering_sequence` with scope
  `remaining_instance_in_story`. Direct Engine, strict stdin/file CLI and lazy MCP use
  the same planner and apply operation. The planner clones the selected `w:num`, assigns
  only the target tail in the same story to a fresh `numId`, preserves earlier and
  unrelated sequence outputs, keeps paragraph text unchanged and retains an exact inverse.
- Plan/apply require exact package fingerprint, stable paragraph node, expected source
  instance/level/start and a reproducible `wnrplan_` ID. Apply blocks signatures, missing
  Microsoft Open XML validation and new SDK errors, writes atomically in place and keeps
  a sibling backup by default. Unknown JSON fields fail before filesystem access.
- MCP exposes closed compact 1.0 output schemas, count/hash evidence and explicit detail
  truncation; it returns no paragraph text or raw XML and never opens Word. The real
  JSON-RPC plan response passed its published schema. Catalog size is 117 actions with 31
  complete metadata contracts and an unchanged explicit gap of 86.
- The linter now has 21 rules and consumes the same sequence executor. Focused regressions
  cover unresolved starts, invalid labels, revision ambiguity, picture bullets and locale
  formats without turning unsupported rendering into false document defects.
- A guarded Microsoft Word acceptance opened the SDK-valid repaired package read-only.
  Engine and Word both returned values `1,7,8,9` and labels `1.,7.,8.,9.`; Word closed
  without save and the repaired file hash remained byte-exact.
- Full native verification passed 592/592 Engine and 434/434 Native tests. All four
  scoped format gates and deterministic schema export are clean. Every .NET command used
  `C:\Users\Admin\.dotnet8\dotnet.exe` SDK 8.0.423.
- Two independent builds produced identical 196-file, 87,708,110-byte trees and
  identical 36,918,950-byte ZIPs at SHA-256
  `569c313de07e30ce2fe4614a2ace13bd99ad355013160908ef772f4870896da0`.
  Executable, runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `c0aeeea51984ccde2cdf81a755a8e2991d855edbc111559676d4df8a8f993a82`,
  `9cc178822b114a5216b252bd938f9e68ae9e3ce2b07b74123c3255d24ff6b0ec`,
  `f94c0ad072a6d3d63d75108634a21af7e6a323c957c4ae610abbb298b8b0bc82` and
  `4905569f1a7d03d312d3c8999e1bd6e692ede324a85c2161e848889d90eb6ff5`.
- Installed and enabled `0.39.0+codex.20260725013020`. Build, personal source and active
  cache contain the same 196 files with zero path/length/hash differences. Installed
  discovery reports 117 actions, 15 tools and 31 complete metadata contracts; direct
  action inspection returned the closed read-only 1.0 plan contract with the required
  detail-truncation field.

## Word-compatible numbering sequence execution — 2026-07-25

- Added bounded `WordListSequenceGraphBuilder` execution and lazy
  `wordtoolkit.inspect_ooxml_numbering/1.0` `view=sequences`. It resolves direct and
  paragraph-style numbering, isolates state per story root and `numId`, applies higher-
  level and section-break restarts and legal numbering, and returns stable `wdli_`/`wdls_`
  identities without paragraph text. Counter and label certainty remain separate.
- Eight focused Engine tests cover nested counters, restart rules, replacement-level
  start behavior, section resets, style inheritance/direct removal, unsupported labels,
  invalid/missing declarations, ambiguous revisions and hard limits. Native regressions
  prove the closed sequence-item contract, content-free response, filters, full operation
  metadata and unknown-argument rejection before package reading.
- An Open XML SDK-valid guarded fixture passed against real Microsoft Word 16.0 build
  16.0.20131. Word and the engine both returned values `1,9,10,2,9,1,9` and labels
  `1.,1.i,1.j,2.,2.i,1.,1.i`; the hidden read-only document was closed without save and
  the package hash stayed exact. This qualifies replacement-level start precedence,
  ignored replacement-level restart and `w15:restartNumberingAfterBreak`. The first result
  conflicts with Microsoft's written note and is exposed as a compatibility warning.
- Full local verification passed 582/582 Engine, 428/428 Native and 1,313/1,329 Python
  tests with 16 intentional environment/model skips. Ruff is clean, mypy passes all 29
  maintained modules, scoped .NET format is clean, the schema generator is stable and the
  standalone Microsoft Open XML validator builds with zero warnings/errors. Every .NET
  command used `C:\Users\Admin\.dotnet8\dotnet.exe` SDK 8.0.423 or a subprocess with
  `DOTNET_ROOT`/`PATH` pinned to that installation.
- Two pinned SDK builds produced identical 196-file, 87,598,120-byte trees and identical
  36,889,700-byte ZIPs at SHA-256
  `3ea9c02ce12dff2ce52fc885024554fc6f78d96eb436fac8dc99d4484c034add`.
  Executable, runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `78de30c24ab0073ea5af6f080e4e6b745e76139e057268b3ffcc193fb4eb8048`,
  `816cd5aba867db2166545672d454e2e5c63e47e36e0b83a79413ae289641918e`,
  `dd5ea9b003afd80b7b7049521ff2c2a8f7e70bed1a55c41473522268c0cdfa68` and
  `e45ce3323d22004b72e877ea22979870c77425c93d4da56f3203a8051dd1de19`.
- Installed and enabled `0.39.0+codex.20260725000036`. Build, personal marketplace source
  and active cache each contain the same 196 files with zero path/length/hash differences.
  Installed discovery reports 115 actions, 15 tools and 29 complete metadata contracts.
  The installed executable inspected the advanced torture DOCX through lazy MCP, returned
  12 matched sequence items with exact counter/label coverage, and exposed the closed 1.0
  read-only contract from its embedded schema. Runtime was `dotnet-native`; Python was not
  used.

## Isolated connected-Word behavior probes — 2026-07-24

- Added explicit-confirmation `wordtoolkit.probe_live_word_feature_behaviors/1.0` for real
  native OMath BuildUp, content-control creation, SmartArt insertion and custom Undo
  behavior. Property exposure is no longer mistaken for behavioral proof.
- Each fixed probe runs in a separate invisible unsaved Word document. The connected
  document content is neither read nor mutated, no path/content/identity is returned, no file is saved,
  no network is used and the operation never starts Word.
- Success requires `Close(0)` for every created document, exact restoration of the prior
  active document and window by COM identity, and an unchanged open-document count.
  Cleanup or `EndCustomRecord` uncertainty returns
  `TEMPORARY_DOCUMENT_CLEANUP_FAILED` and quarantines the live handle.
- Six focused regressions cover all passing behaviors, feature failure, zero SmartArt
  layouts, mandatory cleanup, handle quarantine, Undo-record closure failure and rejection
  before Word dispatch. Catalog state is 115 actions, 15 core/gateway tools, 28 complete
  metadata contracts and the same explicit 87-action gap.
- The complete checkpoint is 574/574 Engine, 426/426 Native and 1,313 Python/OOXML passes
  with 16 intentional skips. The exact CI Ruff lane, mypy over 29 maintained modules,
  scoped .NET format, deterministic schema export and the standalone validator are clean.
- A guarded real-Word acceptance test passed. Its target rollback snapshot retained exact
  text/ranges/counts/Saved state and permitted only Word's volatile semantic package
  projection to differ after active-window switching.
- Two pinned SDK 8.0.423 builds produced identical 196-file, 87,523,060-byte trees and
  identical 36,870,505-byte archives at SHA-256
  `6b942f24de52bd82c41dd1cae69dc12c929ea9472d9731cde7babf7763dee1d9`.
  Executable, runtime, Engine and Open XML SDK adapter SHA-256 values are
  `b38cc86a57ee18799621af58e631b9afbb45b392113ba3e8944c745913f6675b`,
  `94aa2e34eabd3b19254bb6585b1ecbda234a21dc1386b646bad92e8b0ef22b0a`,
  `12158da26fda58d40f5600159cc01c2e92bceff5b4c555e95ec2ce6154986f1c` and
  `bb975458d8d71a9a6b1d0ace1193b3efaeac3111677b45bb337bdacd17e6c0ff`.
- Installed and enabled `0.39.0+codex.20260724230914`; build, personal source and cache
  contain the same 196 files with zero path/length/hash differences and zero Python files.
  Installed discovery returns 115 actions, 15 tools and 28 complete metadata contracts.
- The installed lazy MCP attached to Word 16.0 build 16.0.20131. All four probes passed;
  four scratch documents were created and closed, previous active document/window and
  document count were restored, `live_version` remained 0, the response passed the
  installed output schema, no forbidden field was found and disconnect succeeded.


## Connected Word version profile — 2026-07-24

- Added lazy `wordtoolkit.inspect_live_word_version_profile/1.0` for an already connected
  Word document. It reports raw application version/build, a conservative major family,
  document compatibility/save-format integers and property-access probes for UndoRecord,
  OMath, SmartArt and content controls.
- The operation reads no document content/path/user/licence identity, opens no Word process,
  uses no network and returns no raw COM object. Major 16 is deliberately labelled only
  `word_16_generation`; product edition inference is fixed to false.
- Each COM read is isolated. Failed probes return one of eight fixed issue codes, unknown
  scalar values remain explicit JSON nulls, and `available` is documented only as property
  access evidence rather than behavioural proof.
- Four new regressions cover the closed contract, successful Word 16.0 projection, all
  three probe states, partial failure, null preservation, fixed-code privacy, zero
  sensitive-text reads and rejection before COM dispatch. The full Native suite is
  419/419 using `C:\Users\Admin\.dotnet8\dotnet.exe` from pinned SDK 8.0.423. A fifth
  regression exercises all documented family mappings plus an unknown future value.
- The catalog now contains 114 actions, 15 core/gateway tools and 27 complete metadata
  contracts; 87 actions remain explicitly uncovered.
- Two package builds produced identical 196-file, 87,475,140-byte trees with zero Python
  files and identical 36,857,975-byte ZIPs at SHA-256
  `746201dcc9b7ea1b4147a8388df539d212589bce58dbb015da978d4db568d3b2`.
  Their executable, runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `d4fc2cc507450b155f83ac3ff7624e60c947958e81bde18fcb30dc54eb285751`,
  `8e6153a9a855bd2e25ef866a907569d749fd10b9a682c8844b3ef237af7e5bfc`,
  `5e42720f4712a943fe6d6fef534f420eec79dc7479db1cd6d5b695926027662b` and
  `d4edf6ff4ca3864a7ca42913b6eec429403830adcae87ba1f7853e15f032f5cb`.
- Installed and enabled `0.39.0+codex.20260724220014`; build, personal source and enabled
  cache contain the same 196 files with zero path/length/hash differences. Installed
  discovery reports 114 actions, 15 exposed tools and 27 complete metadata contracts.
- A real installed lazy-MCP call attached to the current Word process and returned
  `Version=16.0`, `Build=16.0.20131`, compatibility mode 15, save format 12, four
  `available` probes and zero issues without changing `live_version`. The full response
  passed the installed action's own output schema and contained none of the forbidden
  content/path/user/licence field names.
- Hosted CI run `30123410969` on code head `9e96323` passed all five jobs. The downloaded
  Windows artifact contained the exact local distributable: 36,857,975 bytes and SHA-256
  `746201dcc9b7ea1b4147a8388df539d212589bce58dbb015da978d4db568d3b2`.

Research and exact limits are recorded in
[`RESEARCH-LIVE-WORD-VERSION-PROFILE-2026.md`](RESEARCH-LIVE-WORD-VERSION-PROFILE-2026.md).

## Bounded OOXML encryption detection — 2026-07-24

- Added a neutral `wordtoolkit.inspect_ooxml_encryption/1.0` Engine operation, strict
  `inspect-encryption` JSON CLI and lazy `inspect_ooxml_encryption` MCP action. The same
  result contract distinguishes OPC ZIP, encrypted OOXML compound files, other compound
  files, partial markers, malformed CFB and unknown input without invoking Microsoft Word.
  General package inspection now fails with distinct `DOCUMENT_ENCRYPTED` or
  `ENCRYPTION_CONTAINER_INVALID` codes instead of claiming those envelopes are corrupt ZIP.
- The cross-platform CFB probe validates signature/version/sector geometry, DIFAT/FAT and
  directory chains, root-child identity, MiniFAT and regular/mini-stream bounds. A positive
  result requires one root `EncryptionInfo` stream, one root `EncryptedPackage` stream and
  one root DataSpaces storage. At most eight `EncryptionInfo` bytes are read to classify
  Standard 2.2/3.2/4.2, Extensible 3.3/4.3 and Agile 4.4; an unknown future version remains
  detected as encrypted with an explicit warning instead of becoming a false negative.
- Passwords, key derivation, decryption and encryption are absent. The operation returns no
  path, stream name, raw bytes or document content and uses no network. Full nested
  DataSpaces validation, a universal `DOCUMENT_ENCRYPTED` boundary and an authorized,
  zeroizing decrypt/encrypt adapter remain open.
- Deterministic Engine and Native regressions cover all six recognized version pairs,
  an unknown future version, CFB v3/v4 sector geometry, ZIP, missing markers, invalid
  encryption header flags, malformed FAT identity, path privacy, exact stream-position restoration,
  resource bounds, strict CLI, lazy closed metadata, MCP dispatch, unknown-field/password
  rejection and zero COM calls. The catalog is now 113 actions with 26 complete metadata
  contracts; 87 remain uncovered.
- A licensed Word 16.0 smoke saved a fresh 19,456-byte password-protected DOCX. The Release
  CLI detected CFB v3/512, all three root markers and Agile 4.4 with zero issue codes. The
  temporary document and fixed test-only password were deleted after the check; detection
  never received the password and did not invoke Word. This probe exposed Word's legitimate
  surplus FAT-sector preallocation; the corrected parser accepts a bounded surplus while
  retaining mathematical-minimum, physical-sector and maximum-count checks.
- The complete local checkpoint passes 574/574 Engine, 414/414 Native and 1313 Python
  tests with 16 intentional skips. All .NET commands use the repository-required local
  SDK 8.0.423 at `C:\Users\Admin\.dotnet8\dotnet.exe`.
- Two pinned SDK 8.0.423 builds produced byte-identical 196-file, 87,452,589-byte
  plugin trees and byte-identical 36,851,090-byte ZIPs at SHA-256
  `9ac60e38c5263ddae3ca6b0202f77e620223b2bd68a8e05b15b5ae94ec67867e`.
  The executable, runtime, Engine and Open XML SDK adapter hashes are respectively
  `13317f545c00c0de141f19bbb707335d01b14b4d6221c8fed43c23075429ecd1`,
  `e1c0376b456d7d77a773857c0e1b3b08f06b7d9de9574b4981f546aef91b9628`,
  `d4e341d892cdaac0b4ba1fed1582bffba522ce2691845d52a7908d17ccaf7776` and
  `f2f2ece3b2a662c4f2316b43f4a343a714743ade1310df3f9db526f38b44b657`.
- Installed and enabled `0.39.0+codex.20260724210114` has zero path/length/hash
  differences across build, personal source and cache. Its runtime reports 113 actions,
  15 exposed tools and 26 complete metadata contracts. The packaged and enabled-cache
  Engine assemblies share the exact Engine hash above; the same source revision passed the
  licensed Word probe.
  A separate installed strict-CLI/lazy-MCP smoke on a generated valid OPC package returned
  `wordtoolkit.inspect_ooxml_encryption/1.0`, validated against the output schema returned
  by the installed action inspector, exposed no path and used no Python in the runtime.
- Hosted CI run `30121281344` passed Linux Engine, Python/rendering, Windows Native/package,
  Open XML validator and remote-container jobs. The clean Windows artifact is exactly
  36,851,090 bytes with SHA-256
  `9ac60e38c5263ddae3ca6b0202f77e620223b2bd68a8e05b15b5ae94ec67867e`,
  matching both local archives byte for byte.

Research and exact limitations are recorded in
[`RESEARCH-OOXML-ENCRYPTION-DETECTION-2026.md`](RESEARCH-OOXML-ENCRYPTION-DETECTION-2026.md).

## Content-free observability and local audit spine — 2026-07-24

- Added a versioned neutral-Engine observability contract around .NET `ActivitySource`
  and `Meter`. Telemetry is opt-in, has no exporter dependency and admits only registered
  operation identity, version, fixed effects, normalized outcome and error code. Arguments,
  document text, XML, paths, relationship targets and package fingerprints have no event
  or metric field; hostile unknown action names collapse to one fixed dimension.
- Added independent `off`, bounded-memory and local-JSONL audit modes. A bounded
  nonblocking channel isolates document operations from slow or throwing sinks; queue
  drops and write failures remain visible. Throwing host activity/metric listeners are
  contained and counted rather than replacing the operation result. Memory capacity and 1–365-day technical
  retention are explicit. The local sink is write-through and never exposes its directory
  through public metadata.
- Added a closed `wordtoolkit.audit.event/1.0` format with source-ordered sequence,
  random correlation and an unkeyed SHA-256 append chain. The chain is deliberately
  marked unauthenticated: it detects an inconsistent observed segment but is not a
  signature, trusted timestamp or compliance proof. Strict `audit-log verify` rejects
  duplicate/unknown fields, malformed lines, size/event/line overflow, sequence gaps and
  hash drift while returning neither the input path nor event bodies.
- Added lazy `inspect_wordtoolkit_observability` as native action 112 and explicit metadata
  contract 25. Its summary-first response opens no Word instance and reads no document;
  event pages stop at 32, while correlation IDs and record hashes require separate opt-ins.
- Added Engine and Native regressions for opt-in behavior, redaction, dimension
  allowlisting, chain mutation, capacity/retention, concurrency, slow/throwing sinks,
  queue overflow, strict JSONL parsing, real `ActivitySource`/`Meter` dimensions,
  environment configuration, MCP dispatch, lazy schema closure and path-free CLI output.
  Remote export, authenticated/external anchoring, transaction-durable mutation evidence,
  legal hold, access audit, secure deletion and cross-segment manifests remain open.
- Full Release evidence is 553/553 Engine tests, 410/410 Native tests and 1309 passed
  Python tests with 16 intentional skips. Ruff is clean, mypy reports no issues in the
  maintained 29-file layer, .NET format and `git diff --check` are clean, the generated
  catalog is stable, and the real observability result validates against its Draft
  2020-12 output schema.
- Two SDK 8.0.423 builds produced byte-identical 196-file, 87,396,612-byte trees and
  byte-identical 36,831,975-byte ZIPs at SHA-256
  `2028f140497c272032e5fd24084602a8e6716998adf92f4b9049a74aae70084f`.
  Executable, runtime, Engine and Open XML SDK adapter SHA-256 values are
  `af6a52742333822dd61b7d16749bf12d34f81add41bc4f4f8f42e8be4c736396`,
  `c33bc6fbd48e8fb2ecb2ae52f31afc7444e875d2167bb32342ef49a5b948187a`,
  `8cd004a43e7f6bbe8a5c665ff7828293323bc896819bbfe84782c10fb76a2932` and
  `5605101e4485fd186785d7c206eef3f995903a9dd8dd9ec1b1f575d556172ece`.
  Build, personal source and enabled `0.39.0+codex.20260724201229` cache contain the
  same 196 files with zero path/length/hash differences and no Python runtime files.
  The clean hosted-Windows CI package job then produced the same 36,831,975-byte
  distributable with the same SHA-256, closing the separate-checkout reproducibility gap
  for this exact commit and supported packaging lane.
  Installed discovery is enabled and reports the exact stamped version, 112 operations,
  15 exposed tools and 25 explicit contracts. Its real memory-audit MCP smoke returned
  `wordtoolkit.inspect_observability/1.0`, one prior safe event, `bounded_memory`, and
  false content/argument/path disclosure. A separate real JSONL smoke verified two
  persisted events and did not disclose its configured directory.

## Trusted extension registry foundation — 2026-07-24

- Added explicit, allowlisted and versioned registration in the neutral Engine for
  package/storage, typed-part, semantic, validation/lint/repair, command, render/convert,
  OCR, index, policy and telemetry capability kinds. Policy independently constrains
  extension identity, trust/isolation, interface kind/version, permissions and maximum
  input/output/concurrency/timeout resources. Registration freezes into a read-only,
  deterministic SHA-256 catalog; no directory scan or arbitrary assembly load exists.
- Invocation resolves the exact host interface, rejects oversized input/output, refuses
  calls beyond the concurrency ceiling and links cancellation to a cooperative timeout.
  Documentation and public metadata state that trusted in-process code has full process
  authority: this is dependency injection, not a sandbox or safe preemption boundary.
  Reserved out-of-process values cannot be registered by the current builder.
- Registered `wordtoolkit.validator.openxml.microsoft365` as the first real capability.
  Production semantic-style, comment, review, formatter, package-patch, merge and rollback
  paths now route Microsoft Open XML SDK validation through the registry-backed adapter.
  The first full parallel Native run rejected calls after two concurrent validations;
  the stateless built-in profile was corrected to the host's 64-active-request ceiling,
  while an isolated limit-one regression still proves hard concurrency enforcement.
- Added one shared content-free catalog operation across direct Engine, native
  `extensions` CLI and lazy `inspect_wordtoolkit_extensions` MCP. Its checked-in input and
  output schemas pass Draft 2020-12 meta-validation and accept the real CLI envelope. The
  action catalog now reports 112 actions, 15 exposed tools and 25 complete metadata
  contracts, leaving the metadata gap unchanged at 87.
- Full local evidence is 539/539 Engine tests, 403/403 Native tests and 1309 passed Python
  tests with 16 intentional skips. Ruff is clean, mypy reports no issues in 29 source
  files, the documentation generator is stable, and changed .NET projects compile and
  format cleanly under the pinned SDK 8.0.423.
- Two pinned SDK 8.0.423 builds produced identical 196-file, 87,285,844-byte trees and
  identical 36,798,079-byte ZIPs at SHA-256
  `e298ebc80a64a6c5fb36ca579c041633963a383ff82a153cb8aa3c43bcf3c2d2`.
  Executable, runtime, Engine and Open XML SDK adapter SHA-256 values are
  `d87fefff3e10005d2a33a50e4d36005ca2bbe8b9377e8a83dfbe2628e8c817f2`,
  `6d5e1c5a4a1c67dc5ea0f076be583fa049b038b23f28fa853a402ad3876ca6c3`,
  `e7e224524a0923712136da70b71777034596a2638c49cb092f52d4637970a8df` and
  `8f8f9c012df596779963048dac494cd49185ce157b7672b37160c3614487c813`.
  Build, personal source and enabled `0.39.0+codex.20260724191908` cache have zero
  path/length/hash differences. Installed discovery at that checkpoint reported 111 actions,
  15 exposed tools and 24 explicit contracts; the installed MCP extension call returned the exact catalog
  hash `dfb26f3c1da808d94ebfac6782fff391f9e174e7c606235a9e05de6dc2b234bd`.

## Bounded Flat OPC transport convergence — 2026-07-24

- Added `FlatOpcPackageCodec` and `FlatOpcWordPackageOperation` to the neutral Engine,
  with one typed contract shared unchanged by direct .NET, the strict
  `flat-opc-package` CLI and lazy `convert_ooxml_flat_opc` MCP. The implementation streams
  the outer XML, prohibits DTDs and resolution, enforces package budgets, reconstructs
  content types, handles binary and AltChunk payloads and publishes only a fully verified
  create-new artifact. It never opens Word or returns document XML.
- Semantic acceptance compares the exact part-name/content-type/relationship sets,
  binary bytes and complete XML part roots. A packaged real-corpus smoke initially failed
  closed on `_rels/.rels` because `XDocument.DeepEquals` treated the XML declaration as
  part semantics. The corrected comparator ignores only that transport declaration; a
  permanent regression now exports and imports the bundled advanced Word document.
- The published 13-case corruption corpus and 20 Engine Flat OPC tests cover DTD/entity,
  shape, Base64, URI collision/traversal, depth/size, deterministic reconstruction,
  official Open XML SDK interoperability and the real-DOCX case. Four Native tests prove
  CLI/MCP/direct parity. The full checkpoint passes 531 Engine, 399 Native and 1309 Python
  tests with 16 intentional skips; Ruff, mypy over 29 maintained modules and .NET format
  verification are clean.
- Hosted Linux then exposed an older cross-platform create-new race in the shared semantic
  renderer: two writers could both pass `File.Move(overwrite:false)` and report success.
  Publication now atomically creates a same-filesystem hard link from the closed, flushed
  temporary artifact, so exactly one writer can create the public directory entry. The
  existing race regression passes 20 repeated local runs before the full suite.
- Pinned SDK 8.0.423 produced two byte-identical 196-file, 87,203,265-byte trees and two
  byte-identical 36,776,448-byte ZIPs at SHA-256
  `58e65922b03e6c81240cfe128827b8d74f66646b1781bcb247e7716cb151b4ef`.
  Executable, runtime, Engine and Open XML SDK adapter SHA-256 values are
  `384053b7da94b71c372797ae134f1e60132d54ce0dc60523df57f83fc9c79196`,
  `f7c2627bcfbd8865f0d9107af327f7e488802ffef9e7cafdfb7a4fdbbf9876c6`,
  `120bcf4d1c54087c0e34c611b0ccba84f7b5f82fe6ce025652bca8591a216e8b` and
  `b9a1d1fd63982c6da20460c99e4266ed0a970ec85d07be02b14a3a5f513b5035`.
  Build output, personal source and enabled `0.39.0+codex.20260724184043` cache have zero
  path/length/hash differences. Installed discovery reports 110 actions, 15 exposed MCP
  tools and 23 explicit contracts; its real-DOCX round trip has semantic parity, zero
  package errors and zero orphan parts.

## Transport-neutral package rollback convergence — 2026-07-24

- Added public Engine plan/apply contracts and `PatchRollbackWordPackageOperation` for
  saved-package rollback. It owns reverse derivation, exact current/artifact identity,
  destination-bound plan IDs, semantic/risk/type/schema evidence, authorization gates,
  no-op semantics and atomic publication without opening Word.
- Added one strict JSON codec shared by direct .NET callers, the new non-interactive
  `patch-rollback-package` CLI and the existing lazy MCP plan/apply actions. Unknown
  fields fail closed. The Native adapter now supplies only the Microsoft Open XML SDK
  validator and runtime timing metadata; its private reverse-planning branch was removed.
- Seven direct Engine regressions cover exact restore plus redo backup, stale and cross-path
  state, case-sensitive destination binding, active-content authorization, no-op behavior,
  validator absence and closed JSON.
  One Native regression proves canonical plan-result parity across SDK, CLI and MCP and
  then applies the reviewed rollback through the CLI. The full checkpoint passes 511
  Engine, 395 Native and 1309 Python tests with 16 intentional skips.
- Pinned SDK 8.0.423 produced two byte-identical 196-file, 87,150,234-byte trees and two
  byte-identical 36,755,923-byte ZIPs at SHA-256
  `c9216579cede83aec95cab4b895a30008d9c33fcfb28ba0ea3163566e937648d`.
  Build, personal marketplace source and enabled `0.39.0+codex.20260724165517` cache have
  zero path/length/hash differences. Installed discovery reports 109 actions, 15 exposed
  MCP tools and 22 explicit contracts. Executable, runtime, Engine and Open XML SDK
  adapter SHA-256 values are `8bcb384c8f4dd4fea526c9e4c2ac81935d4542e1c7dc7436a4812d5f1dd2aabb`,
  `71f12542a6da3f22b3fe6e9b04234ed33448978c99a64139c2f46e1b75c61fbe`,
  `b0e93320b14ebe247988ef6b62d5ac5429df87b98df87fcd70d9cc509c5ddb42` and
  `e6ce1df006670abba3f630304f31dd4d3a6aa80fa3a9d92d2af768741f9f4125`.

## Public destination-bound saved-package rollback — 2026-07-24

- Added `plan_ooxml_patch_rollback` and `apply_ooxml_patch_rollback`. The caller supplies
  the exact current package fingerprint and original artifact `patch_id`; the runtime
  verifies that the package is still the original result, derives `Reverse()` internally
  and returns a distinct destination-bound `wtrollback_` plan ID.
- Apply rebuilds the complete reverse candidate, requires the same fingerprint, source
  patch ID and rollback-plan ID, re-runs semantic/risk/package-type/Open XML validation,
  enforces separate signature, active-content, external-relationship, opaque-binary and
  new-error authorizations, then uses the existing atomic writer. The default backup
  contains the pre-rollback state and can support an explicit redo decision.
- The response exposes filenames, IDs, fingerprints, counts and bounded evidence only;
  no patch payload, raw XML or Word process crosses the action boundary. Exact no-op
  rollback performs no write and creates no backup.
- Eleven focused service regressions now cover forward patch behavior plus exact rollback,
  backup contents, stale result state, plan/path binding, macro authorization, no-op
  behavior, privacy and closed versioned contracts. The full checkpoint passes 504
  Engine tests, 394 Native tests and 1309 Python tests with 16 intentional skips; both
  changed .NET projects pass `dotnet format --verify-no-changes`, and both new output
  schemas pass Draft 2020-12 meta-validation.
- Installed runtime `0.39.0+codex.20260724161042` is enabled and reports 109 actions, 15
  exposed tools and 22 explicit metadata contracts. Build output, personal marketplace
  source and enabled cache each contain the same 196 files and 87,093,417 bytes with zero
  path/length/hash differences. A second same-checkout output tree also has zero
  differences and its 36,739,798-byte ZIP is byte-identical at SHA-256
  `77b8216c597e2462aafbffdaf0f60110841b171385ba35c0b505cdd4fc298609`.
  This proves reproducibility within the pinned host/toolchain and checkout; a second
  clean checkout remains a separate release gate.

## Independent recovery with semantic rollback proof — 2026-07-24

- Failed `apply_live_word_operations` publication no longer ends with Word's Undo as the
  only recovery mechanism. The staged batch retains the baseline Flat OPC in process and,
  when Undo fails or cannot prove restoration, opens it as a separate hidden, read-only
  Word document and copies the baseline main story back into the target with one
  cross-document `Range.FormattedText` assignment.
- Recovery equivalence now combines exact content/target/context boundaries and hashes,
  save state and structural counts with a namespace-aware whole-document semantic Flat OPC
  hash. It ignores only WordprocessingML attributes whose local name starts with `rsid`.
  Two consecutive semantic reads must match; four unstable reads return
  `ROLLBACK_SNAPSHOT_UNSTABLE`. `Document.Saved` is restored only after the rest of the
  checkpoint matches and is then verified again.
- A fake-COM fault-injection test forces `Undo=false`, performs the independent restoration
  and proves clean text, zero equations, unchanged `live_version=0` and a reusable handle.
  The failure remains the original `EQUATION_INVALID` because complete recovery was proved.
  Existing false, throwing, no-op, partial and visible-only Undo cases still fail closed
  when the independent path fails or leaves state drift.
- A forced real Word 16.0 test starts with native OMath, contaminates the target without
  Undo and performs the same Flat OPC recovery. Main-story text, paragraph count and OMath
  count return exactly to baseline, but the semantic whole-document Flat OPC hash still
  differs beyond `w:rsid*`. That document remains quarantined. The result proves useful
  cleanup, not package-exact recovery, and prevents a clean-looking document from being
  mislabeled as transactionally restored.
- Current gates pass **504 Engine**, **389 Native** and **1,309 Python tests**, with **16
  intentional Python skips**; Ruff and both modified C# project format verifiers pass.
  Full package-exact restoration or a sanctioned live document-identity swap remains the
  open boundary.
- Enabled runtime `0.39.0+codex.20260724154131` reports **107 actions**, **15 exposed
  tools** and **20 explicit metadata contracts**. Build, personal marketplace source and
  enabled cache contain the same **196 files** and **87,050,540 bytes**, with zero path,
  length or SHA-256 differences. The **36,731,827-byte** ZIP SHA-256 is
  `88fe233ed24107866d757677f809f80732427acf140859576dec0f3f74642c55`;
  executable, native runtime assembly, Engine assembly and Open XML SDK adapter hashes are
  `7239581ad34ecde54a192f70339d672e200507574a6023e450aa9ebb9784f841`,
  `318bbbbf411f979a4b308f0dc24ec003be324f041b83d0db2a4e2915296b1f9e`,
  `4e37936da2c499d7ebd79dbf94b572ce2509e0786f703f7abdeaed997ca7f09d`
  and `bdab24fb5ec8a5b0eae51cae2b3cf85258e363cd8a5f7d033dd1e47c54000068`.

## Complete staged live-batch publication — 2026-07-24

- Replaced equation-only staging with a full Flat OPC clone of the current target. Text,
  paragraph boundaries, styles, all supported formatting properties and native OMath are
  built and verified in that isolated Word document before the target is touched.
- The target receives the candidate through one cross-document `Range.FormattedText`
  assignment. Before `live_version` advances, readback proves exact published length and
  text fingerprint, operation ranges, requested formatting, global/range-local equation
  counts, display type, scoped equation styles and optional semantic equation contracts.
- The clone opens with macros and link updates disabled, must close without saving and its
  temporary Flat OPC file must be deleted. Full target Flat OPC/text/structure is checked
  before publication. Hidden drift during staging returns `ROLLBACK_FAILED`, quarantines
  the identity and never risks undoing unrelated user history.
- Fourteen focused tests cover exact/partial/visible-only/false/throwing Undo, failed
  custom-record closure, isolated equation rejection, success through exactly one target
  assignment with zero target-side OMath builds, rejection before target mutation,
  failed-open artifact deletion and hidden target drift during staging. The full gates
  pass **504 Engine**, **389 Native**
  and **1,309 Python tests**, with **16 intentional Python skips**; Ruff passes.
- Real Word 16.0 published two independently formatted paragraphs and the eight-line
  complex integral derivation in one staged batch. Post-publication semantic readback
  proved all six native integrals, all six differentials and their lower-baseline
  placement. This is direct Word evidence, not only fake-COM evidence.
- Installed runtime `0.39.0+codex.20260724154131` reports 107 actions, 15 exposed tools
  and 20 explicit metadata contracts. Build, personal marketplace source and enabled
  cache are identical at 196 files and 87,050,540 bytes with zero path, length or hash
  differences. The 36,731,827-byte ZIP SHA-256 is
  `88fe233ed24107866d757677f809f80732427acf140859576dec0f3f74642c55`.
- One boundary remains open: independent reconstruction is attempted after a partially
  applied `FormattedText` publication, but real Word can normalize the recovered package
  beyond the accepted `w:rsid*` session metadata. Without complete semantic equality,
  WordToolkit reports `ROLLBACK_FAILED` and quarantines that state; it does not call the
  document clean or allow the agent to continue.

## Operation-wide verified Word rollback — 2026-07-24

- Removed the legacy rollback helper that closed a custom record, called `Undo(1)` once
  and swallowed both failures. All current native custom-Undo mutation families now use
  one fail-closed verifier and quarantine the live handle/document identity when exact
  restoration cannot be proved.
- The checkpoint covers whole-document Flat OPC, main-story text/OOXML, every accessible
  linked story range, exact target/context ranges, save state and structural counts.
  SmartArt text and review properties add supplemental state fingerprints because Word's
  custom Undo does not reliably cover those COM states.
- Ten adversarial rollback tests include visible-state-only restoration that leaves hidden
  OOXML residue, supplemental-state drift and a reflection contract proving that the old
  silent `Rollback` entry point cannot return. The complete native suite passes 382/382.
- A forced real Word 16.0 acceptance probe inserted one line and called the shared failure
  path. Word returned `true` from `Undo(1)` and restored the visible text, paragraph count
  and equation count, but whole-document Flat OPC, range OOXML and the story graph still
  differed. The runtime returned `ROLLBACK_FAILED` and quarantined the identity. This is
  direct evidence that a successful Boolean Undo result is not transaction proof.
- This closes the silent-rollback lie, not live publication atomicity. Complete
  heterogeneous staging and target publication recoverable without Word Undo remain the
  next P0 boundary.
- Full gates pass **504 Engine**, **382 Native** and **1,309 Python tests**, with **16
  intentional Python skips**; Ruff and all four C# format verifiers pass. Installed
  runtime `0.39.0+codex.20260724142108` reports 107 actions and 15 exposed tools. Build,
  marketplace source and enabled cache are identical at 196 files and 86,989,407 bytes.
  The 36,718,529-byte ZIP SHA-256 is
  `4df5585249c0e5cfbad23b0273ce97170fdc2598da34dda7d0ff09e01be3ede0`.

## Verified mixed-operation rollback and equation staging — 2026-07-24

- Replaced the false `Undo(1)` success assumption in the shared mixed text/equation
  mutation path with an exact pre-mutation checkpoint and mandatory post-Undo proof.
  The checkpoint covers live version, Word save state, main-story text, target/context
  WordOpenXML, range boundaries and paragraph/equation/table/field/bookmark/shape/comment/
  note/section counts. A false or throwing Undo, failed custom-record closure, unreadable
  state or any mismatch now returns `ROLLBACK_FAILED` and quarantines both the handle and
  document identity instead of exposing a reusable version-zero lie.
- Every equation in a batch is built, styled and read back in an unsaved hidden Word
  staging document before the target is touched. A staging failure discards that document
  and preserves target content, counts, live version and handle. Publication still uses
  verified rollback because target-side Word normalization can fail independently.
- Seven adversarial fake-COM regressions cover exact rollback, residue after a no-op or
  partial Undo, false/throwing Undo, failed custom-record closure, handle invalidation,
  reconnect blocking, explicit quarantine release and isolated staging rejection. Full
  release gates pass **504 Engine**, **378 Native** and **1,309 Python tests**, with **16
  intentional Python skips**; Ruff lint and all four C# format verifiers pass.
- A real Word 16.0 failure probe proved the original defect rather than merely simulating
  it: Word returned `false` from `Undo(1)` after a post-payload validation failure and the
  target changed from one empty paragraph to 33 paragraphs. Runtime
  `0.39.0+codex.20260724131918` returned `ROLLBACK_FAILED`, reported the structural/hash
  mismatch names, invalidated the handle and blocked inspection until explicit quarantine
  release. The contaminated unsaved document was deliberately neither saved nor closed.
- The final installed `0.39.0+codex.20260724132826` runtime then staged and published the
  full eight-line complex derivation of `int x^3 e^(2x) sin(3x) dx`. Native and semantic
  readback both passed; six n-ary integrals and six correctly placed differentials were
  counted, and expected/actual contract SHA-256 values were identical at
  `d736af3f7abfdce5d381bf23d5bd2d5ececeb8bac6cd8cf1d001f41a8dd8c708`.
  The saved package contains two paragraphs and one native OMath object; Microsoft Open
  XML SDK validation reports zero errors.
- The exact 14,332-byte DOCX proof has SHA-256
  `3206be53194410220de5afb4304bb280a16fdf80320fb59f01d6e94ee7de64f7`.
  Word's one-page 61,013-byte PDF has SHA-256
  `020f9ece9711fbe40bacdf6bc8b544a3c9019e959e4bbf48452c73c13860ee2b`.
  Its 180-DPI page was inspected directly: all eight aligned lines are legible, integral
  differentials stay on the baseline, and there is no raw `eqarray(...)`, clipping,
  overlap or broken glyph.
- Build output, personal source and enabled cache contain the same **196 files** and
  **86,962,059 bytes**, with zero path/length/hash differences. Installed discovery
  reports **107 actions**, **15 exposed tools** and **20 explicit metadata contracts**.
  The 36,709,200-byte ZIP SHA-256 is
  `bf6b236f7f9191559d4cb3a16ff49b8013d2daf527187a728dbb202c6861ebfc`;
  executable, runtime assembly, Engine assembly and SDK adapter SHA-256 values are
  `4ade449b2ef2dc6fb44a649c120e6866574a68586019b57b939c802df1ee8a42`,
  `e30f0ae103c0d84f7106a8b362f6b18f24d801a4f6ed2aba61360528f30b8174`,
  `b653e2e59d9bed94016ed2787fc9d64b7f9f0a55ed2776df9bcb852ccc37080c`
  and `e4d0fbe83f8d66105d84417705142da82fa3db64ec2027b98957aa9e0c6fdbe8`.

## Native index entries and indexes — 2026-07-24

- Added guarded native `XE` marking and `INDEX` insertion as actions 106 and 107.
  High-level inputs cover up to eight hierarchy levels, cross-references, an existing
  bookmark page range, page-number emphasis, heading separators, indented/run-in layout,
  zero-to-four columns, accented-letter grouping and six tab leaders. Neither action
  accepts raw field instructions or returns entry/bookmark/cross-reference/generated
  index text.
- Both actions require the current live version and one fresh target token where needed,
  run in one non-replayable custom Undo record, verify exact native collection/field
  deltas and option readback, and roll back on mismatch. Reference-table contract 1.1 now
  updates native indexes alongside contents, figures and authorities.
- Saved-package reference and dependency graphs now resolve complete non-deleted `XE`
  entries to concrete complete `INDEX` field nodes. Real-Word, package, PDF and release
  evidence for this slice is recorded only after those gates complete.

## Native authority citations and table of authorities — 2026-07-24

- Added `wordtoolkit.mark_live_word_authority_citation/1.0` and
  `wordtoolkit.insert_live_word_table_of_authorities/1.1` as native actions 104 and 105.
  Marking requires one fresh non-empty range/selection token and creates one exact native
  `TA` field in category 1–16. Insertion accepts one category or category 0 for all,
  requires matching native marks and creates one editable native `TOA` through Word.
- Both mutations require the current live version, use one non-replayable custom Undo
  record, verify exact native field/collection deltas and roll back on mismatch. TOA
  insertion additionally reads back all separators, `Passim`, entry formatting,
  category-header and tab-leader settings. The default is a real tab with dotted leaders;
  citation text, separator values, generated table text, field instructions and COM
  objects never cross the response boundary.
- Real Word exposed two defects before release. An empty `IncludeSequenceName` produced
  false `0-N` page ranges, so the action now passes `Type.Missing`. An empty entry
  separator crushed entries against page numbers, so the default is one tab plus the
  semantic `dots` leader. Exact option readback makes both fixes fail closed.
- Saved-package reference analysis now completes all field parsing before cross-field
  resolution. Complete category-compatible `TA` entries resolve to concrete complete
  `TOA` fields; a category-zero table matches every valid authority category. The unified
  dependency graph emits three resolved `field_reference` edges instead of false
  missing-target warnings.
- Full local gates pass **500 Engine**, **362 Native** and **1,309 Python tests** with
  **16 intentional skips**. Ruff lint and all four C# format verifiers pass. The broader
  Ruff format check still reports 14 pre-existing Python files that this slice did not
  touch; they were not mechanically rewritten into an unrelated release change.
- The exact installed `0.39.0+codex.20260724113419` runtime marked three citations and
  inserted one all-category authority table in Word 16.0. Microsoft Open XML SDK
  validation returned zero errors. Independent inspection found four complete fields
  (`TA` x3, `TOA` x1), three resolved reference dependencies, a 158-node/239-edge fully
  resolved dependency graph and zero issues.
- The exact 14,399-byte DOCX has SHA-256
  `28515ac5afbbffd489bae3e6ed62e68b6c7c38d33230b50d52ed28db0a4e3562`.
  Its three-page A4 Word PDF is 44,174 bytes at SHA-256
  `a824863c425a598dc79be2ec764f34e31a9e122ca1eb24ae2bb7f5b4f6a9d82b`.
  Every page was inspected at 144 DPI: `Brown v. Board of Education` displays page 2 and
  `Forrester v. Craddock` displays pages 1, 3 with clean dotted leaders, no clipping,
  overlap, glyph boxes, raw field syntax or false `0-N` prefix.
- Build output, personal source and enabled cache each contain the same **196 files** and
  **86,856,508 bytes**, with zero path/length/hash differences and zero Python files. The
  self-contained 36,685,680-byte ZIP has SHA-256
  `c90c7dcf4b8ee3f2a341ddb2e2254e7fc3b942be040c65b8732744699e50460d`.

## Native table-of-contents insertion — 2026-07-24

- Added `wordtoolkit.insert_live_word_table_of_contents/1.0` as native action 103.
  It inserts at the document start/end or a fresh collapsed cursor, accepts semantic
  heading levels and heading/outline source flags, optionally repaginates and updates,
  and never accepts raw field instructions.
- One non-replayable custom Undo record contains native `TablesOfContents.Add` plus the
  optional repagination/update. Success requires a one-object collection delta, a unique
  exact range reacquisition, a non-empty range and at least one field. Any mismatch
  requests one rollback; generated contents text, field code and COM objects are absent
  from the response.
- Full local gates pass **498 Engine**, **355 Native** and **1,309 Python tests** with
  **16 intentional skips**. Ruff and both C# format verifiers pass. Exact installed
  discovery reports **103 actions**, **15 exposed tools** and **16 explicit metadata
  contracts**.
- The final installed `0.39.0+codex.20260724102603` runtime created a disposable Word
  document with five Heading 1/2 entries over three pages, inserted the contents table
  at position zero, repaginated, updated, saved, validated and exported it. Microsoft
  Open XML SDK was available and returned zero errors.
- Independent inspection of the exact 14,992-byte unlocked copy found **one complete
  `TOC` and five complete `PAGEREF` fields**, five resolved dependencies and zero issues,
  incomplete/external/application-invoking fields. Its SHA-256 is
  `718627cd5f91b126aced63f5f1cc3890cc15fefa3fb9cd99567a4c2d63ff0982`.
- The three-page A4 Word PDF is 40,742 bytes at SHA-256
  `bafdd436de71c37e8cc481948b16fed4b839818e9383907d9d10e94af472f221`.
  Every 150-DPI page was inspected: the two-level contents table has legible leaders and
  page numbers 1–3, with no clipping, overlap, black glyph boxes or raw field syntax.
- Build output, personal source and enabled cache each contain the same **196 files** and
  **86,798,454 bytes**, with zero path/length/hash differences. The self-contained
  36,669,666-byte ZIP has SHA-256
  `6a5761da11cd6cf7769b9e669d636474a55053b5799887760d6912570604190d`.

## Guarded update of native reference tables — 2026-07-24

- Added `wordtoolkit.update_live_word_reference_tables/1.0` as native action 102. One
  request selects all native contents/figures/authorities tables or one exact kind and
  one-based index, rejects more than 128 objects, repaginates by default and performs the
  native full `Update` inside one custom Undo record. It requires the current live
  version, rechecks all three collection counts and every resulting field range, rolls
  back on mismatch and returns no generated table text, raw field instruction or COM
  object.
- Five new fake-COM regressions cover all-kind success and response privacy, exact-index
  selection without repagination, zero targets, the 128-object ceiling and full rollback
  after invalid post-update readback. A separate Engine regression fixes valid
  Word-generated `TA \l ... \s ... \c ...` fields so the long citation creates the
  typed index-entry edge without a false `FIELD_TARGET_MISSING` warning.
- Full local gates pass **498 Engine tests**, **351 Native tests** and **1,309 Python
  tests** with **16 intentional skips**. Ruff and C# formatting checks pass. Capability
  discovery reports **102 actions**, **15 exposed MCP tools** and **15 explicit metadata
  contracts**.
- The final installed `0.39.0+codex.20260724100603` runtime updated one
  `TablesOfContents`, one `TablesOfFigures` and one `TablesOfAuthorities` object together
  in Word 16.0. Counts before/after stayed 1/1/1, repagination and range/field readback
  succeeded, and Microsoft Open XML SDK validation returned zero errors.
- Independent saved-package inspection found **15 complete native fields**: two `TOC`,
  nine nested `PAGEREF`, two `SEQ`, one `TA` and one `TOA`; issue, external-field and
  application-invoking-field counts are all zero. The 16,891-byte DOCX has SHA-256
  `d8e2bb820e821bcd77bbd9d800e786749260316face98259a51fc05aa5f83263`.
- Word exported a four-page, 80,453-byte PDF at SHA-256
  `8bef6305acb5d5d4d51d8fb2e5b1381521ff86c38d4109a453ce0afb062ee141`.
  All four 150-DPI page rasters were inspected: the contents table, table list,
  authorities entry, captions, leaders and page numbers are present and legible, with no
  clipping, overlap, black glyph boxes or broken page margins.
- Build output, personal source and enabled cache each contain the same **196 files** and
  **86,772,501 bytes**, with zero path/length/hash differences. The self-contained
  36,663,305-byte ZIP has SHA-256
  `8a8fcdaf462a07f6bf5de1f4a2512d70ba522a002b822e5686a9813cf6ef9466`.

## Native captions and table of figures — 2026-07-24

- Added `wordtoolkit.insert_live_word_caption/1.0` and
  `wordtoolkit.insert_live_word_table_of_figures/1.0` as native actions 100 and 101.
  Both require the current live version, reject raw field code, resolve built-in labels
  through Word, use one custom Undo record, verify native field/collection counts and
  request one bounded rollback on failed verification. Custom labels must already exist.
- Seven focused fake-COM tests cover closed versioned contracts, localized and custom
  captions, a bounded custom-label scan, non-disclosure of caption title text, rollback
  after a bad field delta, successful table-of-figures update and rejection when
  matching captions are absent. Full local gates pass **497 Engine tests**, **346 Native
  tests** and **1,309 Python tests** with
  **16 intentional skips**. Ruff and C# formatting checks pass.
- The installed `0.39.0+codex.20260724093257` runtime reports **101 actions**, **15
  exposed MCP tools** and **14 explicit metadata contracts**. Build output, personal
  source and enabled cache each contain the same 196 files with zero path/length/hash
  differences.
- Through the installed MCP STDIO boundary, Word 16.0.20131 created two native table
  captions in the label-configured `above` position and one updated table of figures.
  The live version advanced from 0 to 3; Word exposed two matching captions, one
  `TablesOfFigures` object and seven live fields.
- The saved DOCX passes Microsoft Open XML SDK validation with zero errors. Independent
  reference inspection reports two complete `SEQ`, one complete `TOC` and two nested
  `PAGEREF` fields with zero issues. The 14,541-byte DOCX has SHA-256
  `3532d2bd0a8f90badfb021c57a4fd0477981c3853979d7647f40eed92f7eb9c9`.
- Word exported a 44,536-byte one-page PDF at SHA-256
  `c9fdeba24ede315342003cbe512ec8d266044fd61811e225e8bdd03e7945dea0`.
  Its 144-DPI raster shows both captions, both tables, two table-of-figures entries,
  dotted leaders and page numbers without clipping or overlap.
- The self-contained 36,657,762-byte ZIP has SHA-256
  `0f75b9cf43413080b2a9cc4765b52c3fbaf07ce24338d8044c2566c71c43943c`.

## Guarded live SmartArt text — 2026-07-24

- Added `wordtoolkit.prepare_live_word_smartart_text_edits/1.0` and
  `wordtoolkit.apply_live_word_smartart_text_edits/1.0` as native actions 98 and 99.
  Preparation reads a bounded complete root, returns at most 32 nodes and issues
  one-time tokens bound to Word root identity, layout/style/color, structure and every
  node text hash. Apply uses one non-replayable custom Undo record, exact post-write
  readback and automatic rollback. Stable no-ops do not mutate Word state.
- Nine focused drawing/SmartArt tests pass, including real mutation semantics in the COM
  fake, stale-context rejection before Undo, rollback on normalized readback, privacy and
  stable no-op. Full local gates pass **497 Engine tests**, **339 Native tests** and
  **1,309 Python tests** with **16 intentional skips**. Ruff passes.
- The installed `0.39.0+codex.20260724084026` runtime edited one native five-node
  SmartArt through the real MCP STDIO boundary. Live version advanced from 0 to 1,
  structure fingerprint remained unchanged, the exact new text was read back, the file
  saved and Microsoft Open XML SDK validation returned zero errors.
- The 20,482-byte source and 20,512-byte result have SHA-256
  `152df1fc626f24e4900f7a8a748cb5cd1e2638fbed31bd4089050fada8488737` and
  `4b2a39bc136582fbc615c45ff23bcdf84c188dfa9c86ab6d002fa0e3c8a8388f`.
  Word updated both DiagramML `data1.xml` and persisted `drawing1.xml`; both contain the
  new text exactly once and the old text zero times after save.
- Word exported 23,892-byte before and 23,183-byte after PDFs. Their one-page 144-DPI
  rasters retain the same five box bounds; the new three-line text remains inside its
  original box without clipping or overlap.
- Two pinned SDK builds produced byte-identical **196-file**, **86,690,088-byte** trees
  and **36,645,859-byte** archives at SHA-256
  `bb3ccf021e2135a1ca89c83920fcdd3c7aa73713936ac65db22febbe90096cf4`.
  The personal source and enabled cache have zero path/length/hash differences. Installed
  discovery reports **99 actions**, **15 exposed MCP tools** and **12 explicit metadata
  contracts**.
- General SmartArt authoring is still absent. The new slice does not create/delete/reorder
  nodes, change hierarchy or mutate layout/style/color, and it does not claim a durable
  DiagramML-to-COM node identity or cross-version pixel parity.

## Group-aware safe saved-package formatter — 2026-07-24

- Added `wordtoolkit.plan_ooxml_format/1.0` and
  `wordtoolkit.apply_ooxml_format/1.0` as native actions 96 and 97. The initial
  `remove_redundant_direct_formatting` policy removes fully modeled scalar
  paragraph/run property elements whose direct cascade result equals the preceding
  resolved value. It now also admits `rFonts`, `color`, `u` and paragraph/run `shd`
  only after a bounded candidate-by-candidate package reparse proves complete group
  equivalence. Conditional-table, revision and unmodeled cascade layers remain
  untouched; more than 64 composite proofs aborts the whole plan.
- Every candidate must preserve OPC structure, the exact predicted fingerprint, the set
  of changed parts, projected semantic content and effective formatting on every
  affected paragraph/run; paragraph edits include descendant runs. Baseline-aware Open
  XML SDK validation then gates apply. Signed packages, unavailable/truncated validation,
  new validation errors and changed apply-plan evidence are blocked.
- Plan is read-only. Apply rebuilds the exact output-path-bound plan and atomically
  creates only a new same-extension file. The source and existing destinations cannot be
  overwritten. Stable no-op apply creates no file. Neither operation opens Word or
  returns document text/raw XML.
- Full local gates pass **497 Engine tests**, **334 Native tests** and **1,309
  Python/OOXML tests**, with 16 intentional optional-environment skips. Ruff is clean.
  The native schema exposes 97 actions; ten actions now have explicit operation version,
  permissions, reversibility and output schemas.
- Two pinned .NET SDK 8.0.423 builds produced byte-identical 196-file,
  86,623,437-byte expanded trees and 36,627,205-byte ZIPs. The archive SHA-256 is
  `6bb2fce0a85bf61f03aeab320c68af985061bbcbff02e09b55299872f759a66f`;
  the executable is
  `62b0829110b427af883309a7bac951518a52a7c586b768b87f51fcea4d1aee76`,
  the native runtime assembly is
  `3491726cbeea318bec438d099b8db47c4362e9309f9c034b6ddadaaa7f41f2cd`,
  and the engine assembly is
  `a8444f95d58f76193e31866c27c48074fb2c043e44f86d3b32ce0c747df23756`.
- The personal source and enabled cache at `0.39.0+codex.20260724080018` each contain
  the same 196 files with zero path/length/hash differences. Installed capability
  discovery reports 97 actions and the exact runtime version.
- A cold installed-runtime plan scanned 12 candidates, performed five composite proofs
  and removed exactly 11 elements/330 source bytes from `word/document.xml`. Engine,
  semantic, effective-formatting and baseline-aware Open XML validation all passed,
  and apply produced the exact predicted fingerprint
  `ce2bb1fa46ff438053b9ff4e7c0b498198c9130783e56431dbd57817cfe8e8dc`.
  A second plan was a stable no-op and no-op apply created no file.
- The installed runtime then connected the source and opened the result read-only in
  Microsoft Word. Both snapshots were valid with zero Microsoft Open XML SDK errors and
  exported to one-page 23,821-byte PDFs. Their 144-DPI page PNGs were byte-identical at
  SHA-256 `2a882af2560fb684e55664c647e964ae3eebd98403292eaf07def3463895c966`;
  visual inspection found no clipping, overlap or formatting drift. Word PID 14820 was
  unchanged. This proves one licensed Word equality point, not a broad multi-version
  rendering corpus.

## Word-executed drawing layout — 2026-07-24

- Added `wordtoolkit.inspect_live_word_drawing_layout/1.0` as native action 95. It
  projects bounded layout evidence calculated by the connected Microsoft Word build for
  floating shapes, inline shapes, groups and optional SmartArt nodes. Positions preserve
  their Word reference frame and distinguish alignment constants from point offsets;
  viewport pixels remain a separate explicit opt-in and are never called page geometry.
- The action caps the root scan at 10,000, output at 100, group expansion at 128 members
  and depth 16, SmartArt expansion at 128 nodes and 256 associated shapes, diagnostics at
  100 and opt-in text at 4,096 characters. Text-bearing COM getters are not called when
  `include_text=false`. Raw XML, raw COM objects and external content are never returned.
- Full gates pass **488 Engine tests**, **329 Native tests** and **1,309 Python/OOXML
  tests**, with 16 intentional environment/model skips. Ruff is clean. The PowerShell
  5.1 acceptance scripts parse without errors, the native schema exposes 95 actions and
  `git diff --check` reports no whitespace errors.
- Two pinned .NET SDK 8.0.423 builds produced byte-identical 196-file,
  86,518,137-byte expanded trees and 36,600,389-byte ZIPs. The archive SHA-256 is
  `b7e9476d605e630c370e76a8982f9efac99579b281c9b4048bb2447cfce13952`;
  the executable SHA-256 is
  `b07d2d666cd5cbdc73a3c904ed57c59acea31589f3d9449317f3a62bfe3f589b` and
  the runtime assembly SHA-256 is
  `8511af2b7069add7dd0bdc0e0331500a1e0e88043a60fca75fbac483edf146a0`.
  All trees contain zero Python runtime files.
- The complete 49-action licensed Word gate passed on the packaged runtime in 51.614
  seconds. Capability discovery returned 95 actions; the gate produced a validated DOCX,
  a 166,264-byte PDF, 49 paragraphs, one table, 12 editable equations, one image, one
  comment, one footnote and one endnote, then closed only its own document. The existing
  Word process ID was unchanged before and after the run.
- The personal source and enabled cache at `0.39.0+codex.20260724063719` each contain
  the same 196 files with zero path/length/hash differences from the release tree. A cold
  installed-runtime proof over `lo_groupshape_sdt.docx` returned one floating Word group
  and one group-local child normalized to native `msoAutoShape`, page 1/section 1,
  150.2 by 815.35 points, right-aligned relative to the page, -44.25 points relative to
  the paragraph and in-front-of-text wrapping. The response was exact and untruncated,
  had zero diagnostics and returned no sensitive text, XML or COM objects. The source
  hash remained
  `83c47ec672afd0bce726f90582f40ebe96e10514c1f6da3bfec5bc9507db456c`,
  and the pre-existing Word process remained running.

## Declared DrawingML/VML shape topology and path model — 2026-07-24

- Extended `WordFigureCaptionGraph` instead of adding a competing graph. Shape
  representations now own stable `wdsh_` group/shape/picture/graphic-frame/content-part
  nodes with parent/child topology, DrawingML transforms, recognized preset geometry,
  bounded custom paths and move/line/arc/quadratic/cubic/close commands, formula points,
  fill/line summaries, known effect kinds and text-flow declarations. VML path source is
  reduced to length plus SHA-256 evidence. No formula, effect, text or page layout is
  executed.
- Source limits cap shape nodes at 100,000, paths at 200,000, commands/effects at
  500,000 and formula points at 1,000,000. Individual formulas stop at 256 characters;
  every retained node/path/command/point/effect is charged to the shared `wop1` operation
  lease. Unknown enum-like tokens remain diagnostics rather than trusted values.
- The dependency graph adds typed `FigureShape` nodes plus
  `FigureRepresentationContainsShape` and `FigureShapeContainsShape` edges. The existing
  honest gap remains `drawingml_vml_rendered_geometry_and_layout_execution`: declared
  shape structure is covered, rendered Word geometry is not.
- `inspect_ooxml_figures` remains counter-only by default. Full shape data requires
  `include_shape_details=true`, `view=representations`, `detail=declared` and
  `max_items<=2`; one response is capped at 64 nodes. Path commands/formula points still
  require `include_geometry=true` and stop at 64 paths, 128 commands, 256 points and
  4,096 formula characters. Names/text still require `include_text=true`.
- The Microsoft 365 Open XML SDK validates the group/custom-geometry/effect/text-box
  fixture with zero errors. Hostile tests cover over-limit path points and formulas plus
  untrusted text-wrap and line-cap tokens.
- Full gates pass **488 Engine tests**, **325 Native tests** and **1,309 Python/OOXML
  tests** with 16 intentional environment/model skips. Release builds have zero warnings;
  Ruff is clean and mypy passes all 29 maintained Python files.
- Two pinned .NET SDK 8.0.423 package builds produced byte-identical 196-file,
  86,418,307-byte expanded trees and 36,572,351-byte ZIPs. The canonical archive SHA-256
  is `efdb5c5a78b2284296bc4ee3d8f85afa3f98a5d83c0fdb93b2314160babfb9db`;
  the executable SHA-256 is
  `b870d1d17891e36137d8e3d5aa4998478b0d519639d5bea2dcf426d9f7dce866` and
  the runtime assembly SHA-256 is
  `3e92a7bd9f4f688805cb0d3762049748ee3a6d1a24ea90f8a3577361a16b9ae4`.
  All trees contain zero Python files.
- The personal source and enabled cache at `0.39.0+codex.20260724055428` each contain
  the same 196 files with zero path/length/hash differences from the release tree.
  Installed capability discovery reports that exact version, 94 actions and the updated
  shape-aware `inspect_ooxml_figures` contract.
- The installed runtime inspected real `lo_groupshape_sdt.docx` without opening Word or
  changing the source hash. A 6,598-character complete MCP line returned one shape
  representation, both declared group nodes and no truncation. Installed dependency
  inspection returned 67 nodes and 72 edges, including one representation-to-shape and
  one shape-to-shape edge, while retaining the exact rendered-layout coverage gap.
- The preceding licensed real-Word equation gate remains the latest 48-action live Word
  proof. It was not rerun for this package-only read slice.

## Declared DrawingML/VML placement — 2026-07-24

- Extended the existing `WordFigureCaptionGraph` instead of creating a second layout
  model. DrawingML anchors now retain typed simple position, reference frames,
  alignment/offset, effect extents, stacking/overlap flags, Office 2010 relative size,
  wrap side/distances and bounded tight/through polygons. Microsoft 365 validation of
  the canonical anchor fixture reports zero errors.
- Known VML position, margin/size, z-order, relative-frame, tenth-percent, wrap-distance,
  visibility and `wrapcoords` declarations are typed. Physical lengths normalize to EMU;
  arbitrary enum-like strings are not projected as trusted values. DrawingML and VML
  polygons share a 4,096-point source limit and operation-wide resource accounting.
- `inspect_ooxml_figures` returns declared scalar placement only with
  `detail=declared`; exact polygon coordinates additionally require
  `include_geometry=true`, `view=representations`, `max_items<=2` and are capped at 128 points per
  response item. Every placement says it is declared data, not rendered geometry. The
  package path never opens Word or runs layout.
- The former `drawingml_vml_advanced_layout` omission is gone. The dependency graph now
  names only `drawingml_vml_rendered_geometry_and_layout_execution` as the remaining
  boundary.
- Full gates pass **486 Engine tests**, **324 Native tests** and **1,309 Python/OOXML
  tests** with 16 intentional environment/model skips. Release builds have zero warnings;
  Ruff is clean and mypy passes all 29 maintained Python files.
- Two pinned .NET SDK 8.0.423 builds from commit `9b8c0c4` produced byte-identical
  196-file, 86,304,532-byte expanded trees and 36,538,553-byte ZIPs at SHA-256
  `fa1e93ba23963066f5e6b02db4367d41fd590e4254edc7abec448316a2e4f061`.
  Both trees and ZIPs are identical and contain zero Python files.
- The personal marketplace and enabled cache at
  `0.39.0+codex.20260724050626` each contain the same 196 files with zero path, length or
  hash differences from the release tree. Installed capability discovery reports the
  exact version, 94 actions and the unchanged lazy `inspect_ooxml_figures` action.
- The installed runtime inspected the real `poi_drawing.docx` fixture: 21 figures were
  found, the two-item geometry page returned in a 4,738-character complete MCP line,
  every placement was marked declared-only, Word remained closed and the source hash
  stayed unchanged. The installed dependency summary returned 3,515 nodes and 3,773
  edges, retained figure and SmartArt coverage, removed
  `drawingml_vml_advanced_layout` and exposed only
  `drawingml_vml_rendered_geometry_and_layout_execution` as the drawing-layout gap.

## Native SmartArt graph and compact inspector — 2026-07-24

- Added `WordDiagramGraph`, a bounded read-only model for Transitional and Strict
  DiagramML references, data/layout/quick-style/color parts, persisted drawings, points,
  connections and definition identities. Point text is counted and discarded. The
  Microsoft Open XML SDK validates the reference fixture before parser assertions run;
  malformed cardinality, duplicate model IDs, unresolved endpoints, invalid orders,
  unsafe XML and resource-limit breaches are all gated.
- Integrated diagram and point nodes plus definition, containment, connection and part
  edges into the shared dependency graph. `smartart_diagrams` is no longer an unmodeled
  dependency domain. Office layout execution, geometry rendering and mutation remain
  explicit boundaries.
- Added lazy `inspect_ooxml_diagrams` as native action 94. Six paged views, exact
  diagram/point-type filters, independent key/hash/source opt-ins, a shared `wop1` lease
  and a 10 KiB projected-item ceiling keep the response bounded. Point text and raw XML
  are unavailable under every option; Word, Office layout and external targets are never
  opened or executed. Default output remains below 5,000 characters and the maximal
  synthetic keyed/source page remains below the 32 KiB complete-response gate.
- Full gates pass **483 Engine tests**, **323 Native tests** and **1,309 Python/OOXML
  tests** with 16 intentional environment/model skips. Release builds have zero
  warnings. Ruff is clean and mypy passes all 29 maintained Python source files.
- Two pinned .NET SDK 8.0.423 builds from commit `2608cc9` produced byte-identical
  196-file, 86,260,370-byte expanded trees and 36,523,652-byte ZIPs at SHA-256
  `808ff3dd51faa0b76fb562da7dea5ea9222856a5158d2330e27c506c25e2844a`.
  Both trees and ZIPs are identical and contain zero Python files.
- The personal marketplace and enabled cache at
  `0.39.0+codex.20260724043611` each contain the same 196 files with zero path, length or
  hash differences from the release tree. The installed executable reports that exact
  version, 94 actions and the lazy, read-only, closed-world
  `inspect_ooxml_diagrams` contract.
- Fresh installed-runtime MCP calls over `lo_chart.docx` return a 961-character empty
  SmartArt summary with zero issues and a 3,225-character dependency summary with 75
  nodes, 83/83 resolved edges and `smartart_diagrams=true`. Both calls report
  `word_opened=false` and `python_used=false`; the source SHA-256 remains
  `222628bcdb587c232e968d6aa1ba0a70dfd80845a4a2b8050316ec9d142ad33f`.

## Typed document properties and field dependencies — 2026-07-24

- Added a bounded read-only graph for OPC core, Office extended and custom typed
  properties. Exact relationship/content-type families, Strict/Transitional namespaces,
  standard custom `fmtid`, numeric `pid`, duplicate case-insensitive names/IDs, one typed
  value child and scalar lexical forms are validated. Core created/modified timestamps
  additionally require an `xsi:type` QName resolving to `dcterms:W3CDTF`. Invalid values
  remain diagnosed and cannot resolve a field; complex/binary values are classified
  without decoding.
- Added lazy `inspect_ooxml_properties` as native action 93. Summary-first paging, exact
  `wdp_`/family/type filters and a 32 KiB projected-item ceiling keep the response
  bounded. Custom names, scalar values, HMAC equality fingerprints and source provenance
  have four independent opt-ins. Raw XML, complex values and field results are absent;
  the action never opens Word or mutates the package.
- Integrated document-property and persistent-document-variable definition nodes with
  `DOCPROPERTY` and `DOCVARIABLE` reads in the unified dependency graph. `SET`/`ASK` are
  not misrepresented as persistent definitions. The nine-producer golden oracle now
  records the added definition edges; its existing non-property counts remain unchanged.
- Focused verification passes all 14 property/dependency and complete-golden-oracle
  cases. The full gates pass **471 Engine tests**, **318 Native tests** and
  **1,309 Python tests** with 16 intentional environment/model skips. Ruff is clean;
  mypy is clean across the maintained 29-file `src/wordtoolkit` layer. The broader
  historical `scripts` directory is not part of that mypy claim and still has known
  typing debt.
- Release packaging now embeds the complete manifest SemVer, including build metadata,
  into the native assembly. `--version`, MCP `serverInfo.version` and
  `toolkit_version` therefore identify the exact installed build instead of collapsing
  every package to the same base version.
- Two pinned .NET SDK 8.0.423 builds from commit `f378726` produced byte-identical
  196-file, 86,133,096-byte expanded trees and 36,490,920-byte ZIPs at SHA-256
  `6505f683e21486c257f55ad6cebd102cf661ea5b57b3b2701ad0fe4d51c644d8`.
  The personal marketplace and enabled cache at
  `0.39.0+codex.20260724033816` have zero path/length/hash differences and contain zero
  Python files. The installed executable reports that exact version and 93 actions.
- Fresh installed-runtime calls over `lo_chart.docx` report 27 valid properties with
  zero issues, then 75 dependency nodes and 83/83 resolved edges with exact property/
  variable coverage. The complete mirrored responses are 3,278 and 7,320 characters;
  Word remains unopened and the source SHA-256 remains
  `222628bcdb587c232e968d6aa1ba0a70dfd80845a4a2b8050316ec9d142ad33f`.
  The licensed real-Word acceptance gate also passes the complete eight-row complex
  derivation of `\int x^3e^{2x}\sin(3x)\,dx`: six integrals and six owned differentials
  survive native build-up/readback with equal canonical contract hashes, and the
  disposable document is discarded.

## Typed active-content metadata graph — 2026-07-24

- Added a bounded read-only graph for legacy/ISO Word OLE declarations, embedded and
  linked targets, ActiveX XML/binary bindings, embedded-package payloads, VBA
  project/data/support/customization parts, VBA project-signature parts and OPC package
  signature topology. Exact standardized relationship URIs are required; suffix
  lookalikes do not enter the model. Orphan declarations, duplicate relationship IDs,
  missing targets, macro-container contradictions and invalid signature topology remain
  typed diagnostics.
- Added lazy `inspect_ooxml_active_content` as native action 92. Summary-first paging and
  exact IDs/kinds keep requests small. Names, targets, hashes and source provenance have
  separate opt-ins. Raw XML, field-code text, binary values, ActiveX license strings and
  property values are unavailable. The action never opens Word, decodes a binary, opens
  an embedded package, executes code, follows an external target or presents signature
  presence as cryptographic validation.
- Integrated typed payload, declaration and ActiveX nodes plus six edge families into
  the unified dependency graph and shared `wop1` lease. The graph now exposes an exact
  active-content coverage flag and source issue count while retaining explicit gaps for
  binary internals/execution, cryptographic validation/resigning and encrypted packages.
  The cross-producer golden oracle changed only for the LibreOffice chart fixture whose
  embedded workbook now receives one payload node and two typed edges.
- Current local verification passes 457/457 Engine and 313/313 Native tests with no
  skips, plus 1309 Python/OOXML tests with 16 intentional environment/model skips.
  Release builds have zero warnings, Ruff is clean and mypy passes all 29 maintained
  Python source files. Four native inspector regressions enforce default redaction, a
  sub-5,000-character direct result, a sub-8,000-character complete gateway envelope, zero COM
  calls, independent disclosure opt-ins, exact selector failure and the shared operation
  budget boundary down to one byte. Two independent .NET SDK 8.0.423 package builds from
  commit `41e85a1` produced byte-identical 196-file, 86,038,586-byte trees and
  36,461,821-byte ZIPs at SHA-256
  `5ef40ebf6a112bc2b791b5e862cafc78a0cd275ce88c46721f39a65ce61c677e`.
  The personal marketplace and enabled cache at `0.39.0+codex.20260724023427` have zero
  path/length/hash differences and contain zero Python files. Fresh installed-runtime
  calls over `lo_chart.docx` report one `embedded_package` payload, 48 dependency nodes,
  56 edges and active-content coverage; both actions keep Word, macros, binary decoding,
  embedded-package opening, signature verification and external traversal disabled. The
  complete dependency response is 7,040 characters, below its 8,000-character gate.

## Typed bibliography source graph — 2026-07-23

- Added a provider-neutral, read-only bibliography graph for both supported Word source
  namespaces. It retains source provenance, stable identity, typed Tag/SourceType/GUID/
  LCID fields, bounded scalar metadata, contributor roles, people and corporate names.
  Unique case-insensitive tags resolve `CITATION` fields to concrete source nodes in the
  unified dependency graph; missing or duplicate tags remain unresolved.
- Added lazy `inspect_ooxml_bibliography` as native action 91. Default output is paged and
  redacts tags, titles, GUIDs, names, field values, style paths and URIs. The fixed policy
  parses the package only: Word is not opened, fields are not evaluated, bibliography
  XSLT is not executed and external targets are not followed. One 640 MiB `wop1` lease
  now spans OPC, semantic, reference and bibliography projection.
- Closed the independent review's three P1 defects before publication. People and
  corporate names are capped across the whole source; response projection has a shared
  64 KiB payload budget; low-entropy values use process-keyed HMAC fingerprints; and
  duplicate singleton identity fields fail closed rather than selecting the first XML
  value. Manual final diff review additionally capped unique unmodeled element names at
  256 per source and charged their characters before display-string materialization.
  Empty citation lookup fails unresolved instead of throwing.
- Final local verification passed 448/448 Engine and 291/291 Native tests with no skips,
  plus 1309 Python tests with 16 intentional environment/model skips. Release builds of
  the native runtime, Open XML SDK validator adapter and benchmark project have zero
  warnings/errors. Ruff is clean and mypy passes all 29 maintained Python source files.
- The reproducible Release benchmark generated 10,000 unique Book sources, 10,000 people
  and 10,000 matching `CITATION` fields. Seven bibliography builds resolve 10,000/10,000
  citations with zero issues, retain the package fingerprint and measure 642.5681 ms
  median, 811.9211 ms p95/max, 245,631,072 median thread-allocated bytes and
  81,230,184/671,088,640 accounted operation bytes on Windows 10 x64, .NET 8.0.29,
  12 logical processors, workstation GC. The raw JSON is checked in under
  `docs/benchmarks/bibliography-10k-2026-07-23.json`. No prior-feature baseline exists,
  so no speedup is claimed.

## Remote heterogeneous draft batch — 2026-07-23

- Added the provider-neutral `wordtoolkit.apply_document_operations/1.0` contract and
  the remote MCP adapter over all 33 ordinary draft-mutator types. The generated closed
  `oneOf` schema rejects read-only hybrid actions, unknown/nested transaction fields,
  empty or 17-operation lists and invalid types. Runtime repeats validation through each
  standalone Pydantic contract and enforces a 1 MiB aggregate argument limit. The same
  exporter produces `schemas/draft-operations.v1.json` with input/success/error schemas,
  permissions, side effects, limits and examples that pass Draft 2020-12 validation.
- One locked clone receives the ordered operations; the final candidate is snapshotted
  and structurally validated once, then the active engine swaps and `draft_version`
  advances once. Injected middle-operation and final-validation failures preserve the
  original engine, package hashes and version. Cancellation drains both staging and
  transaction success before lock release. Image binding uses one Apps-compatible
  top-level `files` array plus per-operation `file_index`; missing, nested and unused
  references fail before a fork. Partial downloads are removed, while a complete staged
  upload remains explicitly outside document atomicity and is covered by regression.
- Final local verification passed 438/438 Engine and 283/283 Native tests with no skips,
  the Open XML validator Release build with zero warnings/errors, and 1309 Python/OOXML
  tests with 16 intentional environment/model skips. Ruff and targeted mypy over the
  changed contract, adapter and test modules are clean.
- Fifteen Windows x64/Python 3.13 in-process FastMCP samples measured
  `format_paragraph -> enable_track_changes -> insert_paragraph` at 189.479 ms median
  across three standalone COW calls and 70.901 ms in one batch (-62.58%, -118.578 ms).
  The measured compact request JSON fell from 480 to 427 characters (-11.04%), with one
  instead of three COW commits/version increments. Creation/close and the optional SDK
  validator were excluded. A representative one-step compact success envelope fell from
  290 to 102 characters by removing repeated protocol/backend/atomicity fields and
  operation-name echoes. The generated 33-variant input schema is regression-capped
  below 20,000 compact characters and measured 19,088; the complete compact remote
  catalog rose from 57,566 to 77,439 characters (+34.52%), so the request saving is not
  misreported as a whole-catalog token saving.

## WordToolkit 0.39.0 operation-wide dependency resource lease — 2026-07-23

- Added one cumulative `word_operation_accounted_v1` lease across ZIP/OPC admission,
  metadata, lossless XML, semantic projection, typed styles/numbering/references/
  sections/charts/figures/content-controls/tables and final dependency-graph retention.
  The calibrated default is 640 MiB. The existing 128 MiB graph-local `wdg1` boundary
  remains independent; MCP reports the new lease only as compact `operation_budget`
  model `wop1`.
- Moved hostile-archive rejection ahead of `ZipArchive.Entries`: bounded EOCD/ZIP64
  preflight now rejects excessive entry counts, central-directory bytes, multi-disk
  archives and invalid subtraction-based offsets. Public non-seekable streams pass
  through a 576 MiB bounded random-name `CreateNew` spool with `FileShare.None` and
  `DeleteOnClose`; its buffer is charged and zeroed. OPC metadata counts, diagnostics,
  XML retention and typed aggregate limits all reject before the next guarded retention.
- Cancellation now reaches metadata XML loading and semantic fingerprint recursion.
  Resource exhaustion returns bounded `PACKAGE_LIMIT` stage/attempted-charge data; it
  does not return paths, XML or document content. Case-insensitive ZIP collision
  diagnostics disclose only a spelling count instead of joining attacker-controlled
  names. The exact dependency input schema remains byte-for-byte unchanged at SHA-256
  `e371a9c3800f58dcd685c80a9d5a63cee967aa2ba563a8bb01965c373f06b7a2`.
- Final local verification passed 438/438 Engine and 283/283 Native tests with no skips.
  The full Python/OOXML lane passed 1,279 tests with 16 intentional environment/model
  skips; Ruff is clean and mypy succeeds across 28 maintained source files. Independent
  correctness/resource and final contract red teams found no remaining P0/P1 issue.
- Five cold 99,997-node samples deterministically charge 539,282,576/671,088,640
  operation bytes and 130,132,744/134,217,728 graph bytes. Medians are 2,956.082 ms
  dependency build, 5,491.3294 ms measured total, 175,334,544 retained managed bytes,
  1,610,477,144 allocated bytes and 615,305,216 bytes peak working set. Against the
  same-host 0.38 series, peak working set rose 6.75%; no peak-memory win is claimed.
- Two pinned .NET SDK 8.0.423 builds produced byte-identical 196-file, 85,759,136-byte
  trees and 36,380,971-byte ZIPs at SHA-256
  `2875209550cca57b36d0e99873d443c337f89950e5230d3083110398da0d7468`.
  The package contains no Python files. The enabled personal-plugin cache at
  `0.39.0+codex.20260723142922` has zero path/length/hash differences from that tree;
  installed initialization reports runtime 0.39.0 and capability discovery reports 90
  actions. A packaged read-only dependency smoke call returned 209 nodes, 259 edges,
  both `wop1` and `wdg1`, and left the source DOCX hash unchanged.

## WordToolkit 0.38.0 dependency-graph byte boundary — 2026-07-23

- Replaced two eager dictionaries of per-node edge arrays with compact incoming and
  outgoing compressed-row offset/index arrays. The public typed adjacency view preserves
  deterministic kind/ID order without allocating a list for every node; endpoint checks
  and cancellation remain fail-closed.
- Added the deterministic `dependency_graph_accounted_v1` model. Production rejects
  before retaining an item that would cross 128 MiB and independently caps keys and
  metadata at 65,536 characters. The MCP response exposes only
  `byte_budget: {model, used, maximum}` and stays inside the existing sub-8,000-character
  full-envelope regression gate.
- The exact-budget regression rebuilds one graph with a ceiling one byte below its
  measured usage and requires `WordDependencyLimitException`. Compact incoming/outgoing
  ordering, missing-node empties, option validation and Native summary disclosure are
  covered. Full local results are 429/429 Engine, 281/281 Native and 1279/1279 Python
  passed with 16 intentional Python skips; Ruff and mypy are clean.
- Five cold-process 99,997-node/99,996-edge Windows x64 samples per version account
  130,132,744 of 134,217,728 bytes and use exactly 1,599,952 adjacency-index bytes.
  Against 0.37.0, median retained managed memory fell 16.8%, managed allocations 4.2%,
  dependency-build time 9.0% and total measured time 5.4%. Median peak working set was
  effectively flat (+0.1%), so no peak-memory reduction is claimed.
- This closes only the dependency graph's own missing byte boundary. The semantic and
  typed source graphs are still constructed first under independent limits; a shared
  operation-wide resource lease and immutable parsed-story storage remain open work.
- Two pinned .NET SDK 8.0.423 builds produced byte-identical 196-file, 85,730,528-byte
  trees and 36,372,963-byte ZIPs at SHA-256
  `69c3406b238590dae096370a550ff6902352972cbe942b2344e2d80dca1e0541`.
  The personal marketplace and enabled 0.38.0 cache have zero path/length/hash
  differences. Installed capability discovery reports runtime 0.38.0 and 90 actions.
  A packaged MCP smoke test over `pandoc_image_vml.docx` returns 119 nodes, 175 edges,
  `wdg1` usage 194,760/134,217,728 bytes and a 7,526-character call response while
  omitting six diagnostic items by default, keeping Word closed and leaving external
  targets unfollowed.

## WordToolkit 0.37.0 Figure/Caption graph — 2026-07-23

- Added a fingerprint-bound logical Figure/Caption graph, conservative
  `mc:AlternateContent` handling, strict Transitional/Strict QName parsing, inert
  internal/external resources and ambiguity-preserving caption association.
- Default lazy output is redacted and issue-free unless requested; closed runtime input
  validation matches the schema. Word, binary payloads, external targets and active
  content are never opened or executed by this action.
- Full Engine suite: **427 passed**. Full Native suite: **280 passed**. Python/OOXML:
  **1,279 passed, 16 intentional skips**. Ruff and maintained 28-module mypy are clean.
- The current 10,000-figure/10,000-distinct-relationship benchmark reports 1,897.6 ms
  median and 2,084.6 ms p95 across seven graph builds. It retained 317,194,760 managed
  bytes from the pre-read baseline and peaked at 1,251,549,184 bytes working set.
- Two pinned .NET SDK 8.0.423 builds produced identical **196-file**,
  **85,719,250-byte** trees and identical **36,369,889-byte** ZIPs at SHA-256
  `addb0ac796b9c5e41e6174f2bd1937d9fe996ae88015a81af5b000968caa63c7`.
  The enabled `wordtoolkit@personal` 0.37.0 cache has zero file/hash differences from
  the built package and its installed CLI reports 90 actions.
- Independent red teams found and forced fixes for quadratic relationship lookup,
  hidden VML direct targets, mixed revisions, false MCE primacy, foreign namespaces,
  unbounded QName collection, input-schema drift, repeated issue previews and error
  provenance leaks. At this 0.37.0 checkpoint the system-level dependency graph still
  lacked a byte budget; 0.38.0 closes that graph-local gap without claiming a
  whole-pipeline memory ceiling.

## WordToolkit 0.36.0 exact-target semantic SVG — 2026-07-23

- Added `wordtoolkit.render_ooxml_semantic_svg/1.0` as the seventh transport-neutral
  Engine/CLI/MCP operation. It requires the exact package fingerprint and semantic target
  ID, then creates one deterministic, self-contained SVG with selectable text,
  accessibility metadata and estimated flow/table geometry. The closed contract states
  `paginated=false`, `exact_text_metrics=false` and `pixel_equivalence_claimed=false`.
- HTML and SVG now share package preparation, target authorization, backend metadata and
  atomic create-new publication while retaining their public 1.0 contracts. The SVG path
  is bounded before final XML materialization at 40,000 text lines, 100,000 generated
  elements, a 1,000,000-pixel canvas dimension and a 256 MiB artifact. UNC and Windows
  device namespaces are rejected before any filesystem probe, preserving `network=none`.
- Security regressions target the hostile text, external hyperlink and complex field
  themselves: package text resembling `<script>` remains inert text, no active link or
  external URI is emitted, cached field results survive and instructions remain absent.
  Review annotations are backed by real review-graph evidence. Resource exhaustion,
  stale/missing/out-of-scope targets and structurally non-renderable roots fail closed
  without an output or temporary-file residue. Independent red-team review found no
  remaining P0, P1 or P2 issue in the final diff.
- Full Engine suite: **408 passed, 0 skipped**. Full Native suite: **272 passed, 0
  skipped**. Full Python/OOXML suite: **1,279 passed, 16 intentional skips**. Ruff, the
  maintained 28-module mypy lane, six HTML/SVG schema regressions and the modified-file
  C# whitespace lane are clean.
- Native package version: `0.36.0+codex.20260723100732`, with 15 exposed core/gateway
  tools and **89** native actions. Seven actions publish complete operation-version,
  permission, reversibility and output-schema metadata.
- Two repository-pinned .NET SDK 8.0.423 Windows builds produced byte-identical
  **196-file**, **85,514,480-byte** expanded trees and byte-identical
  **36,306,172-byte** ZIPs. ZIP SHA-256:
  `5076fc4b41670ae05752ab36c0d23ff66fac7a2b8d753706b15473a478e4682a`;
  expanded manifest SHA-256:
  `f9bfd22a28d04e509968f3fb799e2e283e9ff8e3ccabdb14fdbc7503c1bc7207`.
  The installed and enabled `wordtoolkit@personal` cache has the same version, file count,
  byte count and manifest with zero file differences from the built package.
- Hosted CI run [`29992366785`](https://github.com/Fr4u/WordToolkit/actions/runs/29992366785)
  passed all five jobs for commit `d6b663922a45f8e271872f61a63dffa86bf21256`.
  Downloaded Windows artifact `8557592140` matched both pinned-SDK local ZIPs byte for
  byte and its normalized 196-file tree had zero length/hash differences. The first local
  package built under SDK 10.0.300 was rejected as release evidence after its bundled
  self-contained runtime differed; the official SDK 8.0.423 archive was verified against
  Microsoft release metadata before rebuilding.
- The packaged executable selected the first semantic table in the checked-in Mammoth
  fixture under its exact fingerprint and created one **2,266-byte** SVG at SHA-256
  `b8e436cd184ab1b316321e305ad850f1f21a43e518428d6d7e9e54f6a48536d9`.
  XML inspection found four real text nodes, zero script/`foreignObject` nodes and zero
  active-link, event or external-URI attributes. The source hash and Word process count
  did not change; the response contained no source/output path and none of the four
  rendered text values.
- The checked-in 10,000-node benchmark projected 9,996 nodes, selected one six-node table
  and emitted a **1,305-byte** SVG seven times with one SHA-256
  (`a1c854f359ad28b50c2661576b0a348cdc0f4e0a1e7ac21c155c2ba414fe2ad3`).
  Median render time was **449.10 ms**, p95/max **844.87 ms**. The source stayed
  byte-identical, Word did not open and all external/active-content flags remained false.
  Whole-package projection still dominates; this is determinism and bounded-artifact
  evidence, not a Word-layout or proportional speedup claim.

## WordToolkit 0.35.0 semantic HTML renderer — 2026-07-23

- Added `wordtoolkit.render_ooxml_semantic_html/1.0` through one dependency-free Engine,
  strict JSON CLI and lazy MCP path. It renders the main body or all projected text
  stories to deterministic self-contained HTML without opening Word, following external
  relationships or executing active content.
- The artifact is explicitly `semantic_preview_non_paginated`: cached field results are
  retained while instructions are suppressed, hyperlinks are inert, tracked changes are
  annotated, equations use bounded linear-text fallbacks, and drawings/extensions become
  visible placeholders. CSP, HTML escaping, a 256 MiB limit, adaptive block containers,
  create-new writes and a two-writer race regression harden the boundary.
- Added an additive, fingerprint-bound `target_node_id` mode to the same contract. It
  renders one semantic subtree without siblings and rejects unbound, stale, missing,
  out-of-scope or structurally non-renderable targets before output creation. Selected
  rows, cells, nested tables, row-level revisions and recursively nested row/cell content
  controls receive valid synthetic table context; ambiguous mixed wrapper chains fail
  closed. Engine, CLI and MCP produce byte-identical selected artifacts and return no
  selected text in their JSON responses.
- On the checked-in 10,000-node synthetic benchmark path, the selected six-node table
  produced a 3,074-byte artifact versus 541,043 bytes for the full document (0.5682%).
  Two selected renders were byte-identical. Full-package projection still dominates
  latency, so this is recorded as an artifact/content-scope reduction, not a matching
  speedup claim.
- Full Engine suite: **396 passed, 0 skipped**. Full Native suite: **269 passed, 0
  skipped**. Full Python/OOXML suite: **1,276 passed, 16 intentional skips**; Ruff and
  the 28-module mypy lane are clean.
- Native package version: `0.35.0+codex.20260723091515`, with 15 exposed core/gateway
  tools and **88** native actions. Six actions now publish complete operation-version,
  permission, reversibility and output-schema metadata.
- Two supported Windows builds produced byte-identical **196-file**,
  **85,442,968-byte** expanded trees and byte-identical **36,285,575-byte** ZIPs. ZIP
  SHA-256: `a0737809d981cd739cade8f4036818408877afd903ff1049d538f29b4b99fb41`;
  expanded manifest SHA-256:
  `a45a778680bf0c9773089556e8a88104d155ebbf044bd852a24f20155d0596e4`.
  The manifest format is sorted relative path, byte length and lowercase file SHA-256,
  tab-separated and serialized as UTF-8/LF without a final newline.
- The packaged executable selected the real Mammoth table by semantic node ID under its
  exact package fingerprint and created one 3,742-byte HTML artifact at SHA-256
  `abb38860e9c0e2ad34bbec6131fe6438e64c8ee1ff7c2305fb49e3c1cded3c35`.
  An HTML5 parser found one table, one body, two rows and four cells with zero invalid
  table/body/row/cell parent relationships. The source hash and Word process count did
  not change, and the JSON response contained none of the four fixture cell values.
- Fifteen cold packaged processes rendered the checked-in Mammoth fixture at **254.66
  ms median** and **290.44 ms p95/max**. All 15 artifacts were the same 3,628 bytes with
  one SHA-256. Static HTML-tree, CSP and external-resource checks passed. The in-app
  browser blocked `file://`, so no visual screenshot claim is made and no bypass was used.
- Hosted CI run [`29988472344`](https://github.com/Fr4u/WordToolkit/actions/runs/29988472344)
  passed all five jobs for commit `dc72a8e811217bb9515b0ad30810c8925b3081cb`.
  Downloaded Windows artifact `8556049406` matched both local ZIPs byte for byte; after
  normalizing its single `wordtoolkit/` wrapper, the hosted 196-file tree had zero
  length/hash differences and the same expanded-manifest SHA-256.

## WordToolkit 0.35.0 logical table graph — 2026-07-22

- Added `WordTableGraphBuilder`, a bounded Transitional/Strict WordprocessingML read
  graph for declared grids, physical-to-logical cell placement, `gridBefore`/`gridAfter`,
  `gridSpan`, exact-span vertical merges, separately retained legacy `hMerge`, nested
  tables, contiguous repeating headers, row table-property exceptions, widths,
  fixed/autofit layout and Word-effective floating positioning.
- Integrated nested tables and vertical-merge continuation cells into the shared
  dependency graph. Table diagnostics now retain a source-linked dependency subject,
  and the accessibility linter consumes the same typed header result instead of parsing
  row XML independently.
- Added lazy `inspect_ooxml_tables` with summary/table/row/cell/merge/issue views, exact
  table/row/cell filters, paging and independent layout/name/source opt-ins. Cell text and
  raw XML have no response field. Nested ID and grid-width arrays are capped at 100 with
  explicit truncation flags.
- Full native engine suite: **340 passed, 0 skipped**.
- Full native MCP host suite: **228 passed, 0 skipped**.
- Full Python compatibility suite: **1,273 passed, 16 intentional skips**; Ruff clean.
  Open XML validator built with zero warnings. Generated basic and advanced artifacts
  passed structural and visual checks; the advanced report contained zero validation,
  warning or accessibility findings.
- Checked-in table scale points cover 10,000 and 100,000 physical cells. The smaller
  point completed package read + semantic projection + table build in 0.89 seconds with
  110.1 MiB peak working set. The larger completed in 5.23 seconds with 579.3 MiB peak
  working set and 1,885.1 MiB managed allocations. Both produced zero diagnostics; the
  production five-million-cell limit remains a rejection bound, not a throughput claim.
- Native package version: `0.35.0+codex.20260722231852`, with 14 public gateway tools
  and **84** lazy native actions.
- Two independent local builds produced byte-identical **195-file**, **85,056,480-byte**
  expanded trees and byte-identical **36,172,619-byte** ZIPs. ZIP SHA-256:
  `910a1cece37397f61568ea0a230fe663fdf54d834d8a8d367fa56b37ddfe1c13`.
  Executable SHA-256:
  `59fb8419dbe664d1b1ae7f0b5df35b7738cfd1c43b69719049d8e06dd64cd138`.
  Runtime assembly SHA-256:
  `f6e8923897b7578a0344347ec5c43f802f01f2de093553a819d036736ac698cd`.
  Document-engine assembly SHA-256:
  `1d7780bf01c5242b9e3d41e162d870a4bd7c7131653287e0c6737b97b18cc888`.
- The exact packaged MCP inspected the advanced torture DOCX as 2 tables, 34 rows and
  251 cells with zero table issues. Its complete compact JSON-RPC response was 2,308
  characters, did not start Word, left the Word process set unchanged, and contained no
  source part path, cell text or raw table XML.
- Mandatory hosted CI run [`29959678412`](https://github.com/Fr4u/WordToolkit/actions/runs/29959678412)
  passed all five jobs with zero annotations. Its downloaded Windows artifact matched
  both local ZIPs and the 195-file expanded tree byte for byte at the SHA-256 above.
  Human review and the licensed Word release gate remain required; the public release
  stays at 0.34.0.

## WordToolkit 0.35.0 content-control and Custom XML binding graph — 2026-07-22

- Added a typed, source-linked read graph for `w:sdt` controls, physical Custom XML
  stores, Word's built-in core/extended property stores, standard/Office 2013 bindings,
  restricted child-element XPath targets and repeating-section topology.
- Integrated control/store/target/repeating nodes and edges into the shared dependency
  graph. The nine-fixture semantic oracle was updated only after independent OPC part
  enumeration confirmed every newly counted physical or built-in store.
- Added lazy `inspect_ooxml_content_controls` with exact filters, paging and independent
  name/binding/source opt-ins. Custom XML values, visible bound values and raw XML never
  leave the action. Nested identifier arrays are capped at 100 and namespace/schema
  arrays at 20, with explicit truncation flags.
- Replaced quadratic positional XPath sibling rescans with one per-store parent/QName
  index and bounded intermediate expansion.
- Full native engine suite: **331 passed, 0 skipped**.
- Full native MCP host suite: **223 passed, 0 skipped**.
- The checked-in 10,000-binding point completed in 2.30 seconds with 201.9 MiB peak
  working set. The 100,000-control/binding/target point completed in 15.40 seconds with
  1,761.8 MiB peak working set and 5,960.7 MiB managed allocations. It required a
  benchmark-only 64 MiB metadata budget; production remains at 16 MiB. The result proves
  reachability on the measured 64 GiB host, not cheap operation.
- Native package version: `0.35.0+codex.20260722223601`, with 14 public gateway tools
  and **83** lazy native actions.
- Two independent local builds produced byte-identical **195-file**, **84,897,393-byte**
  expanded trees and byte-identical **36,123,774-byte** ZIPs. ZIP SHA-256:
  `dcaa12c58eed3b1b03f10c6772083a934c62e84f4615e738bee975f37fc7d471`.
  Executable SHA-256:
  `59fb8419dbe664d1b1ae7f0b5df35b7738cfd1c43b69719049d8e06dd64cd138`.
  Runtime assembly SHA-256:
  `5f3cee35fe78dd0f93392413b3990412b2e3d3da775b39abb216fd7d93ebecdc`.
- The exact packaged executable inspected the advanced torture DOCX as one control, one
  resolved binding, one target and two stores with zero issues and no Word process. The
  complete compact JSON-RPC response was 2,718 characters and contained no Word/Custom
  XML part path or raw XML element.
- Mandatory CI run `29956272868` passed all five jobs with zero check-run annotations.
  Its downloaded Windows artifact
  matched both local ZIPs byte for byte; the expanded hosted and local trees each had
  195 files and 84,897,393 bytes with zero file/hash/length differences. Human review
  and the licensed Word release gate remain required; the public release stays at
  0.34.0.

## WordToolkit 0.35.0 markup-compatibility graph — 2026-07-22

- Added a bounded, source-preserving ECMA-376 Part 3 fifth-edition graph for
  `Ignorable`, `ProcessContent`, `MustUnderstand` and `AlternateContent`, with explicit
  application/extension configuration and non-executed legacy preservation-hint
  inventory.
- Added the lazy, read-only `inspect_ooxml_markup_compatibility` action. Namespace and
  source details are independently opt-in; the action never preprocesses XML, follows
  external targets, opens Word or mutates the package.
- Full native engine suite: **316 passed, 0 skipped**.
- Full native MCP host suite: **218 passed, 0 skipped**.
- Full Python compatibility suite: **1,273 passed, 16 skipped**; Ruff clean. Open XML
  validator built with zero warnings, and the generated basic/advanced artifacts passed
  structural and visual checks.
- Corrected MCE scale points remain below their declared ceilings at 99,999, 499,999 and
  998,998 XML elements. The largest point took 4.78 seconds for package read plus graph
  build, retained about 1.03 GB managed memory and is documented as a hard boundary, not
  an ordinary workload.
- Native package version: `0.35.0+codex.20260722212907`, with 14 public gateway tools
  and **82** lazy native actions.
- Two independent local builds produced byte-identical **195-file**, **84,713,190-byte**
  expanded trees and byte-identical **36,077,499-byte** ZIPs. ZIP SHA-256:
  `5312432d0ff5e8ea2c0c4ca664011225be7ea7ad49a0b7b091aa9db4efba2ea3`.
  Runtime assembly SHA-256:
  `d8f3346b7f29b9bbf85fc4b1f2d38d903836c514a245629e85bb16694b58c78b`.
- The exact packaged MCP inspected the real LibreOffice chart document as 11 XML parts,
  five compatibility rules and one alternate-content block. Its data object was 1,218
  characters and complete response 3,054 characters; it returned no namespace URI,
  source-part name, formula or raw XML and left the Word process set unchanged.
- Mandatory CI run `29951495361` passed all five jobs with zero annotations. Its
  downloaded Windows artifact matched both local ZIPs and expanded trees byte for byte.
  Human review remains required. The public release stays at 0.34.0; no licensed Word
  release gate is claimed for this saved-package tranche.

## WordToolkit 0.35.0 classic chart graph — 2026-07-22

- Added a bounded typed graph for classic Transitional and Strict DrawingML charts:
  all 16 plot families, series/source roles, formulas, literal/reference/multi-level
  cache metadata, axes/cross-axis links, `externalData` and related package parts.
- Cached point values are discarded by the engine and have no public model property.
  Titles, formulas, format codes and source relationships are independently redacted;
  external targets and embedded workbooks are never opened.
- The shared dependency graph now includes chart, series and axis nodes plus chart
  containment and related-part edges. Office 2016 extended charts remain preserved and
  explicitly unmodeled; chart editing/rendering/workbook synchronization are not
  claimed.
- Full native engine suite: **309 passed, 0 skipped**.
- Full native MCP host suite: **213 passed, 0 skipped**.
- Full Python compatibility suite: **1,273 passed, 16 skipped**; Ruff clean.
- Native package version: `0.35.0+codex.20260722203415` with 14 public gateway tools
  and **81** lazy native actions.
- Native package: **195 files**, **84,535,811 expanded bytes**, **36,034,548 ZIP bytes**.
- ZIP SHA-256:
  `26b7c1ff41933a26a2cd812aec70abe2f74408005eba8a76d48918409f2f0f88`.
- Executable SHA-256:
  `59fb8419dbe664d1b1ae7f0b5df35b7738cfd1c43b69719049d8e06dd64cd138`.
- Runtime assembly SHA-256:
  `59ac4a47cfe5446cfcd4248f6b20a1398b740fae2967dcc98beaf7416efa3c09`.
- Two independent local builds produced byte-identical ZIPs and byte-identical
  195-file expanded trees. The packaged executable inspected the real LibreOffice
  chart fixture as one plot, three series, two axes, 27 cached point entries and one
  embedded workbook without opening Word or the workbook. Its compact content was 875
  characters and the complete tool-call response was 2,260 characters.
- Mandatory CI run `29947553481` passed all five jobs, and its downloaded Windows ZIP
  matched both local archives byte for byte at the same size and SHA-256. Human review
  remains required. The public release stays at 0.34.0, and the licensed 48-action Word
  gate is not re-claimed by this saved-package-only tranche.

## WordToolkit 0.35.0 semantic golden corpus — 2026-07-22

- Added a versioned semantic oracle for nine public DOCX fixtures from Apache POI,
  Pandoc, Mammoth, LibreOffice and a real-world Microsoft Word document.
- The manifest binds every fixture by file SHA-256 and package fingerprint, then checks
  exact semantic and dependency-kind counts plus style, numbering, field, review,
  section and effective-formatting facts. It contains no document text or raw XML.
- Independent source-part checks confirmed the selected style inheritance, numbering
  mappings, field types, comment anchors, tracked move pair, text boxes, six
  header/footer references and chart relationship.
- Focused semantic-oracle test: **1 passed** in 612 ms.
- Full native engine suite: **284 passed, 0 skipped**.
- Full native MCP host suite: **208 passed, 0 skipped**.
- Full Python compatibility suite: **1,273 passed, 16 skipped**; Ruff clean.
- Open XML SDK validator: built successfully with zero compiler warnings. Regenerated
  basic and advanced artifacts passed structural, SDK, accessibility and LibreOffice
  visual checks; the advanced document retained 17 native equations across 11 pages.
- Native package version:
  `0.35.0+codex.20260722195005`.
- Native package: **195 files**, **84,409,303 expanded bytes**, **35,998,031 ZIP bytes**.
- ZIP SHA-256:
  `bdbeaf9414e1b56311c6518c49787a76dc700da0fff184cbf62f8bb2c0079ed5`.
- Executable SHA-256:
  `59fb8419dbe664d1b1ae7f0b5df35b7738cfd1c43b69719049d8e06dd64cd138`.
- Runtime assembly SHA-256:
  `d08bb344be1d80e1836dd5bf5b44ae8d7b2f529414fd536e825c78f9931e9634`.
- Two independent local builds produced byte-identical ZIPs and byte-identical expanded
  trees. Packaged MCP initialization reported protocol `2025-06-18`; a compact semantic
  inspection returned the expected 17-node `poi_styles.docx` graph in a 1,448-character
  JSON-RPC response without opening Word or returning source XML.
- This is stronger semantic regression evidence, not release approval or a claim of
  Word-identical rendering. The public release remains 0.34.0 until the draft pull
  request receives human review and the self-hosted Microsoft Word gate runs.

## WordToolkit 0.16.0 verification — 2026-07-20

- Full Python suite: **1,284 passed, 16 skipped**.
- Focused live Word and local STDIO suite: **70 passed**.
- Ruff: clean.
- mypy: clean across 28 first-party source files.
- MCP schemas: 65 remote tools and 103 unique local tools, including all seven
  new live Find/review/layout/Undo contracts.
- WordToolkit skill validation: passed.
- Codex plugin validation: passed.
- Microsoft Word 16.0 acceptance: native Find 2/2, transactional replacement
  2/2, comment add/reply/resolve, tracked insertion, tokenized revision
  acceptance and guarded Undo all passed.
- Resulting live DOCX: valid OPC ZIP, zero WordToolkit structural errors and
  zero Microsoft Open XML SDK errors.
- Real Word evidence:
  `artifacts/wordtoolkit-live-competition-test/real-word-live-gap-test.json`.

The stage table and counts below are the earlier 0.1.1 baseline retained as
historical evidence. They are not the current release totals.

Results recorded on 2026-07-18 in the supplied Linux build environment.

| Stage | Result | Evidence |
|---|---|---|
| 1. Source and license audit | Complete | `docs/RESEARCH-AUDIT.md`, pinned upstream commits, MIT licenses and notices retained. |
| 2. Architecture and threat model | Complete | `docs/ARCHITECTURE.md`, `docs/SECURITY.md`, versioned tool schemas and migrations. |
| 3. Vertical DOCX flow | Complete locally | Authenticated MCP tests cover create/open, paragraph edit, native OMML insertion, validation, versioned save and signed download metadata. |
| 4. Document tools | Complete for the declared v1 contract | 65 small, typed MCP tools exported in `schemas/mcp-tools.v1.json`; unit and integration coverage includes structure, styles, formatting, lists, tables, stories, revisions, fields, notes, comments, images and math. |
| 5. Rendering and visual QA | Complete with stated renderer limits | The nine-page advanced acceptance DOCX was rendered with LibreOffice headless and Poppler, passed strict blank/sparse/edge/font checks, and every page was manually inspected after two correction iterations. |
| 6. Codex/ChatGPT plugin | Complete as an installable template | Plugin manifest, remote MCP configuration and WordToolkit skill validate with the Codex plugin validator. The deployment URL intentionally remains operator-configurable. |
| 7. Public HTTPS and phone test | Blocked on operator infrastructure | A public deployment cannot be created without an operator-owned hosting account, domain, OAuth issuer/client configuration and secrets. Cloud Run and Render examples plus phone setup instructions are included, but no public endpoint is claimed. |
| 8. Security and regression audit | Complete for this environment | Security tests, dependency packaging checks, structural OOXML validation, rendering, schema parsing and the full regression suite passed. Microsoft Word and the .NET SDK were unavailable; dedicated CI jobs and a Word COM validation script are supplied. |

## Exact automated results

- Full Python suite: **1194 passed, 15 skipped** in 7.03 seconds.
- Focused math/security/vertical-flow suite: **52 passed**; advanced equation module alone: **37 passed**.
- Skips: fourteen optional spaCy `en_core_web_lg` tests and one test that would download a model during a request. Production code deliberately forbids such downloads.
- Ruff: all checked WordToolkit source, scripts and first-party tests passed.
- Plugin validator: passed.
- JSON/YAML parsing: passed for schemas, plugin files and deployment descriptors.
- Advanced acceptance script: **passed** with 9 pages, 4 sections, 2 tables, 17 native equations, 0 structural errors, 0 structural warnings, 0 accessibility issues and 0 layout risks.
- Python package build: `wordtoolkit_mcp-0.1.1-py3-none-any.whl` and source distribution built successfully; vendored `docx-mcp` license and notice files are present in the wheel.
- Docker and local .NET build: not run because neither executable was present in this environment.

## Generated-document evidence

| Artifact | Structural result | Native math | Render result |
|---|---:|---:|---:|
| `WordToolkit-equations.docx` | valid; 0 errors; 0 warnings | 5 `m:oMath`, including 4 block equations | one-page PDF and PNG; visual heuristic passed |
| `WordToolkit-showcase.docx` | valid; 0 errors; 0 warnings | 1 native block equation | one-page PDF and PNG; visual heuristic passed |
| `WordToolkit-advanced-torture-test.docx` | valid; 0 errors; 0 warnings; protected custom/opaque parts byte-identical | 17 `m:oMath` values; 16 block plus 1 inline; 16 source round-trips and 48 export/reparse checks | nine-page mixed Letter/A4 portrait/landscape PDF; 9 PNGs; no blank/sparse/edge/font warning; all pages manually inspected |

The advanced document additionally contains comments and a reply, footnote/endnote, tracked insertions/deletions, fixed-layout and split/merged tables, inline/floating DrawingML with alt text, fields and references, multilevel numbering, four sections, first/default/even headers and footers, a bound content control, custom XML, a font table and an opaque linked binary. Those unmodified protected parts were compared byte-for-byte after save and reopen. The structural validator is not a substitute for the Microsoft Open XML SDK validator: the Docker image builds that official validator, while this local run correctly reports it as unavailable instead of claiming an SDK pass.
