"""
Modelo del panel de Tutorial (Spells Editor).
"""
from dataclasses import dataclass, field
from typing import List, Dict, Optional
import pygame


@dataclass
class SpellsTutorialPanelModel:
    active: bool = False
    step_index: int = 0
    steps: List[Dict[str, object]] = field(default_factory=lambda: [
        {
            "title": "Bienvenido al Spells Editor",
            "text": (
                "Este tutorial te guiará por las funciones clave: abrir el picker, "
                "seleccionar un hechizo, duplicar/eliminar con Add/Remove y ubicar el panel de propiedades."
            ),
            "highlight": {"kind": "toolbar", "item": "tutorial_spells"},
            "checklist": [
                {"id": "open_tutorial", "label": "Abrir el panel de Tutorial", "condition": {"kind": "always"}},
            ],
        },
        {
            "title": "Mostrar el Picker",
            "text": (
                "Usa el botón de toolbar para mostrar el Spells Picker (lista de hechizos). "
                "Esto habilita selección y acceso al panel de propiedades."
            ),
            "highlight": {"kind": "toolbar", "item": "spells_on_map"},
            "checklist": [
                {"id": "picker_visible", "label": "Mostrar el Picker de Hechizos", "condition": {"kind": "picker_visible"}},
            ],
        },
        {
            "title": "Seleccionar un hechizo",
            "text": (
                "Haz clic sobre un hechizo del picker para seleccionarlo. El nombre se muestra en el pie del panel."
            ),
            "highlight": {"kind": "panel", "item": "picker"},
            "checklist": [
                {"id": "selected_changed", "label": "Seleccionar un hechizo en el picker", "condition": {"kind": "selected_changed"}},
            ],
        },
        {
            "title": "Duplicar y Eliminar (Add/Remove)",
            "text": (
                "En el panel Add/Remove: activa 'Add' para duplicar el hechizo seleccionado (haz clic en el grid), "
                "o 'Remove' para eliminar un hechizo."
            ),
            "highlight": [
                {"kind": "add_remove", "item": "add_spell"},
                {"kind": "add_remove", "item": "remove_spell"},
            ],
            "checklist": [
                {"id": "duplicated", "label": "Duplicar un hechizo (Add)", "condition": {"kind": "spell_count_increased"}},
                {"id": "deleted", "label": "Eliminar un hechizo (Remove)", "condition": {"kind": "spell_count_decreased"}},
            ],
        },
        {
            "title": "Panel de Propiedades",
            "text": (
                "Con un hechizo seleccionado y el picker visible, el panel de Propiedades aparece a la derecha. "
                "Desde allí puedes editar valores y cambiar el sprite/vfx."
            ),
            "highlight": {"kind": "panel", "item": "properties"},
            "checklist": [
                {"id": "properties_visible", "label": "Ver el panel de Propiedades", "condition": {"kind": "properties_visible"}},
            ],
        },
        {
            "title": "Finalizar",
            "text": (
                "¡Listo! Puedes cerrar el tutorial con ESC o con el botón Cerrar."
            ),
            "highlight": {"kind": "none"},
            "checklist": [
                {"id": "finish", "label": "Finalizar Tutorial (opcional)", "condition": {"kind": "always"}},
            ],
        },
    ])

    # Runtime/geometry
    panel_rect: Optional[pygame.Rect] = None
    button_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
    # Progreso de checklist por paso (ids marcados)
    checklist_done_by_step: Dict[int, set] = field(default_factory=dict)

    # Tracking para evaluar condiciones
    last_selected_id: Optional[str] = None
    last_spells_count: Optional[int] = None

    def reset_runtime(self) -> None:
        self.button_rects.clear()
        # panel_rect se recalcula en cada render
