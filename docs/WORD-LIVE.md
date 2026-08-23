# Word Live

Word Live is the Windows-only local STDIO capability shipped by the native
WordToolkit 0.39 plugin. It can attach to the automation-visible
`Word.Application` object registered in the Windows Running Object Table or
start Word explicitly through native COM. Document open, close and application
quit are available only through dedicated bounded lifecycle tools with strict
path, save-policy and confirmation checks.

## Tools

| Tool | Purpose |
|---|---|
| `list_live_word_documents` | Enumerate automation-visible open documents. |
| `start_word_application` | Start Word visibly or attach to the running application. |
| `create_live_word_document` | Create a new live DOCX, optionally at one explicit new path. |
| `open_live_word_document` | Open one explicit Word-readable local path with macros and link updates disabled. |
| `connect_live_word_document` | Create an opaque handle for exactly one open document. |
| `inspect_live_word_document` | Read live metadata, paragraph/table/equation counts and save state. |
| `map_live_word_structures` | Inventory all Word stories and broad document collections without returning content. |
| `inspect_live_word_structure_items` | Read one bounded page of semantic metadata from any mapped native collection. |
| `inspect_live_word_drawing_layout` | Read Word-executed floating, inline, group and optional SmartArt layout without returning COM or XML. |
| `inspect_live_word_version_profile` | Read raw Word version/build, document compatibility/save format and bounded feature-member probes without content or identity. |
| `probe_live_word_feature_behaviors` | After explicit confirmation, test OMath, content-control, SmartArt and custom-Undo behavior in isolated unsaved scratch documents with mandatory cleanup proof. |
| `prepare_live_word_smartart_text_edits` | Resolve one exact SmartArt root and issue one-time tokens bound to the complete node structure and text context. |
| `apply_live_word_smartart_text_edits` | Replace up to 32 token-verified single-line SmartArt node texts in one Undo record with exact readback and rollback. |
| `inspect_live_word_smartart_layouts` | Read a bounded, paged catalog of layouts exposed by the connected Word process; returns opaque version-bound layout tokens, never raw COM or XML. |
| `insert_live_word_smartart` | Insert one inline native SmartArt object using a fresh layout token and exactly one range or selection token, with one Undo record, count/readback verification and rollback. |
| `inspect_live_word_equation_learning` | Inspect privacy-preserving aggregate native-equation outcomes. |
| `inspect_live_word_structure_learning` | Inspect aggregate native-type scan evidence and the adaptive rescan policy. |
| `inspect_live_word_object_model_types` | Query a paged catalog of types in the installed Word COM type library. |
| `inspect_live_word_object_model_members` | Query methods, properties, parameters, variables or enum values for one exact installed Word type. |
| `inspect_live_word_member_capabilities` | Browse one deterministic policy and signature profile per installed Word member. |
| `preflight_live_word_member_operations` | Validate typed targets, result chaining, arguments and policy for a bounded member-operation graph. |
| `execute_live_word_member_operations` | Execute catalog-backed reads or document-scoped edits by stable capability ID. |
| `get_live_word_selection` | Read the active cursor/selection and issue a bounded selection token. |
| `find_live_word_text` | Run bounded native Word Find and return exact ranges plus short context. |
| `replace_live_word_text` | Preflight a bounded match set, replace it in one Undo record and restore Track Changes state. |
| `inspect_live_word_review` | Read tokenized comments or revisions through one bounded page. |
| `manage_live_word_review` | Add/reply/resolve/delete comments, accept/reject one revision or set Track Changes with the correct rollback policy. |
| `diagnose_live_word_layout` | Scan bounded paragraph-flow and pagination risks without returning document text. |
| `inspect_live_word_undo` | Inspect Word Undo and issue a token only for a current `WordToolkit:` top entry. |
| `undo_live_word_operation` | Undo exactly one token-verified WordToolkit entry without crossing user edits. |
| `insert_live_word_text` | Insert text at a verified cursor/selection or the document end. |
| `format_live_word_selection` | Apply validated style, font and paragraph formatting to the exact token-verified selection. |
| `insert_live_word_table` | Convert one validated text payload into a native Word table. |
| `preflight_live_word_table_formulas` | Validate typed calculations for existing table cells without attaching to Word. |
| `insert_live_word_table_formulas` | Insert and update a typed batch of native calculation fields in one existing table. |
| `update_live_word_table_fields` | Recalculate every existing native field in one selected table with one collection update. |
| `insert_live_word_list` | Convert one validated paragraph payload into a native bulleted or numbered list. |
| `preflight_live_word_bookmarks` | Validate bounded native bookmark names and ranges without attaching to Word. |
| `insert_live_word_bookmarks` | Insert and verify up to 200 native named ranges in one transaction. |
| `preflight_live_word_fields` | Validate an allowlisted native-field batch without attaching to Word. |
| `insert_live_word_fields` | Insert and update up to 200 allowlisted native Word fields in one transaction. |
| `insert_live_word_caption` | Insert one localized native caption at exactly one fresh selection or found range token, with a real `SEQ` field and guarded readback. |
| `insert_live_word_table_of_figures` | Create and optionally update one native table of figures from existing captions. |
| `insert_live_word_table_of_contents` | Create, optionally repaginate and update one native contents table from semantic heading settings. |
| `mark_live_word_authority_citation` | Mark one fresh non-empty range as a native category-bound table-of-authorities entry. |
| `insert_live_word_table_of_authorities` | Create, optionally repaginate and update one native authority table for one or all categories with verified separators and leaders. |
| `mark_live_word_index_entry` | Mark one fresh token-bound location as a native hierarchical XE entry, cross-reference or bookmark page range. |
| `insert_live_word_index` | Create, optionally repaginate and update one native index with verified semantic layout options. |
| `update_live_word_reference_tables` | Refresh existing contents, figures, authorities and indexes in one bounded guarded transaction. |
| `insert_live_word_image` | Embed one bounded local image as a native inline shape. |
| `insert_live_word_comment` | Add one native comment to a fresh token-verified range or selection. |
| `insert_live_word_note` | Add one native footnote or endnote. |
| `set_live_word_header_footer` | Set one bounded header/footer variant in one section. |
| `insert_live_word_equation` | Create, build up and selectively read back one native Word OMath. |
| `insert_live_word_equations_batch` | Insert up to 100 native equations in one COM attachment, with automatic bounded readback for sensitive structures. |
| `preflight_live_word_equations` | Convert up to 200 formulas without touching Word; compact mode returns lengths, fingerprints and readback flags only. |
| `apply_live_word_operations` | Append up to 200 interleaved text/equation operations through one COM attachment, one payload and one Undo transaction. |
| `validate_live_word_document` | Validate a temporary copy of the already-saved DOCX. |
| `export_live_word_pdf` | Export the current live document through Word's native PDF renderer. |
| `save_live_word_document` | Call `Document.Save()` on the same existing path. Persistence is not a content mutation, so this action does not increment `live_version`. |
| `close_live_word_document` | Close one connected document using an explicit save/discard policy. |
| `quit_word_application` | Quit Word only with explicit confirmation and a save/discard-all policy. |
| `disconnect_live_word_document` | Release only the WordToolkit handle. |

