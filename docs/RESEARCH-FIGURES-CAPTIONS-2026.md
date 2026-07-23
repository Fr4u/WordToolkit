# Figure and caption research — 2026-07-23

This is differential research for the Figure/Caption vertical slice. It does not claim
to survey every Word library or plugin. Each statement is labelled as `FACT`,
`INFERENCE`, `DECISION` or `UNKNOWN` so implementation choices cannot hide behind
marketing language.

## Standards and Microsoft behavior

- **FACT:** ECMA-376 is the normative Office Open XML family; the current public
  publication contains the package, WordprocessingML, DrawingML and markup
  compatibility specifications. Source: [ECMA-376](https://ecma-international.org/publications-and-standards/standards/ecma-376/).
- **FACT:** Microsoft documents WordprocessingML drawing content as `w:drawing`
  containing `wp:anchor` or `wp:inline`, then `a:graphic/a:graphicData`; legacy VML can
  appear through `w:pict`. Source: [MS-ODRAWXML WordprocessingML
  content](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-odrawxml/0f92b026-1207-4eac-a725-d2907262124b).
- **FACT:** `wp:inline` is an inline drawing object and `wp:anchor` is a floating
  drawing anchor with positioning/wrapping state. Sources: [Open XML SDK
  Inline](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.wordprocessing.inline?view=openxml-3.0.1),
  [Open XML SDK Anchor](https://learn.microsoft.com/fr-fr/dotnet/api/documentformat.openxml.drawing.wordprocessing.anchor?view=openxml-3.0.1).
- **FACT:** `wp:docPr` carries non-visual drawing properties including identifier,
  name, title and description. Source: [Open XML SDK
  DocProperties](https://learn.microsoft.com/pt-br/dotnet/api/documentformat.openxml.drawing.wordprocessing.docproperties?view=openxml-3.0.1).
- **FACT:** DrawingML blips can declare embedded and linked relationships; legacy VML
  image data has a separate relationship/source model. Sources: [Open XML SDK
  Blip](https://learn.microsoft.com/fr-fr/dotnet/api/documentformat.openxml.drawing.blip?view=openxml-3.0.1),
  [Open XML SDK VML ImageData](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.vml.imagedata?view=openxml-3.0.1).
- **FACT:** Word's `Range.InsertCaption` inserts a caption label/title around a range;
  `TablesOfFigures.Add` builds a table of figures from captions or styles; caption
  labels are managed separately. Sources: [Word `InsertCaption`](https://learn.microsoft.com/it-it/OFFICE/VBA/api/word.range.insertcaption),
  [Word `TablesOfFigures.Add`](https://learn.microsoft.com/en-us/office/vba/api/word.tablesoffigures.add),
  [Word `CaptionLabels`](https://learn.microsoft.com/pl-pl/OFFICE/VBA/api/word.captionlabels).
- **FACT:** Word's field API creates fields over a range. Caption numbering therefore
  has independently observable field evidence; it is not a direct relationship from a
  drawing node. Source: [Word `Fields.Add`](https://learn.microsoft.com/en-us/office/vba/api/word.fields.add).
- **INFERENCE:** Proximity plus caption style/`SEQ` evidence can support a useful
  association, but it cannot become a structural fact because the file format does not
  declare a figure-caption edge.
- **DECISION:** WordToolKit requires a mutual unique best score and reports ties instead
  of silently choosing a nearby paragraph.
- **UNKNOWN:** Exact caption placement and table-of-figures refresh behavior across all
  supported desktop Word versions has not been measured in the licensed visual corpus.

## Competitor/API differential

The categories are deliberately kept separate: desktop automation, open-source OOXML
libraries, a commercial document SDK and an office-suite UNO runtime solve different
problems.

| Product/category | Publicly documented capability | Gap relevant to this slice | WordToolKit decision |
|---|---|---|---|
| Microsoft Word Object Model / desktop automation | `InlineShape` exposes inline objects and title metadata; caption and table-of-figures commands operate through live Word. Sources: [`InlineShape`](https://learn.microsoft.com/en-us/office/vba/api/word.inlineshape), [`InlineShape.Title`](https://learn.microsoft.com/en-us/office/vba/api/word.inlineshape.title) | Requires Word/application state; it is not a bounded, source-ordinal OPC graph and its live command surface is not a safe default for untrusted packages | Keep live Word as an authoritative optional adapter; keep saved-package inspection parse-only and inert |
| Open XML SDK / typed OOXML | Strongly typed `Inline`, `Anchor`, `DocProperties`, `Blip` and VML elements | Typed markup does not by itself supply one logical AlternateContent figure or an evidence-scored caption association | Build a semantic graph above the SDK/package model and retain exact source provenance |
| python-docx / open-source Python | Official docs expose inline shapes and state that only inline pictures are supported, while floating shapes are not. Sources: [shapes overview](https://python-docx.readthedocs.io/en/latest/user/shapes.html), [shape API](https://python-docx.readthedocs.io/en/latest/api/shape.html) | No documented floating-shape model or source-linked caption graph | Cover inline, anchor, VML and legacy object declarations without requiring Python or Word |
| Apache POI XWPF / open-source Java | `XWPFDocument` exposes document/picture APIs over DOCX. Source: [Apache POI `XWPFDocument`](https://poi.apache.org/apidocs/dev/org/apache/poi/xwpf/usermodel/XWPFDocument.html) | The public document API is lower-level than a bounded logical figure/caption/dependency contract | Preserve low-level evidence but expose stable high-level IDs, paging and redaction |
| docx4j / open-source Java | The project documents a JAXB-based Java representation of WordprocessingML and drawing-related content. Sources: [docx4j forum/documentation](https://www.docx4java.org/forums/docx-java-f6/), [getting started](https://www.docx4java.org/docx4j/Docx4j_GettingStarted.pdf) | General OOXML object access does not establish a vendor-neutral AI response contract or prove a caption link | Keep the domain model independent of MCP/CLI/model vendor and distinguish declaration from inference |
| Aspose.Words / commercial SDK | High-level drawing namespaces expose `ShapeBase`, `ImageData`, names and titles. Sources: [drawing namespace](https://reference.aspose.com/words/net/aspose.words.drawing/), [`ImageData.Title`](https://reference.aspose.com/words/net/aspose.words.drawing/imagedata/title/), [`ShapeBase.Name`](https://reference.aspose.com/words/net/aspose.words.drawing/shapebase/name/) | Proprietary high-level API; public docs do not prove the exact source-linked, redacted AI graph defined here | Compete on inspectability, explicit limits, provenance and an open neutral operation contract, not on unsupported rendering claims |
| LibreOffice UNO / office-suite runtime | UNO can enumerate text graphic objects through `XTextGraphicObjectsSupplier`. Source: [LibreOffice UNO API](https://api.libreoffice.org/docs/idl/ref/interfacecom_1_1sun_1_1star_1_1text_1_1XTextGraphicObjectsSupplier.html) | Runtime object enumeration depends on LibreOffice import/layout behavior and is not the original package declaration graph | Use LibreOffice later as a separate compatibility/render adapter, never as the only source of truth |

## Resulting architecture

- **DECISION:** Keep the Engine vendor-neutral. The figure graph depends on the OPC,
  lossless XML, semantic, reference and style layers, not on Word COM, MCP or an AI
  provider.
- **DECISION:** Preserve all representations in one logical figure and expose why one
  representation is preferred. Do not claim MCE evaluation unless the MCE graph was
  given a concrete understood-namespace set.
- **DECISION:** Make resources inert metadata. External targets stay unresolved, image
  bytes are not decoded and embedded/OLE packages are not opened.
- **DECISION:** Redact document text, source paths and relationship targets by default
  through independent opt-ins. This reduces both data exposure and model context.
- **DECISION:** Feed only proven objects and explicit unresolved candidates into the
  shared dependency graph.
- **DECISION:** Measure at the declared 10,000-object point with repeated samples. The
  first attempt exposed quadratic algorithms; fixing them was part of the feature, not
  deferred performance polish.

## Evidence still required

- **UNKNOWN:** Word/LibreOffice visual parity for inline/anchor/VML fallback combinations.
- **UNKNOWN:** Reading order and accessibility behavior across grouped shapes and text
  boxes.
- **UNKNOWN:** Full SmartArt relationship and fallback semantics.
- **UNKNOWN:** Safe OLE/embedded-package extraction policy and malware scanning adapter.
- **UNKNOWN:** Cross-version caption insertion, renumbering and table-of-figures refresh.
- **UNKNOWN:** Mutation round trips for figures/captions. This slice does not mutate
  them, so package preservation is inherited from the lossless read path rather than
  claimed as an edit proof.
