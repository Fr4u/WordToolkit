from __future__ import annotations

from collections.abc import Iterable
from dataclasses import dataclass, field
from typing import Any


@dataclass(frozen=True, slots=True)
class EquationNode:
    kind: str
    value: str = ""
    children: tuple[EquationNode, ...] = field(default_factory=tuple)
    attrs: tuple[tuple[str, str], ...] = field(default_factory=tuple)

    @classmethod
    def make(
        cls,
        kind: str,
        value: str = "",
        children: Iterable[EquationNode] = (),
        **attrs: str,
    ) -> EquationNode:
        return cls(
            kind, value, tuple(children), tuple(sorted((str(k), str(v)) for k, v in attrs.items()))
        )

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> EquationNode:
        if not isinstance(data, dict) or not isinstance(data.get("kind"), str):
            raise ValueError("Equation AST nodes require a string 'kind'")
        children = data.get("children", [])
        attrs = data.get("attrs", {})
        if not isinstance(children, list) or not isinstance(attrs, dict):
            raise ValueError("Equation AST children must be a list and attrs an object")
        return cls.make(
            data["kind"],
            str(data.get("value", "")),
            (cls.from_dict(item) for item in children),
            **{str(k): str(v) for k, v in attrs.items()},
        )

    def to_dict(self) -> dict[str, Any]:
        data: dict[str, Any] = {"kind": self.kind}
        if self.value:
            data["value"] = self.value
        if self.children:
            data["children"] = [child.to_dict() for child in self.children]
        if self.attrs:
            data["attrs"] = dict(self.attrs)
        return data

    def attr(self, key: str, default: str = "") -> str:
        return dict(self.attrs).get(key, default)


EMPTY = EquationNode.make("row")


def row(*nodes: EquationNode) -> EquationNode:
    flattened: list[EquationNode] = []
    for node in nodes:
        if node.kind == "row":
            flattened.extend(node.children)
        elif node.kind != "empty":
            flattened.append(node)
    if len(flattened) == 1:
        return flattened[0]
    return EquationNode.make("row", children=flattened)
