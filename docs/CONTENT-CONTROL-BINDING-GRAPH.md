# Content-control and Custom XML binding graph

This document defines the first typed, read-only model for Word structured document
tags (`w:sdt`), physical Custom XML stores, Word's built-in property stores,
`dataBinding` targets and Office 2013 repeating sections. It is evidence for analysis
and planning. It is not a claim that WordToolkit can yet refresh bound display text,
edit a Custom XML store, or reproduce every Word-version binding behavior.

## Why the graph exists

A content control is not just a wrapper around visible text. Its behavior can depend on
properties in `w:sdtPr`, a package-resident data store outside the visible Word story
selected by `storeItemID`, a
namespace context encoded in `prefixMappings`, an XPath target, its placement level and
the topology of nested repeating-section items. Treating `w:sdt` as one generic semantic
node loses the dependency that matters when a template is populated, repaired, diffed,
merged or inspected for privacy.

The engine therefore projects stable high-level objects:

- `WordContentControlDefinition` for type, level, lock, placeholder, native ID,
  alias/tag, temporary state and parent control;
- `WordCustomXmlStoreDefinition` for a physical `customXml` item or Word's virtual core
  and extended property stores;
- `WordContentControlBindingDefinition` for the normalized store identity, binding
  dialect, safe resolution status and stable target IDs;
- `WordContentControlBindingTarget` for a selected XML element's stable source ordinal
  and expanded name, never its value;
- `WordRepeatingSectionDefinition` for the container/item topology and the binding-target
  cardinality check.

All objects retain the package fingerprint. Supplying semantic or dependency inputs
from another package fails before graph construction.

## Package and namespace model

Physical stores are discovered from OPC relationships whose final relationship-type
segment is `customXml`. The data item must have exactly one internal `customXmlProps`
relationship. The properties part is parsed for its `datastoreItem/@itemID` GUID and
bounded `schemaRef` inventory. Relationship discovery is used instead of guessing from
`/customXml/itemN.xml` filenames.

The graph also exposes the two Word virtual stores observed in producer documents:

- core properties: `6c3c8bc8-f283-45ae-878a-bab7291924a1`;
- extended properties: `6668398d-a668-4e3e-a5eb-62b293d839f1`.

Those IDs resolve to the package parts selected by their exact OPC content types. A
duplicate content type or duplicate normalized item ID remains an ambiguity diagnostic.
The implementation does not silently select one duplicate.

Content-control discovery supports Transitional and Strict WordprocessingML, Office
2010 checkbox/entity controls, and Office 2013 repeating-section, repeating-item and
rich-text `dataBinding` markup. The type map includes plain/rich text, picture,
checkbox, combo/dropdown, date, building blocks, equation, group, citation,
bibliography, repeating controls and entity picker. Multiple mutually exclusive type
elements produce `Unknown` plus an error.

## Deliberately restricted XPath

Running an arbitrary XPath engine against attacker-controlled DOCX input would turn a
read-only inspector into a resource and complexity trap. WordToolkit accepts only:

- an absolute path beginning with `/`;
- child-element steps;
- optional namespace prefixes declared by `prefixMappings`;
- optional positive integer positions such as `[1]`.

Example:

```text
/p:profile[1]/p:customer[1]/p:name[1]
```

The engine does not execute `//`, attributes, wildcards, functions, axes, variables,
string predicates, arithmetic or arbitrary expressions. Unsupported syntax receives
`XPathUnsupported`; malformed or unbound names receive `XPathInvalid`; a conforming path
with no result receives `TargetMissing`. These statuses are evidence and are never
silently normalized into a guessed target.

Malformed store XML is retained as an unreadable store when its properties identity is
still available. The binding then receives `StoreUnreadable`. XML byte/element/depth
limits continue to fail hard; malformed input is recoverable evidence, but a resource
violation is not downgraded to a warning.

## Repeating sections

The Office 2013 repeating-section container is joined to its direct
`repeatingSectionItem` controls. When its binding resolves, the graph compares the
number of selected XML elements with the number of item controls. A mismatch is an
error, not a request to add or delete items automatically. A repeating item outside a
repeating-section parent and a non-item direct child are separately diagnosed.

## Unified dependency graph

The shared `WordDependencyGraph` now contains dedicated nodes for content controls,
Custom XML stores and binding targets. It adds edges for:

- part → defined content control;
- part → defined physical or built-in store;
- content control → selected store;
- store → resolved target;
- content control → resolved target;
- repeating section → repeating item.

Unresolved stores remain unresolved nodes and edges. Every endpoint is checked during
materialization. `content_control_custom_xml_bindings` has consequently been removed
from the explicitly-unmodeled dependency domains, while mutation and display refresh
remain outside the implemented contract.

## AI and privacy boundary

The lazy `inspect_ooxml_content_controls` action is summary-first and paged. Its default
response contains stable IDs, enum/status counts, topology and diagnostics only. Separate
switches control disclosure:

- `include_names`: aliases, tags, placeholder names and repeating-section titles;
- `include_binding_details`: item GUIDs, XPath, prefix mappings, namespace/schema names
  and target element names;
- `include_source`: part paths, content types, semantic/native IDs and source ordinals.

Custom XML values, bound display text and raw XML are never returned under any switch.
The action never starts Word, mutates the package, refreshes a binding or follows an
external relationship. Regression tests put distinct secrets in every redacted field
and enforce a sub-5,000-character default data/content response plus a sub-8,000-character
complete JSON-RPC gateway envelope.

Paging is not the only response bound. Target-ID and repeating-item-ID arrays are capped
at 100 entries per returned object; schema-reference and namespace-mapping arrays are
capped at 20. Each object reports its complete count and a `*_truncated` flag, so one
legal high-cardinality binding cannot tunnel thousands of identifiers through one page.

## Resource limits

Independent limits cover story parts, stores, controls, bindings, aggregate and
per-binding targets, issues, XML bytes/elements, XPath characters, prefix mappings,
namespace declarations and retained metadata. Cancellation is checked before package
projection and throughout store, control, binding, XPath and repeating-section loops.

XPath child lookup is indexed by store parent and expanded element name. Positional
steps therefore select an array element instead of rescanning all siblings for every
binding, and intermediate expansion is rejected as soon as the per-binding target limit
would be crossed. The checked-in scale harness proves 10,000 and 100,000 distinct
positional bindings. On the recorded Windows x64 host the 100,000 point resolved every
binding in 15.40 seconds, but retained about 1.24 GiB managed memory, allocated about
5.82 GiB and peaked near 1.72 GiB working set. It also required a benchmark-only 64 MiB
metadata-character budget instead of the 16 MiB production default. That is evidence
that the ceiling is reachable on the measured machine, not a claim that the path is
cheap.

The checked-in tests cover physical stores, both real Word built-in property stores,
the advanced torture Custom XML binding, Office 2010 control metadata, Office 2013
repeating sections, duplicate identities, malformed stores, unbound/unsupported XPath,
missing targets, package-fingerprint ownership, cancellation and resource rejection.

## Primary design evidence

- [Open XML SDK `DataBinding`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.databinding?view=openxml-3.0.1)
- [Open XML SDK `SdtProperties`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtproperties?view=openxml-3.0.1)
- [MS-DOCX Office 2013 `dataBinding`](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/2805f4e9-9333-4e7a-bb56-f1ce0e9e8e25)
- [Word repeating-section controls](https://learn.microsoft.com/en-us/previous-versions/office/jj604048%28v%3Doffice.15%29)
- [Custom XML `datastoreItem/@itemID`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.customxmldataproperties.datastoreitem.itemid?view=openxml-3.0.1)
- [Custom XML properties part](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.customxmlpart.customxmlpropertiespart?view=openxml-3.0.1)
- [Custom XML storage in Office packages](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/59d313b6-b9a8-4850-83f1-e87ad9abd509)
- [Office well-defined Custom XML parts](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/52769434-bde1-4e81-a128-7001873acb2b)
- [Row-level SDT content restriction](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtcontentrow?view=openxml-3.0.1)

## Known gaps

- No Custom XML value mutation, binding refresh or lossless SDT property edit.
- No general XPath engine and no attributes/functions/descendant axes.
- No proof across every desktop Word build or compatibility mode.
- No schema validation of the application-defined Custom XML vocabulary.
- No template slot fill planner, repeating-item insertion/deletion transaction, mail
  merge integration, co-authoring integration or conflict-aware Custom XML merge.
- Office 2013 rich-text binding content is identified but not decoded or rewritten.

Those are real missing systems. The graph does not rename them into “supported” merely
because their source bytes are preserved.