## Verified rollback and quarantine

No WordToolkit live mutation path equates an attempted `Document.Undo(1)` with a
successful rollback. Every custom-Undo mutation now captures the live version, saved
state, a whole-document Flat OPC hash, main-story text/OOXML hashes, every accessible
linked story-range hash, exact target and bounded-context text/OOXML hashes,
content/target/context boundaries, and paragraph, equation, table, field, bookmark,
inline/floating shape, comment, footnote, endnote and section counts before its first
write. SmartArt text and review properties that are not reliably represented by Word's
Undo contract add dedicated supplemental state fingerprints. The mixed text/equation
path additionally builds, styles and reads back every native equation in an unsaved
hidden staging document before target publication.

After a failed mutation, the custom Undo record must close, `Undo(1)` must return `true`,
and every captured value must match. If no observable state changed, the runtime avoids
an unsafe Undo that could cross into an unrelated history entry and returns the original
error directly.

If any proof is missing, the operation returns `ROLLBACK_FAILED` with the original error
code, Undo outcome, mismatch names and before/after structural summaries. It does not
return document text, OOXML or the hashes. The original live handle is removed and kept
as a quarantine tombstone. Calls through that handle and attempts to reconnect the same
open document return `LIVE_DOCUMENT_QUARANTINED`. A deliberate
`disconnect_live_word_document` clears the tombstone without closing Word; this is an
acknowledgement of unsafe state, not evidence that the document repaired itself.
Real Word 16.0 proves why this is deliberately strict: a one-line insertion followed by
`Undo(1)` returned `true` and restored the visible text and structural counts, while the
Flat OPC, range OOXML and story graph still differed. WordToolkit reported
`ROLLBACK_FAILED` instead of laundering that drift as a successful transaction.

`insert_live_word_table_of_contents` inserts at the document start by default, or at the
document end or a fresh token-verified collapsed cursor. It accepts heading levels 1–9,
heading-style/outline-level source flags, page-number and hyperlink options, and optional
repagination/update. It calls Word's native `TablesOfContents.Add`, then requires a
one-object collection delta, one uniquely reacquired non-empty range and at least one
field. A mismatch rolls the custom Undo record back. No field instruction or generated
contents text crosses the tool boundary.

`mark_live_word_authority_citation` requires exactly one fresh non-empty selection or
range token and category 1–16. Omitted short and long citation strings are derived from
that exact target but are not returned. WordToolkit calls the native
`TablesOfAuthorities.MarkCitation`, then requires one new type-74 field with the exact
code range and category. Any mismatch rolls the custom Undo record back.

