from __future__ import annotations

from dataclasses import dataclass
from typing import Optional


@dataclass
class HoverState:
    node_id: Optional[str] = None
    edge_id: Optional[str] = None
    handle: Optional[str] = None
