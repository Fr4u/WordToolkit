from __future__ import annotations

from lxml import etree

from ..errors import ErrorCode, WordToolkitError
from ..security import parse_xml_bytes
from .ast import EMPTY, EquationNode, row

M_NS = "http://schemas.openxmlformats.org/officeDocument/2006/math"
W_NS = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
M = f"{{{M_NS}}}"
W = f"{{{W_NS}}}"
NSMAP = {"m": M_NS, "w": W_NS}


def _local(element: etree._Element) -> str:
    return etree.QName(element).localname


def _child(element: etree._Element, local: str) -> etree._Element | None:
    return next((x for x in element if isinstance(x.tag, str) and _local(x) == local), None)


def _required_single_child(element: etree._Element, local: str) -> etree._Element:
    children = [child for child in element if isinstance(child.tag, str) and _local(child) == local]
    if len(children) != 1:
        raise WordToolkitError(
            ErrorCode.EQUATION_INVALID,
            "OMML equation object requires exactly one child container",
            {"element": _local(element), "child": local, "count": len(children)},
        )
    return children[0]


def _contents(element: etree._Element | None) -> EquationNode:
    if element is None:
        return EMPTY
    return row(*(_parse_node(child) for child in element if isinstance(child.tag, str)))


def parse_omml(source: str) -> EquationNode:
    root = parse_xml_bytes(source.encode("utf-8"), part="equation.omml")
    if etree.QName(root).namespace != M_NS or _local(root) not in {"oMath", "oMathPara"}:
        raise WordToolkitError(
            ErrorCode.EQUATION_INVALID, "OMML root must be m:oMath or m:oMathPara"
        )
    if _local(root) == "oMathPara":
        omath = _child(root, "oMath")
        if omath is None:
            raise WordToolkitError(ErrorCode.EQUATION_INVALID, "m:oMathPara has no m:oMath")
        root = omath
    return _contents(root)