`insert_live_word_table_of_authorities` targets the document start/end or a fresh
collapsed cursor. Category 1–16 selects one category; category 0 includes all valid
authority marks. It requires at least one matching native entry and calls
`TablesOfAuthorities.Add` with Word's sequence-name argument genuinely omitted. The
default entry separator is one tab and the default leader is dots; alternative semantic
leaders are `spaces`, `dashes`, `lines`, `heavy` and `middle_dot`. The action reads back
every separator, `Passim`, entry-formatting, category-header and leader setting from the
created native object. It also requires one exact non-empty range and at least one field.
Failed readback requests one Undo. Citation text, separator values, generated table text,
field instructions and COM objects do not cross the tool boundary.

`mark_live_word_index_entry` requires exactly one fresh selection or range token. Omitted
main-entry text is derived from the target; explicit text permits a collapsed cursor.
Up to eight colon-free `subentries` form a Word hierarchy without making the model write
field syntax. A cross-reference is mutually exclusive with an existing-bookmark page
range and page-number bold/italic options. WordToolkit calls `Indexes.MarkEntry`, then
requires one new type-4 field with the exact code range and parsed option readback. Entry,
bookmark and cross-reference text remain private.

`insert_live_word_index` targets the document start/end or a fresh collapsed cursor and
requires at least one complete native `XE`. It maps semantic heading separators,
`indented|run_in`, zero-to-four columns, accented-letter grouping and six leader choices
to `Indexes.Add`, then reads all options back from the resulting `Index`. It optionally
repaginates and updates, requires one unique non-empty type-8 field range and rolls back
on any mismatch. Generated index text and field instructions are never returned.

`update_live_word_reference_tables` targets all existing tables of contents, figures,
authorities and indexes by default, or one exact collection and optional one-based index. It
updates at most 128 objects, calls Word repagination first unless disabled, and performs
the native full `Update` on each object. The operation requires the current
`expected_version`, uses one custom Undo record and verifies that all four collection
counts remain unchanged and every refreshed object still owns a readable non-empty
field range. It returns counts and verification flags only: no table result text, field
instructions or COM objects. A single cross-kind `page_numbers_only` option is
intentionally absent because Word does not expose that narrower operation uniformly for
all four object families.

For visually stable UnicodeMath, fractional coefficients must use explicit
multiplication. Write `1/3·(x^2+1)^(3/2)`, not
`1/3 (x^2+1)^(3/2)`. Without the multiplication operator, Word may extend the
fraction denominator across the following expression.

Use `·` between visible factors, parenthesize compound bases before applying a
power, and prefer direct UnicodeMath symbols such as `√(...)` in Word Live.
Conditions that the UnicodeMath parser does not support should be written in
nearby prose. WordToolkit rejects a numeric fractional coefficient followed by
an implicit factor, so ambiguous input fails before Word can build the wrong
fraction.

Quantum-mechanics notation such as `\hbar`, `^\dagger`, Greek variables and
commutators should use LaTeX input. Direct UnicodeMath can drop the Planck
symbol or split the dagger exponent. LaTeX formulas still need explicit
`\cdot` operators after fractions and between visible factors. Preflight
advanced notation through the converter and inspect the saved native equation
when semantic fidelity matters.

Use explicit differential notation for integrals. Prefer
`\int f(x)\,\mathrm{d}x`; `\,d x`, `\operatorname{d}x` and `\dd x` are also
recognized. The converter emits U+2146 `ⅆ` and an invisible Word operand group
`〖…〗`. This prevents `BuildUp()` from raising the differential into an exponent
or leaving it outside the integral body. A generic plain `d` is not silently
reinterpreted as a differential.

These 55 native desktop actions are absent from the remote HTTP MCP server.

## Native find and transactional replace

`find_live_word_text` uses Word's native `Range.Find` engine. Search text caps
at Word's 255-character limit, results cap at 5,000, and each returned context
is bounded independently. The bridge never returns or rebuilds the complete
document. Word wildcard mode supports Word special-character syntax and makes
whole-word matching ineffective, which the result reports explicitly.

`replace_live_word_text` requires `expected_version` and discovers the complete
requested match set before any write. Exceeding `max_replacements` fails before
the custom Undo record begins. Matches are replaced from the last range toward
the first so earlier coordinates remain stable. The caller chooses whether to
preserve, temporarily enable or temporarily disable Track Changes; the exact
prior state is restored in a `finally` path. A partial native failure requests
one Undo and leaves the live version unchanged.

## Token-safe live review

`inspect_live_word_review` pages either comments or revisions and gives each
item a fresh HMAC token. The token binds the connected document, live version,
collection, index, native range, author, date, type, content hash, resolution
state and reply count. `manage_live_word_review` refuses a raw comment or
revision index without the matching current token.

