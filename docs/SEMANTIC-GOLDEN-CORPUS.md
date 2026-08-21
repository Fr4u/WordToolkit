# Semantic golden corpus

The native engine has two different corpus gates. They prove different things and must
not be confused.

- `DocumentCorpusSmokeTests` parses every supported XML part in the bundled corpus,
  proves an exact-byte no-op, and checks that dependency-graph endpoints close. This
  catches crashes, accidental normalization and broken graph topology.
- `GoldenSemanticCorpusTests` checks hand-reviewed semantic facts stored in
  `native/WordToolkit.Engine.Tests/Corpus/golden-semantic-v1.json`. This catches a
  parser that still runs but silently changes what the document means.

## Version 1 scope

The first manifest fixes exact expectations for nine public fixtures from five
producer families:

| Fixture | Producer family | Primary oracle domains |
|---|---|---|
| `poi_styles.docx` | Apache POI | style inheritance and effective formatting |
| `poi_complex_lists.docx` | Apache POI | abstract numbering, instances and level semantics |
| `poi_field_codes.docx` | Apache POI | complex AUTHOR/CREATEDATE fields and numbering |
| `real_hyperlinks_footnotes.docx` | real-world Microsoft Word | notes, comments, links, drawings and unresolved REF behavior |
| `pandoc_comments.docx` | Pandoc | comments, reply threads and comment stories |
| `pandoc_track_move.docx` | Pandoc | tracked moves, revision text and track-revisions settings |
| `mammoth_textbox.docx` | Mammoth | nested text boxes and duplicate bookmark diagnostics |
| `poi_header_footer.docx` | Apache POI | six header/footer bindings across seven stories |
| `lo_chart.docx` | LibreOffice | chart-part preservation behind an opaque drawing boundary |

Every entry binds the source file SHA-256 and the engine's order-independent package
fingerprint. It then fixes exact semantic-node counts, complete node-kind counts,
style/default summaries, selected style facts, numbering summaries, reference and
field facts, review summaries, section bindings, dependency counts and complete
dependency-edge-kind counts. The style fixture also fixes selected effective paragraph
and run properties using source part, lexical element ordinal and semantic kind; it
does not depend on document text or a transient node ID.

Dependency expectations include every physical Custom XML item plus Word's built-in
core and extended property stores. The July 2026 content-control graph change added
only these source-proven store nodes and `defines_custom_xml_store` edges to the nine
existing fixtures; the corresponding OPC parts were independently enumerated before
the oracle was updated.

The manifest contains no document text, raw XML, field result text, comment text,
relationship targets or binary payloads. Tests operate on the engine's typed objects.

## Oracle provenance

The version 1 facts were first produced through the packaged, read-only native MCP
inspection surface and then cross-checked directly against the source OPC parts. The
independent checks confirmed:

- 12 style definitions and the `berschrift1` → `Standard` inheritance edge;
- six abstract numbering definitions, six numbering instances and their mappings;
- AUTHOR and CREATEDATE instructions with two complex field starts;
- five comment definitions and five complete anchor/reference sets;
- one move-from revision, one move-to revision and two move-range starts;
- two text-box containers and two bookmark starts;
- three header plus three footer references;
- one chart relationship.

This is a semantic oracle, not a rendering oracle. It does not prove Word-identical
pagination, visual fidelity, full ECMA-376 coverage or correctness for documents that
are absent from the manifest. The large smoke corpus, hostile/fuzz tests, Open XML SDK
validation and real-Word acceptance remain separate gates.

## Updating the manifest

Never refresh expected values merely because the test failed. A failure is evidence of
one of three things: a source fixture changed, the engine regressed, or the engine was
intentionally corrected. Identify which one before touching the manifest.

For an intentional semantic change:

1. inspect the affected typed model and the source OPC part independently;
2. explain the old and new interpretation in the pull request;
3. update only the affected facts and hashes;
4. keep `schema_version` unchanged for value corrections and increment it for an
   incompatible manifest shape or oracle policy;
5. run the focused test, both complete native test suites and the normal release gates.

Run the focused gate with the pinned SDK:

```powershell
dotnet test native/WordToolkit.Engine.Tests/WordToolkit.Engine.Tests.csproj `
  --filter FullyQualifiedName~GoldenSemanticCorpusTests
```
