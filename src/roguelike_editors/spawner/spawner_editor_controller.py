"""Controlador principal del Spawner Editor: coordina modelo, vista y eventos.

Responsabilidades:
- Orquestar subcontroladores (título, toolbars, paneles de plantillas/instancias/propiedades).
- Sincronizar visibilidad/estado de UI con el toolbar activo y flags transitorios.
- Propagar cambios a entidades vivas del ECS (templates/instancias).
"""
from __future__ import annotations

from typing import Optional, Any
import pygame

from roguelike_editors.spawner.spawner_editor_model import SpawnerEditorModel
from roguelike_editors.spawner.spawner_editor_events import SpawnerEditorEventHandler
from roguelike_editors.spawner.spawner_editor_view import SpawnerEditorView
from roguelike_editors.spawner.spawner_title.spawner_title_controller import SpawnerTitleController
from roguelike_editors.spawner.spawner_toolbar.spawner_toolbar_controller import SpawnerToolbarController
from roguelike_editors.spawner.spawner_templates_panel.spawner_manager_controller import SpawnerManagerController
from roguelike_editors.spawner.spawner_instances_panel.spawner_list_instances_controller import SpawnerListInstancesController
from roguelike_editors.spawner.spawner_instance_properties_panel.instance_properties_controller import InstancePropertiesController
from roguelike_editors.spawner.spawner_tutorial_panel import SpawnerTutorialPanelController
from roguelike_editors.spawner.controller import (
    # Focus
    start_hold_focus as _start_hold_focus,
    end_hold_focus as _end_hold_focus,
    # Template propagation
    propagate_template_saved as _propagate_template_saved,
    # Instance actions
    instances_selection_changed as _instances_selection_changed,
    instance_saved as _instance_saved,
    # Lifecycle
    lifecycle_set_game as _lifecycle_set_game,
    lifecycle_toggle_visible as _lifecycle_toggle_visible,
    # Placement
    begin_place_template as _begin_place_template_helper,
    # Manager actions
    after_delete_template as _after_delete_template_helper,
    # Orchestrator
    orchestrate_handle_event,
    orchestrate_render,
)