Adding, replying, deleting, accepting and rejecting run in one custom Undo
record and verify the native collection postcondition. Adding a comment also
requires a fresh non-empty selection token. After every mutation the live
version advances and all earlier review tokens become stale.

Word does not reliably record `Comment.Done` or `Document.TrackRevisions`
property assignments inside custom Undo. Those two actions therefore save the
prior value, assign the explicit requested state, read it back and restore the
old value if assignment or verification fails. They do not falsely claim a
Ctrl+Z entry.

## Bounded layout diagnosis

`diagnose_live_word_layout` reads at most 25,000 paragraphs and returns no
paragraph text. The one-pass scan checks long keep-with-next chains, headings
with body-length text, page-break-before on body paragraphs, oversized
keep-together paragraphs, disabled widow control, runs of empty paragraphs,
manual page breaks and heading-style overuse. Issues cap at 2,000 and report
paragraph numbers, styles, lengths and severity.

## Word-executed drawing layout

`inspect_live_word_drawing_layout` is the bridge between declared OOXML placement and
the object layout calculated by the connected Microsoft Word build. It can repaginate
the document, then scan at most 10,000 root drawing objects across the main story and
linked Word stories. Results are paged to at most 100 roots and classify floating
`Shape` objects separately from character-like `InlineShape` objects.

Floating results include the anchor range, page and section, size, rotation, visibility,
z-order, page/margin/column/character or paragraph/line position references, alignment
constants versus numeric point offsets, relative percentages when defined, wrapping and
conditional page-relative bounds. A page-relative box is emitted only when both reference
frames are the page and both positions are numeric. Group members are optional, flattened
to at most 128 entries and explicitly use group-local coordinates.

Inline objects remain in text-flow coordinates. Word's page-relative range positions are
returned only when Word reports a nonnegative visible value; they are marked viewport
dependent because Word returns `-1` for off-screen ranges. Optional `Window.GetPoint`
screen rectangles are limited to ten roots, can fail when the whole object is not visible,
and are always labelled pixels of the active window rather than page geometry.

SmartArt node projection is a separate opt-in capped at 128 semantic nodes and 256
associated rendered shapes. It exposes hierarchy level, hidden/type state, child count
and SmartArt-layout coordinates. Names, titles, alternative text and node text are not
even read unless `include_text=true`; a shared 4,096-character response budget and
512-character field ceiling then apply. Raw XML, raw COM objects and external fetches
have no response path.

This is authoritative only for the installed Word build, fonts, printer/layout settings,
view and current connected version. Traversal IDs are runtime locators, not durable OOXML
IDs. Word may normalize declared DrawingML/VML group nodes into different runtime shape
types, so package and live inspection must remain separate and both discrepancies must be
reported.

## Connected Word version profile

`inspect_live_word_version_profile` reads only the already connected application's raw
`Application.Version` and `Application.Build`, plus the document's numeric
`CompatibilityMode` and `SaveFormat`. It conservatively maps the documented major versions
11, 12, 14 and 15 to Word 2003, 2007, 2010 and 2013. Major version 16 is reported only as
`word_16_generation`, because that value alone does not identify a product edition.

Four independent property-access probes report `available`, `unavailable` or
`probe_failed` for `UndoRecord`, `OMathAutoCorrect`, `SmartArtLayouts` and document
`ContentControls`. They do not mutate a document or enumerate a collection. Availability
only proves that the current COM object exposed that member; it is not a promise that every
operation behaves the same across builds, channels, locale, document modes or policies.
Each failed read produces a fixed issue code instead of returning exception text.

The response contains no document text, path, raw COM object, user identity or licence
identity, does not start Word and uses no network. Compatibility profiles follow the
documented `WdCompatibilityMode` values 11, 12, 14, 15 and 65535; unknown values remain
unknown instead of being coerced into a newer profile.

## Isolated feature-behavior probes

`probe_live_word_feature_behaviors` exists because successful COM property access is not
proof that an operation works. It requires `confirm_scratch_documents=true` and is
deliberately marked non-read-only: it temporarily changes Word application state by
creating documents and switching the active document/window. It issues no content, style
or object mutation to the connected document and does not change `live_version`. Word may
still refresh volatile view/session package metadata during activation, so the action
explicitly does not claim whole-package identity.

The fixed probes are native OMath creation plus `BuildUp`, rich-text content-control
creation, insertion of the first locally available SmartArt layout, and creation/closure/
execution of one custom Undo record. Each probe gets a separate invisible unsaved Word
document. SmartArt reports `unavailable` only when Word exposes zero layouts; other COM or
verification failures report `failed` with a fixed issue code. No exception text, scratch
text, path, COM object, user identity or licence identity enters the result.

There is no soft cleanup path. After every probe WordToolkit calls `Document.Close(0)`,
reactivates the exact prior document and window, checks both COM identities, and checks that
the open-document count equals its baseline. `EndCustomRecord` failure is also cleanup
failure because the record belongs to application state. Any uncertainty returns
`TEMPORARY_DOCUMENT_CLEANUP_FAILED`, quarantines the connected handle and requires an
explicit disconnect before reconnecting. A normal feature failure may be reported only
after cleanup has been proved.

