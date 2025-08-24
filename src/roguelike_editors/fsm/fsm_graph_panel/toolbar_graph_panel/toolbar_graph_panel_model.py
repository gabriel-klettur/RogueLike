from __future__ import annotations
from dataclasses import dataclass, field
from typing import List, Dict


@dataclass
class FsmGraphToolbarModel:
    buttons: List[str] = field(
        default_factory=lambda: [
            'select', 
            'clone_node', 
            'add_node', 
            'delete', 
            'connect', 
            'disconnect',             
            'mark_ini', 
            'mark_end', 
            'zoom_in', 
            'zoom_out'
        ]
    )
    height: int = 48
    padding: int = 8
    button_size: int = 40
    # Absolute button rects (screen space) filled by the View after rendering
    rects_abs: Dict[str, 'pygame.Rect'] = field(default_factory=dict)


__all__ = ["FsmGraphToolbarModel"]
