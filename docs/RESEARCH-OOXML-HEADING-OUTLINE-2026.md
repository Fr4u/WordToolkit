# Typed OOXML heading and outline model

Status: implemented and qualified against Microsoft Word 16.0 build 16.0.20131 on
2026-07-25.

## The distinction the engine must preserve

A Word heading is not a paragraph whose localized style name happens to contain
`Heading`, `Nagłówek`, or any other familiar label. The operative saved-package
declaration is `w:outlineLvl`, resolved through paragraph-property precedence. Microsoft
defines its stored value as `0` through `9`: values `0` through `8` represent the nine
outline levels, while `9` means no outline level. If the element is omitted, the
paragraph has no outline level.

The Word object model exposes a different numeric surface. `Paragraph.OutlineLevel`
uses `1` through `9` for headings and `10` for body text. A paragraph formatted with a
built-in Heading 1 through Heading 9 style has the corresponding Word outline level,
but that convenience does not make style display names a portable classification rule.

The engine therefore uses this explicit mapping:

| Saved OOXML | Engine status/level | Word COM |
|---|---|---|
| `w:outlineLvl/@w:val = 0..8` | heading level `1..9` | `1..9` |
| `w:outlineLvl/@w:val = 9` | body text | `10` |
| no effective declaration | body text with no claimed source | `10` |
| malformed, duplicate, out-of-range, broken style chain | unresolved | no value invented |

## Resolution and hierarchy policy

`WordOutlineGraphBuilder` creates exactly one resolution record for every projected
paragraph in every supported Word story. It resolves, in order:

1. direct paragraph `w:pPr/w:outlineLvl`;
2. the exact paragraph style and its base-first `w:basedOn` chain;
3. document-default paragraph properties;
4. implicit body text when no declaration exists.

A valid direct declaration is authoritative even if an unused style reference is
broken. A malformed declaration at a higher-precedence layer does not silently fall
through to a lower layer. A missing, wrong-type, or cyclic style chain makes the
paragraph unresolved. The graph never guesses from style IDs or visible names.

Hierarchy is built separately per story from the nearest preceding shallower eligible
heading. Missing intermediate levels are diagnosed; synthetic parents are not created.
Paragraphs inside tracked-revision or unresolved Markup Compatibility containers remain
classified but are excluded from the hierarchy because the engine has not selected an
application view. Text-box flows form their own story hierarchy and are never folded
into the main-document outline.

The unified dependency graph reuses the existing paragraph nodes and adds two typed
edges: `outline_level_derived_from_style` and `outline_parent`. It does not create a
second, conflicting heading identity.

## Public operation and disclosure policy

`inspect_ooxml_heading_outline` and the strict `heading-outline-package` CLI expose the
contract `wordtoolkit.inspect_ooxml_heading_outline/1.0`. The default is the main-story
hierarchy with stable paragraph IDs, levels and counts. Heading text, style identifiers
and source locations are independent opt-ins; raw XML is unavailable. Positive paging
offsets require the expected package fingerprint.

The operation is read-only and bounded. It never starts Word, follows external
relationships, mutates the package, or returns a title hash that could become a cheap
dictionary oracle. The implementation caps paragraphs, headings, issues, XML part size,
ancestry depth, request size and returned page size.

## Qualification evidence

The gated `RealWordHeadingOutlineAcceptanceTests` fixture is valid under the Microsoft
365 Open XML SDK validator and contains style-derived levels 1 and 2, a direct level 9,
explicit and implicit body text, and a header-story heading. The engine result matched
`Paragraph.OutlineLevel` for every marked paragraph when Word 16.0 build 16.0.20131
opened the file read-only with repair disabled. The file SHA-256 was unchanged after
close. This qualifies one installed build; it is not a universal multi-version claim.

## Known boundaries

- `stylesWithEffects.xml` is inventoried but is not executed as Word's effective style
  engine, so its presence makes complete outline coverage false.
- Tracked revisions and Markup Compatibility branches are not view-selected.
- The graph does not infer semantic roles such as theorem, chapter, or definition from
  heading text.
- There is no heading mutation, renumbering, table-of-contents repair, or cross-version
  Word corpus in this slice.

## Primary Microsoft sources

- [Open XML SDK `OutlineLevel`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.outlinelevel?view=openxml-3.0.1)
- [Word `Paragraph.OutlineLevel`](https://learn.microsoft.com/en-us/office/vba/api/word.paragraph.outlinelevel)
- [Word `WdOutlineLevel`](https://learn.microsoft.com/en-us/office/vba/api/word.wdoutlinelevel)
- [Open XML SDK `BasedOn`](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.basedon)
- [Create and add a paragraph style](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-create-and-add-a-paragraph-style-to-a-word-processing-document)
- [Apply a style to a paragraph](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-apply-a-style-to-a-paragraph-in-a-word-processing-document)
