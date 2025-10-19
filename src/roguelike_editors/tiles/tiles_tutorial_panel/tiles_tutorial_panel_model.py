"""
Modelo del panel de Tutorial (Tiles Editor).
"""
from dataclasses import dataclass, field
from typing import List, Dict, Optional, Any, Tuple
import pygame


@dataclass
class TilesTutorialPanelModel:
    active: bool = False
    step_index: int = 0
    steps: List[Dict[str, Any]] = field(default_factory=lambda: [
        {
            "title": "Bienvenido al Tiles Editor",
            "text": (
                "Este tutorial te guía por las funciones clave: abrir el editor, seleccionar un tile en el Picker, "
                "pintar con el pincel por capas, usar el cuentagotas (eyedropper), borrar/restaurar a default, "
                "ver/cambiar capas, y pintar colisiones."
            ),
            "highlight": {"kind": "toolbar", "item": "tutorial_tiles"},
            "checklist": [
                {"id": "open_tutorial", "label": "Abrir el panel de Tutorial", "condition": {"kind": "always"}},
            ],
        },
        {
            "title": "Abrir el editor de Tiles",
            "text": (
                "Activa el editor de Tiles (F8) y asegúrate de ver la Toolbar y el Size Panel. "
                "El botón 'View' del toolbar muestra el panel de vista."
            ),
            "highlight": {"kind": "toolbar", "item": "view"},
            "checklist": [
                {"id": "tiles_editor_active", "label": "Editor de Tiles activo (F8)", "condition": {"kind": "tiles_editor_active"}},
                {"id": "size_panel_visible", "label": "Ver Size Panel visible", "condition": {"kind": "size_panel_visible"}},
            ],
        },
        {
            "title": "Seleccionar un tile en el Picker",
            "text": (
                "Pulsa la herramienta Brush para abrir el Picker si no está abierto, y elige un tile. "
                "Con la rueda mientras brush está activo puedes cambiar la capa activa."
            ),
            "highlight": {"kind": "toolbar", "item": "brush"},
            "checklist": [
                {"id": "picker_open", "label": "Picker abierto", "condition": {"kind": "picker_open"}},
                {"id": "choice_selected", "label": "Elegir un tile (brush)", "condition": {"kind": "choice_selected"}},
            ],
        },
        {
            "title": "Pintar tiles en la capa actual",
            "text": (
                "Haz clic y arrastra sobre el mapa para pintar con el brush. "
                "El tamaño del pincel se configura en el Size Panel."
            ),
            "highlight": {"kind": "toolbar", "item": "brush"},
            "checklist": [
                {"id": "brush_painted", "label": "Pintar al menos un tile", "condition": {"kind": "brush_painted"}},
            ],
        },
        {
            "title": "Cuentagotas (Eyedropper)",
            "text": "Usa Eyedropper para muestrear un tile del mapa y ajustar la capa si hay overlay.",
            "highlight": {"kind": "toolbar", "item": "eyedropper"},
            "checklist": [
                {"id": "eyedropper_used", "label": "Usar Eyedropper una vez", "condition": {"kind": "eyedropper_used"}},
            ],
        },
        {
            "title": "Borrar y Restaurar (Default)",
            "text": "Activa Delete para borrar y Default para restaurar tiles por región (según el Size Panel).",
            "highlight": [
                {"kind": "toolbar", "item": "delete"},
                {"kind": "toolbar", "item": "default"},
            ],
            "checklist": [
                {"id": "delete_done", "label": "Borrar una región", "condition": {"kind": "delete_done"}},
                {"id": "default_done", "label": "Restaurar una región a default", "condition": {"kind": "default_done"}},
            ],
        },
        {
            "title": "Capas (Layers)",
            "text": "Abre la vista de capas y cambia la capa activa.",
            "highlight": {"kind": "toolbar", "item": "view_layers"},
            "checklist": [
                {"id": "layers_open", "label": "Abrir panel de capas", "condition": {"kind": "layers_open"}},
                {"id": "layer_changed", "label": "Cambiar capa activa", "condition": {"kind": "layer_changed"}},
            ],
        },
        {
            "title": "Colisiones",
            "text": "Activa 'View Collisions' y pinta una zona de colisión.",
            "highlight": {"kind": "toolbar", "item": "view_collisions"},
            "checklist": [
                {"id": "collisions_mode", "label": "Entrar en modo colisiones", "condition": {"kind": "collisions_mode"}},
                {"id": "collision_painted", "label": "Pintar colisión/andar", "condition": {"kind": "collision_painted"}},
            ],
        },
        {
            "title": "Fin",
            "text": "Pulsa Cerrar para finalizar el tutorial.",
            "highlight": {"kind": "none"},
            "checklist": [
                {"id": "finish", "label": "Finalizar (opcional)", "condition": {"kind": "always"}},
            ],
        },
    ])

    # Geometría y runtime
    panel_rect: Optional[pygame.Rect] = None
    button_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
    checklist_done_by_step: Dict[int, set] = field(default_factory=dict)
    # Posición y drag (RMB)
    pos: Optional[Tuple[int, int]] = None
    dragging: bool = False
    drag_offset: Tuple[int, int] = (0, 0)

    def reset_runtime(self) -> None:
        self.button_rects.clear()
        # panel_rect se recalcula en cada render
        # checklist_done_by_step se mantiene al navegar, pero podemos limpiar al cerrar
