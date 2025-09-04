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
                {"id": "select_building", "label": "Seleccionar un edificio mediante click", "condition": {"kind": "active_selected_changed"}},
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
                {"id": "hover_active_prev", "label": "Resaltar un edificio (hover o activo)", "condition": {"kind": "hover_or_active"}},
                {"id": "select_building_prev", "label": "Seleccionar un edificio mediante click", "condition": {"kind": "active_selected_changed"}},
                {"id": "resized", "label": "Redimensionar el edificio", "condition": {"kind": "resized"}},
                {"id": "reset_done", "label": "Hacer Reset del edificio", "condition": {"kind": "reset_done"}},
            ],
        },
        {
            "title": "Barra de Split",
            "text": (
                "Arrastra la barra de split para ajustar el split_ratio del edificio activo. "
                "El split define la línea de corte entre la parte superior (decorativa/sin colisión, donde el jugador puede quedar 'detrás') "
                "y la parte inferior (con colisión y apoyo en el suelo). Úsalo para que el personaje pase por detrás de techos/copas "
                "y para delimitar correctamente la zona que colisiona."
            ),
            # Resaltar el handle de split con highlight preciso
            "highlight": {"kind": "tool_ui", "item": "split_handle"},
            "checklist": [
                {"id": "hover_active_prev2", "label": "Resaltar un edificio (hover o activo)", "condition": {"kind": "hover_or_active"}},
                {"id": "select_building_prev2", "label": "Seleccionar un edificio mediante click", "condition": {"kind": "active_selected_changed"}},
                {"id": "split_changed", "label": "Cambiar el valor del Split", "condition": {"kind": "split_changed"}},
            ],
        },
        {
            "title": "Capas Z (Bottom)",
            "text": (
                "Usa los botones + y − alrededor del edificio activo para ajustar la capa Z inferior (bottom). "
                "Las capas Z determinan el orden de dibujo y superposición entre edificios y otros elementos; valores Z más altos se dibujan por encima. "
                "La capa Z inferior posiciona la base del edificio dentro de ese orden (suelo/pies). Ajusta con +/− para encajar con el entorno."
            ),
            # Resaltar control inferior (bottom) como ejemplo
            "highlight": [
                {"kind": "tool_ui", "item": "z_bottom_plus"},
                {"kind": "tool_ui", "item": "z_bottom_minus"},
            ],
            "checklist": [
                {"id": "hover_active_prev3", "label": "Resaltar un edificio (hover o activo)", "condition": {"kind": "hover_or_active"}},
                {"id": "select_building_prev3", "label": "Seleccionar un edificio mediante click", "condition": {"kind": "active_selected_changed"}},
                {"id": "z_bottom_plus", "label": "Incrementar Z Bottom (+)", "condition": {"kind": "z_bottom_plus"}},
                {"id": "z_bottom_minus", "label": "Disminuir Z Bottom (−)", "condition": {"kind": "z_bottom_minus"}},
            ],
        },
        {
            "title": "Capas Z (Top)",
            "text": (
                "Ajusta la capa Z superior (top) usando los botones + y −. "
                "Las capas Z controlan el orden de superposición. La capa Z superior define dónde se dibuja la parte alta del edificio, "
                "permitiendo que el personaje pase por detrás de techos/copas sin afectar la base. Ajusta 'top' en relación a otros edificios altos."
            ),
            # Resaltar control superior (top)
            "highlight": [
                {"kind": "tool_ui", "item": "z_top_plus"},
                {"kind": "tool_ui", "item": "z_top_minus"},
            ],
            "checklist": [
                {"id": "hover_active_prev4", "label": "Resaltar un edificio (hover o activo)", "condition": {"kind": "hover_or_active"}},
                {"id": "select_building_prev4", "label": "Seleccionar un edificio mediante click", "condition": {"kind": "active_selected_changed"}},
                {"id": "z_top_plus", "label": "Incrementar Z Top (+)", "condition": {"kind": "z_top_plus"}},
                {"id": "z_top_minus", "label": "Disminuir Z Top (−)", "condition": {"kind": "z_top_minus"}},
            ],
        },
        {
            "title": "Eliminar y Deshacer",
            "text": (
                "Haz click para seleccionar un edificio. Luego elimina usando Supr o el botón 'E' (Eliminar). "
                "Finalmente, deshaz con Ctrl+Z o usando el icono de 'Deshacer' en la toolbar."
            ),
            "highlight": [
                # Resaltar el edificio hasta que se elimine
                {"kind": "editor_building", "which": "hovered_or_active", "hide_if_done": ["deleted_building"]},
                # Tras eliminar, guiar al usuario al icono de Deshacer de la toolbar hasta completar el undo
                {"kind": "toolbar", "item": "undo", "depends_on_done": ["deleted_building"], "hide_if_done": ["undo_delete"]},
            ],
            "checklist": [
                {"id": "hover_active2", "label": "Resaltar un edificio (hover o activo)", "condition": {"kind": "hover_or_active"}},
                {"id": "select_building_prev5", "label": "Seleccionar un edificio mediante click", "condition": {"kind": "active_selected_changed"}},
                {"id": "deleted_building", "label": "Eliminar un edificio (Supr o botón 'E')", "condition": {"kind": "deleted_building"}},
                {"id": "undo_delete", "label": "Deshacer la última eliminación (Ctrl+Z o icono 'Deshacer')", "condition": {"kind": "undo_delete"}},
            ],
        },
        {
            "title": "Panel de Colisiones",
            "text": (
                "Activa el botón de colisiones en la toolbar para editar colliders. "
                "Selecciona el tipo en el picker (# sólido o . caminable) y pinta sobre el edificio. "
                "Nota: solo puedes pintar sobre el edificio seleccionado/activo; selecciona primero y luego pinta. "
                "Puedes mover el picker (RMB) y alternar alcance CG/CU para aplicar a todos los de la misma imagen o solo a esta instancia. "
                "Si usas CU, guarda overrides con 'Save CU'."
            ),
            # Señalar el botón de colisiones en la toolbar
            "highlight": {"kind": "toolbar", "item": "buildings_colliders"},
            "checklist": [
                {"id": "colliders_mode", "label": "Activar modo de colisiones", "condition": {"kind": "colliders_mode_on"}},
                {"id": "colliders_choice", "label": "Elegir tipo en el picker (# o .)", "condition": {"kind": "colliders_choice_selected"}},
                {"id": "colliders_painted", "label": "Pintar una celda de colisión en el edificio", "condition": {"kind": "colliders_painted"}},
                {"id": "colliders_painted_on_selected", "label": "Pintar en el edificio seleccionado/activo", "condition": {"kind": "colliders_painted_on_selected"}},
                {"id": "colliders_picker_moved", "label": "Mover el panel del picker (RMB y arrastrar)", "condition": {"kind": "colliders_picker_moved"}},
                {"id": "colliders_scope_toggled", "label": "Alternar alcance entre CG y CU", "condition": {"kind": "colliders_scope_toggled"}},
                {"id": "colliders_scope_cg", "label": "Establecer alcance CG (global por image_path)", "condition": {"kind": "colliders_scope_cg"}},
                {"id": "colliders_scope_cu", "label": "Establecer alcance CU (único por instancia)", "condition": {"kind": "colliders_scope_cu"}},
                {"id": "colliders_saved_button", "label": "Guardar overrides por instancia con el botón 'Save CU'", "condition": {"kind": "colliders_saved_button"}},
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
    last_image_size: Optional[tuple[int, int]] = None
    # Alcance de colisiones (CG/CU) para detectar toggles en el tutorial
    last_collider_scope: Optional[str] = None

    def reset_runtime(self) -> None:
        self.button_rects.clear()
        # panel_rect se recalcula en cada render
        # checklist_done_by_step se mantiene al navegar, pero podemos limpiar al cerrar