def _parse_node(element: etree._Element) -> EquationNode:
    tag = _local(element)
    if tag == "r":
        text = "".join(
            x.text or "" for x in element.iter() if isinstance(x.tag, str) and _local(x) == "t"
        )
        normal = any(_local(x) == "nor" for x in element.iter() if isinstance(x.tag, str))
        if normal:
            return EquationNode.make("text", text)
        if text.replace(".", "", 1).isdigit():
            return EquationNode.make("number", text)
        if text in "+−-=×÷±∓<≤>≥≈≠∈∉→↦,;:|()[]{}‖":
            return EquationNode.make("operator", text)
        return EquationNode.make("identifier", text)
    if tag == "f":
        return EquationNode.make(
            "fraction",
            children=(_contents(_child(element, "num")), _contents(_child(element, "den"))),
        )
    if tag == "sSup":
        return EquationNode.make(
            "superscript",
            children=(_contents(_child(element, "e")), _contents(_child(element, "sup"))),
        )
    if tag == "sSub":
        return EquationNode.make(
            "subscript",
            children=(_contents(_child(element, "e")), _contents(_child(element, "sub"))),
        )
    if tag == "sSubSup":
        return EquationNode.make(
            "sub_sup",
            children=(
                _contents(_child(element, "e")),
                _contents(_child(element, "sub")),
                _contents(_child(element, "sup")),
            ),
        )
    if tag == "rad":
        degree = _contents(_child(element, "deg"))
        body = _contents(_child(element, "e"))
        hidden = any(
            _local(x) == "degHide" and x.get(f"{M}val", "1") != "0"
            for x in element.iter()
            if isinstance(x.tag, str)
        )
        return EquationNode.make(
            "radical", children=(body,) if hidden or degree == EMPTY else (body, degree)
        )
    if tag == "nary":
        char = "∑"
        prop = _child(element, "naryPr")
        if prop is not None:
            char_el = _child(prop, "chr")
            if char_el is not None:
                char = char_el.get(f"{M}val", char)
        return EquationNode.make(
            "nary",
            char,
            (
                _contents(_child(element, "e")),
                _contents(_child(element, "sub")),
                _contents(_child(element, "sup")),
            ),
        )
    if tag == "d":
        begin, end = "(", ")"
        prop = _child(element, "dPr")
        if prop is not None:
            b, e = _child(prop, "begChr"), _child(prop, "endChr")
            begin = b.get(f"{M}val", begin) if b is not None else begin
            end = e.get(f"{M}val", end) if e is not None else end
        return EquationNode.make(
            "delimiter", children=(_contents(_child(element, "e")),), begin=begin, end=end
        )
    if tag in {"box", "borderBox"}:
        body = _contents(_required_single_child(element, "e"))
        attrs = {"notation": "box"}
        if tag == "box":
            # m:box is an OfficeMath grouping object, not a visible border box.
            # Preserve its source family for exact OMML roundtrips while the
            # less expressive LaTeX/UnicodeMath exports use the shared box glyph.
            attrs["omml_kind"] = "box"
        return EquationNode.make("enclosure", children=(body,), **attrs)
    if tag == "m":
        rows: list[EquationNode] = []
        for matrix_row in element:
            if not isinstance(matrix_row.tag, str) or _local(matrix_row) != "mr":
                continue
            cells = [
                EquationNode.make("cell", children=(_contents(cell),))
                for cell in matrix_row
                if isinstance(cell.tag, str) and _local(cell) == "e"
            ]
            rows.append(EquationNode.make("matrix_row", children=cells))
        return EquationNode.make("matrix", children=rows)
    if tag == "eqArr":
        return EquationNode.make(
            "equations",
            children=(_contents(x) for x in element if isinstance(x.tag, str) and _local(x) == "e"),
        )
    if tag == "acc":
        char = "^"
        prop = _child(element, "accPr")
        char_el = _child(prop, "chr") if prop is not None else None
        if char_el is not None:
            char = char_el.get(f"{M}val", char)
        return EquationNode.make("accent", char, (_contents(_child(element, "e")),))
    if tag in {"limLow", "limUpp"}:
        return EquationNode.make(
            "limit_lower" if tag == "limLow" else "limit_upper",
            children=(_contents(_child(element, "e")), _contents(_child(element, "lim"))),
        )
    if tag == "func":
        return EquationNode.make(
            "function",
            children=(_contents(_child(element, "fName")), _contents(_child(element, "e"))),
        )
    if tag in {"oMath", "oMathPara", "e", "num", "den", "sub", "sup", "deg", "fName", "lim"}:
        return _contents(element)
    return _contents(element)


def to_omml(node: EquationNode, *, display: bool) -> etree._Element:
    omath = etree.Element(f"{M}oMath", nsmap=NSMAP)
    _append(omath, node)
    if not display:
        return omath
    para = etree.Element(f"{M}oMathPara", nsmap=NSMAP)
    para.append(omath)
    return para


def omml_string(node: EquationNode, *, display: bool) -> str:
    return etree.tostring(to_omml(node, display=display), encoding="unicode")


def _container(parent: etree._Element, local: str, node: EquationNode) -> None:
    child = etree.SubElement(parent, f"{M}{local}")
    _append(child, node)


