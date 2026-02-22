from __future__ import annotations
from dataclasses import dataclass
from typing import Optional


@dataclass
class SetsPanelCloneModel:
    last_cloned_id: Optional[str] = None
    error: Optional[str] = None


__all__ = ["SetsPanelCloneModel"]
