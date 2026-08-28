from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Literal

from ..errors import ErrorCode, WordToolkitError
from .ast import EquationNode
from .latex import parse_latex, to_latex
from .limits import MAX_EQUATION_NESTING_DEPTH, MAX_EQUATION_SOURCE_CHARACTERS
from .mathml import parse_mathml, to_mathml
from .omml import omml_string, parse_omml, to_omml
from .unicode_math import parse_unicode_math, to_unicode_math

EquationFormat = Literal["latex", "unicodemath", "mathml", "omml", "ast"]
ARITY: dict[str, tuple[int, int | None]] = {
    "row": (0, None),
    "empty": (0, 0),
    "identifier": (0, 0),
    "number": (0, 0),
    "operator": (0, 0),
    "text": (0, 0),
    "fraction": (2, 2),
    "superscript": (2, 2),
    "subscript": (2, 2),
    "sub_sup": (3, 3),
    "radical": (1, 2),
    "nary": (3, 3),
    "delimiter": (1, 1),
    "matrix": (1, None),
    "matrix_row": (1, None),
    "cell": (1, 1),
    "equations": (1, None),
    "accent": (1, 1),
    "limit_lower": (2, 2),
    "limit_upper": (2, 2),
    "function": (2, 2),
    "enclosure": (1, 1),
    "phantom": (1, 1),
    "prescript": (3, 3),
}


@dataclass(frozen=True, slots=True)
class SemanticComparison:
    equivalent: bool
    left_canonical: dict[str, Any]
    right_canonical: dict[str, Any]


class MathEngine:
    def parse(self, value: str | dict[str, Any], input_format: EquationFormat) -> EquationNode:
        try:
            return self._parse(value, input_format)
        except RecursionError as exc:
            raise self._nesting_error(input_format) from exc

    def _parse(self, value: str | dict[str, Any], input_format: EquationFormat) -> EquationNode:
        if input_format == "ast":
            if not isinstance(value, dict):
                raise ValueError("AST input must be an object")
            node = EquationNode.from_dict(value)
        else:
            if not isinstance(value, str):
                raise ValueError(f"{input_format} input must be a string")
            if not value.strip():
                raise WordToolkitError(
                    ErrorCode.EQUATION_INVALID,
                    "Equation input is empty",
                    {"input_format": input_format},
                )
            if len(value) > MAX_EQUATION_SOURCE_CHARACTERS:
                raise WordToolkitError(
                    ErrorCode.LIMIT_EXCEEDED,
                    "Equation input exceeds 100,000 characters",
                    {
                        "input_format": input_format,
                        "characters": len(value),
                        "maximum_characters": MAX_EQUATION_SOURCE_CHARACTERS,
                    },
                )
            parsers = {
                "latex": parse_latex,
                "unicodemath": parse_unicode_math,
                "mathml": parse_mathml,
                "omml": parse_omml,
            }
            node = parsers[input_format](value)
        return self._canonicalize(node, depth=1, input_format=input_format)

    def convert(
        self,
        value: str | dict[str, Any],
        input_format: EquationFormat,
        output_format: EquationFormat,
        *,
        display: bool = False,
    ) -> str | dict[str, Any]:
        node = self.parse(value, input_format)
        if output_format == "ast":
            return node.to_dict()
        if output_format == "omml":
            return omml_string(node, display=display)
        if output_format == "mathml":
            return to_mathml(node, display=display)
        if output_format == "latex":
            return to_latex(node)
        return to_unicode_math(node)

    def omml_element(
        self, value: str | dict[str, Any], input_format: EquationFormat, *, display: bool
    ):
        return to_omml(self.parse(value, input_format), display=display)

    def compare(
        self,
        left: str | dict[str, Any],
        left_format: EquationFormat,
        right: str | dict[str, Any],
        right_format: EquationFormat,
    ) -> SemanticComparison:
        left_node = self.parse(left, left_format)
        right_node = self.parse(right, right_format)
        return SemanticComparison(
            equivalent=left_node == right_node,
            left_canonical=left_node.to_dict(),
            right_canonical=right_node.to_dict(),
        )

    def canonicalize(self, node: EquationNode) -> EquationNode:
        try:
            return self._canonicalize(node, depth=1, input_format="ast")
        except RecursionError as exc:
            raise self._nesting_error("ast") from exc

    def _canonicalize(
        self, node: EquationNode, *, depth: int, input_format: EquationFormat
    ) -> EquationNode:
        if depth > MAX_EQUATION_NESTING_DEPTH:
            raise self._nesting_error(input_format)
        if node.kind not in ARITY:
            raise ValueError(f"Unsupported equation AST kind: {node.kind}")
        minimum, maximum = ARITY[node.kind]
        if len(node.children) < minimum or (maximum is not None and len(node.children) > maximum):
            raise ValueError(
                f"Equation AST kind {node.kind!r} requires "
                f"{minimum if minimum == maximum else f'{minimum}..{maximum or "many"}'} children"
            )
        if len(node.value) > 100_000:
            raise ValueError("Equation AST node value exceeds the limit")
        children = tuple(
            self._canonicalize(child, depth=depth + 1, input_format=input_format)
            for child in node.children
        )
        value = node.value
        if node.kind == "operator" and value == "−":
            value = "-"
        if node.kind == "function":
            function_name = EquationNode.make("identifier", children[0].value)
            argument = children[1]
            if (
                argument.kind == "delimiter"
                and argument.attr("begin") == "("
                and argument.attr("end") == ")"
            ):
                argument = argument.children[0]
            return EquationNode.make("function", children=(function_name, argument))
        if node.kind in {"limit_lower", "limit_upper"} and children[0].value == "lim":
            return EquationNode.make(
                node.kind,
                children=(EquationNode.make("identifier", "lim"), children[1]),
            )
        if node.kind == "row":
            flat: list[EquationNode] = []
            for child in children:
                if child.kind == "row":
                    flat.extend(child.children)
                elif child.kind != "empty" and not (child.kind == "operator" and not child.value):
                    flat.append(child)
            for index in range(1, len(flat)):
                if (
                    flat[index - 1].kind in {"limit_lower", "limit_upper"}
                    and flat[index].kind == "delimiter"
                    and flat[index].children[0].kind == "fraction"
                    and flat[index].attr("begin") == "("
                    and flat[index].attr("end") == ")"
                ):
                    flat[index] = flat[index].children[0]
            if len(flat) == 1:
                return flat[0]
            return EquationNode.make("row", children=flat)
        return EquationNode(
            node.kind,
            value.strip() if node.kind != "text" else value,
            children,
            tuple(sorted(node.attrs)),
        )

    @staticmethod
    def _nesting_error(input_format: EquationFormat) -> WordToolkitError:
        return WordToolkitError(
            ErrorCode.LIMIT_EXCEEDED,
            "Equation nesting exceeds 128 levels",
            {
                "input_format": input_format,
                "maximum_nesting_depth": MAX_EQUATION_NESTING_DEPTH,
            },
        )
