from __future__ import annotations
from dataclasses import dataclass
from typing import Optional


@dataclass
class ListTemplatesDeleteModel:
    # Confirmation dialog state
    confirm_visible: bool = False
    confirm_text: str = ""
    confirm_target_index: Optional[int] = None
    confirm_target_id: Optional[str] = None
    last_deleted_id: Optional[str] = None
    error: Optional[str] = None
    # Number of instances removed in last deletion (for observers/refresh)
    last_deleted_instances_count: int = 0


__all__ = ["ListTemplatesDeleteModel"]