## Guarded SmartArt node text

Use the exact story/collection/source locator returned by the drawing-layout inspector;
the traversal-only `wdlo_` value is not a mutation identity. Preparation reads the full
bounded SmartArt text context and issues one-time tokens bound to the live version, Word
shape/range identity, layout/style/color IDs, complete node structure and every node text
hash. Text previews remain opt-in.

Apply accepts at most 32 unique single-line replacements from one prepared root. It
rechecks the complete context before opening one custom Undo record, writes through
Word's `SmartArtNode.TextFrame2.TextRange.Text`, then demands exact target readback,
unchanged structure and unchanged untargeted text. Any mismatch requests one bounded
Undo. Exact no-ops do not create Undo entries, repaginate or advance the version.

This is text mutation only. Node creation, deletion, reorder, hierarchy changes and
layout/style/color edits remain unsupported. The real-Word fixture and persisted-drawing
synchronization evidence are recorded in
`docs/RESEARCH-SMARTART-TEXT-EDITING-2026.md`.

## Guarded WordToolkit Undo

Word exposes its Undo dropdown through an undocumented CommandBars control.
`inspect_live_word_undo` treats that access as optional and fails closed when
it is absent. It issues an `undo_token` only when the current top entry begins
with `WordToolkit:`.

`undo_live_word_operation` accepts no count. It rechecks the top label, token
and `expected_version`, then calls `Document.Undo(1)` exactly once. A manual
typing or UI action above the WordToolkit entry blocks the operation instead
of being silently crossed.

## Installed Word object model

`inspect_live_word_object_model_types` builds or queries an in-process catalog
from the actual Word COM type library on this PC. A refresh uses the persistent
Word COM host, reads bounded type information and replaces the memory cache. It
never launches Word and never resolves a document.

The catalog includes public type names and kinds, GUIDs, numeric flags,
methods, property accessors, parameter names/types/flags, return types,
variables and enum values. It excludes COM base dispatch members and never
stores documentation strings or the fully qualified Help paths returned by
`ITypeInfo::GetDocumentation`.

The scan caps at 2,000 types, 2,000 members per type and 50,000 members total.
The release workstation scan found 767 types and 12,167 members in Word
type-library version 8.7 with zero errors and no truncation. The measured cold
scan took 6.276 seconds; later in-process queries use the memory catalog and
normally finish in tens of milliseconds. Nothing is persisted to disk. A new
MCP process scans again; use `refresh=true` only after an Office update or when
fresh metadata is explicitly required.

The catalog feeds a deterministic capability registry. Every catalog entry
receives exactly one stable capability ID, accessor group, typed signature,
target contract, effect class and execution policy. Constants and event
callbacks remain present but non-executable. Lifecycle, macro, DDE, print,
mail, password, path, web and application-global effects fail closed with an
explicit reason instead of disappearing from coverage statistics.

`inspect_live_word_member_capabilities` pages this registry without exposing
12,167 separate MCP tool schemas. `preflight_live_word_member_operations`
accepts at most 50 operations and 512 KiB, resolves every capability ID,
checks the allowed document root, validates argument counts and primitive COM
types, and permits result chaining only when the declared return type matches
the next target or parameter.

`execute_live_word_member_operations` repeats preflight against the current
catalog, resolves the already-connected document inside one COM attachment and
never accepts a raw member name or dotted COM path. Read-only graphs leave the
live version unchanged. A mutating graph requires `expected_version`, runs in
one custom Undo record and advances the version once only after every step
succeeds. A native failure requests one Undo and leaves the version unchanged.
The response's `executed_count` counts every executed operation, while the
`results` array contains only operations that supplied a `result_id`; callers
that need a read value must publish and then reference that result explicitly.

The registry does not pretend that all 12,167 entries are edits. On the
release workstation it classified 3,756 enum constants as metadata-only,
4,665 members as bounded reads, 2,226 as document-scoped writes and 1,520 as
blocked events or unsafe/global effects. The sum remains exactly 12,167 and
all capability IDs are unique.

## Native equation fidelity

The COM `OMaths.Add` path creates an equation from linear text and
`OMath.BuildUp()` converts it to professional layout. Creating an OMath object
or preserving one advanced symbol is not proof that Word kept the complete
formula.

The native runtime verifies successful OMath creation and final equation count.
It also automatically reads back structurally sensitive n-ary operators,
differentials, matrices, cases, equation arrays, accents, hbar and dagger
notation through the new equation range's bounded `WordOpenXML`. One top-level
OMath is securely parsed, canonical hashes and symbol counts are compared, and
every differential must remain below the corresponding `m:nary/m:e`. A mismatch
raises `EQUATION_INVALID`, rolls back the transaction and leaves the live version
unchanged. `verify_readback=true` extends the gate to a low-risk equation.

