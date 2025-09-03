"""
Modelo del panel de Tutorial (Buildings Editor).
"""
from dataclasses import dataclass, field
from typing import List, Dict, Optional
import pygame


@dataclass
class BuildingsTutorialPanelModel:
    active: bool = False
    step_index: int = 0
    steps: List[Dict[str, str]] = field(default_factory=lambda: [
        {
            "title": "Bienvenido al Buildings Editor",
            "text": (
                "Este tutorial te guiará por las funciones clave: seleccionar y mover, redimensionar, barra de split, "
                "capas Z, eliminar y deshacer, panel de colisiones, y el picker de assets con Add/Remove."
            ),
            # Resaltar el botón del tutorial en la toolbar para ubicar la UI principal
            "highlight": {"kind": "toolbar", "item": "tutorial_building"},
            "checklist": [
                {"id": "open_tutorial", "label": "Abrir el panel de Tutorial", "condition": {"kind": "always"}},
            ],
        },
        {
            "title": "Selección y movimiento",
            "text": (
                "Pasa el ratón para resaltar edificios; usa la rueda para alternar cuando hay solapados. "
                "Arrastra con botón derecho para mover un edificio; al soltar, se reasignan zona y relativos."
            ),
            # Resalta el edificio hovered/activo (área de trabajo)
            "highlight": {"kind": "editor_building", "which": "hovered_or_active"},
            "checklist": [
                {"id": "hover_active", "label": "Resaltar un edificio (hover o activo)", "condition": {"kind": "hover_or_active"}},
                {"id": "move_building", "label": "Mover el edificio (arrastra con RMB)", "condition": {"kind": "active_position_changed"}},
            ],
        },
        {
            "title": "Redimensionar y Reset",
            "text": (
                "Pulsa R sobre el edificio hovered para iniciar redimensionado (suelta R para terminar). "
                "Usa el handle de Reset para restaurar tamaño/ratio por defecto."
            ),
            "highlight": {"kind": "editor_building", "which": "hovered_or_active"},
            "checklist": [
                {"id": "resizable_ready", "label": "Tener un edificio seleccionado u hovered", "condition": {"kind": "hover_or_active"}},
            ],
        },
        {
            "title": "Barra de Split",
            "text": (
                "Arrastra la barra de split para ajustar el split_ratio del edificio activo."
            ),
            # Resaltar el handle de split con highlight preciso
            "highlight": {"kind": "tool_ui", "item": "split_handle"},
            "checklist": [
                {"id": "split_changed", "label": "Cambiar el valor del Split", "condition": {"kind": "split_changed"}},
            ],
        },
        {
            "title": "Capas Z",
            "text": (
                "Usa los botones +/− alrededor del edificio activo para moverlo entre capas Z (top/bottom)."
            ),
            # Resaltar control inferior (bottom) como ejemplo
            "highlight": {"kind": "tool_ui", "item": "z_bottom_plus"},
            "checklist": [
                {"id": "z_bottom_changed", "label": "Cambiar Z Bottom (+/−)", "condition": {"kind": "z_bottom_changed"}},
            ],
        },
        {
            "title": "Capas Z (Top)",
            "text": (
                "También puedes ajustar la capa superior (top) con sus controles."
            ),
            # Resaltar control superior (top)
            "highlight": {"kind": "tool_ui", "item": "z_top_plus"},
            "checklist": [
                {"id": "z_top_changed", "label": "Cambiar Z Top (+/−)", "condition": {"kind": "z_top_changed"}},
            ],
        },
        {
            "title": "Eliminar y Deshacer",
            "text": (
                "Pulsa Supr para borrar el edificio bajo el ratón. Usa Ctrl+Z para deshacer la última eliminación."
            ),
            "highlight": {"kind": "editor_building", "which": "hovered_or_active"},
            "checklist": [
                {"id": "hover_active2", "label": "Resaltar un edificio (hover o activo)", "condition": {"kind": "hover_or_active"}},
            ],
        },
        {
            "title": "Panel de Colisiones",
            "text": (
                "Activa el botón de colisiones en la toolbar para editar colliders. "
                "Puedes alternar alcance CG/CU por edificio."
            ),
            # Señalar el botón de colisiones en la toolbar
            "highlight": {"kind": "toolbar", "item": "buildings_colliders"},
            "checklist": [
                {"id": "colliders_mode", "label": "Activar modo de colisiones", "condition": {"kind": "colliders_mode_on"}},
            ],
        },
        {
            "title": "Add/Remove y Picker",
            "text": (
                "Con el botón de manager abres Add/Remove y el Picker. "
                "Haz RMB en un asset para arrastrar y suéltalo con RMB en el mapa para colocarlo."
            ),
            # Primero, señalar el botón de manager. (El picker se abre desde aquí)
            "highlight": {"kind": "toolbar", "item": "buildings_manager"},
            "checklist": [
                {"id": "picker_visible", "label": "Abrir Add/Remove y Picker", "condition": {"kind": "picker_visible"}},
            ],
        },
        {
            "title": "Guardar y Salir",
            "text": (
                "Pulsa Ctrl+S para guardar en cualquier momento. Esc cierra el editor y guarda cambios."
            ),
            # Sin highlight específico; solo información
            "highlight": {"kind": "none"},
            "checklist": [
                {"id": "finish", "label": "Finalizar Tutorial (opcional)", "condition": {"kind": "always"}},
            ],
        },
    ])

    # Geometría y runtime
    panel_rect: Optional[pygame.Rect] = None
    button_rects: Dict[str, pygame.Rect] = field(default_factory=dict)  # keys: prev,next,close
    # Progreso de checklist por paso (ids marcados)
    checklist_done_by_step: Dict[int, set] = field(default_factory=dict)
    # Runtime metrics para evaluar condiciones
    last_active_building_id: Optional[int] = None
    last_active_pos: Optional[tuple[int, int]] = None
    last_split_ratio: Optional[float] = None
    last_z_bottom: Optional[int] = None
    last_z_top: Optional[int] = None

    def reset_runtime(self) -> None:
        self.button_rects.clear()
        # panel_rect se recalcula en cada render
        # checklist_done_by_step se mantiene al navegar, pero podemos limpiar al cerrar