def _append(parent: etree._Element, node: EquationNode) -> None:
    c = node.children
    if node.kind == "row":
        for child in c:
            _append(parent, child)
        return
    if node.kind in {"identifier", "number", "operator", "text"}:
        if not node.value:
            return
        run = etree.SubElement(parent, f"{M}r")
        if node.kind == "text":
            props = etree.SubElement(run, f"{M}rPr")
            etree.SubElement(props, f"{M}nor")
        text = etree.SubElement(run, f"{M}t")
        text.text = node.value
        return
    if node.kind == "fraction":
        fraction = etree.SubElement(parent, f"{M}f")
        _container(fraction, "num", c[0])
        _container(fraction, "den", c[1])
        return
    if node.kind in {"superscript", "subscript", "sub_sup"}:
        tag = {"superscript": "sSup", "subscript": "sSub", "sub_sup": "sSubSup"}[node.kind]
        script = etree.SubElement(parent, f"{M}{tag}")
        _container(script, "e", c[0])
        if node.kind in {"subscript", "sub_sup"}:
            _container(script, "sub", c[1])
        if node.kind == "superscript":
            _container(script, "sup", c[1])
        elif node.kind == "sub_sup":
            _container(script, "sup", c[2])
        return
    if node.kind == "radical":
        radical = etree.SubElement(parent, f"{M}rad")
        props = etree.SubElement(radical, f"{M}radPr")
        if len(c) == 1:
            etree.SubElement(props, f"{M}degHide").set(f"{M}val", "1")
            etree.SubElement(radical, f"{M}deg")
        else:
            _container(radical, "deg", c[1])
        _container(radical, "e", c[0])
        return
    if node.kind == "nary":
        nary = etree.SubElement(parent, f"{M}nary")
        props = etree.SubElement(nary, f"{M}naryPr")
        etree.SubElement(props, f"{M}chr").set(f"{M}val", node.value or "∑")
        etree.SubElement(props, f"{M}limLoc").set(f"{M}val", "undOvr")
        _container(nary, "sub", c[1])
        _container(nary, "sup", c[2])
        _container(nary, "e", c[0])
        return
    if node.kind == "delimiter":
        delimiter = etree.SubElement(parent, f"{M}d")
        props = etree.SubElement(delimiter, f"{M}dPr")
        etree.SubElement(props, f"{M}begChr").set(f"{M}val", node.attr("begin", "("))
        etree.SubElement(props, f"{M}endChr").set(f"{M}val", node.attr("end", ")"))
        _container(delimiter, "e", c[0])
        return
    if node.kind == "matrix":
        matrix = etree.SubElement(parent, f"{M}m")
        for source_row in c:
            matrix_row = etree.SubElement(matrix, f"{M}mr")
            for cell in source_row.children:
                _container(matrix_row, "e", cell.children[0])
        return
    if node.kind == "equations":
        array = etree.SubElement(parent, f"{M}eqArr")
        for equation in c:
            _container(array, "e", equation)
        return
    if node.kind == "accent":
        accent = etree.SubElement(parent, f"{M}acc")
        props = etree.SubElement(accent, f"{M}accPr")
        etree.SubElement(props, f"{M}chr").set(f"{M}val", node.value or "^")
        _container(accent, "e", c[0])
        return
    if node.kind in {"limit_lower", "limit_upper"}:
        limit = etree.SubElement(
            parent, f"{M}{'limLow' if node.kind == 'limit_lower' else 'limUpp'}"
        )
        _container(limit, "e", c[0])
        _container(limit, "lim", c[1])
        return
    if node.kind == "function":
        function = etree.SubElement(parent, f"{M}func")
        _container(function, "fName", c[0])
        _container(function, "e", c[1])
        return
    if node.kind == "enclosure":
        notation = node.attr("notation", "box")
        if notation != "box":
            raise WordToolkitError(
                ErrorCode.EQUATION_INVALID,
                "OMML export does not support this enclosure notation",
                {"notation": notation},
            )
        omml_kind = node.attr("omml_kind", "borderBox")
        if omml_kind not in {"box", "borderBox"}:
            raise WordToolkitError(
                ErrorCode.EQUATION_INVALID,
                "OMML export does not support this enclosure object family",
                {"omml_kind": omml_kind},
            )
        box = etree.SubElement(parent, f"{M}{omml_kind}")
        etree.SubElement(box, f"{M}{omml_kind}Pr")
        _container(box, "e", c[0])
        return
    if node.kind == "cell" and c:
        _append(parent, c[0])