LaTeX `\mathbf{...}` and `\boldsymbol{...}`, Presentation MathML `mathvariant`
and OMML run/control properties use a separate style-preserving gate. Reserved
private-use sentinels delimit the requested scopes only in the
temporary linear payload and survive Word's `BuildUp()` across fractions,
radicals, scripts and n-ary objects. A bounded internal rewrite removes all
sentinels and applies native `m:sty="p"`, `m:sty="b"`, `m:sty="i"` or
`m:sty="bi"` to the enclosed math runs. Separate scopes write `m:ctrlPr/w:rPr`
bold/italic properties onto the intended OfficeMath
objects so structural glyphs such as fraction bars, radicals, delimiters and
n-ary operators carry the requested style too. Word then reads the equation back
again; a style-placement hash, run and control counts, and the normal semantic
contract must all agree. Readback normalizes Word's documented default italic/roman
run properties and arbitrary coalescing of adjacent sibling runs only when every
effective property is identical. Direct per-character
`Range.Font` mutation is not used because real Word testing showed that mixed
bold/italic edits inside a built OMath can destabilize COM.

MathML inheritance is resolved from `math` and `mstyle`, then overridden at the token.
The fourteen variants representable by native Word styles or mathematical alphabets
are preserved. The contextual Arabic `initial`, `tailed`, `looped` and `stretched`
variants fail with `EQUATION_INVALID` because silently turning them into ordinary
Latin/Arabic text would be data loss.

For textual conditions such as
`\begin{cases}x^2&\text{gdy }x\ge0\\-x&\text{gdy }x<0\end{cases}`,
ordinary U+0020 is insufficient because Word drops it outside quoted math text.
The converter emits bounded U+2003 case-column spacing and U+2005 text-boundary
spacing instead. Those characters survive Word build-up and are significant in
the semantic readback hash, so losing either one rolls the transaction back.

This is structural preservation evidence, not a proof of mathematical
equivalence. The response returns hashes, counts and verification flags, never
raw OMML or the reconstructed formula.

The in-process learning counters retain only input format and success/failure
counts. Formula text, document text, names and paths are not retained.

## Fast native tables

`insert_live_word_table` accepts a rectangular array of strings. It validates
all dimensions and cell characters before attaching to Word, writes the
complete matrix once using tabs between columns and paragraph marks between
rows, then calls `Range.ConvertToTable` with the tab separator.

This removes the usual row-by-row and cell-by-cell COM traffic. The operation
supports up to 200 rows, 50 columns, 5,000 cells and 500,000 characters. Cell
newlines become Word manual line breaks; tabs and cell-marker characters are
rejected because they would corrupt the table grid.

The same Undo transaction covers payload insertion, conversion, style,
AutoFit, alignment and repeating header-row formatting. Table-count mismatch
or any COM failure rolls the complete mutation back and leaves the live version
unchanged.

## Native table calculations

`preflight_live_word_table_formulas` validates up to 200 cell calculations
without attaching to Word. Each item contains a 1-based destination row and
column, one of `sum`, `average`, `count`, `max`, `min` or `product`, and exactly
one typed source:

- `directions`: one or two of `above`, `below`, `left`, `right`;
- `cell_range`: bounded start/end row and column coordinates.

The bridge generates `=SUM(ABOVE)` or an A1-style expression such as
`=AVERAGE(C2:C3)` internally. Raw formula strings, bookmarks, arbitrary field
switches and external field types are not accepted. A source range containing
its destination is rejected before Word is resolved.

`insert_live_word_table_formulas` requires a 1-based index for an existing
uniform rectangular table. It resolves the table once, checks every source and
destination, and refuses a non-empty destination unless
`replace_existing=true` was explicit for that cell. The complete batch runs
through one COM attachment, one screen-update suspension and one custom Undo
record.

WordToolkit inserts native field type 34 directly at the collapsed content
range of each cell, requires exactly one resulting field and a non-empty
native result range, then checks the final document field count. Word
calculates formula fields when they are inserted. The default fast path avoids
an immediate duplicate recalculation; set `force_update=true` only when an
additional `Field.Update()` is required. List, decimal and thousands separators
are read once from `Application.International` and applied to the complete
batch. Responses do not contain the generated formula code, source values or
result text.

`update_live_word_table_fields` is the fast refresh path after source cells
change. It reads only the count and numeric `Type` of up to 5,000 fields in one
table, calls `table.Range.Fields.Update()` once inside one custom Undo record,
then requires Word's return value to be zero and verifies the same field count
and type histogram. Any reported error or structural drift rolls back the
transaction and leaves the live version unchanged. An empty table is a
version-stable no-op. Codes, source values and displayed results are never
returned.

## Fast native lists

