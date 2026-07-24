# Security model

WordToolkit has two different deployment surfaces. The personal Codex plugin is a
local Windows process speaking line-delimited MCP over standard input/output and using
Word COM. The retained Python service is a remote HTTP/container deployment with OAuth,
uploads and rendering. Controls that belong to one surface do not magically protect the
other.

## Trust boundaries

Untrusted inputs are OAuth tokens, MCP JSON, file URLs supplied by ChatGPT, ZIP metadata, XML, relationships, image bytes, Markdown and renderer inputs. The remote service trusts only its configuration, immutable container image, configured identity provider and files it has produced inside the session root. The local plugin additionally trusts its installed runtime and explicit user-selected local paths; document contents and MCP arguments remain untrusted.

## Local native MCP and COM boundary

The local server accepts one JSON-RPC message per line, capped at 8 MiB. It drains an
oversized line, returns a bounded protocol error and continues at the next message.
Active request IDs are unique, capped at 64 and have independent cancellation tokens;
`notifications/cancelled` and `$/cancelRequest` cancel only the named request.

Cancellation cannot safely interrupt an arbitrary COM call already executing inside
Microsoft Word. Queued calls observe cancellation before starting. If an executing call
is cancelled, the host refuses new Word work until it returns and resets its COM proxy.
An executing non-replayable operation reports `WORD_OPERATION_OUTCOME_UNKNOWN`, not
successful cancellation. Non-replayable is the default and remains blocked after an
unknown outcome until runtime restart, reconnect and inspection. Only explicitly proven
read-only or idempotent delegates may reconnect once; cancellation is rechecked before
that replay. Busy-call retries stop within the 30-second budget.
If it remains hung, the supervisor must terminate and restart only
`wordtoolkit-native.exe`; it must never kill `WINWORD.EXE`. After restart, callers must
reconnect and re-inspect the document because the abandoned operation may have completed
inside Word. See [MCP cancellation and recovery](MCP-RECOVERY.md).

## Authentication and authorization

Production startup fails unless the public URL is HTTPS, JWT OAuth mode is enabled, issuer/audience are configured and the signing key is non-default. JWT signatures are resolved through the configured JWKS endpoint; issuer, audience, expiry, issued-at and subject are verified. Tools require `documents:read` or `documents:write`. MCP authorization metadata identifies the resource endpoint.

`development_token` exists only for loopback/local Docker testing. It must never be exposed on a public host.

## Upload and SSRF controls

ChatGPT file inputs use `_meta["openai/fileParams"]`. The server downloads an authorized HTTPS URL with redirects disabled by default and revalidates every redirect. Allowed host suffixes are configurable; DNS resolution rejects loopback, private, link-local, multicast, reserved and unspecified addresses. Localhost HTTP is allowed only for local tests. Downloads stream to a bounded session file and do not log body content or credentials.

If an authorized file reference omits the optional `file_name`, the downloader derives a filename only from an allowlisted extension in the final URL, declared MIME type or response `Content-Type`. An unrecognized type fails closed. Interrupted and failed downloads remove their partial session file; operation-specific package/image validation still runs after download.

Image uploads are decoded and verified by Pillow before embedding. Allowed file extensions are operation-specific. DOCM/DOTM are rejected before package inspection.

## OPC/ZIP controls

- compressed upload, entry-count and total-uncompressed-size limits;
- per-entry compression-ratio limit for substantial entries;
- rejection of absolute, drive-qualified, backslash, NUL, `.` and `..` names;
- rejection of symlinks, duplicate names and case-colliding names;
- required core OPC/Word parts;
- extraction only beneath a newly created session directory;
- a second resolved-path containment check during extraction;
- ZIP CRC validation before publication.

The default limits are 50 MiB compressed, 250 MiB expanded, 5,000 entries and a 100:1 substantial-entry ratio. Production operators should lower them for narrower workloads.

## XML and relationship controls

