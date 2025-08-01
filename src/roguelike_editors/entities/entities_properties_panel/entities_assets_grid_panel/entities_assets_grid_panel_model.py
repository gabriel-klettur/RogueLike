from dataclasses import dataclass, field
from typing import List, Tuple, Dict, Optional
import pygame
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.components.rendering.animation_timer import AnimationTimer

@dataclass
class AssetsGridPanelModel:
    """Modelo para el panel de cuadrícula de assets en el panel de propiedades."""


    # Entradas de celdas: lista de (rect, asset_key)
    asset_cell_entries: List[Tuple[pygame.Rect, str]] = field(default_factory=list)
    # Celda hovered y seleccionada
    hovered_asset_cell: Optional[str] = None
    selected_asset_cell: Optional[str] = None  # Add selected asset cell property
    # Preview animation: animators per dir_key
    animators: Dict[str, Animator] = field(default_factory=dict)
    # Timers para controlar intervalo de frames
    timers: Dict[str, AnimationTimer] = field(default_factory=dict)
    # Último frame renderizado por asset_key
    last_frames: Dict[str, pygame.Surface] = field(default_factory=dict)
    last_entity_id: Optional[str] = None
    last_state_tab: Optional[str] = None  # Add selected asset cell property
