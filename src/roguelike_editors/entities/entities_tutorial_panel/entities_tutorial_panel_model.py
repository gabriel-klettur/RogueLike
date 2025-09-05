"""
Modelo del panel de Tutorial (Entities Editor).
"""
from dataclasses import dataclass, field
from typing import List, Dict, Optional
import pygame


@dataclass
class EntitiesTutorialPanelModel:
    active: bool = False
    step_index: int = 0
    steps: List[Dict[str, str]] = field(default_factory=lambda: [
        {
            "title": "Bienvenido al Entities Editor",
            "text": (
                "Este tutorial te guiará por las funciones clave: abrir el editor de entidades, "
                "seleccionar una entidad en el Picker, colocarla en el mapa (Spawn), modo borrar, "
                "y deshacer/rehacer. También verás el modo 'Add Entity on System' para editar propiedades."
            ),
            # Resaltar el botón del tutorial en la toolbar para ubicar la UI principal
            "highlight": {"kind": "toolbar", "item": "tutorial_entities"},
            "checklist": [
                {"id": "open_tutorial", "label": "Abrir el panel de Tutorial", "condition": {"kind": "always"}},
            ],
        },
        {
            "title": "Abrir el editor de Entidades",
            "text": (
                "Activa la herramienta de Entidades en la toolbar (icono del mapa con NPCs). "
                "Esto mostrará el Add/Remove y el Picker de entidades."
            ),
            "highlight": {"kind": "toolbar", "item": "entities_on_map"},
            "checklist": [
                {"id": "entities_tool_on", "label": "Activar el editor de Entidades", "condition": {"kind": "entities_tool_on"}},
                {"id": "picker_visible", "label": "Ver el Picker de entidades visible", "condition": {"kind": "picker_visible"}},
            ],
        },
        {
            "title": "Spawn de entidades",
            "text": (
                "En el panel Add/Remove, pulsa 'Add' para entrar en modo Spawn. Selecciona una entidad en el Picker y "
                "haz clic en el mapa para colocarla."
            ),
            "highlight": {"kind": "toolbar", "item": "entities_on_map"},
            "checklist": [
                {"id": "spawn_mode_on", "label": "Entrar en modo Spawn (Add)", "condition": {"kind": "spawn_mode_on"}},
                {"id": "spawn_selection", "label": "Seleccionar una entidad en el Picker", "condition": {"kind": "spawn_selection"}},
                {"id": "entity_spawned", "label": "Colocar una entidad en el mapa", "condition": {"kind": "entity_spawned"}},
            ],
        },
        {
            "title": "Borrar entidades",
            "text": (
                "En el panel Add/Remove, pulsa 'Remove' para entrar en modo borrar y haz clic sobre una entidad del mapa "
                "para eliminarla."
            ),
            "highlight": {"kind": "toolbar", "item": "entities_on_map"},
            "checklist": [
                {"id": "delete_mode_on", "label": "Entrar en modo Borrar (Remove)", "condition": {"kind": "delete_mode_on"}},
                {"id": "entity_deleted", "label": "Eliminar una entidad del mapa", "condition": {"kind": "entity_deleted"}},
            ],
        },
        {
            "title": "Deshacer y Rehacer",
            "text": (
                "Usa Ctrl+Z o el botón 'Undo' para deshacer el último cambio, y Ctrl+Y/Shift+Ctrl+Z o 'Redo' para rehacer."
            ),
            "highlight": [
                {"kind": "toolbar", "item": "undo"},
                {"kind": "toolbar", "item": "redo"},
            ],
            "checklist": [
                {"id": "undo_done", "label": "Ejecutar Undo", "condition": {"kind": "undo_done"}},
                {"id": "redo_done", "label": "Ejecutar Redo", "condition": {"kind": "redo_done"}},
            ],
        },
        {
            "title": "Add Entity on System (Propiedades)",
            "text": (
                "Activa 'Add Entity on System' en Add/Remove para ocultar el Picker y abrir el panel de Propiedades "
                "en su lugar. Aquí podrás crear/editar una clase de entidad (id, stats, assets)."
            ),
            "highlight": {"kind": "toolbar", "item": "entities_on_map"},
            "checklist": [
                {"id": "add_system_mode", "label": "Entrar en 'Add Entity on System'", "condition": {"kind": "add_system_mode"}},
            ],
        },
        {
            "title": "Guardar y Salir",
            "text": (
                "Pulsa Ctrl+S para guardar en cualquier momento. Esc cierra el editor de Entidades."
            ),
            "highlight": {"kind": "none"},
            "checklist": [
                {"id": "finish", "label": "Finalizar Tutorial (opcional)", "condition": {"kind": "always"}},
            ],
        },
    ])

    # Geometría y runtime
    panel_rect: Optional[pygame.Rect] = None
    button_rects: Dict[str, pygame.Rect] = field(default_factory=dict)  # keys: prev,next,close
    checklist_done_by_step: Dict[int, set] = field(default_factory=dict)

    # Tracking de estado para condiciones
    last_spawn_mode: Optional[bool] = None
    last_delete_mode: Optional[bool] = None

    def reset_runtime(self) -> None:
        self.button_rects.clear()
        # panel_rect se recalcula en cada render
        # checklist_done_by_step se mantiene al navegar, pero podemos limpiar al cerrar