All parsed XML uses `resolve_entities=False`, `load_dtd=False`, `no_network=True`, `huge_tree=False` and no recovery. DTD and entity declarations are rejected before parsing. Internal relationship targets are normalized relative to the owning part and cannot escape the package. Missing targets are validation errors on publication.

External HTTP, HTTPS and `mailto` relationships can be preserved but are never fetched. `file`, `ftp`, `javascript`, `data`, `vbscript`, absolute internal targets and unknown external schemes are rejected. VBA content types and macro containers are rejected. Macros are never executed.

That rejection statement belongs to the remote draft/upload surface. The local native
saved-package inspector deliberately admits caller-selected DOCM/DOTM files so it can
report their active-content metadata without opening Word. Its
`inspect_ooxml_active_content` path matches exact relationship namespaces, forbids DTDs
and external XML resolution, shares the bounded operation lease from OPC admission
through projection and never decodes binaries, opens embedded packages, executes VBA or
ActiveX, follows external targets, or performs cryptographic signature validation. Raw
XML, field-code text, binary values, ActiveX licenses and property values have no
response field. Names, declared targets, payload hashes and source locations require
four independent opt-ins. This inventory is evidence for policy; it does not authorize
extraction, execution, deletion, signature invalidation or mutation.

## Renderer controls

LibreOffice receives fixed argv with `shell=False`, a one-use profile, bounded runtime, a restricted HOME and no automatic external relationship retrieval by WordToolkit. The container runs as an unprivileged user with a writable data directory. Production deployment should add a read-only root filesystem, seccomp/AppArmor, CPU/memory quotas and egress rules that allow only the identity provider and authorized OpenAI file hosts.

## Data lifecycle and logging

Remote-service content is stored only under the configured ephemeral root. Sessions default to one hour and artifacts to one hour. Exported artifacts are copied out of the working session so closing a draft does not invalidate an unexpired download. A cleanup task closes documents and deletes expired files. Responses contain operation/error classes but never renderer stderr, document text, bearer tokens, file URLs, XML or local paths. Authenticated download URLs are private/no-store and expire.

## `.wtpatch` confidentiality and authenticity

Version-1 raw `.wtpatch` files contain deduplicated exact before and after OPC-entry
payloads. In the worst case they carry nearly the whole document twice. They are local
recovery/change-transfer artifacts, not safe public diffs. Hashes and canonical IDs
detect accidental corruption and internal substitution, but they do not authenticate an
author. Protect raw patches with the same access controls, storage encryption, retention
and deletion policy as the source DOCX, and do not accept one from an untrusted party
merely because its hashes validate.

The optional engine-level patch envelope supports AES-256-GCM with a fresh 96-bit nonce,
a 128-bit authentication tag and canonical metadata as associated data. Optional
ECDSA-SHA256 signs the metadata, tag and payload and binds a restricted signer key ID.
Reading a signed envelope fails unless a verifier is supplied; an expected signer ID can
also be mandatory. Reading encrypted data requires an exact 32-byte key. Keys remain
caller-owned and are never serialized into the envelope. The current MCP surface does
not provision those keys, and the engine does not pretend that a key ID alone establishes
trust. A deployment must bind key IDs to independently trusted public keys and protect
private/encryption keys outside prompts, logs and document storage.

The codec currently materializes payloads in memory. Measured defaults permit 128 MiB
total, 64 MiB per blob, a 4 MiB manifest and a 100:1 compression ratio. These are hard
rejection ceilings, not proof that such an input is operationally safe. An explicit
custom limit can be higher, but it must be backed by workload-specific memory evidence.
The envelope also materializes its serialized patch and AES-GCM payload; it improves
confidentiality/authenticity, not memory scaling.

The in-memory session registry makes this release a single-instance service. Do not run multiple replicas without a shared encrypted object store, distributed locks and shared metadata. The supplied hosting profiles deliberately use one instance.