`insert_live_word_list` accepts up to 1,000 non-empty item strings. It validates
the complete input before attaching to Word, assigns one paragraph payload,
then invokes either `ListFormat.ApplyBulletDefault` or
`ListFormat.ApplyNumberDefault` once for the complete range.

This avoids per-item COM writes while preserving real Word list semantics.
Optional style and bounded font/paragraph formatting are applied inside the
same custom Undo record. A Word failure or unexpected `WdListType` rolls back
the complete mutation and leaves the live version unchanged.

The content-free result reports item count, numeric list type, range and
before/after list counts. It never returns the inserted item text.

## Safe native fields

`preflight_live_word_fields` validates the complete request without resolving
Word. `insert_live_word_fields` assigns one marker payload and replaces markers
from the end of the range with native `Field` objects inside one COM
attachment, one screen-update suspension and one custom Undo record.

The typed allowlist covers page/section counts, dates and times, file and
document properties, word/character counts, sequences, existing bookmark
references and restricted numeric formulas. It never accepts raw field-code
text. DDE, database, include, link, macro and external-data fields are
unreachable through these tools.

Numeric formulas accept numbers, arithmetic/comparison operators, parentheses
and a bounded deterministic-function allowlist. Cell references, bookmark
operands and unknown names are rejected. Use the dedicated `reference` kind
for a bookmark; it must exist before any mutation starts.

The public formula syntax is locale-neutral: use a period as the decimal
separator and a comma between function arguments, for example
`ROUND(1234.5/3,2)`. Immediately before `Fields.Add`, WordToolkit reads Word's
active list, decimal and thousands separators through `Application.International`
and localizes the expression and numeric picture. The localized field code is
never returned.

Every native type is checked and every requested `Field.Update()` must
succeed. An update, type or count mismatch rolls the full batch back.
Responses omit field code and displayed result text.

## Fast native bookmarks

`preflight_live_word_bookmarks` validates names, payload limits and formatting
without attaching to Word. Names begin with an ASCII letter, contain only
ASCII letters, digits or underscores, are at most 40 characters and are unique
without regard to capitalization.

`insert_live_word_bookmarks` accepts up to 200 non-empty ranges and 500,000
total characters. It writes the complete payload once, applies optional style
and formatting to each bookmarked range, calls `Bookmarks.Add`, and verifies
the native name plus exact start/end offsets. Existing names are rejected
before the Undo transaction. A native add, range or final-count mismatch rolls
the complete batch back.

Prefix and suffix text remain outside the bookmark. A later
`insert_live_word_fields` request can use `kind="reference"` to create an
updated native `REF` field targeting the verified name.

## Structure map

`map_live_word_structures` checks the 17 values in `WdStoryType` and follows
`NextStoryRange` through linked text frames and section-specific header/footer
stories. It returns only counts: characters, paragraphs, tables, fields and
equations. It also inventories document collections for sections, styles,
tables, equations, fields, forms, bookmarks, hyperlinks, comments, revisions,
content controls, inline and floating shapes, footnotes, endnotes, list
objects, list paragraphs, subdocuments, variables and generated tables.

The map does not return document text. It explicitly separates structures that
are detected from structures the live bridge can currently edit. Detection is
not falsely presented as mutation support.

With `include_type_histograms=true`, typed collections return numeric
`Type` histograms for native equations, fields, form fields, content controls,
revisions, inline/floating shapes and styles. Lists additionally resolve
`List.Range.ListFormat.ListType`. Requested scans default to 2,000 and cap at
10,000 objects per collection while reporting truncation and read errors.

The native runtime records only in-process observation counters for fixed
collection names. `adaptive_type_histograms` is reported as request metadata;
type histograms run only when `include_type_histograms=true`. The counters never
receive property values, content, per-document counts, paths, owners, handles
or a document-derived fingerprint and disappear when the MCP process exits.
`inspect_live_word_structure_learning` exposes that bounded memory state.

## Bounded structure item inspection

`inspect_live_word_structure_items` accepts one of the 23 collection names
reported by the structure map. It reads a zero-based page of at most 200
native items through one COM attachment. Returned metadata is specific to the
object class and can include range coordinates, native type, rows/columns,
style, dimensions, lock state, bookmark/control identifiers and generated
table settings.

Set `include_text=true` only when item content is required. Each preview is
limited to `max_text_chars`, which defaults to 500 and caps at 2,000. The tool
does not return raw field codes or external hyperlink addresses. A failed
property read is reported on that item and does not destroy the rest of the
page.

`adaptive_property_probing` is reported as request metadata. The native
inspector uses a fixed bounded property specification for each supported
collection and records only an in-process count for that collection. Returned
values and text never enter learning.

## Live formatting

`format_live_word_selection` requires a fresh token for an exact non-empty
selection. It never replaces or returns the selected text. Style, font and
paragraph changes run inside one Undo record and preserve the selection.

