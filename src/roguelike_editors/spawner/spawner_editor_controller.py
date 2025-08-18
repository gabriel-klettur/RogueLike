from __future__ import annotations

from typing import Optional
import pygame
import logging

from roguelike_editors.spawner.spawner_editor_model import SpawnerEditorModel
from roguelike_editors.spawner.spawner_editor_events import SpawnerEditorEventHandler
from roguelike_editors.spawner.spawner_editor_view import SpawnerEditorView
from roguelike_editors.spawner.spawner_title.spawner_title_controller import SpawnerTitleController
from roguelike_editors.spawner.spawner_toolbar.spawner_toolbar_controller import SpawnerToolbarController
from roguelike_editors.spawner.spawner_templates_panel.spawner_manager_controller import SpawnerManagerController
from roguelike_editors.spawner.spawner_instances_panel.spawner_list_instances_controller import SpawnerListInstancesController
from roguelike_editors.spawner.spawner_instance_properties_panel.instance_properties_controller import InstancePropertiesController


class SpawnerEditorController:
    """Coordinator for the Spawner Editor MVC.

    - Holds state in `SpawnerEditorModel`
    - Delegates input to `SpawnerEditorEventHandler`
    - Delegates rendering to `SpawnerEditorView`
    """

    def __init__(self, font: Optional[pygame.font.Font] = None):
        self.model = SpawnerEditorModel()
        self.font = font
        self.game = None  # set via set_game
        # Delegates
        self.title_controller = SpawnerTitleController(self, self.model.title_model, self.font)
        # Toolbar (undo/spawner_manager/redo)
        self.spawner_toolbar = SpawnerToolbarController(self)
        # Spawner lists:
        # - Instances list (data/spawners/instances.json)
        self.spawner_instances = SpawnerListInstancesController()
        # - Manager (templates list, data/spawners/spawners.json)
        self.spawner_manager = SpawnerManagerController()
        # - Instance Properties panel (details of selected entry in instances.json)
        self.instance_properties = InstancePropertiesController()
        self._instances_visible_last: bool = False
        self.events = SpawnerEditorEventHandler(self)
        self.view = SpawnerEditorView(self)
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
        # Wire selection change from Instances list to Instance Properties panel
        try:
            self.spawner_instances.on_selection_changed = self._on_instance_selection_changed
        except Exception:
            pass
        # Wire persist callback from Instance Properties to refresh Instances list
        try:
            self.instance_properties.on_persist = lambda: self.spawner_instances.refresh_from_disk()
        except Exception:
            pass

    # Public API ---------------------------------------------------------------
    def set_game(self, game) -> None:
        self.game = game
        # Keep delegate in sync
        try:
            self.events.set_game(game)
        except Exception:
            pass

    def toggle_visible(self) -> None:
        # Delegate to event handler for consistent cleanup
        try:
            self.events.toggle_visible()
        except Exception:
            # Fallback: minimal toggle
            self.model.visible = not self.model.visible
        # When hiding, also clear toolbar active tool and subpanels visibility
        if not getattr(self.model, 'visible', False):
            try:
                tb = getattr(getattr(self, 'spawner_toolbar', None), 'model', None)
                if tb is not None:
                    tb.active_tool = None
            except Exception:
                pass
            try:
                # Ensure subpanels are hidden so no flags re-enable suppression
                self.spawner_manager.set_visible(False)
            except Exception:
                pass
            try:
                self.spawner_instances.model.visible = False
            except Exception:
                pass
            try:
                self.instance_properties.model.visible = False
            except Exception:
                pass

    def handle_event(self, event: pygame.event.Event) -> bool:
        try:
            # Sync visibility with toolbar active tool
            active_tool = getattr(getattr(self, 'spawner_toolbar', None), 'model', None)
            active_tool = getattr(active_tool, 'active_tool', None)
            # Gate subpanels by editor visibility
            self.spawner_manager.set_visible(self.model.visible and active_tool == 'spawner_manager')
            instances_visible = bool(self.model.visible and (active_tool == 'spawner_list'))
            # Keep model visible in sync for view short-circuit
            try:
                self.spawner_instances.model.visible = bool(instances_visible)
            except Exception:
                pass
            # Sync Instance Properties visibility with active tool and selection
            try:
                sel = self.spawner_instances.get_selected_instance()
                self.instance_properties.model.visible = bool(instances_visible and sel is not None)
            except Exception:
                pass
            # Expose global flag so gameplay input can be suppressed while editor is active
            try:
                world = getattr(getattr(self, 'game', None), 'ecs', None)
                world = getattr(world, 'ecs_world', None)
                if world is not None and hasattr(world, 'state'):
                    placing_active = bool(getattr(self.model, 'placing_template_id', None))
                    setattr(
                        world.state,
                        'spawner_editor_active',
                        bool(self.model.visible and (self.spawner_manager.model.visible or instances_visible or placing_active)),
                    )
            except Exception:
                pass
            # Refresh instances list on first show
            if instances_visible and not self._instances_visible_last:
                try:
                    self.spawner_instances.refresh_from_disk()
                except Exception:
                    pass
            self._instances_visible_last = instances_visible
            # Route toolbar first to consume UI interactions
            if hasattr(self, 'spawner_toolbar') and self.spawner_toolbar.handle_event(event):
                return True
            # Manager captures UI next if visible
            if getattr(self.spawner_manager.model, 'visible', False):
                if self.spawner_manager.handle_event(event):
                    return True
            # Instances list captures next if visible
            if instances_visible:
                if self.spawner_instances.handle_event(event):
                    return True
                # Then route to Instance Properties if visible
                if hasattr(self, 'instance_properties') and getattr(getattr(self.instance_properties, 'model', None), 'visible', False):
                    if self.instance_properties.handle_event(event):
                        return True
            # Title panel (currently no events, but keep parity with other editors)
            if hasattr(self, 'title_controller') and self.title_controller.handle_event(event):
                return True
            return self.events.handle_event(event)
        except Exception:
            return False

    def render(self, screen: pygame.Surface) -> None:
        try:
            # Sync visibility again before rendering (in case of external toggles)
            active_tool = getattr(getattr(self, 'spawner_toolbar', None), 'model', None)
            active_tool = getattr(active_tool, 'active_tool', None)
            # Gate subpanels by editor visibility
            self.spawner_manager.set_visible(self.model.visible and active_tool == 'spawner_manager')
            instances_visible = bool(self.model.visible and (active_tool == 'spawner_list'))
            # Keep model visible in sync for view short-circuit
            try:
                self.spawner_instances.model.visible = bool(instances_visible)
            except Exception:
                pass
            # Expose global flag so gameplay input can be suppressed while editor is active
            try:
                world = getattr(getattr(self, 'game', None), 'ecs', None)
                world = getattr(world, 'ecs_world', None)
                if world is not None and hasattr(world, 'state'):
                    placing_active = bool(getattr(self.model, 'placing_template_id', None))
                    setattr(
                        world.state,
                        'spawner_editor_active',
                        bool(self.model.visible and (self.spawner_manager.model.visible or instances_visible or placing_active)),
                    )
            except Exception:
                pass
            if instances_visible and not self._instances_visible_last:
                try:
                    self.spawner_instances.refresh_from_disk()
                except Exception:
                    pass
            self._instances_visible_last = instances_visible
            self.view.render(screen)
        except Exception:
            pass

    # Internal helpers ---------------------------------------------------------
    def _begin_place_template(self, template_id: str) -> None:
        """Enter placement mode for the provided spawner template id."""
        try:
            self.model.visible = True
            self.model.placing_template_id = str(template_id)
            # Suppress gameplay input while in placement mode
            world = getattr(getattr(self.game, 'ecs', None), 'ecs_world', None)
            if world is not None and hasattr(world, 'state'):
                setattr(world.state, 'spawner_input_suppressed', True)
        except Exception:
            pass

    def _after_delete_template(self, template_id: str, removed_instances: int) -> None:
        """React to a template deletion by refreshing the Instances list.

        Args:
            template_id: The template id that was deleted.
            removed_instances: How many instances were removed in cascade.
        """
        try:
            self.spawner_instances.refresh_from_disk()
        except Exception:
            pass
        try:
            logging.getLogger("roguelike_editors.spawner").info(
                "[SpawnerEditor] Template '%s' deleted. Removed %d instance(s).",
                template_id,
                int(removed_instances or 0),
            )
        except Exception:
            pass

    def _on_instance_selection_changed(self, selected_index: Optional[int], inst: Optional[dict]) -> None:
        """Keep Instance Properties panel in sync with Instances list selection."""
        try:
            # Only show properties when Instances tool is active
            active_tool = getattr(getattr(self, 'spawner_toolbar', None), 'model', None)
            active_tool = getattr(active_tool, 'active_tool', None)
            instances_visible = (active_tool == 'spawner_list')
        except Exception:
            instances_visible = True
        try:
            self.instance_properties.set_instance(inst, index=selected_index)
            # Visibility is controlled by the presence of a selection and the active tool
            self.instance_properties.model.visible = bool(instances_visible and inst is not None)
        except Exception:
            pass
