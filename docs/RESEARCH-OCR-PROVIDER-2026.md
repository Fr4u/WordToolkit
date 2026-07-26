# OCR provider research and fail-closed contract — 2026-07-26

## Decision

WordToolkit now has one narrow OCR vertical slice, not a general document-AI claim.
It discovers raster images referenced by the typed Word figure graph, binds each
candidate to the exact package fingerprint, package part and payload hash, and can send
an explicitly selected candidate to a registered OCR provider. The first provider is a
local Tesseract CLI adapter. No cloud OCR provider, PDF rasterizer, vector-image
rasterizer, handwriting classifier, table extractor or mutation path is implemented.

The default policy is `local_only`. OCR text is document content and remains untrusted.
The default response returns counts, confidence and provenance but no recognized text,
line geometry, source URI or document/image hash. Those disclosures require explicit,
independent opt-ins.

## Primary-source comparison

The provider-neutral contract follows the common evidence that mature OCR surfaces
actually expose instead of flattening every backend to one string:

| Surface | Primary-source behavior used in the design | WordToolkit consequence |
|---|---|---|
| Windows `Windows.Media.Ocr.OcrEngine` | `RecognizeAsync` returns a result split into lines and words; words expose text, size and position. The API also exposes installed recognition languages and a maximum image dimension. [Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine) | Lines and word boxes are first-class, but confidence is nullable because not every provider contract exposes it. No Windows OCR adapter is implemented yet. |
| Azure Document Intelligence Read | The Read model reports paragraphs, lines, words, locations, languages, word confidence and handwriting style; Microsoft explicitly distinguishes document-heavy OCR from general-image OCR. [Read model](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/prebuilt/read?view=doc-intel-4.0.0), [OCR overview](https://learn.microsoft.com/en-us/azure/ai-services/computer-vision/overview-ocr) | Language, geometry, confidence and provider provenance stay separate. A future Azure adapter must declare network and credential permissions and cannot pass `local_only`. |
| Google Cloud Vision | `TEXT_DETECTION` returns text/word boxes, while `DOCUMENT_TEXT_DETECTION` adds a page → block → paragraph → word hierarchy for dense documents. Requests send image content or reference cloud storage. [Google Cloud documentation](https://docs.cloud.google.com/vision/docs/ocr) | Layout mode is a semantic hint, not a promise of a common backend algorithm. A future adapter is networked and must not be selected implicitly. |
| Amazon Textract | Results are typed blocks connected by relationships and contain confidence plus geometry; text detection returns page, line and word blocks. [Textract response objects](https://docs.aws.amazon.com/textract/latest/dg/how-it-works-document-layout.html) | A future adapter needs a richer layout-result version instead of smuggling tables/forms into plain OCR lines. It must declare network and credentials. |
| Tesseract CLI | The official `tsv` output contains page/block/paragraph/line/word coordinates, confidence and text. [Tesseract command-line documentation](https://tesseract-ocr.github.io/tessdoc/Command-Line-Usage.html) | The initial adapter parses strict TSV, normalizes confidence from `0..100` to `0..1`, validates geometry and returns a fixed provider provenance record. |

This comparison does not assert equal accuracy, language coverage, layout quality or
privacy between providers. No comparative accuracy benchmark has been run.

## Source-linked candidate graph

`WordOcrGraphBuilder` consumes `WordFigureCaptionGraph`; it does not scan arbitrary ZIP
entries and invent document meaning from file extensions. A repeated image referenced by
several figures becomes one candidate with all figure/resource/story references. Stable
`wocr_` IDs are derived from the exact package fingerprint, canonical part URI and image
hash.

Eligibility is deliberately narrow:

- only embedded image relationships already resolved by the package/figure projection;
- PNG, JPEG, GIF, BMP, TIFF and WebP declarations with a matching payload signature;
- at most 32 MiB per candidate, 100,000 candidates and 10,000 OCR issues;
- external targets are reported but never fetched;
- SVG, EMF and WMF require a future explicit rasterization stage;
- orphan image parts not referenced by a modeled figure are not OCR candidates;
- a truncated source figure projection makes `candidate_coverage_complete=false`.

The inspection action returns no image bytes and invokes no provider. Pagination requires
the same package fingerprint when the caller wants stale-read protection.

## Provider contract and execution

`IWordOcrProvider` receives verified image bytes, detected content type, exact image hash,
one to four language identifiers, a bounded layout hint, timeout/output limits and an
explicit provider configuration. It returns image dimensions, line/word boxes, nullable
confidence, bounded text/warnings and provenance.

The built-in `wordtoolkit.tesseract-cli` registration declares document-content read,
filesystem-read/write and process-spawn permissions. It declares no network or credential
permission. Execution:

1. requires an absolute local-filesystem executable and model directory from the request or
   `WORDTOOLKIT_TESSERACT_PATH` / `WORDTOOLKIT_TESSDATA_DIR`;
2. refuses relative, UNC, mapped-network, missing and reparse-point paths rather than
   searching `PATH`;
3. hashes the executable and every selected `.traineddata` model before recognition and
   verifies the same hashes again after recognition;
4. probes the exact executable version and its available languages;
5. starts the executable directly with `UseShellExecute=false` and structured argument
   passing, never through a shell;
6. streams the embedded image through standard input and creates no temporary image;
7. fixes `OMP_THREAD_LIMIT=1`, bounds stdout/stderr and applies one end-to-end timeout
   across hashing, version/language probes, stdin and recognition, killing the process
   tree when needed;
8. validates TSV shape, row/count limits, text safety, image dimensions, every word box,
   confidence and the provider result again at the operation boundary;
9. redacts raw provider diagnostics, executable/model paths and raw TSV from responses.

The result does not claim deterministic reproduction even for matching executable/model
hashes because dynamically loaded dependencies and the complete host environment are not
bound. In 0.57 the adapter itself became a host-owned process-boundary proxy. It sends the
typed request through closed, duplicate/unknown-field-rejecting JSON IPC to a fresh,
hash-bound WordToolkit child. The child is attached before request publication to a
Windows Job Object with a 1 GiB aggregate memory ceiling, three-process limit,
kill-on-close and hard timeout/tree termination. A random request ID and pre/post host
binary hashes bind the response to the intended invocation.

In 0.58 the host creates the child suspended inside the stable per-user
`WordToolkit.OcrProviderHost.v1` AppContainer, attaches it to the Job Object and only then
resumes it. No capability SID is supplied, so AppContainer network access is denied. The
parent verifies absolute reparse-free paths and grants the package SID read/execute access
only to the host runtime and explicit provider/model directories. `TEMP`, `TMP` and
`LOCALAPPDATA` point into the private AppContainer profile; declared filesystem-write use
therefore describes the private scratch surface rather than authority to modify user
documents. A hostile probe from the real child denied unbrokered file read/write, allowed
one brokered read while denying its write and failed to connect to a listening localhost
socket. This follows Microsoft's AppContainer dual-principal and capability model:
[AppContainer isolation](https://learn.microsoft.com/en-us/windows/win32/secauthz/appcontainer-isolation),
[launching an AppContainer](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)
and [profile creation](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createappcontainerprofile).
The newer `Experimental_CreateProcessInSandbox` API was not used: Microsoft marks it
Windows 11 experimental, exposes no public header and specifies a FlatBuffer contract;
WordToolkit retains the documented Windows 8+ AppContainer APIs instead.

This is still not an empty-filesystem VM. Machine resources whose existing ACLs already
grant read access to all AppPackages can remain visible, the private profile is writable,
and there is no Win32k syscall-disable policy. Hashing supplies provider provenance but
does not prove that an unsigned configured binary is benevolent.

The package-exact seven-sample benchmark uses the checked-in 15,283-byte stripped PNG and
the self-contained 0.58 executable. All fourteen direct/AppContainer calls produced the
same typed-result SHA-256. Direct median was 324.6673 ms and AppContainer median was
743.9256 ms, a disclosed +419.2583 ms / +129.13% boundary cost. No recognized text is
stored in `docs/benchmarks/ocr-provider-appcontainer-2026-07-26.json`.

## Privacy and stale-state rules

`run_ooxml_ocr` requires an exact package fingerprint and either one to eight explicit
candidate IDs or the explicit Boolean `select_all_eligible=true`. There is no implicit
“OCR everything” default. `local_only` rejects any provider capability declaring network
or credentials before provider invocation; `network_allowed` is an explicit policy
choice, but no network provider ships today.

The source file SHA-256 is read before package projection and again after all provider
calls. A changed package returns `VERSION_CONFLICT`. Image bytes, raw provider output,
raw XML and filesystem paths never enter the result. Recognized text, candidate/image
hashes, line geometry and word detail are independently gated and bounded. Text remains
marked untrusted because OCR can reproduce prompt injection or malicious instructions
printed in an image.

## Executed evidence

The gated `RealOcrAcceptanceTests` test generated a 1,800 × 400 PNG with the phrase
`WORDTOOLKIT OFFLINE OCR`, embedded it as a referenced DOCX image, inspected one eligible
candidate and executed the lazy MCP action against local Tesseract
`v5.5.0.20241111` with the `eng` model. The result contained the expected three words,
bounded line/word geometry, normalized confidence above the required threshold, exact
provider/model/image hashes and `network_used=false`. The successful MCP envelope
conformed to the published closed output schema. The DOCX SHA-256 and the observed Word
process count were unchanged; the COM host invocation count was zero. The same acceptance
now executes through the separate Job Object host rather than invoking the adapter inside
the MCP process.

The seven-sample alternating benchmark used real Tesseract 5.5.0 and the same bound model
over one 16,734-byte PNG. Direct in-process median was 248.0910 ms; the isolated path was
482.1869 ms, a +234.0959 ms / +94.36% correctness cost. All fourteen typed result hashes
were identical and the benchmark returned no recognized text. Raw evidence is in
`docs/benchmarks/ocr-provider-process-boundary-2026-07-26.json`.

Unit/contract tests additionally cover deduplication, signature mismatch, unresolved source
relationships, incomplete figure projection, compact content suppression, source fingerprint
and file-hash drift, local-only denial before provider execution, confidence gates, invalid
provider geometry, strict JSON, explicit selection, lazy MCP/CLI parity, unsafe language
identifiers, empty/UNC/mapped-network/reparse provider paths and refusal to search `PATH`.

## Remaining hard work

- signed/installable third-party provider packages and a restricted-identity/network/
  filesystem-brokered provider sandbox beyond the current crash/resource boundary;
- Windows OCR and explicitly authorized Azure/Google/AWS adapters;
- vector/PDF/page rasterization with its own versioned provenance and pixel limits;
- EXIF orientation, rotation, multi-frame images, color/bit-depth bombs and broader
  hostile decoder corpus;
- Polish and multilingual model qualification; the current machine has only `eng` and
  `osd` Tesseract data;
- tables, forms, handwriting, reading order and provider-specific rich layout contracts;
- accuracy/latency/memory corpus across real scans and providers;
- reviewed application of recognized text as alt text, document text or index content.

Until those exit conditions are met, the audit state is **Partial**, not Implemented.
