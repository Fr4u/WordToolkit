from __future__ import annotations

import pytest

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
