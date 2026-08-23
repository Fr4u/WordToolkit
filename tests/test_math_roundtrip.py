from __future__ import annotations

import pytest

from wordtoolkit.errors import ErrorCode, WordToolkitError
from wordtoolkit.math import MathEngine


@pytest.mark.parametrize(
    "source,source_format",
    [
        (r"\frac{x^2+1}{\sqrt[3]{y}}", "latex"),
        (r"\sum_{i=1}^{n} i^2", "latex"),
        (r"\begin{matrix}a&b\\c&d\end{matrix}", "latex"),
        (r"\vec{x}+\text{speed}", "latex"),
        ("(x^2+1)/√(3&y)", "unicodemath"),
        (
            '<math xmlns="http://www.w3.org/1998/Math/MathML"><mfrac><msup><mi>x</mi><mn>2</mn></msup><mi>y</mi></mfrac></math>',
            "mathml",
        ),
    ],
)
def test_semantic_omml_roundtrip(source: str, source_format: str) -> None:
    math = MathEngine()
    omml = math.convert(source, source_format, "omml", display=True)
    comparison = math.compare(source, source_format, omml, "omml")
    assert comparison.equivalent, comparison
    assert "oMathPara" in omml
    assert "http://schemas.openxmlformats.org/officeDocument/2006/math" in omml


def test_inline_and_block_use_correct_office_math_containers() -> None:
    math = MathEngine()
    inline = math.convert(r"x_1^2", "latex", "omml", display=False)
    block = math.convert(r"x_1^2", "latex", "omml", display=True)
    assert "oMathPara" not in inline and "oMath" in inline
    assert "oMathPara" in block and "oMath" in block


def test_structured_ast_roundtrip() -> None:
    ast = {
        "kind": "fraction",
        "children": [
            {"kind": "identifier", "value": "a"},
            {"kind": "radical", "children": [{"kind": "identifier", "value": "b"}]},
        ],
    }
    math = MathEngine()
    omml = math.convert(ast, "ast", "omml")
    assert math.compare(ast, "ast", omml, "omml").equivalent


def test_structured_ast_rejects_unknown_node_kind() -> None:
    with pytest.raises(ValueError, match="Unsupported equation AST kind"):
        MathEngine().convert({"kind": "rawXml", "value": "<evil/>"}, "ast", "omml")


def test_cases_latex_preserves_the_left_brace_and_equation_array() -> None:
    math = MathEngine()
    source = r"\begin{cases}x+y=1\\2x-y=0\end{cases}"
    omml = math.convert(source, "latex", "omml", display=True)
    exported = math.convert(omml, "omml", "latex", display=True)
    assert "\\begin{cases}" in exported
    assert math.compare(source, "latex", exported, "latex").equivalent


def test_cases_unicodemath_preserves_the_left_brace_and_equation_array() -> None:
    math = MathEngine()
    source = "cases(x+y=1@2x-y=0)"
    omml = math.convert(source, "unicodemath", "omml", display=True)
    exported = math.convert(omml, "omml", "unicodemath", display=True)
    assert exported.startswith("cases(")
    assert math.compare(source, "unicodemath", exported, "unicodemath").equivalent


def test_unicodemath_export_does_not_merge_adjacent_identifiers() -> None:
    math = MathEngine()
    source = r"\int_0^1 e^{-x^2}\,d x"
    omml = math.convert(source, "latex", "omml", display=True)
    exported = math.convert(omml, "omml", "unicodemath", display=True)
    assert "d x" in exported
    assert math.compare(omml, "omml", exported, "unicodemath").equivalent


def test_unicodemath_export_does_not_merge_identifier_with_scripted_identifier() -> None:
    math = MathEngine()
    source = r"E=m c^2"
    omml = math.convert(source, "latex", "omml", display=True)
    exported = math.convert(omml, "omml", "unicodemath", display=True)
    assert "m c^(2)" in exported
    assert math.compare(omml, "omml", exported, "unicodemath").equivalent


