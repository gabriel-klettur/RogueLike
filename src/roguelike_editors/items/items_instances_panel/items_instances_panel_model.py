import pygame
from dataclasses import dataclass, field
from typing import Optional


@dataclass
class ItemsInstancesPanelModel:
    """
    Estado de la UI inferior que muestra las instancias de ítems en el mapa
    y el editor de parámetros de la instancia seleccionada.
    """
    visible: bool = True
    # Layout calculado en draw()
    list_rect: Optional[pygame.Rect] = None
    params_rect: Optional[pygame.Rect] = None
    # Instancia seleccionada actualmente en el listado
    selected_instance: Optional[str] = None
    # Margen y alturas reservadas (mismas que usa el picker para reservar espacio)
    margin: int = 20
    list_h_frac: float = 0.25  # sh // 4
    params_h_frac: float = 0.25  # sh // 4
