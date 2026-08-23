from __future__ import annotations

import re

from ..errors import ErrorCode, WordToolkitError
from .ast import EMPTY, EquationNode, row

GREEK = {
    "alpha": "α",
    "beta": "β",
    "gamma": "γ",
    "delta": "δ",
    "epsilon": "ε",
    "theta": "θ",
    "lambda": "λ",
    "mu": "μ",
    "nu": "ν",
    "xi": "ξ",
    "pi": "π",
    "rho": "ρ",
    "sigma": "σ",
    "tau": "τ",
    "phi": "φ",
    "chi": "χ",
    "psi": "ψ",
    "omega": "ω",
    "Gamma": "Γ",
    "Delta": "Δ",
    "Theta": "Θ",
    "Lambda": "Λ",
    "Pi": "Π",
    "Sigma": "Σ",
    "Phi": "Φ",
    "Psi": "Ψ",
    "Omega": "Ω",
}
SYMBOLS = {
    "sum": "∑",
    "prod": "∏",
    "int": "∫",
    "iint": "∬",
    "iiint": "∭",
    "iiiint": "⨌",
    "oint": "∮",
    "oiint": "∯",
    "oiiint": "∰",
    "bigcup": "⋃",
    "bigcap": "⋂",
    "infty": "∞",
    "partial": "∂",
    "nabla": "∇",
}
NARY_SYMBOLS = {"∑", "∏", "∫", "∬", "∭", "⨌", "∮", "∯", "∰", "⋃", "⋂"}
NARY_COMMANDS = {
    "\\sum",
    "\\prod",
    "\\int",
    "\\iint",
    "\\iiint",
    "\\iiiint",
    "\\oint",
    "\\oiint",
    "\\oiiint",
    "\\bigcup",
    "\\bigcap",
}
FUNCTIONS = {"sin", "cos", "tan", "cot", "sec", "csc", "log", "ln", "exp", "min", "max"}
ACCENTS = {"vec": "→", "hat": "^", "bar": "¯", "tilde": "~", "dot": "˙", "ddot": "¨"}
TOKEN_RE = re.compile(
    r'"(?:[^"\\]|\\.)*"|\\[A-Za-z]+|[A-Za-z][A-Za-z0-9]*|\d+(?:[.,]\d+)?|'
    r"[α-ωΑ-Ω∞∑∏∫∬∭∮∯∰⨌⋃⋂√∂∇→↦¯˙¨±∓▭‖]|<=|>=|!=|:=|[+\-−*/×·=<>^_(),;:@&|~\[\]{}]"
)


def _tokenize_unicode_math(source: str) -> list[str]:
    tokens: list[str] = []
    position = 0
    for match in TOKEN_RE.finditer(source):
        gap = source[position : match.start()]
        unsupported = next(
            (
                (position + index, character)
                for index, character in enumerate(gap)
                if not character.isspace()
            ),
            None,
        )
        if unsupported is not None:
            offset, character = unsupported
            raise WordToolkitError(
                ErrorCode.EQUATION_INVALID,
                "UnicodeMath contains an unsupported character",
                {
                    "offset": offset,
                    "character": character,
                    "codepoint": f"U+{ord(character):04X}",
                },
            )
        tokens.append(match.group(0))
        position = match.end()
    trailing = source[position:]
    unsupported = next(
        (
            (position + index, character)
            for index, character in enumerate(trailing)
            if not character.isspace()
        ),
        None,
    )
    if unsupported is not None:
        offset, character = unsupported
        raise WordToolkitError(
            ErrorCode.EQUATION_INVALID,
            "UnicodeMath contains an unsupported character",
            {
                "offset": offset,
                "character": character,
                "codepoint": f"U+{ord(character):04X}",
            },
        )
    return tokens


def parse_unicode_math(source: str) -> EquationNode:
    stripped = source.strip()
    matrix = _parse_matrix_form(stripped)
    if matrix is not None:
        return matrix
    tokens = _tokenize_unicode_math(source)
    parser = _Parser(tokens)
    node = parser.expression(stop=set())
    if parser.peek() is not None:
        raise WordToolkitError(
            ErrorCode.EQUATION_INVALID, "Unexpected UnicodeMath token", {"token": parser.peek()}
        )
    return node