The formatting object is also accepted by `insert_live_word_text` and each
`type="text"` entry in `apply_live_word_operations`. Supported fields cover
font family and size, `#RRGGBB` color, bold/italic/underline, caps, single or
double strike, hidden text, highlight color indexes `0` through `16`, paragraph
alignment, spacing and indentation, keep-with-next, keep-together,
page-break-before and widow control. The canonical size and alignment names are
`font_size_pt` (1 through 200) and `paragraph_alignment` (`left`, `center`,
`right`, `justify`, or `distribute`). Compatibility
aliases `font_size` and `alignment` are accepted and normalized before COM;
supplying an alias together with its canonical field is invalid. The action
schema publishes types and bounds for the complete property set. Unknown names, wrong JSON
types and out-of-range values fail during preflight, before Word is mutated.
`strike` and `double_strike` may each be false or omitted, but cannot both be
true because Word clears the first mode when the second is applied.

One text operation may use `runs` instead of `text` to apply distinct font
formatting inside a single paragraph. Run-level formatting accepts font fields
only; paragraph fields remain on the parent text operation.

## Local equation learning

WordToolkit stores an in-process aggregate outcome table keyed only by equation
input format and success/failure. It never stores formula text, document text,
paths, owners or live document identifiers.

Successful and failed native Word equation operations update the aggregate
around the COM transaction. The counters are diagnostic evidence, not a parser
policy and not semantic readback. They disappear when the MCP process exits.
`inspect_live_word_equation_learning` exposes the complete bounded state and
never exposes a storage path because no file exists.

## Fast mixed batches

`apply_live_word_operations` accepts ordered objects with `type="text"` or
`type="equation"`. The bridge validates every operation and converts every
formula before it resolves the Word document. Invalid input therefore cannot
leave a partial heading or explanation behind.

LaTeX input is a documented subset rather than a claim of complete TeX macro
compatibility. The converter supports the ordinary fractions, roots, limits,
integrals, differentials, relations used by the live equation path, plus
`\boxed{...}` and `\implies`. Unsupported commands fail during batch preflight
with `failed_operation_index`; they are never discovered after a partial target
publication.

The payload text is assigned once. Equation ranges are then converted to native
OMath from the end of the payload toward the beginning, so Word's build-up of
one expression cannot invalidate the coordinates of a later expression. All
styles, OMath creation, build-up and final equation-count checks live
inside one custom Undo record. A failure requests one rollback and leaves the
live version unchanged.

`optimize_screen_updates=true` temporarily disables Word screen repainting for
the transaction and restores the exact prior value in a `finally` path. The
visible document is scrolled only once, after the complete batch succeeds.
This removes repeated COM startup, repaint and viewport costs. Native
`OMath.BuildUp()` remains a real Word cost and is not hidden by false timing
claims.

Use `preflight_live_word_equations` before large or unfamiliar formula sets.
Its default compact response returns only input/output lengths, a short linear
fingerprint, format/display flags and whether native readback is required. It
never attaches to Word and never changes a document. Request
`response_mode="full"` through the lazy execution gateway only when the exact
Word linear form is needed for diagnosis.

## Concurrency and failure behavior

- COM is initialized in a Windows STA for each serialized bridge operation, and
  no COM proxy crosses a thread boundary.
- Cursor and selection edits require a fresh token containing the connected
  document handle, live version, Word window handle, selection type, story,
  offsets and a hash of nearby text.
- Comment and revision mutations require a fresh content-bound review token;
  a raw collection index never authorizes a mutation.
- Guarded Undo accepts only one current top `WordToolkit:` entry and refuses a
  raw count, stale token, unavailable history or intervening user action.
- A non-empty selection is never replaced unless `replace_selection=true`.
- Mutations run inside a custom Word Undo record. If styling, field
  creation/update, OMath creation, build-up or native fidelity readback fails,
  WordToolkit requests one Undo operation and does not advance the live
  version.
- Read-only, protected and final documents are rejected. Cursor editing is
  limited to the main document story.
- Native math uses `OMaths.Add`, `BuildUp`, the explicit display/inline `Type`
  and an immediate `WordOpenXML` OMML parse.
- Bold math uses only the bounded internal sentinel-to-`m:sty` and
  sentinel-to-`m:ctrlPr/w:rPr` rewrite; every sentinel must disappear and both
  style and semantic readback must pass.
- Hbar and dagger inputs force readback. If the resulting native OMML lacks the
  required symbol, the complete mutation is rolled back.

## Validation and saving

`validate_live_word_document` requires `Document.Saved == true`, copies the
existing DOCX path to an internal temporary location, and runs the same
structural and Microsoft Open XML SDK validators used by isolated drafts. The
temporary copy is deleted before the tool returns. Unsaved changes cause a
version conflict rather than an implicit save.

Saving is a separate explicit mutation. It calls only `Document.Save()` and
rejects unsaved or read-only documents, so WordToolkit cannot silently choose a
new path or open a Save As dialog.