@pytest.mark.parametrize(
    "source,source_format",
    [
        (
            r"\frac{x_i^2+\sqrt[3]{y}}{1+\alpha}+\sum_{k=1}^{n}k^2",
            "latex",
        ),
        (r"\left(\begin{matrix}a&b\\c&d\end{matrix}\right)", "latex"),
        (r"\begin{aligned}x+y&=1\\2x-y&=0\end{aligned}", "latex"),
        (r"\lim_{x\to 0}\frac{\sin x}{x}", "latex"),
        (r"\vec{v}+\hat{x}+\bar{y}+\text{const}", "latex"),
        ("matrix(a&b@c&d)", "unicodemath"),
        ("eqarray(x+y=1@2x-y=0)", "unicodemath"),
        ("√(3&x)+∫_(0)^(1) x^2", "unicodemath"),
    ],
)
@pytest.mark.parametrize("output_format", ["latex", "unicodemath", "mathml"])
def test_omml_export_formats_are_semantically_reparseable(
    source: str, source_format: str, output_format: str
) -> None:
    math = MathEngine()
    omml = math.convert(source, source_format, "omml", display=True)
    canonical = math.parse(omml, "omml")
    exported = math.convert(canonical.to_dict(), "ast", output_format, display=True)
    comparison = math.compare(canonical.to_dict(), "ast", exported, output_format)
    assert comparison.equivalent, {
        "format": output_format,
        "exported": exported,
        "comparison": comparison,
    }


@pytest.mark.parametrize(
    "source,source_format",
    [
        (r"\boxed{x+1}", "latex"),
        ("▭((x)/(y))", "unicodemath"),
        (
            '<math xmlns="http://www.w3.org/1998/Math/MathML"><menclose notation="box"><mi>x</mi></menclose></math>',
            "mathml",
        ),
    ],
)
def test_boxed_formula_uses_border_box_and_roundtrips_all_formats(
    source: str, source_format: str
) -> None:
    math = MathEngine()
    canonical = math.parse(source, source_format)
    omml = math.convert(canonical.to_dict(), "ast", "omml")

    assert "<m:borderBox>" in omml
    assert "<m:borderBoxPr/>" in omml
    assert "<m:d>" not in omml
    assert math.compare(canonical.to_dict(), "ast", omml, "omml").equivalent

    for output_format in ("latex", "unicodemath", "mathml"):
        exported = math.convert(canonical.to_dict(), "ast", output_format)
        assert math.compare(canonical.to_dict(), "ast", exported, output_format).equivalent


def test_border_box_omml_parses_as_boxed_formula() -> None:
    source = """<m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
      <m:borderBox><m:borderBoxPr/><m:e><m:r><m:t>x</m:t></m:r></m:e></m:borderBox>
    </m:oMath>"""
    math = MathEngine()

    ast = math.parse(source, "omml")

    assert ast.kind == "enclosure"
    assert ast.attr("notation") == "box"
    assert math.convert(ast.to_dict(), "ast", "latex") == r"\boxed{x}"
    assert math.convert(ast.to_dict(), "ast", "unicodemath") == "▭(x)"


def test_plain_omml_box_preserves_its_object_family_on_omml_roundtrip() -> None:
    source = """<m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
      <m:box><m:boxPr/><m:e><m:r><m:t>x</m:t></m:r></m:e></m:box>
    </m:oMath>"""
    math = MathEngine()

    ast = math.parse(source, "omml")
    exported = math.convert(ast.to_dict(), "ast", "omml")

    assert ast.attr("omml_kind") == "box"
    assert "<m:box>" in exported
    assert "<m:boxPr/>" in exported
    assert "<m:borderBox>" not in exported
    assert math.compare(source, "omml", exported, "omml").equivalent
    assert math.convert(ast.to_dict(), "ast", "unicodemath") == "▭(x)"


@pytest.mark.parametrize(
    "source,count",
    [
        (
            '<m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"><m:borderBox/></m:oMath>',
            0,
        ),
        (
            '<m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"><m:borderBox><m:e/><m:e/></m:borderBox></m:oMath>',
            2,
        ),
    ],
)
def test_malformed_omml_box_rejects_missing_or_duplicate_body(source: str, count: int) -> None:
    with pytest.raises(WordToolkitError) as error:
        MathEngine().parse(source, "omml")

    assert error.value.code is ErrorCode.EQUATION_INVALID
    assert error.value.details == {"element": "borderBox", "child": "e", "count": count}