def _parse_matrix_form(source: str) -> EquationNode | None:
    match = re.fullmatch(r"(\\?matrix|pmatrix|bmatrix)\((.*)\)", source, flags=re.S)
    if not match:
        eq = re.fullmatch(r"(\\?eqarray|cases)\((.*)\)", source, flags=re.S)
        if not eq:
            return None
        equations = EquationNode.make(
            "equations",
            children=[parse_unicode_math(part) for part in eq.group(2).split("@")],
        )
        if eq.group(1) == "cases":
            return EquationNode.make(
                "row", children=(EquationNode.make("operator", "{"), equations)
            )
        return equations
    rows = []
    for row_text in match.group(2).split("@"):
        cells = [
            EquationNode.make("cell", children=(parse_unicode_math(cell),))
            for cell in row_text.split("&")
        ]
        rows.append(EquationNode.make("matrix_row", children=cells))
    matrix = EquationNode.make("matrix", children=rows)
    if match.group(1) == "pmatrix":
        return EquationNode.make("delimiter", children=(matrix,), begin="(", end=")")
    if match.group(1) == "bmatrix":
        return EquationNode.make("delimiter", children=(matrix,), begin="[", end="]")
    return matrix


class _Parser:
    def __init__(self, tokens: list[str]):
        self.tokens = tokens
        self.pos = 0

    def peek(self) -> str | None:
        return self.tokens[self.pos] if self.pos < len(self.tokens) else None

    def take(self) -> str:
        token = self.peek()
        if token is None:
            raise WordToolkitError(ErrorCode.EQUATION_INVALID, "Unexpected end of equation")
        self.pos += 1
        return token

    def expression(self, stop: set[str], min_precedence: int = 0) -> EquationNode:
        left = self.primary(stop)
        while (token := self.peek()) is not None and token not in stop:
            precedence = {
                "=": 1,
                "<": 1,
                ">": 1,
                "<=": 1,
                ">=": 1,
                "!=": 1,
                "+": 2,
                "-": 2,
                "−": 2,
                "*": 3,
                "×": 3,
                "·": 3,
                "/": 3,
                ",": 0,
                ";": 0,
                ":": 0,
            }.get(token)
            if precedence is None:
                if token in {"^", "_"}:
                    left = self._script(left)
                    continue
                if token in {"@", "&", ")", "]", "}"}:
                    break
                right = self.primary(stop)
                left = row(left, right)
                continue
            if precedence < min_precedence:
                break
            operator = self.take()
            right = self.expression(stop, precedence + 1)
            if operator == "/":
                if (
                    left.kind == "delimiter"
                    and left.attr("begin") == "("
                    and left.attr("end") == ")"
                ):
                    left = left.children[0]
                if (
                    right.kind == "delimiter"
                    and right.attr("begin") == "("
                    and right.attr("end") == ")"
                ):
                    right = right.children[0]
                left = EquationNode.make("fraction", children=(left, right))
            else:
                left = row(left, EquationNode.make("operator", operator), right)
        return left

    def primary(self, stop: set[str], *, consume_scripts: bool = True) -> EquationNode:
        token = self.take()
        if token in {"(", "[", "{", "|", "‖"}:
            close = {"(": ")", "[": "]", "{": "}", "|": "|", "‖": "‖"}[token]
            if self.peek() == close:
                self.take()
                body = EMPTY
            else:
                body = self.expression({close})
                if self.take() != close:
                    raise WordToolkitError(ErrorCode.EQUATION_INVALID, "Mismatched delimiter")
            node = EquationNode.make("delimiter", children=(body,), begin=token, end=close)
        elif token in {"√", "\\sqrt", "sqrt"}:
            if self.peek() == "(" or self.peek() == "{":
                opening = self.take()
                close = ")" if opening == "(" else "}"
                first = self.expression({"&", close})
                if self.peek() == "&":
                    self.take()
                    body = self.expression({close})
                    degree = first
                else:
                    body, degree = first, EMPTY
                if self.take() != close:
                    raise WordToolkitError(ErrorCode.EQUATION_INVALID, "Mismatched radical")
                node = EquationNode.make(
                    "radical", children=(body,) if degree == EMPTY else (body, degree)
                )
            else:
                node = EquationNode.make("radical", children=(self.primary(stop),))
        elif token in NARY_SYMBOLS or token in NARY_COMMANDS:
            symbol = SYMBOLS.get(token.lstrip("\\"), token)
            lower, upper = EMPTY, EMPTY
            while self.peek() in {"_", "^"}:
                marker = self.take()
                value = self.primary(stop | {"_", "^"}, consume_scripts=False)
                value = value.children[0] if value.kind == "delimiter" else value
                lower, upper = (value, upper) if marker == "_" else (lower, value)
            body = (
                self.primary(stop) if self.peek() not in stop and self.peek() is not None else EMPTY
            )
            node = EquationNode.make("nary", symbol, (body, lower, upper))
        elif token == "▭":
            if self.peek() not in {"(", "{"}:
                raise WordToolkitError(
                    ErrorCode.EQUATION_INVALID,
                    "UnicodeMath boxed formula requires a parenthesized body",
                )
            body = self.primary(stop, consume_scripts=False)
            body = body.children[0] if body.kind == "delimiter" else body
            node = EquationNode.make("enclosure", children=(body,), notation="box")
        elif token in {"lim", "\\lim"}:
            base = EquationNode.make("identifier", "lim")
            if self.peek() == "_":
                self.take()
                lower = self.primary(stop, consume_scripts=False)
                lower = lower.children[0] if lower.kind == "delimiter" else lower
                node = EquationNode.make("limit_lower", children=(base, lower))
            else:
                node = base
        elif token in ACCENTS and self.peek() in {"(", "{"}:
            argument = self.primary(stop, consume_scripts=False)
            argument = argument.children[0] if argument.kind == "delimiter" else argument
            node = EquationNode.make("accent", ACCENTS[token], (argument,))
        elif token in {"text", "\\text"} and self.peek() in {"(", "{"}:
            opening = self.take()
            close = ")" if opening == "(" else "}"
            value_token = self.take()
            if not (value_token.startswith('"') and value_token.endswith('"')):
                raise WordToolkitError(
                    ErrorCode.EQUATION_INVALID, "UnicodeMath text() requires a quoted string"
                )
            if self.take() != close:
                raise WordToolkitError(ErrorCode.EQUATION_INVALID, "Mismatched text delimiter")
            node = EquationNode.make("text", value_token[1:-1].replace(r"\"", '"'))
        elif token.startswith('"') and token.endswith('"'):
            node = EquationNode.make("text", token[1:-1].replace(r"\"", '"'))
        elif token.startswith("\\"):
            name = token[1:]
            symbol_value = GREEK.get(name, SYMBOLS.get(name))
            if symbol_value is None:
                raise WordToolkitError(
                    ErrorCode.EQUATION_INVALID,
                    "Unsupported UnicodeMath command",
                    {"command": token},
                )
            node = EquationNode.make("identifier", symbol_value)
        elif re.fullmatch(r"\d+(?:[.,]\d+)?", token):
            node = EquationNode.make("number", token)
        elif token in GREEK:
            node = EquationNode.make("identifier", GREEK[token])
        elif token in FUNCTIONS and self.peek() in {"(", "{"}:
            argument = self.primary(stop)
            argument = argument.children[0] if argument.kind == "delimiter" else argument
            node = EquationNode.make(
                "function", children=(EquationNode.make("identifier", token), argument)
            )
        elif token in {"+", "-", "−", "±", "∓"}:
            node = row(EquationNode.make("operator", token), self.primary(stop))
        elif token in {"→", "↦"}:
            node = EquationNode.make("operator", token)
        else:
            node = EquationNode.make("identifier", token)
        while consume_scripts and self.peek() in {"^", "_"}:
            node = self._script(node)
        return node

    def _script(self, base: EquationNode) -> EquationNode:
        marker = self.take()
        value = self.primary({")", "]", "}", ",", ";", "^", "_"}, consume_scripts=False)
        value = value.children[0] if value.kind == "delimiter" else value
        if marker == "^":
            if base.kind == "subscript":
                return EquationNode.make(
                    "sub_sup", children=(base.children[0], base.children[1], value)
                )
            return EquationNode.make("superscript", children=(base, value))
        if base.kind == "superscript":
            return EquationNode.make(
                "sub_sup", children=(base.children[0], value, base.children[1])
            )
        return EquationNode.make("subscript", children=(base, value))


