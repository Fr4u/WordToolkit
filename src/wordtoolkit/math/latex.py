from __future__ import annotations

import re

from latex2mathml.converter import convert as latex_to_mathml

from ..errors import ErrorCode, WordToolkitError
from .ast import EquationNode
from .mathml import parse_mathml

_MULTILINE_ENVIRONMENTS = {"align", "aligned", "gathered", "cases"}


def _split_top_level_rows(source: str) -> list[str]:
    """Split LaTeX ``\\\\`` rows without splitting commands nested in braces."""
    rows: list[str] = []
    start = 0
    depth = 0
    index = 0
    while index < len(source):
        char = source[index]
        if char == "{" and (index == 0 or source[index - 1] != "\\"):
            depth += 1
        elif char == "}" and (index == 0 or source[index - 1] != "\\"):
            depth = max(0, depth - 1)
        elif source.startswith("\\\\", index) and depth == 0:
            rows.append(source[start:index])
            index += 2
            start = index
            continue
        index += 1
    rows.append(source[start:])
    return [item.strip() for item in rows if item.strip()]


def _parse_multiline_environment(source: str) -> EquationNode | None:
    match = re.fullmatch(r"\s*\\begin\{([A-Za-z*]+)\}(.*)\\end\{\1\}\s*", source, flags=re.S)
    if match is None or match.group(1).rstrip("*") not in _MULTILINE_ENVIRONMENTS:
        return None
    environment = match.group(1).rstrip("*")
    rows = []
    for source_row in _split_top_level_rows(match.group(2)):
        cleaned = re.sub(r"(?<!\\)&", "", source_row).strip()
        rows.append(parse_latex(cleaned))
    if not rows:
        raise WordToolkitError(ErrorCode.EQUATION_INVALID, "LaTeX equation array is empty")
    equations = EquationNode.make("equations", children=rows)
    if environment == "cases":
        return EquationNode.make("row", children=(EquationNode.make("operator", "{"), equations))
    return equations


def parse_latex(source: str) -> EquationNode:
    multiline = _parse_multiline_environment(source)
    if multiline is not None:
        return multiline
    try:
        return parse_mathml(latex_to_mathml(source))
    except WordToolkitError:
        raise
    except Exception as exc:
        raise WordToolkitError(
            ErrorCode.EQUATION_INVALID, "LaTeX conversion failed", {"reason": str(exc)}
        ) from exc


def to_latex(node: EquationNode) -> str:
    c = node.children
    if node.kind == "row":
        if (
            len(c) == 2
            and c[0].kind == "operator"
            and c[0].value == "{"
            and c[1].kind == "equations"
        ):
            rows = r" \\ ".join(to_latex(item) for item in c[1].children)
            return "\\begin{cases}" + rows + "\\end{cases}"
        return " ".join(filter(None, (to_latex(x) for x in c)))
    if node.kind == "number":
        return node.value
    if node.kind == "operator":
        return {"−": "-", "→": r"\to", "↦": r"\mapsto", "≤": r"\le", "≥": r"\ge", "≠": r"\ne"}.get(
            node.value, node.value
        )
    if node.kind == "identifier":
        greek = {
            "α": "alpha",
            "β": "beta",
            "γ": "gamma",
            "δ": "delta",
            "θ": "theta",
            "λ": "lambda",
            "μ": "mu",
            "π": "pi",
            "σ": "sigma",
            "φ": "phi",
            "ω": "omega",
        }
        return "\\" + greek[node.value] if node.value in greek else node.value
    if node.kind == "text":
        return "\\text{" + node.value.replace("}", r"\}") + "}"
    if node.kind == "fraction":
        return f"\\frac{{{to_latex(c[0])}}}{{{to_latex(c[1])}}}"
    if node.kind == "superscript":
        return f"{{{to_latex(c[0])}}}^{{{to_latex(c[1])}}}"
    if node.kind == "subscript":
        return f"{{{to_latex(c[0])}}}_{{{to_latex(c[1])}}}"
    if node.kind == "sub_sup":
        return f"{{{to_latex(c[0])}}}_{{{to_latex(c[1])}}}^{{{to_latex(c[2])}}}"
    if node.kind == "radical":
        return (
            f"\\sqrt{{{to_latex(c[0])}}}"
            if len(c) == 1
            else f"\\sqrt[{to_latex(c[1])}]{{{to_latex(c[0])}}}"
        )
    if node.kind == "nary":
        op = {"∑": "sum", "∏": "prod", "∫": "int", "⋃": "bigcup", "⋂": "bigcap"}.get(
            node.value, node.value
        )
        lower = f"_{{{to_latex(c[1])}}}" if c[1].children or c[1].value else ""
        upper = f"^{{{to_latex(c[2])}}}" if c[2].children or c[2].value else ""
        body = to_latex(c[0])
        return f"\\{op}{lower}{upper}{f' {body}' if body else ''}"
    if node.kind == "delimiter":
        return f"\\left{node.attr('begin', '(')} {to_latex(c[0])} \\right{node.attr('end', ')')}"
    if node.kind == "matrix":
        matrix_rows = [
            " & ".join(to_latex(cell.children[0]) for cell in matrix_row.children)
            for matrix_row in c
        ]
        return "\\begin{matrix}" + r" \\ ".join(matrix_rows) + "\\end{matrix}"
    if node.kind == "equations":
        return "\\begin{aligned}" + r" \\ ".join(to_latex(x) for x in c) + "\\end{aligned}"
    if node.kind == "accent":
        command = {"→": "vec", "¯": "bar", "^": "hat", "~": "tilde", "˙": "dot", "¨": "ddot"}.get(
            node.value, "hat"
        )
        return f"\\{command}{{{to_latex(c[0])}}}"
    if node.kind == "limit_lower":
        base = r"\lim" if c[0].value == "lim" else to_latex(c[0])
        return f"{base}_{{{to_latex(c[1])}}}"
    if node.kind == "function":
        return f"\\operatorname{{{to_latex(c[0])}}}\\left({to_latex(c[1])}\\right)"
    return node.value