def test_nested_and_empty_boxes_preserve_their_structure() -> None:
    math = MathEngine()
    nested = math.parse("▭(▭(x))", "unicodemath")
    nested_omml = math.convert(nested.to_dict(), "ast", "omml")
    empty = math.parse("▭()", "unicodemath")

    assert nested_omml.count("<m:borderBox>") == 2
    assert math.compare(nested.to_dict(), "ast", nested_omml, "omml").equivalent
    assert math.convert(empty.to_dict(), "ast", "latex") == r"\boxed{}"
    assert math.compare(empty.to_dict(), "ast", "▭()", "unicodemath").equivalent


@pytest.mark.parametrize("output_format", ["omml", "latex", "unicodemath"])
def test_unsupported_enclosure_notation_fails_instead_of_becoming_brackets(
    output_format: str,
) -> None:
    ast = {
        "kind": "enclosure",
        "children": [{"kind": "identifier", "value": "x"}],
        "attrs": {"notation": "circle"},
    }

    with pytest.raises(WordToolkitError) as error:
        MathEngine().convert(ast, "ast", output_format)

    assert error.value.code is ErrorCode.EQUATION_INVALID
    assert error.value.details == {"notation": "circle"}


@pytest.mark.parametrize(
    "source,command,symbol",
    [
        (r"\int_0^1 x", r"\int", "∫"),
        (r"\iint_D f(x,y)", r"\iint", "∬"),
        (r"\iiint_V f(x,y,z)", r"\iiint", "∭"),
        (r"\iiiint_W x", r"\iiiint", "⨌"),
        (r"\oint_C f(z)", r"\oint", "∮"),
        (r"\oiint_S x", r"\oiint", "∯"),
        (r"\oiiint_V x", r"\oiiint", "∰"),
    ],
)
def test_integral_families_remain_nary_across_every_format(
    source: str, command: str, symbol: str
) -> None:
    math = MathEngine()
    canonical = math.parse(source, "latex")
    exported_latex = math.convert(canonical.to_dict(), "ast", "latex")
    exported_unicode = math.convert(canonical.to_dict(), "ast", "unicodemath")
    exported_omml = math.convert(canonical.to_dict(), "ast", "omml")

    assert command in exported_latex
    assert exported_unicode.startswith(symbol)
    assert "<m:nary>" in exported_omml
    assert f'm:val="{symbol}"' in exported_omml
    for exported, output_format in (
        (exported_latex, "latex"),
        (exported_unicode, "unicodemath"),
        (exported_omml, "omml"),
    ):
        assert math.compare(canonical.to_dict(), "ast", exported, output_format).equivalent


def test_nary_omml_keeps_lower_upper_and_body_in_their_own_containers() -> None:
    math = MathEngine()
    omml = math.convert(r"\iiint_a^b x", "latex", "omml")
    parsed = math.parse(omml, "omml")

    assert omml.count("<m:sub>") == 1
    assert omml.count("<m:sup>") == 1
    assert omml.count("<m:e>") == 1
    assert parsed.kind == "nary"
    assert parsed.value == "∭"
    assert parsed.children[0].value == "x"
    assert parsed.children[1].value == "a"
    assert parsed.children[2].value == "b"


def test_explicit_unicodemath_multiple_integral_command_is_supported() -> None:
    math = MathEngine()
    parsed = math.parse(r"\iiint_(V) x", "unicodemath")

    assert parsed.kind == "nary"
    assert parsed.value == "∭"
    assert math.convert(parsed.to_dict(), "ast", "unicodemath") == "∭_(V) x"


def test_unicodemath_box_requires_a_parenthesized_body() -> None:
    with pytest.raises(WordToolkitError) as error:
        MathEngine().parse("▭x", "unicodemath")

    assert error.value.code is ErrorCode.EQUATION_INVALID


@pytest.mark.parametrize("source", [r"\boxed{x}", r"\unknown(x)"])
def test_unknown_unicodemath_commands_fail_instead_of_becoming_identifiers(source: str) -> None:
    with pytest.raises(WordToolkitError) as error:
        MathEngine().parse(source, "unicodemath")

    assert error.value.code is ErrorCode.EQUATION_INVALID


@pytest.mark.parametrize("source", ["∭_(a]^(b) f", "∫_(a)^(b] f"])
def test_mismatched_unicodemath_integral_bounds_fail(source: str) -> None:
    with pytest.raises(WordToolkitError) as error:
        MathEngine().parse(source, "unicodemath")

    assert error.value.code is ErrorCode.EQUATION_INVALID
