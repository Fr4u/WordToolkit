from __future__ import annotations

from html import escape

from lxml import etree

from ..errors import ErrorCode, WordToolkitError
from ..security import parse_xml_bytes
from .ast import EMPTY, EquationNode, row

FUNCTION_NAMES = {"sin", "cos", "tan", "cot", "sec", "csc", "log", "ln", "exp", "min", "max"}
FENCE_PAIRS = {"(": ")", "[": "]", "{": "}", "|": "|", "‖": "‖"}


def _local(element: etree._Element) -> str:
    return etree.QName(element).localname


def _elements(element: etree._Element) -> list[etree._Element]:
    return [child for child in element if isinstance(child.tag, str)]


def _text(element: etree._Element) -> str:
    return "".join(element.itertext()).strip()


def parse_mathml(source: str) -> EquationNode:
    root = parse_xml_bytes(source.encode("utf-8"), part="equation.mathml")
    if _local(root) != "math":
        raise WordToolkitError(
            ErrorCode.EQUATION_INVALID, "Presentation MathML must have a math root"
        )
    children = _elements(root)
    semantics = next((x for x in children if _local(x) == "semantics"), None)
    if semantics is not None:
        children = [
            x for x in _elements(semantics) if _local(x) not in {"annotation", "annotation-xml"}
        ][:1]
    return _parse_sequence(children)


def _parse_sequence(children: list[etree._Element]) -> EquationNode:
    output: list[EquationNode] = []
    index = 0
    while index < len(children):
        element = children[index]
        node = _parse_node(element)
        if (
            node.kind == "nary"
            and node.children
            and node.children[0] == EMPTY
            and index + 1 < len(children)
        ):
            body = _parse_node(children[index + 1])
            node = EquationNode.make(
                "nary", node.value, (body, *node.children[1:]), **dict(node.attrs)
            )
            index += 1
        if node.kind != "empty" and not (node.kind == "operator" and not node.value):
            output.append(node)
        index += 1
    combined: list[EquationNode] = []
    index = 0
    while index < len(output):
        node = output[index]
        if node.value in FUNCTION_NAMES and index + 1 < len(output):
            argument = output[index + 1]
            if (
                argument.kind == "delimiter"
                and argument.attr("begin") == "("
                and argument.attr("end") == ")"
            ):
                argument = argument.children[0]
            combined.append(EquationNode.make("function", children=(node, argument)))
            index += 2
            continue
        combined.append(node)
        index += 1
    if (
        len(combined) >= 3
        and combined[0].value in FENCE_PAIRS
        and combined[-1].value == FENCE_PAIRS[combined[0].value]
    ):
        return EquationNode.make(
            "delimiter",
            children=(row(*combined[1:-1]),),
            begin=combined[0].value,
            end=combined[-1].value,
        )
    return row(*combined)


def _parse_node(element: etree._Element) -> EquationNode:
    tag = _local(element)
    children = _elements(element)
    if tag in {"math", "mrow", "mstyle", "mpadded", "mphantom", "semantics"}:
        return _parse_sequence(
            [x for x in children if _local(x) not in {"annotation", "annotation-xml"}]
        )
    if tag == "mi":
        return EquationNode.make("identifier", _text(element))
    if tag == "mn":
        return EquationNode.make("number", _text(element))
    if tag == "mo":
        value = _text(element)
        return EquationNode.make("operator", "" if value in {"\u2061", "\u2062"} else value)
    if tag == "mtext":
        return EquationNode.make("text", _text(element))
    if tag in {"mspace", "none", "maligngroup", "malignmark"}:
        return EquationNode.make("empty")
    if tag == "mfrac" and len(children) >= 2:
        return EquationNode.make(
            "fraction", children=(_parse_node(children[0]), _parse_node(children[1]))
        )
    if tag == "msup" and len(children) >= 2:
        return EquationNode.make(
            "superscript", children=(_parse_node(children[0]), _parse_node(children[1]))
        )
    if tag == "msub" and len(children) >= 2:
        base, sub = _parse_node(children[0]), _parse_node(children[1])
        if base.value == "lim":
            return EquationNode.make("limit_lower", children=(base, sub))
        return EquationNode.make("subscript", children=(base, sub))
    if tag == "msubsup" and len(children) >= 3:
        base, sub, sup = (_parse_node(x) for x in children[:3])
        if base.value in {"∑", "∏", "∫", "⋃", "⋂"}:
            return EquationNode.make("nary", base.value, (EMPTY, sub, sup))
        return EquationNode.make("sub_sup", children=(base, sub, sup))
    if tag == "msqrt":
        return EquationNode.make("radical", children=(_parse_sequence(children),))
    if tag == "mroot" and len(children) >= 2:
        return EquationNode.make(
            "radical", children=(_parse_node(children[0]), _parse_node(children[1]))
        )
    if tag in {"munderover", "munder", "mover"} and children:
        base = _parse_node(children[0])
        lower = _parse_node(children[1]) if tag != "mover" and len(children) > 1 else EMPTY
        upper_index = 2 if tag == "munderover" else 1
        upper = (
            _parse_node(children[upper_index])
            if tag != "munder" and len(children) > upper_index
            else EMPTY
        )
        if base.value in {"∑", "∏", "∫", "⋃", "⋂"}:
            return EquationNode.make("nary", base.value, (EMPTY, lower, upper))
        if base.value == "lim" and tag == "munder":
            return EquationNode.make("limit_lower", children=(base, lower))
        if tag == "mover" and upper.value in {"¯", "→", "^", "~", "˙", "¨"}:
            return EquationNode.make("accent", upper.value, (base,))
        return EquationNode.make("sub_sup", children=(base, lower, upper))
    if tag == "mfenced":
        return EquationNode.make(
            "delimiter",
            children=(_parse_sequence(children),),
            begin=element.get("open", "("),
            end=element.get("close", ")"),
        )
    if tag == "mtable":
        rows: list[EquationNode] = []
        for tr in children:
            if _local(tr) not in {"mtr", "mlabeledtr"}:
                continue
            cells = [
                EquationNode.make("cell", children=(_parse_sequence(_elements(td)),))
                for td in _elements(tr)
                if _local(td) == "mtd"
            ]
            rows.append(EquationNode.make("matrix_row", children=cells))
        if element.get("columnalign") == "left" and all(len(item.children) == 1 for item in rows):
            return EquationNode.make(
                "equations", children=(item.children[0].children[0] for item in rows)
            )
        return EquationNode.make("matrix", children=rows)
    if tag == "menclose":
        notation = element.get("notation", "box")
        return EquationNode.make(
            "enclosure", children=(_parse_sequence(children),), notation=notation
        )
    return _parse_sequence(children) if children else EquationNode.make("text", _text(element))


