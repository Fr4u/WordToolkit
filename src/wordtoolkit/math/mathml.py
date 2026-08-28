from __future__ import annotations

from html import escape

from lxml import etree

from ..errors import ErrorCode, WordToolkitError
from ..security import parse_xml_bytes
from .ast import EMPTY, EquationNode, row
from .limits import MAX_EQUATION_NESTING_DEPTH

FUNCTION_NAMES = {"sin", "cos", "tan", "cot", "sec", "csc", "log", "ln", "exp", "min", "max"}
FENCE_PAIRS = {"(": ")", "[": "]", "{": "}", "|": "|", "‖": "‖"}
NARY_SYMBOLS = {"∑", "∏", "∫", "∬", "∭", "⨌", "∮", "∯", "∰", "⋃", "⋂"}
MATHML_NS = "http://www.w3.org/1998/Math/MathML"
SUPPORTED_MATHML_TAGS = {
    "math",
    "mrow",
    "mstyle",
    "mpadded",
    "mphantom",
    "semantics",
    "annotation",
    "annotation-xml",
    "mi",
    "mn",
    "mo",
    "mtext",
    "mspace",
    "none",
    "maligngroup",
    "malignmark",
    "mfrac",
    "msup",
    "msub",
    "msubsup",
    "mmultiscripts",
    "mprescripts",
    "msqrt",
    "mroot",
    "munderover",
    "munder",
    "mover",
    "mfenced",
    "mtable",
    "mtr",
    "mtd",
    "menclose",
}


def _local(element: etree._Element) -> str:
    return etree.QName(element).localname


def _elements(element: etree._Element) -> list[etree._Element]:
    return [child for child in element if isinstance(child.tag, str)]


def _text(element: etree._Element) -> str:
    return "".join(element.itertext()).strip()


def _validate_mathml_tree(root: etree._Element) -> None:
    pending = [(root, 0)]
    while pending:
        element, depth = pending.pop()
        if depth > MAX_EQUATION_NESTING_DEPTH:
            raise WordToolkitError(
                ErrorCode.LIMIT_EXCEEDED,
                "Equation nesting exceeds 128 levels",
                {
                    "input_format": "mathml",
                    "maximum_nesting_depth": MAX_EQUATION_NESTING_DEPTH,
                },
            )
        name = etree.QName(element)
        if name.namespace != MATHML_NS:
            raise WordToolkitError(
                ErrorCode.EQUATION_INVALID,
                "Presentation MathML contains an element outside the MathML namespace",
                {"element": name.localname, "namespace": name.namespace or ""},
            )
        if name.localname not in SUPPORTED_MATHML_TAGS:
            raise WordToolkitError(
                ErrorCode.EQUATION_INVALID,
                "Presentation MathML contains an unsupported element",
                {"element": name.localname},
            )
        if name.localname not in {"annotation", "annotation-xml"}:
            pending.extend((child, depth + 1) for child in reversed(_elements(element)))


def _require_child_count(
    element: etree._Element, children: list[etree._Element], expected: int
) -> None:
    if len(children) != expected:
        raise WordToolkitError(
            ErrorCode.EQUATION_INVALID,
            "Presentation MathML element has an invalid operand count",
            {"element": _local(element), "expected": expected, "actual": len(children)},
        )


def _presentation_branch(semantics: etree._Element) -> etree._Element:
    presentation = [
        child
        for child in _elements(semantics)
        if _local(child) not in {"annotation", "annotation-xml"}
    ]
    if len(presentation) != 1:
        raise WordToolkitError(
            ErrorCode.EQUATION_INVALID,
            "MathML semantics requires exactly one presentation branch",
            {"actual": len(presentation)},
        )
    return presentation[0]


def _group_fenced_sequences(nodes: list[EquationNode]) -> list[EquationNode]:
    """Turn balanced MathML fence operators into delimiter AST nodes."""
    frames: list[tuple[EquationNode | None, list[EquationNode]]] = [(None, [])]
    closing = set(FENCE_PAIRS.values())
    for node in nodes:
        value = node.value if node.kind == "operator" else ""
        opener = frames[-1][0]
        expected = FENCE_PAIRS.get(opener.value) if opener is not None else None
        if value and value == expected:
            assert opener is not None
            _, contents = frames.pop()
            frames[-1][1].append(
                EquationNode.make(
                    "delimiter",
                    children=(row(*contents),),
                    begin=opener.value,
                    end=value,
                )
            )
        elif value in FENCE_PAIRS and (value not in {"|", "‖"} or value != expected):
            frames.append((node, []))
        elif value in closing:
            frames[-1][1].append(node)
        else:
            frames[-1][1].append(node)
    while len(frames) > 1:
        opener, contents = frames.pop()
        if opener is not None:
            frames[-1][1].extend((opener, *contents))
    return frames[0][1]


def parse_mathml(source: str) -> EquationNode:
    root = parse_xml_bytes(source.encode("utf-8"), part="equation.mathml")
    if etree.QName(root).namespace != MATHML_NS or _local(root) != "math":
        raise WordToolkitError(
            ErrorCode.EQUATION_INVALID,
            "Presentation MathML must have a math root in the MathML namespace",
        )
    _validate_mathml_tree(root)
    return _parse_sequence(_elements(root))


