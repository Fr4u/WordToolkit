# Template style alignment research — 2026-07-26

## Question

How can WordToolkit align selected Word style definitions to a second Word package
without matching localized display names, breaking the target document's existing style
references, importing a user-specific attached-template path, or silently changing
numbering and theme semantics?

## Primary evidence

- Microsoft documents `w:style` as the typed style-definition object and distinguishes
  paragraph, character, linked, table, numbering and default paragraph/run-property
  styles. The stable content reference is `w:styleId`, while name and aliases are UI
  metadata:
  <https://learn.microsoft.com/en-us/office/open-xml/word/how-to-create-and-add-a-paragraph-style-to-a-word-processing-document>
- `w:next` is meaningful for paragraph styles and names the style automatically applied
  to a following paragraph. A missing or wrong-type target is ignored by Word, so copying
  one style without its dependency can create a package that validates yet behaves
  differently during editing:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.nextparagraphstyle>
- `w:link` is a typed linked-style reference. A paragraph/character pair therefore has to
  enter an alignment plan as one dependency closure, not as two independent names:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.style.linkedstyle>
- Microsoft exposes the ordinary and effects-aware style parts through the same
  `StylesPart` base. Updating only `StyleDefinitionsPart` when a
  `StylesWithEffectsPart` exists would leave two style projections out of sync:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.stylespart>
- `w:numStyleLink` and `w:styleLink` connect numbering styles, numbering instances and
  abstract numbering definitions. A style-level `w:numId` is not a self-contained visual
  property:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.numberingstylelink>
  and
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.stylelink>
- `CreateFromTemplate` can attach an absolute template path, and Microsoft warns that the
  resulting relationship is commonly user-specific and breaks when the document is
  shared. Style alignment must therefore copy reviewed definitions, not create an
  implicit external attachment:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.wordprocessingdocument.createfromtemplate>
- `w:linkStyles` asks Word to update styles automatically from an attached template. That
  open-ended future effect is different from one fingerprint-bound transaction and is not
  enabled by this operation:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.linkstyles>

## Resulting contract

The first provider-neutral template-alignment family is deliberately based on stable
style IDs:

1. The target and template are separate, exact-fingerprint Word packages. Both remain
   readable and the template is rechecked immediately before target publication.
2. Inspection reports add/replace candidates by `w:styleId`; it never guesses from
   localized names, aliases or similar formatting.
3. Selecting one candidate automatically selects its complete `basedOn`, `next` and
   linked-style closure. Cycles, missing references and type mismatches block the
   candidate rather than being cut.
4. A style already equivalent after Word-namespace normalization is a no-op and is not a
   candidate. Target-only and unselected styles remain byte-semantically unchanged.
5. Transitional and Strict WordprocessingML are accepted. The copied definition is
   translated only between the two standard Word namespaces; extension namespaces and
   unknown descendants remain preserved.
6. If both packages expose `stylesWithEffects`, the same selected IDs are aligned in both
   parts and both projections are validated. An asymmetric or incomplete effects part
   blocks mutation.
7. Theme-backed attributes are admitted only when the two packages have the same
   canonical theme plus the same `themeFontLang` context. A different theme is not
   flattened into guessed RGB/font values.
8. A selected style with `w:numId`, a numbering style, `numStyleLink` or `styleLink` is
   admitted only when the corresponding target and template numbering dependency is
   proven equivalent. Numbering IDs are never copied across packages merely because the
   integers match.
9. The candidate reparses through the semantic, style and numbering graphs. Selected
   definitions match the template contract, dependencies resolve, unselected target
   definitions and every unplanned OPC entry remain unchanged, no new graph issue appears,
   and an exact inverse reconstructs the target fingerprint.
10. Apply blocks signatures, requires baseline-aware Microsoft Open XML SDK validation,
    writes atomically and retains a sibling target backup by default. It never modifies or
    attaches the template.

## Explicit non-goals

This operation does not infer that `Heading 1`, `Nagłówek 1` or similarly formatted
styles represent the same semantic role. It does not delete target-only styles, import
macros, copy an attached-template relationship, enable `w:linkStyles`, replace the target
theme, rebuild numbering, align document content automatically, or claim rendered
equivalence across Word builds.

Those are later template-engine policies. Hiding them inside a broad “make it look like
the template” switch would be convenient, opaque and unsafe.
