# Document property graph

`WordDocumentPropertyGraph` is the bounded, read-only source of truth for document
metadata stored in the OPC core-properties part, the Office extended-properties part,
and the typed custom-properties part. It does not flatten those three vocabularies into
one string dictionary. Family, source part, declared value type, package reachability,
structural validity and uniqueness remain explicit.

Document variables are deliberately separate. They live under `w:docVars` in
`word/settings.xml`, not in a property part. The unified dependency graph joins both
domains only where a parsed field instruction proves the dependency.

## Package admission and identity

The builder accepts a property part only when the package root contains the exact
standard relationship type and the target has the exact family content type. It also
inventories typed but package-unreachable parts as damaged evidence. Relationship URI
suffix lookalikes, contradictory families, duplicate family parts and duplicate
relationships never become a trusted property source.

Core properties use the OPC core namespace plus the documented Dublin Core terms.
Extended and custom properties recognize both Transitional and Strict namespaces.
Core `created` and `modified` values must also carry an `xsi:type` QName that resolves
to `dcterms:W3CDTF`; spelling the prefix differently is valid, but omitting or rebinding
the QName is not. Custom properties retain their `name`, `pid`, `fmtid`, one typed value child and source
ordinal. The standard custom-property format identifier is
`{D5CDD505-2E9C-101B-9397-08002B2CF9AE}` and a usable `pid` starts at 2. Duplicate
case-insensitive names or numeric IDs remain ambiguous and cannot resolve a field.

Stable `wdp_` IDs are derived from package-bound source identity. They are selectors for
the unchanged package fingerprint, not secrets and not cross-version identities.

## Value model

The public graph distinguishes:

- text, signed and unsigned integers, floating point and decimal values;
- booleans, XML date/time and currency values;
- error codes and class IDs;
- binary, vector, array, variant, empty and unknown values.

Scalar lexical forms are validated before they can enter the field-resolution index.
Invalid dates, numbers, booleans, GUIDs and other typed scalars remain visible as
invalid metadata with a diagnostic; they are never silently coerced. Complex or binary
values are classified and counted, but their contents are neither decoded nor retained
as a scalar response value.

## Field dependencies

`DOCPROPERTY name` produces a typed `Reads` reference and resolves only when exactly one
package-reachable, structurally valid scalar property has that case-insensitive name.
`DOCVARIABLE name` resolves only to one unambiguous persistent `w:docVar` from settings.
`SET` and `ASK` are not rewritten into persistent-variable definitions: they remain
field-local behavior and unresolved where the package does not prove a persistent read.

The unified graph exposes `document_property` and `document_variable` nodes plus
`defines_document_property` and `defines_document_variable` edges. Its coverage flag
states whether both sources were projected. An absent resolution under an invalid,
duplicate or ambiguous source is evidence of damage, not permission to guess Word's
cached display result.

## MCP disclosure policy

Lazy action `inspect_ooxml_properties` supports `summary`, `properties`, `parts` and
`issues` views plus exact family, value-kind and `wdp_` filters. Standard core and
extended schema names are visible. Custom names, scalar values, hashes and source
provenance require four independent opt-ins:

- `include_names` returns bounded custom names;
- `include_values` returns at most 2,048 characters of a validated scalar value;
- `include_hashes` returns process-keyed 16-hex equality fingerprints and, in the parts
  view, source SHA-256;
- `include_source` returns bounded part/content-type, custom `pid`/`fmtid` and source
  ordinal metadata.

Raw XML, complex values, binary payloads and evaluated or cached field results have no
response field. Fingerprints are process-scoped equality hints, not durable IDs and not
password hashes. The operation does not open Word, update a field or mutate the package.

## Hard limits

| Resource | Limit |
|---|---:|
| one property XML part | 16 MiB |
| aggregate property XML | 32 MiB |
| properties per part | 50,000 |
| properties in one graph | 100,000 |
| one value | 1,048,576 characters |
| aggregate value characters | 16 MiB |
| one name | 4,096 characters |
| retained diagnostics | 1,000 |
| MCP page size | 50 items |
| projected MCP items | 32 KiB per response page |

The same 640 MiB `word_operation_accounted_v1` (`wop1`) lease covers OPC admission,
lossless XML and property projection. These are conservative rejection bounds, not a
claim about exact CLR heap or resident memory.

## Non-goals

This slice is inspection and dependency evidence. It does not create, update, delete or
renumber properties; evaluate or refresh fields; decode vectors, arrays, variants or
binary values; mirror Word's Advanced Properties UI; or prove rendering. A future
mutation path must use exact package fingerprints, a reviewed plan, collision-safe
`pid` allocation, field-impact analysis, schema comparison and atomic create-new or
backup-protected application.

## Primary sources

- [Microsoft: set a custom property in a word-processing document](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-set-a-custom-property-in-a-word-processing-document)
- [Microsoft: retrieve application property values](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-retrieve-application-property-values-from-a-word-processing-document)
- [Open XML SDK: `CustomFilePropertiesPart`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.wordprocessingdocument.customfilepropertiespart?view=openxml-3.0.1)
- [Open XML SDK: `DocumentVariables`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.documentvariables?view=openxml-3.0.1)
- [Word object model: `Variable`](https://learn.microsoft.com/en-us/office/vba/api/word.variable)
- [Microsoft: structure of a WordprocessingML document](https://learn.microsoft.com/en-us/office/open-xml/word/structure-of-a-wordprocessingml-document)