def _parse_sequence(children: list[etree._Element]) -> EquationNode:
    output: list[EquationNode] = []
    index = 0
    while index < len(children):
        element = children[index]
        node = _parse_node(element)
        if node.kind in {"identifier", "operator"} and node.value in NARY_SYMBOLS:
            node = EquationNode.make("nary", node.value, (EMPTY, EMPTY, EMPTY))
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
    return row(*_group_fenced_sequences(combined))


def _parse_node(element: etree._Element) -> EquationNode:
    tag = _local(element)
    children = _elements(element)
    if tag in {"math", "mrow", "mstyle", "mpadded"}:
        return _parse_sequence(
            [x for x in children if _local(x) not in {"annotation", "annotation-xml"}]
        )
    if tag == "mphantom":
        return EquationNode.make("phantom", children=(_parse_sequence(children),))
    if tag == "semantics":
        return _parse_node(_presentation_branch(element))
    if tag == "mi":
        _require_child_count(element, children, 0)
        return EquationNode.make("identifier", _text(element))
    if tag == "mn":
        _require_child_count(element, children, 0)
        return EquationNode.make("number", _text(element))
    if tag == "mo":
        _require_child_count(element, children, 0)
        value = _text(element)
        return EquationNode.make("operator", "" if value in {"\u2061", "\u2062"} else value)
    if tag == "mtext":
        _require_child_count(element, children, 0)
        return EquationNode.make("text", _text(element))
    if tag in {"mspace", "none", "maligngroup", "malignmark"}:
        _require_child_count(element, children, 0)
        return EquationNode.make("empty")
    if tag == "mfrac":
        _require_child_count(element, children, 2)
        return EquationNode.make(
            "fraction", children=(_parse_node(children[0]), _parse_node(children[1]))
        )
    if tag == "msup":
        _require_child_count(element, children, 2)
        base, sup = _parse_node(children[0]), _parse_node(children[1])
        if base.value in NARY_SYMBOLS:
            return EquationNode.make("nary", base.value, (EMPTY, EMPTY, sup))
        return EquationNode.make("superscript", children=(base, sup))
    if tag == "msub":
        _require_child_count(element, children, 2)
        base, sub = _parse_node(children[0]), _parse_node(children[1])
        if base.value == "lim":
            return EquationNode.make("limit_lower", children=(base, sub))
        if base.value in NARY_SYMBOLS:
            return EquationNode.make("nary", base.value, (EMPTY, sub, EMPTY))
        return EquationNode.make("subscript", children=(base, sub))
    if tag == "msubsup":
        _require_child_count(element, children, 3)
        base, sub, sup = (_parse_node(x) for x in children[:3])
        if base.value in NARY_SYMBOLS:
            return EquationNode.make("nary", base.value, (EMPTY, sub, sup))
        return EquationNode.make("sub_sup", children=(base, sub, sup))
    if tag == "mmultiscripts":
        if len(children) != 4 or _local(children[1]) != "mprescripts":
            raise WordToolkitError(
                ErrorCode.EQUATION_INVALID,
                "MathML mmultiscripts is supported only for one prescript pair",
                {"child_count": len(children)},
            )
        base = _parse_node(children[0])
        sub = _parse_node(children[2])
        sup = _parse_node(children[3])
        return EquationNode.make("prescript", children=(sub, sup, base))
    if tag == "msqrt":
        return EquationNode.make("radical", children=(_parse_sequence(children),))
    if tag == "mroot":
        _require_child_count(element, children, 2)
        return EquationNode.make(
            "radical", children=(_parse_node(children[0]), _parse_node(children[1]))
        )
    if tag in {"munderover", "munder", "mover"}:
        _require_child_count(element, children, 3 if tag == "munderover" else 2)
        base = _parse_node(children[0])
        lower = _parse_node(children[1]) if tag != "mover" and len(children) > 1 else EMPTY
        upper_index = 2 if tag == "munderover" else 1
        upper = (
            _parse_node(children[upper_index])
            if tag != "munder" and len(children) > upper_index
            else EMPTY
        )
        if base.value in NARY_SYMBOLS:
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
        if not children:
            raise WordToolkitError(
                ErrorCode.EQUATION_INVALID,
                "Empty MathML tables are not representable by the equation AST",
            )
        rows: list[EquationNode] = []
        for tr in children:
            if _local(tr) != "mtr":
                raise WordToolkitError(
                    ErrorCode.EQUATION_INVALID,
                    "MathML table contains a non-row element",
                    {"element": _local(tr)},
                )
            row_children = _elements(tr)
            if not row_children:
                raise WordToolkitError(
                    ErrorCode.EQUATION_INVALID,
                    "Empty MathML table rows are not representable by the equation AST",
                )
            if any(_local(item) != "mtd" for item in row_children):
                raise WordToolkitError(
                    ErrorCode.EQUATION_INVALID,
                    "MathML table row contains a non-cell element",
                    {"element": _local(tr), "child_count": len(row_children)},
                )
            cells = [
                EquationNode.make("cell", children=(_parse_sequence(_elements(td)),))
                for td in row_children
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
    raise WordToolkitError(
        ErrorCode.EQUATION_INVALID,
        "Presentation MathML element is unsupported in this position",
        {"element": tag},
    )


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
    if node.kind == "phantom":
        return f"<mphantom>{_emit(c[0])}</mphantom>"
    if node.kind == "prescript":
        return (
            f"<mmultiscripts>{_emit(c[2])}<mprescripts/>{_emit(c[0])}{_emit(c[1])}</mmultiscripts>"
        )
    if node.kind == "cell":
        return _emit(c[0])
    return escape(node.value)