def to_mathml(node: EquationNode, *, display: bool = False) -> str:
    body = _emit(node)
    display_attr = ' display="block"' if display else ""
    return f'<math xmlns="http://www.w3.org/1998/Math/MathML"{display_attr}>{body}</math>'


def _emit(node: EquationNode) -> str:
    c = node.children
    if node.kind == "row":
        return "<mrow>" + "".join(_emit(x) for x in c) + "</mrow>"
    if node.kind == "identifier":
        return f"<mi>{escape(node.value)}</mi>"
    if node.kind == "number":
        return f"<mn>{escape(node.value)}</mn>"
    if node.kind == "operator":
        return f"<mo>{escape(node.value)}</mo>" if node.value else ""
    if node.kind == "text":
        return f"<mtext>{escape(node.value)}</mtext>"
    if node.kind == "fraction":
        return f"<mfrac>{_emit(c[0])}{_emit(c[1])}</mfrac>"
    if node.kind == "superscript":
        return f"<msup>{_emit(c[0])}{_emit(c[1])}</msup>"
    if node.kind == "subscript":
        return f"<msub>{_emit(c[0])}{_emit(c[1])}</msub>"
    if node.kind == "sub_sup":
        return f"<msubsup>{_emit(c[0])}{_emit(c[1])}{_emit(c[2])}</msubsup>"
    if node.kind == "radical":
        return (
            f"<msqrt>{_emit(c[0])}</msqrt>"
            if len(c) == 1
            else f"<mroot>{_emit(c[0])}{_emit(c[1])}</mroot>"
        )
    if node.kind == "nary":
        base = f"<mo>{escape(node.value)}</mo>"
        decorated = f"<msubsup>{base}{_emit(c[1])}{_emit(c[2])}</msubsup>"
        return decorated + _emit(c[0])
    if node.kind == "delimiter":
        return f'<mfenced open="{escape(node.attr("begin", "("))}" close="{escape(node.attr("end", ")"))}">{_emit(c[0])}</mfenced>'
    if node.kind == "matrix":
        return (
            "<mtable>"
            + "".join(
                "<mtr>"
                + "".join(f"<mtd>{_emit(cell.children[0])}</mtd>" for cell in matrix_row.children)
                + "</mtr>"
                for matrix_row in c
            )
            + "</mtable>"
        )
    if node.kind == "equations":
        return (
            '<mtable columnalign="left">'
            + "".join(f"<mtr><mtd>{_emit(x)}</mtd></mtr>" for x in c)
            + "</mtable>"
        )
    if node.kind == "accent":
        return f'<mover accent="true">{_emit(c[0])}<mo>{escape(node.value)}</mo></mover>'
    if node.kind == "limit_lower":
        return f"<munder>{_emit(c[0])}{_emit(c[1])}</munder>"
    if node.kind == "function":
        return f"<mrow>{_emit(c[0])}<mo>\u2061</mo>{_emit(c[1])}</mrow>"
    if node.kind == "enclosure":
        return (
            f'<menclose notation="{escape(node.attr("notation", "box"))}">{_emit(c[0])}</menclose>'
        )
    if node.kind == "cell":
        return _emit(c[0])
    return escape(node.value)