## Word Live field, object-model, bookmark, learning and equation boundaries

Saved-package review inspection is a bounded parse-only path. It joins comments,
story anchors, threaded replies, durable IDs, extensible/reaction inventory, people,
tracked revisions, named moves, permission ranges and review settings without opening
Word or changing the package. Comment/revision text, author/editor/person names,
provider/user identifiers and move names are fingerprinted/redacted by default. A text
preview requires explicit sensitive opt-in plus a positive character bound; source
metadata is separately opt-in and raw XML is never returned. Independent limits cap
parts, comments, anchors, revisions, move/permission ranges, people, text, thread depth
and diagnostics. The inspector cannot accept/reject changes, resolve comments, merge
review state, execute external content or authorize an edit merely because a permission
marker exists.

Saved-package equation inspection is a bounded parse-only path. It does not start Word,
invoke conversion, return raw OMML, evaluate content or follow relationships. The
default response contains only structural counts, statuses and short fingerprints;
formula and node text remain absent. A bounded preview is accepted only when both
`include_sensitive=true` and a positive `text_preview_chars` are supplied. Properties,
source paths and node lists are separately opt-in and paged. XML parsing prohibits DTDs
and external resolution, and independent limits cap parts, equations, math paragraphs,
nodes, depth, properties, text and diagnostics.

Saved-package reference inspection is parse-only. It recognizes complex and simple
field instructions, including DDE, DDEAUTO, LINK, INCLUDE, INCLUDETEXT,
INCLUDEPICTURE, IMPORT, DATABASE and HYPERLINK, but never evaluates a field, launches
an application or follows a target. The default MCP response omits bookmark names,
instruction text, cached result text and dependency keys; it returns bounded counts,
types and short fingerprints instead. Raw bounded values require explicit sensitive
detail, and external targets remain inert even then. Field, token, instruction, result,
story, bookmark and diagnostic counts all have independent limits.

The local Word Live bridge never accepts arbitrary field-code text. Field
creation uses a typed allowlist for static document metadata, page/section
counters, dates/times, sequences, existing bookmark references and restricted
numeric formulas. DDE, database, include, link, macro and external-data fields
are unreachable through the public field tools.

Formula identifiers are allowlisted, switches are generated from validated
options, and reference bookmarks must exist before mutation. Fields are
prepared before Word is attached, created inside one Undo transaction,
type-checked and updated. Locale translation reads only Word's separator
settings and cannot expand the accepted formula grammar. Responses omit field
code and result text.

Native table calculations use a separate typed contract. Callers select one
of six aggregate functions and either one or two positional directions or a
bounded row/column range. WordToolkit generates the A1-style reference and
native formula internally; raw expressions are not accepted. Destination
cells must be empty unless replacement is explicitly enabled. Every cell is
validated before the Undo transaction, and each resulting field must have
native type 34 plus a calculated result range. Word calculates on insertion;
the optional `force_update` path additionally requires every explicit update
to succeed. Responses omit formulas, source values and displayed results.

Existing table-field refresh accepts only a live document handle plus a
1-based table index. It caps the collection at 5,000 fields, reads only numeric
types, performs one native collection update inside one Undo transaction and
verifies count/type stability. It never accepts field codes and never returns
field results. A nonzero Word update result fails and rolls back; the reported
field index is treated as advisory because Microsoft documents that it can be
inaccurate in some Word versions.

The installed object-model catalog is separate from document learning. It
extracts only public API metadata from the already-running Word type library:
type/member/parameter names, kinds, numeric flags, type descriptors and enum
values. It does not resolve a document and excludes documentation text, Help
file paths, document counts, content, paths, handles and owner identifiers.
The cache is bounded, schema-checked, atomically replaced and refreshed only
on an explicit request or cache miss. Catalog discovery never authorizes a
new mutation path by itself.