class SpawnerEditorController:
    """Coordinador del patrón MVC del Spawner Editor.

    - Mantiene estado en `SpawnerEditorModel`.
    - Delega input en `SpawnerEditorEventHandler`.
    - Delega renderizado en `SpawnerEditorView`.
    """

    def __init__(self, font: Optional[pygame.font.Font] = None):
        """Inicializa el controlador del Spawner Editor.

        Crea el modelo, registra controladores delegados (título, toolbars, paneles)
        y conecta callbacks entre paneles (Templates/Instances/Properties), además de
        configurar el manejador de eventos y la vista principal.

        Args:
            font: Fuente opcional para textos UI del editor.
        """
        self.model = SpawnerEditorModel()
        self.font = font
        self.game = None  # set via set_game
        # Delegates
        self.title_controller = SpawnerTitleController(self, self.model.title_model, self.font)
        # Toolbar (undo/spawner_manager/redo)
        self.spawner_toolbar = SpawnerToolbarController(self)
        # Instance Toolbar removed (actions moved to Instances list rows)
        # Spawner lists:
        # - Instances list (data/spawners/spawners_instances.json)
        self.spawner_instances = SpawnerListInstancesController()
        try:
            self.spawner_instances.editor = self
        except Exception:
            pass
        # - Manager (templates list, data/spawners/spawners_templates.json)
        self.spawner_manager = SpawnerManagerController()
        # - Instance Properties panel (details of selected entry in spawners_instances.json)
        self.instance_properties = InstancePropertiesController()
        self._instances_visible_last: bool = False
        self.events = SpawnerEditorEventHandler(self)
        self.view = SpawnerEditorView(self)
        # Tutorial overlay (created after view for alignment)
        self.tutorial = SpawnerTutorialPanelController(self, self.view)
        # Ensure default tool is the Instances list so panels are visible on startup
        try:
            self.spawner_toolbar.set_active('spawner_instances')
        except Exception:
            pass
        # Wire Add button callback from Templates list to begin placement mode
        try:
            self.spawner_manager.list_controller.on_add_template = self._begin_place_template
        except Exception:
            pass
        # Wire after-delete callback to refresh Instances panel
        try:
            self.spawner_manager.list_controller.on_after_delete_template = self._after_delete_template
        except Exception:
            pass
        # When a template is saved (e.g., trigger.radius edited), refresh live ECS configs
        try:
            self.spawner_manager.props_controller.on_template_saved = self._on_template_saved
        except Exception:
            pass
        # Wire selection change from Instances list to Instance Properties panel
        try:
            self.spawner_instances.on_selection_changed = self._on_instance_selection_changed
        except Exception:
            pass
        # Wire hold-to-focus callbacks from Instances list
        try:
            self.spawner_instances.on_start_hold_focus = self._on_start_hold_focus
            self.spawner_instances.on_end_hold_focus = self._on_end_hold_focus
        except Exception:
            pass
        # Wire persist callback from Instance Properties to refresh Instances list
        try:
            self.instance_properties.on_persist = lambda: self.spawner_instances.refresh_from_disk()
        except Exception:
            pass
        # Live update of visuals when instance overrides are saved
        try:
            self.instance_properties.on_instance_saved = self._on_instance_saved
        except Exception:
            pass
        # Track last manager visible state for pulses
        self._manager_visible_last: bool = False

    # ---- Internal state helpers are provided by `roguelike_editors.spawner.controller.ui_state` ----

    # Hold-to-focus integration ------------------------------------------------
    def _on_start_hold_focus(self, x_px: float, y_px: float) -> None:
        """Activa el modo "hold-to-focus" y centra la cámara en las coordenadas dadas."""
        _start_hold_focus(self, x_px, y_px)

    def _on_end_hold_focus(self) -> None:
        """Desactiva el "hold-to-focus" y restaura el input/cámara del juego."""
        _end_hold_focus(self)
        return

    # Template change propagation --------------------------------------------
    def _on_template_saved(self, updated_template: dict) -> None:
        """Propaga cambios del template a entidades vivas del ECS."""
        _propagate_template_saved(self, updated_template)

    # Public API ---------------------------------------------------------------
    def set_game(self, game: Any) -> None:
        """Asocia el objeto `game` al editor y lo propaga a delegados relevantes.

        Permite a componentes como `InstancePropertiesController` acceder a la cámara
        y al mundo ECS para actualizar visuales y leer estado global.

        Args:
            game: Instancia de juego que expone al menos `camera` y `ecs.ecs_world`.
        """
        _lifecycle_set_game(self, game)

    def toggle_visible(self) -> None:
        """Alterna la visibilidad del editor y aplica limpieza segura al ocultarlo.

        - Delegamos en `events.toggle_visible()` para centralizar efectos colaterales.
        - Al ocultar: cancelamos modos/flags (hold, toolbar, subpaneles) y
          restablecemos flags globales en `world.state` relacionados con input/cámara.
        """
        _lifecycle_toggle_visible(self)

    def handle_event(self, event: pygame.event.Event) -> bool:
        """Orquesta el enrutado de eventos hacia toolbars, paneles y lógica del editor.

        Orden de prioridad (aprox.):
        1) Overlays modales (Visuals Picker, Tutorial)
        2) Toolbars (principal e instancia)
        3) Panel Manager (Templates) e Instances + Properties (si visibles)
        4) Título
        5) Event handler modular (`SpawnerEditorEventHandler`)

        Además sincroniza el estado de UI/flags globales antes de despachar.

        Args:
            event: Evento de pygame recibido del bucle principal.

        Returns:
            True si algún subcomponente consumió el evento; False en caso contrario.
        """
        return orchestrate_handle_event(self, event)

    def render(self, screen: pygame.Surface) -> None:
        """Renderiza los overlays del editor y sincroniza el estado de UI cada frame.

        - Vuelve a calcular/aplicar estado de UI para tolerar cambios externos.
        - Centra la cámara durante el "hold-to-focus" si procede.
        - Dibuja la vista del editor y overlays del tutorial encima.

        Args:
            screen: Superficie de destino donde se dibuja la UI.
        """
        orchestrate_render(self, screen)

    # Internal helpers ---------------------------------------------------------
    def _begin_place_template(self, template_id: str) -> None:
        """Entra en modo de colocación para el `template_id` indicado."""
        _begin_place_template_helper(self, template_id)

    def _after_delete_template(self, template_id: str, removed_instances: int) -> None:
        """Reacciona a la eliminación de un template refrescando datos y registrando el evento."""
        _after_delete_template_helper(self, template_id, removed_instances)

    def _on_instance_selection_changed(self, selected_index: Optional[int], inst: Optional[dict]) -> None:
        """Mantiene el panel de Propiedades en sincronía con la selección en Instancias."""
        _instances_selection_changed(self, selected_index, inst)

    # Instance change propagation (live visuals) -----------------------------
    def _on_instance_saved(self, inst: dict, changed_key: Optional[str] = None) -> None:
        """Al guardar una instancia, re-enlaza su visual si cambió el `building_id`."""
        _instance_saved(self, inst, changed_key)
