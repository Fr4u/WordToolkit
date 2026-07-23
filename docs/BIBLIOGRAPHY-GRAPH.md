# Bibliography source graph

## Boundary

`WordBibliographyGraphBuilder` is a bounded, read-only saved-package adapter. It turns
Word's document bibliography source list into stable semantic objects and joins a
`CITATION` field to a concrete source only when the tag is unambiguous. It does not open
Word, evaluate or refresh fields, execute bibliography XSLT, render a formatted
bibliography, fetch a URI, or mutate Custom XML.

Microsoft documents that a document's current source list is stored under `customXml`,
that a citation field carries the source `Tag`, and that LCID controls localized display
behavior. The Open XML SDK exposes `b:Sources` as the shared-bibliography root with
`b:Source` children and publishes the canonical `SourceType` value set. Those are the
contract boundaries used here:

- <https://learn.microsoft.com/en-us/office/vba/word/concepts/working-with-word/working-with-bibliographies>
- <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.bibliography.sources?view=openxml-3.0.1>
- <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.bibliography.datasourcevalues?view=openxml-3.0.1>
- <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.customxmlparttype?view=openxml-3.0.1>

## Discovery and namespaces

The builder considers internal `customXml` relationship targets, sources of
`customXmlProps` relationships and preserved `/customXml/*.xml` parts. It parses them
with the lossless safe-XML reader, but projects only a root named `Sources` in one of:

- `http://schemas.openxmlformats.org/officeDocument/2006/bibliography`;
- `http://schemas.microsoft.com/office/word/2004/10/bibliography`.

Unrelated Custom XML stays outside this graph. A malformed candidate produces a bounded
diagnostic and is never interpreted. Orphan bibliography parts remain visible with
`IsPackageReachable=false`; reachability is evidence, not a deletion instruction.

## Semantic objects

Each collection records a stable ID, part/source location, namespace, package
reachability, incoming relationship count, selected style, style name, version, URI and
its ordered source IDs.

Each source records:

- stable ID and collection/source location;
- `Tag`, `SourceType`, normalized GUID and parsed non-negative LCID;
- title and year shortcuts;
- ordered scalar fields with known/unmodeled classification;
- contributor roles, people and corporate names;
- duplicate-identity and unmodeled-element evidence.

A unique valid GUID is the preferred stable identity seed. A case-normalized tag is the
fallback; the source ordinal is used only when neither exists. An occurrence counter
keeps duplicate identities distinct. Therefore inserting or moving an unrelated source
does not churn a well-identified source ID, while deliberately ambiguous duplicates do
not pretend to have stable cross-edit identity. Repeated singleton fields fail closed:
two `Tag`, `Guid` or `SourceType` elements produce diagnostics and no typed shortcut or
citation binding is selected from their first value. Raw bounded fields remain available
for diagnosis.

## Citation resolution

The existing reference parser emits an inert `Reads:Citation` edge keyed by the field's
first positional tag. The unified dependency builder adds `BibliographyCollection` and
`BibliographySource` nodes, `DefinesBibliographyCollection` and
`BibliographyContainsSource` edges, then redirects the citation edge to the source node
only when exactly one case-insensitive tag matches. Missing and duplicate tags retain an
unresolved `ReferenceTarget`; no first/last-source guess is made.

## Limits and diagnostics

Default independent hard limits include:

| Resource | Limit |
|---|---:|
| Custom XML candidates | 2,048 |
| bibliography parts | 256 |
| sources | 100,000 |
| scalar fields per source | 256 |
| contributor roles per source | 64 |
| people per source | 1,024 |
| corporate names per source | 1,024 |
| unique unmodeled element names per source | 256 |
| source part bytes | 64 MiB |
| XML elements per part | 1,000,000 |
| characters per value | 32,768 |
| aggregate retained value characters | 32 MiB |
| diagnostics | 10,000 |

Both `inspect_ooxml_bibliography` and `inspect_ooxml_dependencies` create one shared
640 MiB `wop1` operation lease before OPC retention and pass it through semantic,
reference and bibliography projection. People and corporate-name limits are aggregated
across every contributor role in one source, not reset per role. Unmodeled names have a
separate per-source count limit and consume the aggregate metadata-character budget
before their display strings are materialized. The dependency graph
keeps its independent `wdg1` graph budget. Repeated parsing remains a known allocation
cost until shared immutable XML storage exists. XML parsing and retained collection,
source, field, contributor, person, corporate-name and diagnostic records are accounted
separately; the model remains a conservative deterministic proxy, not a CLR-heap claim.

Diagnostics cover malformed/undecodable XML, multiple collections, missing/duplicate
tags, duplicate GUIDs, invalid GUID/LCID values, missing/unknown source types, duplicate
singleton fields and preserved unmodeled children/extensions. Diagnostic messages never
contain field values.

## AI-facing inspection

Lazy `inspect_ooxml_bibliography` supports `summary`, `collections`, `sources`, `fields`,
`contributors`, `citations` and `issues`, exact source ID/tag/type filters, paging and
independent sensitive/source-location opt-ins. The default response returns counts,
types, stable IDs and bounded fingerprints, not tags, titles, GUIDs, names, field values,
style paths, URIs, raw XML or cached citation text. Its execution policy is fixed to:

`parse_package_only_never_open_word_evaluate_fields_execute_xslt_or_follow_external_targets`

Paging scans the bounded typed collection to retain an exact `matched_item_count`, but
projects response objects only for the requested page. A small `max_items` therefore
does not first materialize every source field, contributor or citation result. The
primary page and optional diagnostic page also share a 65,536-character
`bibliography_projected_payload_characters_v1` budget. If the next bounded item would
cross it, `response_budget_truncated=true` and `next_offset` resumes at the first item
not returned. One oversized item fails with `PACKAGE_LIMIT` instead of escaping the
bound.

Redacted values use a 64-bit prefix of process-keyed HMAC-SHA-256, reported as
`fingerprint_scope=process_hmac_sha256_64`. They support equality checks only while the
native process lives and do not expose a public unsalted hash of low-entropy values such
as a year. They are not durable identifiers. Stable `wbs_` source IDs are selectors, not
confidentiality boundaries.

The current slice is not a bibliography renderer or repair engine. Source-type-specific
required-field rules, locale/style rendering, safe source mutation, citation refresh and
multi-version Word round-trip evidence remain open work.