The bounded structure-item inspector never returns raw field codes or external
hyperlink addresses. It caps pages at 200 items and optional text at 2,000
characters per item. Property failures are reported without exception text.
Adaptive learning receives only fixed collection/property names, aggregate
success/failure counts and timing after the COM attachment is released; it
does not receive any returned value or text.

Live bookmark names use a restricted 40-character grammar. Existing-name
collisions are checked before mutation, and native names plus exact ranges are
verified inside one Undo transaction. Bookmark text is never returned.

Structure learning stores only fixed collection/property names, bounded native
integer enum values, probe outcomes, rescan thresholds and aggregate duration.
It never receives property values, content, document counts, paths, owners,
handles or document-derived identifiers.

Native Find rejects unsafe control bytes and caps Word's search string at 255
characters. Transactional replacement discovers one complete bounded match set
before mutation, refuses excess results, edits from the final range backward
and restores the prior Track Changes state. A content failure, Undo-record
closure failure or Track Changes restoration failure requests rollback and
does not advance the live version.

Comment and revision writes never trust a collection index by itself. The
preceding inspection issues an HMAC token over document ID, live version,
collection, index, range, metadata, content hash and review state. Any external
item change or WordToolkit version change invalidates the token.

User-requested Undo cannot accept a raw step count. It requires Word to expose
the current top label, requires the label to begin with `WordToolkit:`, and
binds that label to a fresh HMAC token and exact live version. An intervening
manual action or a newer verified property change without its own Word Undo
entry creates a hard barrier.

The native bridge verifies OMath creation and final equation count. Sensitive
equations additionally undergo bounded immediate `WordOpenXML` readback: DTDs
and external resolution are prohibited, exactly one top-level OMath is required,
and element/depth/character limits are enforced before canonical hashes, symbol
counts and integral-owned differential ancestry are compared. Differentials in
ordinary derivative notation are not required to have an `m:nary` ancestor. A
build-up, parse, contract or placement mismatch fails closed and rolls back. Responses
expose verification facts and hashes, never raw OMML. The in-process learning counters
retain only input format and success/failure counts—not formula text, document content,
names or paths.

Equation style scopes reserve 24 private-use sentinels that are rejected in every
caller-supplied equation format. They exist only in the temporary Word build
payload. The bounded readback rewriter requires balanced, ordered markers inside
exactly one OMath, removes them all, applies only native `m:sty="p"`, `m:sty="b"`,
`m:sty="i"` or `m:sty="bi"` to math runs and matching `m:ctrlPr/w:rPr` bold/italic
properties to explicitly targeted structural controls,
reinserts the same one-equation range and compares a second style-contract hash over
both run and control placement. Marker loss, marker leakage, changed weight, malformed
XML or equation-count drift raises `EQUATION_INVALID` and rolls back the complete Undo
record. The hash treats Word's documented implicit italic/roman defaults and equivalent
adjacent-run coalescing as canonical, but it retains normal-text/literal flags, script,
effective style, text and structural-control ordering. Failure diagnostics contain no
formula text or raw OMML.

## Failure behavior

Errors use stable codes and never expose tracebacks or local paths. Unsafe/invalid input fails closed. A structural or Open XML SDK validation error prevents export. Renderer failure does not make an unrendered document appear visually verified.

## Production checklist

- Use a dedicated OAuth 2.1/OIDC tenant and exact audience.
- Rotate the artifact signing secret and keep it in a managed secret store.
- Pin the public hostname and allowed upload suffixes.
- Set one instance until a distributed session backend exists.
- Mount only a bounded ephemeral disk; monitor quota and cleanup.
- Restrict outbound egress and inbound traffic to HTTPS.
- Add container and dependency scanning before a public remote deployment. The current
  CI builds the container but does not yet claim a vulnerability scan.
- Run the Microsoft Word interoperability workflow on a licensed self-hosted Windows runner before a release.
- Verify retention and deletion against organizational policy.
