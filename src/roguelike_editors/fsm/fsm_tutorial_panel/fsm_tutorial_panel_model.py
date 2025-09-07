"""
Modelo del panel de Tutorial (FSM Editor).
Inspirado en BuildingsTutorialPanelModel pero adaptado al flujo del FSM.
"""
from __future__ import annotations
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple, Any


@dataclass
class FsmTutorialPanelModel:
    active: bool = False
    step_index: int = 0
    # Lista de pasos con checklist. Cada item: {id, label, condition: {kind}}
    steps: List[Dict[str, Any]] = field(default_factory=lambda: [
        {
            "title": "Bienvenido al FSM Editor",
            "text": (
                "Este tutorial te guía por las funciones clave: lista de sets, canvas del grafo, "
                "navegación (pan/zoom), selección y mover nodos, edición de etiquetas, "
                "añadir/clonar/eliminar nodos, conectar/desconectar, marcar inicio/fin y leyenda."
            ),
            "highlight": {"kind": "toolbar", "item": "tutorial_fsm"},
            "checklist": [
                {"id": "open_tutorial", "label": "Abrir el panel de Tutorial", "condition": {"kind": "always"}},
            ],
        },
        {
            "title": "Abrir lista de Sets",
            "text": (
                "Abre la lista de sets con el botón 'Sets' en la toolbar (o tecla S)."
            ),
            "highlight": {"kind": "toolbar", "item": "sets_list"},
            "checklist": [
                {"id": "sets_list_open", "label": "Abrir la lista de sets", "condition": {"kind": "sets_panel_visible"}},
                {"id": "set_selected", "label": "Seleccionar un set", "condition": {"kind": "set_selected"}},
            ],
        },
        {
            "title": "Navegación del Grafo",
            "text": (
                "Con el ratón sobre el canvas: rueda para zoom, botón central para pan. Las teclas + y - también hacen zoom."
            ),
            "highlight": {"kind": "graph_canvas"},
            "checklist": [
                {"id": "zoom_changed", "label": "Cambiar el zoom", "condition": {"kind": "zoom_changed"}},
                {"id": "pan_changed", "label": "Hacer pan (arrastre con MMB)", "condition": {"kind": "pan_changed"}},
            ],
        },
        {
            "title": "Seleccionar y mover un nodo",
            "text": (
                "Haz click en un estado para seleccionarlo. Arrástralo con el botón izquierdo para moverlo."
            ),
            "highlight": {"kind": "graph_canvas"},
            "checklist": [
                {"id": "node_selected", "label": "Seleccionar un nodo", "condition": {"kind": "node_selected"}},
                {"id": "node_moved", "label": "Mover el nodo seleccionado", "condition": {"kind": "node_moved"}},
            ],
        },
        {
            "title": "Editar etiqueta",
            "text": (
                "Haz doble click sobre la etiqueta de un nodo o transición para editar. Pulsa Enter para confirmar."
            ),
            "highlight": {"kind": "graph_canvas"},
            "checklist": [
                {"id": "edit_started", "label": "Iniciar edición de texto", "condition": {"kind": "edit_started"}},
                {"id": "edit_committed", "label": "Confirmar edición", "condition": {"kind": "edit_committed"}},
            ],
        },
        {
            "title": "Añadir / Clonar / Eliminar",
            "text": (
                "Usa la toolbar del grafo para añadir un nodo, clonar el seleccionado o eliminar."
            ),
            "highlight": {"kind": "graph_toolbar", "item": "add_node"},
            "checklist": [
                {"id": "node_added", "label": "Añadir un nodo", "condition": {"kind": "nodes_count_increased"}},
                {"id": "node_cloned", "label": "Clonar un nodo", "condition": {"kind": "nodes_count_increased"}},
                {"id": "node_deleted", "label": "Eliminar un nodo", "condition": {"kind": "nodes_count_decreased"}},
            ],
        },
        {
            "title": "Conectar / Desconectar",
            "text": (
                "Usa las herramientas de conectar y desconectar: arrastra desde el handle del borde de un nodo al otro."
            ),
            "highlight": {"kind": "graph_toolbar", "item": "connect"},
            "checklist": [
                {"id": "edge_added", "label": "Crear una transición", "condition": {"kind": "edges_count_increased"}},
                {"id": "edge_removed", "label": "Eliminar una transición", "condition": {"kind": "edges_count_decreased"}},
            ],
        },
        {
            "title": "Marcar Inicio / Fin",
            "text": (
                "Marca un estado como inicial o final usando los botones de la toolbar del grafo."
            ),
            "highlight": {"kind": "graph_toolbar", "item": "mark_ini"},
            "checklist": [
                {"id": "set_initial", "label": "Marcar estado inicial", "condition": {"kind": "initial_changed"}},
                {"id": "set_end", "label": "Marcar estado final", "condition": {"kind": "terminal_changed"}},
            ],
        },
        {
            "title": "Leyenda",
            "text": (
                "Abre y cierra la leyenda con su botón; al hacer click dentro, se bloquea la interacción del canvas."
            ),
            "highlight": {"kind": "legend_button"},
            "checklist": [
                {"id": "legend_toggled", "label": "Alternar la leyenda", "condition": {"kind": "legend_toggled"}},
            ],
        },
        {
            "title": "Fin",
            "text": (
                "¡Listo! Puedes cerrar el panel con ESC o con el botón Cerrar."
            ),
            "highlight": {"kind": "none"},
            "checklist": [
                {"id": "finish", "label": "Finalizar Tutorial (opcional)", "condition": {"kind": "always"}},
            ],
        },
    ])

    # Geometría y runtime
    panel_rect: Optional[Tuple[int, int, int, int]] = None  # representaremos como pygame.Rect en la vista
    button_rects: Dict[str, Any] = field(default_factory=dict)

    # Progreso de checklist por paso
    checklist_done_by_step: Dict[int, set] = field(default_factory=dict)

    # Tracking para detectar cambios
    last_zoom: Optional[float] = None
    last_pan: Tuple[float, float] | None = None
    last_selected_node_id: Optional[str] = None
    last_selected_node_pos: Optional[Tuple[int, int]] = None
    last_nodes_count: Optional[int] = None
    last_edges_count: Optional[int] = None
    last_initial_node_id: Optional[str] = None
    legend_collapsed_prev: Optional[bool] = None
    # Edición de texto
    editing_started: bool = False
    last_editing_any: bool = False

    def reset_runtime(self) -> None:
        self.button_rects.clear()
        # panel_rect recalculado en cada render
        # Conservar checklist para navegación entre pasos
