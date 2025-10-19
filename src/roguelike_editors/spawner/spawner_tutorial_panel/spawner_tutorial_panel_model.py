from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Dict, Optional


@dataclass
class SpawnerTutorialPanelModel:
    active: bool = False
    step_index: int = 0
    steps: List[Dict[str, object]] = field(default_factory=lambda: [
        {
            "title": "Bienvenido al Spawner Editor",
            "text": (
                "Este tutorial te guiará por las funciones clave: panel principal (toolbar), lista de instancias, "
                "manager de plantillas, añadir y colocar spawners, mover su ancla (RMB), confirmar cambio de zona, "
                "modo eliminar con confirmación, y enfoque temporal de cámara desde la lista."
            ),
            "highlight": {"kind": "toolbar_main", "item": "tutorial_spawner"},
            "checklist": [
                {"id": "open_tutorial", "label": "Abrir el panel de Tutorial", "condition": {"kind": "always"}},
            ],
        },
        {
            "title": "Barra principal y paneles",
            "text": (
                "Usa los iconos de la barra principal para alternar la Lista de Instancias y el Manager de Plantillas. "
                "Ambos se sitúan a la derecha de la toolbar principal."
            ),
            "highlight": [
                {"kind": "toolbar_main", "item": "spawner_list"},
                {"kind": "toolbar_main", "item": "spawner_manager"},
            ],
            "checklist": [
                {"id": "instances_open", "label": "Abrir la Lista de Instancias", "condition": {"kind": "instances_open"}},
                {"id": "manager_open", "label": "Abrir el Manager de Plantillas", "condition": {"kind": "manager_open"}},
            ],
        },
        {
            "title": "Lista de Instancias y Enfoque de Cámara",
            "text": (
                "Selecciona una instancia para ver sus propiedades. En la lista, mantén pulsado el botón sobre el segmento "
                "de coordenadas (@ zona (x,y)) para centrar temporalmente la cámara. Suelta para restaurar."
            ),
            "highlight": {"kind": "panel", "item": "instances_panel"},
            "checklist": [
                {"id": "instance_selected", "label": "Seleccionar una instancia en la lista", "condition": {"kind": "instance_selected"}},
                {"id": "hold_focus_started", "label": "Mantener pulsado sobre coords para enfocar cámara", "condition": {"kind": "hold_focus_started"}},
                {"id": "hold_focus_ended", "label": "Soltar para restaurar cámara", "condition": {"kind": "hold_focus_ended"}},
            ],
        },
        {
            "title": "Añadir y colocar un Spawner",
            "text": (
                "Pulsa el botón de Añadir en la toolbar de instancias, elige una plantilla del desplegable y haz clic en el mapa para colocarla. "
                "Puedes cancelar con ESC antes de colocar."
            ),
            "highlight": {"kind": "toolbar_instance", "item": "add_spawner"},
            "checklist": [
                {"id": "add_mode_on", "label": "Entrar en modo Añadir (desplegable visible)", "condition": {"kind": "add_mode_on"}},
                {"id": "template_selected", "label": "Elegir una plantilla del desplegable", "condition": {"kind": "template_selected"}},
                {"id": "placement_done", "label": "Colocar una instancia con clic en el mapa", "condition": {"kind": "placement_done"}},
            ],
        },
        {
            "title": "Mover el ancla (RMB) y cambio de zona",
            "text": (
                "Arrastra con el botón derecho (RMB) sobre un spawner para mover su ancla. Si atraviesas a otra zona, el editor pedirá confirmación. "
                "Confirma con Y/Enter o cancela con N/Esc."
            ),
            "highlight": {"kind": "panel", "item": "world"},
            "checklist": [
                {"id": "drag_started", "label": "Iniciar arrastre con RMB sobre una instancia", "condition": {"kind": "drag_started"}},
                {"id": "persist_drop", "label": "Soltar y persistir nueva posición", "condition": {"kind": "persist_drop"}},
                {"id": "zone_confirm_open", "label": "Abrir confirmación por cambio de zona", "condition": {"kind": "zone_confirm_open"}},
                {"id": "zone_confirm_yes", "label": "Confirmar cambio de zona", "condition": {"kind": "zone_confirm_yes"}},
                {"id": "zone_confirm_no", "label": "Cancelar cambio de zona", "condition": {"kind": "zone_confirm_no"}},
            ],
        },
        {
            "title": "Eliminar una instancia",
            "text": (
                "Activa el modo Eliminar en la toolbar de instancias, haz clic sobre una instancia y confirma con Y/Enter (o cancela con N/Esc)."
            ),
            "highlight": {"kind": "toolbar_instance", "item": "remove_spawner"},
            "checklist": [
                {"id": "remove_mode_on", "label": "Activar modo Eliminar", "condition": {"kind": "remove_mode_on"}},
                {"id": "delete_confirm_open", "label": "Abrir confirmación de borrado", "condition": {"kind": "delete_confirm_open"}},
                {"id": "delete_done", "label": "Confirmar borrado de una instancia", "condition": {"kind": "delete_done"}},
            ],
        },
        {
            "title": "Editar propiedades y aplicar",
            "text": (
                "Con una instancia seleccionada, edita propiedades en el panel de la derecha (por ejemplo, building_id en overrides) y guarda. "
                "Los cambios se reflejan en vivo en el mundo."
            ),
            "highlight": {"kind": "panel", "item": "instance_properties"},
            "checklist": [
                {"id": "properties_saved", "label": "Guardar cambios en propiedades de una instancia", "condition": {"kind": "properties_saved"}},
            ],
        },
    ])

    # Geometría y runtime
    panel_rect: Optional[object] = None  # pygame.Rect at runtime
    button_rects: Dict[str, object] = field(default_factory=dict)  # pygame.Rect by key
    checklist_done_by_step: Dict[int, set] = field(default_factory=dict)

    def reset_runtime(self) -> None:
        self.button_rects.clear()
        # panel_rect recalculada en render
        # checklist_done_by_step se conserva entre pasos
