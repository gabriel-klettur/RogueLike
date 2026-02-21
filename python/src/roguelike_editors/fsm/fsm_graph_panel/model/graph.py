from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict


@dataclass
class Node:
    id: str
    x: float
    y: float
    label: str = ""


@dataclass
class Edge:
    id: str
    source: str
    target: str
    label: str = ""


@dataclass
class GraphModel:
    nodes: Dict[str, Node] = field(default_factory=dict)
    edges: Dict[str, Edge] = field(default_factory=dict)
