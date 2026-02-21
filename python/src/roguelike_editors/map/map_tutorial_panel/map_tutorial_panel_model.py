"""
Modelo del panel de Tutorial (Map Editor).
"""
from dataclasses import dataclass, field
from typing import List, Dict, Optional
import pygame
from roguelike_engine.map.model.layer import Layer


@dataclass
class MapTutorialPanelModel:
    active: bool = False
    step_index: int = 0
    steps: List[Dict] = field(default_factory=lambda: [
        {
            "title": "Bienvenido al Map Editor",
            "text": (
                "Este tutorial te guiará por las funciones clave: seleccionar zona, pan/zoom de cámara, "
                "visibilidad de capas, pintar tiles por zona, colliders, y CRUD de zonas (añadir, borrar, renombrar). "
                "Pulsa ESC para cerrar el tutorial en cualquier momento."
            ),
            # Resaltar la toolbar para ubicar la UI principal
            "highlight": {"kind": "toolbar", "item": "view_layers"},
            "checklist": [
                {"id": "open_tutorial", "label": "Abrir el panel de Tutorial", "condition": {"kind": "always"}},
            ],
        },
        {
            "title": "Seleccionar zona",
            "text": (
                "Haz click sobre una zona para seleccionarla. Doble clic inicia el modo de renombrar."
            ),
            # Resaltar la zona seleccionada
            "highlight": {"kind": "editor_zone", "which": "selected"},
            "checklist": [
                {"id": "select_zone", "label": "Seleccionar una zona", "condition": {"kind": "zone_selected_changed"}},
            ],
        },
        {
            "title": "Pan/Zoom de cámara",
            "text": (
                "Usa el botón medio o derecho para arrastrar el mapa (pan). Usa la rueda del ratón para hacer zoom."
            ),
            "highlight": {"kind": "none"},
            "checklist": [
                {"id": "panned", "label": "Mover la cámara (pan)", "condition": {"kind": "camera_panned"}},
                {"id": "zoomed", "label": "Cambiar el zoom", "condition": {"kind": "camera_zoom_changed"}},
            ],
        },
        {
            "title": "Visibilidad de capas",
            "text": (
                "Pulsa el botón 'view_layers' del toolbar para abrir/cerrar el panel de visibilidad de capas."
            ),
            "highlight": {"kind": "toolbar", "item": "view_layers"},
            "checklist": [
                {"id": "layers_opened", "label": "Abrir el panel de capas", "condition": {"kind": "layers_view_opened"}},
            ],
        },
        {
            "title": "Pintar tiles por zona",
            "text": (
                "Activa 'paint_tiles' y haz clic en una zona; confirma en el diálogo. Se aplicará el overlay 'floor' y se guardará por zona."
            ),
            "highlight": {"kind": "toolbar", "item": "paint_tiles"},
            "checklist": [
                {"id": "paint_tiles_confirmed", "label": "Confirmar pintado de tiles", "condition": {"kind": "paint_tiles_confirmed"}},
                {"id": "paint_tiles_done", "label": "Pintado finalizado", "condition": {"kind": "paint_tiles_finalized"}},
                {"id": "undo_any", "label": "Usar Undo (Ctrl+Z)", "condition": {"kind": "undo_performed"}},
                {"id": "redo_any", "label": "Usar Redo (Ctrl+Y)", "condition": {"kind": "redo_performed"}},
            ],
        },
        {
            "title": "Colliders (vaciar / pintar)",
            "text": (
                "Activa 'clear_colliders' o 'paint_colliders', haz clic en una zona y confirma. "
                "Al finalizar, el índice espacial se recalcula automáticamente."
            ),
            "highlight": [
                {"kind": "toolbar", "item": "clear_colliders"},
                {"kind": "toolbar", "item": "paint_colliders"},
            ],
            "checklist": [
                {"id": "clear_colliders_done", "label": "Vaciar colliders (finalizado)", "condition": {"kind": "clear_colliders_finalized"}},
                {"id": "paint_colliders_done", "label": "Pintar colliders (finalizado)", "condition": {"kind": "paint_colliders_finalized"}},
            ],
        },
        {
            "title": "CRUD de zonas",
            "text": (
                "Añade zonas nuevas (grid-aligned), bórralas con confirmación y renómbralas con doble clic + Enter."
            ),
            "highlight": [
                {"kind": "toolbar", "item": "add_zone"},
                {"kind": "toolbar", "item": "delete_zone"},
            ],
            "checklist": [
                {"id": "zone_added", "label": "Agregar una zona", "condition": {"kind": "zone_added"}},
                {"id": "zone_deleted", "label": "Eliminar una zona", "condition": {"kind": "zone_deleted"}},
                {"id": "zone_renamed", "label": "Renombrar una zona", "condition": {"kind": "zone_renamed"}},
            ],
        },
        {
            "title": "Guardar y Salir",
            "text": (
                "Pulsa Ctrl+S para guardar zonas. ESC cierra el editor y guarda la cámara."
            ),
            "highlight": {"kind": "none"},
            "checklist": [
                {"id": "zones_saved", "label": "Guardar zonas (Ctrl+S)", "condition": {"kind": "zones_saved"}},
                {"id": "finish", "label": "Finalizar tutorial (opcional)", "condition": {"kind": "always"}},
            ],
        },
    ])

    # Geometría y runtime
    panel_rect: Optional[pygame.Rect] = None
    button_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
    # Progreso por paso
    checklist_done_by_step: Dict[int, set] = field(default_factory=dict)
    # Métricas runtime
    last_selected_zone: Optional[str] = None
    last_camera_offset: Optional[tuple[float, float]] = None
    last_camera_zoom: Optional[float] = None
    last_layers_open: Optional[bool] = None
    last_zone_count: Optional[int] = None

    def reset_runtime(self) -> None:
        self.button_rects.clear()
        # panel_rect se recalcula en cada render
        # checklist_done_by_step se mantiene entre pasos
