"""
Modelo para el panel de Add/Remove del Buildings Editor.
"""

from dataclasses import dataclass, field
from typing import Dict, Optional, Tuple
import pygame


@dataclass
class BuildingsAddRemovePanelModel:
    # Activación visual del panel
    active: bool = False

    # Geometría del panel (último rect calculado)
    panel_rect: Optional[pygame.Rect] = None

    # Rectángulos de iconos absolutos para hit testing
    icon_rects: Dict[str, pygame.Rect] = field(default_factory=dict)

    # Herramientas disponibles y selección activa (para ToolbarView)
    tools: list[str] = field(default_factory=lambda: [
        'add_building',
        'remove_building',
        'add_building_on_system',
    ])
    active_tool: Optional[str] = None

    # Config de layout
    icon_size: int = 64
    padding: int = 8
    margin: int = 8

    # Último hover (para efectos visuales futuros)
    hovered_key: Optional[str] = None

    def reset_runtime(self) -> None:
        self.icon_rects.clear()
        self.hovered_key = None

