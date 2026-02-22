from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Optional
import pygame


@dataclass
class ItemsTutorialPanelModel:
    active: bool = False
    step_index: int = 0

    # Steps configuration for the Items Editor tutorial
    steps: List[Dict] = field(default_factory=lambda: [
        {
            "title": "Bienvenido al Items Editor",
            "text": (
                "Este tutorial te guía por las funciones clave: mostrar el picker (Items on Map), "
                "añadir ítems al mapa, eliminar drops, editar propiedades y assets, y añadir nuevos ítems al sistema."
            ),
            "highlight": {"kind": "toolbar", "item": "tutorial_items"},
            "checklist": [
                {"id": "open_tutorial", "label": "Abrir el panel de Tutorial", "condition": {"kind": "always"}},
            ],
        },
        {
            "title": "Mostrar Items en el Mapa",
            "text": (
                "Pulsa el botón 'Items on Map' para mostrar el picker de ítems y el sub-toolbar Add/Remove."
            ),
            "highlight": {"kind": "toolbar", "item": "items_on_map"},
            "checklist": [
                {"id": "items_on_map_on", "label": "Activar 'Items on Map'", "condition": {"kind": "items_on_map_on"}},
                {"id": "picker_visible", "label": "Ver el picker de ítems", "condition": {"kind": "picker_visible"}},
            ],
        },
        {
            "title": "Añadir un Ítem al Mapa",
            "text": (
                "Activa 'Add' en el sub-toolbar, elige un ítem en el picker y colócalo en el mapa."
            ),
            "highlight": {"kind": "add_remove_toolbar", "item": "add_item"},
            "checklist": [
                {"id": "add_mode_on", "label": "Activar modo 'Add'", "condition": {"kind": "add_mode_on"}},
                {"id": "spawn_selection", "label": "Elegir un ítem en el picker", "condition": {"kind": "spawn_selection"}},
                {"id": "item_spawned", "label": "Posicionar el ítem en el mapa", "condition": {"kind": "item_spawned"}},
            ],
        },
        {
            "title": "Eliminar un Drop del Mapa",
            "text": (
                "Activa 'Remove' y haz clic sobre un ítem del mapa para eliminarlo."
            ),
            "highlight": {"kind": "add_remove_toolbar", "item": "remove_item"},
            "checklist": [
                {"id": "remove_mode_on", "label": "Activar modo 'Remove'", "condition": {"kind": "remove_mode_on"}},
                {"id": "item_deleted", "label": "Eliminar un drop del mapa", "condition": {"kind": "item_deleted"}},
            ],
        },
        {
            "title": "Editar Propiedades del Ítem",
            "text": (
                "Selecciona un ítem en el picker y edita propiedades en el panel derecho. Doble clic para editar y Enter para guardar."
            ),
            "highlight": {"kind": "properties_panel"},
            "checklist": [
                {"id": "edit_started", "label": "Iniciar edición de una propiedad", "condition": {"kind": "edit_started"}},
                {"id": "properties_saved", "label": "Guardar cambios de propiedad", "condition": {"kind": "properties_saved"}},
            ],
        },
        {
            "title": "Cambiar el Asset/Icono",
            "text": (
                "En la pestaña 'assets' del panel de propiedades, haz doble clic en la celda y elige una imagen."
            ),
            "highlight": {"kind": "properties_panel"},
            "checklist": [
                {"id": "assets_picker_open", "label": "Abrir el selector de assets", "condition": {"kind": "assets_picker_open"}},
                {"id": "asset_changed", "label": "Cambiar y guardar el icono", "condition": {"kind": "asset_changed"}},
            ],
        },
        {
            "title": "Añadir Ítem al Sistema",
            "text": (
                "Activa 'Add Item on System' para crear un nuevo ítem. Completa campos y pulsa Confirmar."
            ),
            "highlight": {"kind": "add_remove_toolbar", "item": "add_item_on_system"},
            "checklist": [
                {"id": "add_system_mode_on", "label": "Activar 'Add Item on System'", "condition": {"kind": "add_system_mode_on"}},
                {"id": "add_system_confirm", "label": "Confirmar nuevo ítem", "condition": {"kind": "add_system_confirm"}},
            ],
        },
        {
            "title": "Finalizar",
            "text": ("Puedes usar este editor para gestionar y probar tus ítems rápidamente. ¡Listo!"),
            "highlight": {"kind": "none"},
            "checklist": [
                {"id": "finish", "label": "Fin del tutorial", "condition": {"kind": "always"}},
            ],
        },
    ])

    # Runtime geometry
    panel_rect: Optional[pygame.Rect] = None
    button_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
    checklist_done_by_step: Dict[int, set] = field(default_factory=dict)
