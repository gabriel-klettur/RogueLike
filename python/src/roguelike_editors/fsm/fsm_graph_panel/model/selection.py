from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional, Set


@dataclass
class SelectionState:
    node_ids: Set[str] = field(default_factory=set)
    edge_ids: Set[str] = field(default_factory=set)
    primary: Optional[str] = None
