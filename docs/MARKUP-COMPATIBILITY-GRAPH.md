# Markup Compatibility graph

Status: initial read-only ECMA-376 Part 3 fifth-edition implementation.

## Why this is an engine boundary

An OOXML consumer does not understand a document merely because it can parse XML. The
effective markup depends on which namespaces the consuming application understands and
which expanded names the markup specification declares as application-defined extension
elements. An unknown element may be discarded with its subtree, unwrapped while its
children remain, retained as an opaque extension island, or live inside the selected or
discarded branch of `mc:AlternateContent`.

WordToolkit therefore does not call Open XML SDK MCE preprocessing on its lossless
source. Microsoft documents that preprocessing removes unselected and unsupported
markup and that a later save persists only what remains. That behavior is useful for a
targeted transformed view but is poison as the storage truth of a lossless engine.

Primary evidence:

- [ECMA-376, including Part 3 fifth edition](https://ecma-international.org/publications-and-standards/standards/ecma-376/)
- [Open XML SDK markup-compatibility processing and save warning](https://learn.microsoft.com/en-us/office/open-xml/general/introduction-to-markup-compatibility)
- [Microsoft Word extension integration through MCE](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/728a7abc-7f55-40dc-90a7-1276ff53c8b2)
- [Open XML SDK legacy compatibility-attribute surface](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.markupcompatibilityattributes?view=openxml-3.0.1)

## Implemented model

`WordMarkupCompatibilityGraphBuilder` scans every OPC part whose declared content type
is XML. Each part is parsed through the bounded lossless XML source model; raw text is
never copied into the graph. The model records:

- in-scope `mc:Ignorable`, `mc:ProcessContent` and `mc:MustUnderstand` declarations;
- prefix and qualified-name resolution by namespace URI, including wildcard
  `ProcessContent` names;
- current-edition syntax diagnostics for MCE elements and attributes;
- `AlternateContent` branch order, required namespaces, first-match selection and
  fallback selection;
- ignored elements, process-content unwrapping and ignored qualified attributes;
- whether an affected element can actually influence output after ignored ancestors and
  unselected branches are considered;
- unresolved must-understand namespaces on effective content;
- explicit application-defined extension elements whose complete subtrees remain opaque;
- legacy `PreserveElements` and `PreserveAttributes` declarations as preserved,
  non-executed advisory evidence;
- stable namespace, part, rule, branch, affected-element and mismatch IDs.

The application configuration is an explicit set of understood namespace URIs. The
markup configuration is an explicit set of application-defined extension expanded
names. The engine does not infer either from a file extension, a vague Office-version
label, an `ext`-looking local name, or a namespace's publication date.

The reference model evaluates branch selection independently of output reachability.
This matters for nested alternate content: a nested branch can satisfy its own
requirements while remaining absent from output because an ancestor branch was not
selected. Affected-element records keep those two facts separate.

## MCP contract

The lazy `inspect_ooxml_markup_compatibility` action exposes eight bounded views:

- `summary` — rule-kind counts and invalid-token totals;
- `parts` — parse and coverage counts per XML part;
- `namespaces` — stable namespace IDs and usage counts;
- `rules` — resolved declarations without raw XML;
- `alternate_content` — branch requirements and selection;
- `affected` — ignored, unwrapped, or retained-with-ignored-attributes elements;
- `must_understand` — unresolved effective requirements;
- `issues` — syntax, binding, structure and edition diagnostics.

Namespace URIs and affected local names are redacted by default because private
vocabularies can disclose tenant and business-domain information. Part URIs, content
types, source hashes and XML ordinals require a separate source opt-in. Results are
paged, issue previews are capped, and the default complete JSON-RPC envelope has a
regression ceiling below 8,000 characters.

The action is parse-only. It does not open Word, preprocess or rewrite XML, evaluate an
extension vocabulary, follow relationships, open embedded packages, or claim that an
empty issue page proves compatibility with a particular Word build.

## Resource policy

The graph enforces independent limits for XML-part count, per-part and aggregate XML
bytes, per-part and aggregate elements, distinct namespaces, rule declarations,
alternate-content records, affected elements, must-understand mismatches, configuration
size and diagnostics. Cancellation is checked before package work and throughout part,
element and branch traversal. Malformed XML-typed parts become bounded error records;
resource-limit violations abort the analysis.

## Verified cases

Engine tests cover:

- namespace aliases and inherited ignorable/process-content state;
- ignored elements, ignored attributes and unwrapped content;
- first matching choice, fallback, and output reachability beneath an unselected branch;
- must-understand mismatch reporting;
- explicit application-defined extension islands;
- legacy preservation hints;
- unbound prefixes and malformed alternate-content structure;
- malformed XML parts, cancellation and resource ceilings;
- a real LibreOffice document containing a chart and related XML parts.

Native tests prove default redaction, explicit namespace details, explicit application
configuration, source opt-in, invalid-input rejection, zero COM invocations and bounded
default result plus full MCP envelope.

The Windows x64 scale baseline records 99,999, 499,999 and 998,998 actual XML elements.
Package-read plus graph-build time was 0.65 s, 2.75 s and 4.78 s respectively. The
largest point retained 1,029,273,392 managed bytes and reached a 1,161,748,480-byte peak
working set. Those numbers prove that the hard ceiling is reachable on the measured
64 GiB host; they also prove that the current object-rich graph is expensive. They are
not a rendering result, a default workload recommendation or a promise about another
machine. Exact reports live in `docs/benchmarks/2026-07-22-windows-x64/`.

## Deliberate limits

This is not yet an MCE serializer or a compatibility transform. It does not emit the
reference-model output document, mutate an alternate branch, synthesize a fallback,
maintain compatibility declarations after a namespace-changing edit, or provide a
version-pinned catalogue of every namespace understood by every Word build. Legacy
preservation hints are not executed. Application-defined extension semantics remain
opaque by design. These are remaining modules and corpus obligations, not behavior
silently guessed by the initial graph.