def to_unicode_math(node: EquationNode) -> str:
    c = node.children
    if node.kind == "row":
        if (
            len(c) == 2
            and c[0].kind == "operator"
            and c[0].value == "{"
            and c[1].kind == "equations"
        ):
            return "cases(" + "@".join(to_unicode_math(item) for item in c[1].children) + ")"
        values = []
        for index, child in enumerate(c):
            value = to_unicode_math(child)
            if (
                child.kind == "fraction"
                and index > 0
                and c[index - 1].kind in {"limit_lower", "limit_upper"}
            ):
                value = f"({value})"
            if index > 0 and c[index - 1].kind == "identifier":
                starts_with_identifier = child.kind == "identifier" or (
                    child.kind in {"subscript", "superscript", "sub_sup"}
                    and child.children[0].kind == "identifier"
                )
                if starts_with_identifier:
                    value = " " + value
            values.append(value)
        return "".join(values)
    if node.kind in {"identifier", "number", "operator"}:
        return node.value
    if node.kind == "text":
        return 'text("' + node.value.replace('"', r"\"") + '")'
    if node.kind == "fraction":
        return f"({to_unicode_math(c[0])})/({to_unicode_math(c[1])})"
    if node.kind == "superscript":
        return f"{to_unicode_math(c[0])}^({to_unicode_math(c[1])})"
    if node.kind == "subscript":
        return f"{to_unicode_math(c[0])}_({to_unicode_math(c[1])})"
    if node.kind == "sub_sup":
        return f"{to_unicode_math(c[0])}_({to_unicode_math(c[1])})^({to_unicode_math(c[2])})"
    if node.kind == "radical":
        return (
            f"√({to_unicode_math(c[0])})"
            if len(c) == 1
            else f"√({to_unicode_math(c[1])}&{to_unicode_math(c[0])})"
        )
    if node.kind == "nary":
        lower = f"_({to_unicode_math(c[1])})" if c[1].children or c[1].value else ""
        upper = f"^({to_unicode_math(c[2])})" if c[2].children or c[2].value else ""
        body = to_unicode_math(c[0])
        return f"{node.value}{lower}{upper}{f' {body}' if body else ''}"
    if node.kind == "delimiter":
        body = to_unicode_math(c[0])
        if c[0].kind == "matrix" and node.attr("begin") == "(" and node.attr("end") == ")":
            return "p" + body
        if c[0].kind == "matrix" and node.attr("begin") == "[" and node.attr("end") == "]":
            return "b" + body
        return f"{node.attr('begin', '(')}{to_unicode_math(c[0])}{node.attr('end', ')')}"
    if node.kind == "matrix":
        return (
            "matrix("
            + "@".join(
                "&".join(to_unicode_math(cell.children[0]) for cell in r.children) for r in c
            )
            + ")"
        )
    if node.kind == "equations":
        return "eqarray(" + "@".join(to_unicode_math(x) for x in c) + ")"
    if node.kind == "accent":
        command = {"→": "vec", "¯": "bar", "^": "hat", "~": "tilde", "˙": "dot", "¨": "ddot"}.get(
            node.value, "hat"
        )
        return f"{command}({to_unicode_math(c[0])})"
    if node.kind == "limit_lower":
        return f"{to_unicode_math(c[0])}_({to_unicode_math(c[1])})"
    if node.kind == "function":
        return f"{to_unicode_math(c[0])}({to_unicode_math(c[1])})"
    if node.kind == "enclosure":
        notation = node.attr("notation", "box")
        if notation == "box":
            return f"▭({to_unicode_math(c[0])})"
        raise WordToolkitError(
            ErrorCode.EQUATION_INVALID,
            "UnicodeMath export does not support this enclosure notation",
            {"notation": notation},
        )
    return node.value
