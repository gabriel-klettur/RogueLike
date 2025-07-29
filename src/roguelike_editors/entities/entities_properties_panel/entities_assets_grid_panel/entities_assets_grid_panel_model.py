from dataclasses import dataclass, field
from typing import List, Tuple, Dict, Optional
import pygame

@dataclass
class AssetsGridPanelModel:
    """Modelo para el panel de cuadrícula de assets en el panel de propiedades."""






    # Entradas de celdas: lista de (rect, asset_key)
    asset_cell_entries: List[Tuple[pygame.Rect, str]] = field(default_factory=list)
    # Celda hovered y seleccionada
    hovered_asset_cell: Optional[str] = None
    selected_asset_cell: Optional[str] = None
