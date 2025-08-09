from dataclasses import dataclass, field
from typing import List, Tuple, Dict, Optional
import pygame
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.components.rendering.animation_timer import AnimationTimer

@dataclass
class AssetsGridPanelModel:
    """
    Modelo para el panel de cuadrícula de assets dentro del panel de propiedades.

    Campos principales:
    - asset_cell_entries: pares (Rect, asset_key) usados para hit-testing y pintura.
    - hovered_asset_cell / selected_asset_cell: estado UI para resaltado/selección.
    - animators: animadores por clave de asset (solo subpestaña de set).
    - timers: controlan el intervalo de frames por clave.
    - last_frames: surface del último frame renderizado por clave.
    - last_entity_id / last_state_tab: cache para detectar cuándo reconstruir.
    - active_set_rect: rectángulo del combo "Activo" para eventos de click.
    """


    # Entradas de celdas: lista de (rect, asset_key)
    asset_cell_entries: List[Tuple[pygame.Rect, str]] = field(default_factory=list)
    # Celda hovered y seleccionada
    hovered_asset_cell: Optional[str] = None
    selected_asset_cell: Optional[str] = None
    # Preview animation: animators por asset_key
    animators: Dict[str, Animator] = field(default_factory=dict)
    # Timers para controlar intervalo de frames
    timers: Dict[str, AnimationTimer] = field(default_factory=dict)
    # Último frame renderizado por asset_key
    last_frames: Dict[str, pygame.Surface] = field(default_factory=dict)
    last_entity_id: Optional[str] = None
    last_state_tab: Optional[str] = None
    # Rect del combobox "Activo" para detectar clicks
    active_set_rect: Optional[pygame.Rect] = None
