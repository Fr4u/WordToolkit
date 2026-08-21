# OfficeMath repair research — 2026-07-26

## Question

What can WordToolkit repair in saved OMML without guessing mathematical intent,
flattening an editable equation, or silently replacing one Word interpretation with
another?

## Primary evidence

- Microsoft documents `m:oMathPara` as a display-math container holding one or more
  `m:oMath` elements. Its strongly typed `ParagraphProperties` member represents the
  single `m:oMathParaPr` property child:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.math.paragraph>
- Microsoft documents `m:oMathParaPr` as the property container for math-paragraph
  justification:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.math.paragraphproperties>
- The generated Open XML SDK object model exposes object property containers such as
  `m:funcPr` and `m:mPr` as typed singleton properties rather than an unordered list:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.math.functionproperties>
  and
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.math.matrixproperties>
- `OpenXmlValidator.Validate(OpenXmlPackage)` is the Microsoft SDK boundary for checking
  the exact candidate package against the Office schema:
  <https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.validation.openxmlvalidator>
- Microsoft’s SDK repository describes the SDK as a low-level OPC/Open XML framework,
  not a high-level productivity or repair engine, and requires detailed knowledge of
  ISO/IEC 29500:
  <https://github.com/dotnet/Open-XML-SDK>

## Resulting repair boundary

The first safe repair family removes a complete group of later duplicates only when:

1. the existing source-linked equation graph already reports the matching duplicate
   property/container issue;
2. every member of the sibling group is canonically identical, including expanded
   names, sorted non-namespace attributes, text, comments, processing instructions and
   descendants;
3. the package fingerprint, candidate ID and full candidate fingerprint still match;
4. all selected groups are removed from a lossless XML source through exact byte-span
   patches, with no unrelated reserialization;
5. the candidate package reparses with complete bounded equation/candidate coverage;
6. no equation issue code/severity count increases and every selected issue class is
   reduced;
7. the affected part has the same independent normalized XML fingerprint before and
   after collapsing all canonically redundant property duplicates;
8. every unplanned OPC entry is byte-identical and an exact inverse reconstructs the
   original package fingerprint;
9. Microsoft Open XML SDK validation introduces no error and the candidate error count
   is strictly lower than the baseline count;
10. the package has no declared digital signature and the final write is atomic with a
    sibling backup by default.

One request may carry up to 32 fingerprinted candidates. This is a token-efficiency
decision, not permission for an unreviewed “fix all” switch.

## Explicit non-goals

The engine does not automatically choose between non-equivalent duplicate properties.
It does not invent missing numerator, denominator, base, limit, matrix cell, run text,
settings or property values. It does not reorder malformed children, pad ragged matrices,
split equations Word would merge, delete empty equations, interpret preserved extension
markup, convert notation, prove mathematical equivalence or claim visual equivalence.

Those cases require either a stronger typed semantic proof, explicit human intent, or a
licensed Word execution and visual-validation lane. A parser may admit uncertainty. A
repair transaction may not hide it.
