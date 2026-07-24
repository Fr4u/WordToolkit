# Native SmartArt diagram graph

`WordDiagramGraph` is the saved-package model for native Word SmartArt. It treats a
diagram as structured DiagramML rather than a picture or an opaque XML island. The layer
is read-only: it proves structure and dependencies but does not claim that Office layout
execution, visual rendering or mutation is solved.

## Modeled surface

The builder discovers `dgm:relIds` references in package-reachable source parts and
models:

- diagram data, layout, quick-style and color relationships;
- the optional persisted drawing selected through `dsp:dataModelExt/@relId`;
- DiagramML points, stable identities, point types, property-set metadata, placeholder
  flags and text character counts;
- connections, source/destination endpoints, connection type and declared ordering;
- package reachability, exact relationship provenance and required-part resolution;
- definition unique IDs/minimum versions and deterministic diagram, point and connection
  identities;
- Transitional and Strict DiagramML namespaces and relationship types.

Point text is counted during parsing and discarded. The public graph retains no point
text value. Raw XML is not part of the model.

## Structural validation

The parser rejects unsafe XML and enforces bounded source parts, diagram parts, XML
bytes/elements, diagrams, points, connections, identifiers, text-character totals and
diagnostics. It reports missing or duplicated point/connection model IDs, ambiguous or
missing endpoints, invalid connection orders, invalid point property-set cardinality,
invalid placeholder values, malformed data-model cardinality, unresolved relationships,
definition mismatches and unreferenced diagram parts.

The reference fixture is opened by the Microsoft Open XML SDK and validated for the
Microsoft 365 file format before it is used to prove parser behavior. That gate matters:
a parser can otherwise appear correct only because its synthetic input is wrong in the
same direction.

## Dependency integration

`WordDependencyGraph` adds `diagram` and `diagram_point` nodes with
`defines_diagram`, `diagram_contains_point`, `diagram_connects_points` and
`diagram_uses_part` edges. Unresolved endpoints remain explicit unresolved nodes/edges;
they are not silently dropped. Diagram diagnostics enter the shared issue stream with a
`DGM:` prefix. `smartart_diagrams` has been removed from the explicitly unmodeled list.

## Token and privacy boundary

`inspect_ooxml_diagrams` has six paged views: `summary`, `diagrams`, `points`,
`connections`, `parts` and `issues`. The default response is short and redacts model
IDs, definition IDs and source provenance. Independent opt-ins expose bounded keys,
process-keyed 16-hex fingerprints, or source/relationship metadata. Point text and raw
XML remain unavailable under every option.

One `wop1` lease covers OPC reading and SmartArt projection. A 10 KiB projected-item
ceiling, a maximum page size of 50 and a 32 KiB complete-response regression gate prevent
large identifiers from flooding model context. The inspector does not start Word,
execute Office layout, mutate the package, open external targets or run Python.

## Honest boundary

This slice does not execute Office's diagram layout algorithms, reconstruct arbitrary
rendered geometry, choose a fallback based on a target application's capabilities, edit
SmartArt, synchronize the persisted drawing after a change or prove pixel parity with
Word. The package layer still preserves those parts, while this graph reports what is
known and what is unresolved.

## Standards anchors

- Microsoft documents the DiagramML data model represented by
  [`dgm:dataModel`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.diagrams.datamodel?view=openxml-3.0.1),
  including its point and connection lists.
- [`dgm:pt`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.diagrams.point?view=openxml-3.0.1)
  and [`dgm:cxn`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.diagrams.connection?view=openxml-3.0.1)
  are distinct typed elements; the graph preserves that distinction.
- [`dgm:relIds`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.diagrams.relationshipids?view=openxml-3.0.1)
  carries the data/layout/style/color relationship IDs used by an inserted diagram.
- The MS-ODRAWXML diagram contract explains the persisted drawing, layout fallback and
  presentation-association mapping:
  [Diagrams](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-odrawxml/c36c1152-3aec-45e5-8f16-936da2678e5d).
