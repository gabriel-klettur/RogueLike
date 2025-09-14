from __future__ import annotations

from typing import Optional
from types import SimpleNamespace
from dataclasses import dataclass
import pygame
import logging
from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.spawner.services.persistence import find_instance_in_json

from roguelike_editors.spawner.spawner_editor_model import SpawnerEditorModel
from roguelike_editors.spawner.spawner_editor_events import SpawnerEditorEventHandler
from roguelike_editors.spawner.spawner_editor_view import SpawnerEditorView
from roguelike_editors.spawner.spawner_title.spawner_title_controller import SpawnerTitleController
from roguelike_editors.spawner.spawner_toolbar.spawner_toolbar_controller import SpawnerToolbarController
from roguelike_editors.spawner.spawner_templates_panel.spawner_manager_controller import SpawnerManagerController
from roguelike_editors.spawner.spawner_instances_panel.spawner_list_instances_controller import SpawnerListInstancesController
from roguelike_editors.spawner.spawner_instance_properties_panel.instance_properties_controller import InstancePropertiesController
from roguelike_editors.spawner.spawner_instance_toolbar.spawner_instance_toolbar_controller import SpawnerInstanceToolbarController
from roguelike_editors.spawner.spawner_tutorial_panel import SpawnerTutorialPanelController


@dataclass(frozen=True)
class _UIState:
    """Snapshot of transient UI state computed from the model and toolbar.

    Centralizes visibility conditions so both `handle_event` and `render` remain
    small and consistent.
    """
    active_tool: Optional[str]
    hold: bool
    placing_active: bool
    manager_visible: bool
    instances_visible: bool


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
        # Instance Toolbar (add/remove spawner instances)
        self.instance_toolbar = SpawnerInstanceToolbarController(self)
        # Spawner lists:
        # - Instances list (data/spawners/spawners_instances.json)
        self.spawner_instances = SpawnerListInstancesController()
        # - Manager (templates list, data/spawners/spawners_templates.json)
        self.spawner_manager = SpawnerManagerController()
        # - Instance Properties panel (details of selected entry in spawners_instances.json)
        self.instance_properties = InstancePropertiesController()
        self._instances_visible_last: bool = False
        self.events = SpawnerEditorEventHandler(self)
        self.view = SpawnerEditorView(self)
        # Tutorial overlay (created after view for alignment)
        self.tutorial = SpawnerTutorialPanelController(self, self.view)
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

    # ---- Internal state helpers ---------------------------------------------
    def _compute_ui_state(self) -> _UIState:
        """Compute current UI/visibility state based on model and toolbar."""
        try:
            active_tool = getattr(getattr(self, 'spawner_toolbar', None), 'model', None)
            active_tool = getattr(active_tool, 'active_tool', None)
        except Exception:
            active_tool = None
        hold = bool(getattr(self.model, 'hold_focus_active', False))
        placing_active = bool(getattr(self.model, 'placing_template_id', None))
        manager_visible = bool(self.model.visible and (active_tool == 'spawner_manager') and not hold)
        instances_visible = bool(
            self.model.visible
            and (active_tool == 'spawner_list')
            and not hold
            and not getattr(self.model, 'add_mode_active', False)
            and not getattr(self.model, 'remove_mode_active', False)
            and not placing_active
        )
        return _UIState(
            active_tool=active_tool,
            hold=hold,
            placing_active=placing_active,
            manager_visible=manager_visible,
            instances_visible=instances_visible,
        )

    def _apply_ui_state(self, state: _UIState) -> None:
        """Synchronize panels visibility and global flags from a `_UIState`."""
        # Gate subpanels by editor visibility
        try:
            self.spawner_manager.set_visible(bool(state.manager_visible))
        except Exception:
            pass
        # Instances panel visibility (also mirrored to its model for view short-circuit)
        try:
            vis = bool(state.instances_visible)
            self.spawner_instances.model.visible = vis
        except Exception:
            pass
        # Instance Toolbar visible whenever the editor is visible
        try:
            self.instance_toolbar.model.visible = bool(self.model.visible)
        except Exception:
            pass
        # Instance Properties visibility depends on selection and Instances visibility
        try:
            sel = self.spawner_instances.get_selected_instance()
            self.instance_properties.model.visible = bool(state.instances_visible and sel is not None)
        except Exception:
            pass
        # Global world flag so gameplay input is suppressed while editor activity is present
        try:
            world = getattr(getattr(self, 'game', None), 'ecs', None)
            world = getattr(world, 'ecs_world', None)
            if world is not None and hasattr(world, 'state'):
                setattr(
                    world.state,
                    'spawner_editor_active',
                    bool(self.model.visible and (
                        state.manager_visible or state.instances_visible or state.placing_active or state.hold
                    )),
                )
        except Exception:
            pass

    def _update_tutorial_pulses(self, state: _UIState) -> None:
        """Emit one-frame tutorial pulses when panels change visibility."""
        try:
            if (
                self.model.visible and (state.active_tool == 'spawner_list') and not state.hold
                and not getattr(self.model, 'add_mode_active', False)
                and not getattr(self.model, 'remove_mode_active', False)
                and not state.placing_active and not self._instances_visible_last
            ):
                setattr(self.model, 'tutorial_instances_open_pulse', True)
        except Exception:
            pass
        try:
            if state.manager_visible and not self._manager_visible_last:
                setattr(self.model, 'tutorial_manager_open_pulse', True)
        except Exception:
            pass

    def _maybe_refresh_instances_on_first_show(self, state: _UIState) -> None:
        """Refresh instances list when the Instances tool becomes visible."""
        if (self.model.visible and (state.active_tool == 'spawner_list')) and not self._instances_visible_last:
            try:
                self.spawner_instances.refresh_from_disk()
            except Exception:
                pass

    # Hold-to-focus integration ------------------------------------------------
    def _on_start_hold_focus(self, x_px: float, y_px: float) -> None:
        self.model.hold_focus_active = True
        self.model.hold_focus_target_px = (float(x_px), float(y_px))
        # Suppress gameplay input while focusing
        try:
            world = getattr(getattr(self, 'game', None), 'ecs', None)
            world = getattr(world, 'ecs_world', None)
            if world is not None and hasattr(world, 'state'):
                setattr(world.state, 'spawner_input_suppressed', True)
                # Mark hold-focus active in global state so the main loop stops following the player
                setattr(world.state, 'spawner_hold_focus', True)
        except Exception:
            pass
        # Immediately center camera this frame so world render uses the focused position
        try:
            cam = getattr(self.game, 'camera', None)
            if cam is not None:
                cam.update(SimpleNamespace(x=float(x_px), y=float(y_px)))
        except Exception:
            pass
        # Tutorial pulse
        try:
            setattr(self.model, 'tutorial_hold_focus_started_pulse', True)
        except Exception:
            pass

    def _on_end_hold_focus(self) -> None:
        self.model.hold_focus_active = False
        self.model.hold_focus_target_px = None
        # Re-enable gameplay input
        try:
            world = getattr(getattr(self, 'game', None), 'ecs', None)
            world = getattr(world, 'ecs_world', None)
            if world is not None and hasattr(world, 'state'):
                setattr(world.state, 'spawner_input_suppressed', False)
                setattr(world.state, 'spawner_hold_focus', False)
        except Exception:
            pass
        # Tutorial pulse
        try:
            setattr(self.model, 'tutorial_hold_focus_ended_pulse', True)
        except Exception:
            pass
        return

    # Template change propagation --------------------------------------------
    def _on_template_saved(self, updated_template: dict) -> None:
        """Propagate template edits (e.g., trigger.radius) to live ECS entities.

        - Finds all entities with matching template_id
        - Re-resolves config by merging updated template with per-instance overrides
        - Updates trigger/policy/waves/spawner_type and recalculates cooldown_frames
        """
        try:
            world = getattr(getattr(self, 'game', None), 'ecs', None)
            world = getattr(world, 'ecs_world', None)
            if not world:
                return
            t_id = str(updated_template.get('id')) if isinstance(updated_template, dict) else None
            if not t_id:
                return
            comps = getattr(world, 'components', {})
            if 'SpawnerConfig' not in comps:
                return
            for eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
                try:
                    cfg = comps['SpawnerConfig'][eid]
                except Exception:
                    continue
                try:
                    if str(getattr(cfg, 'template_id', '')) != t_id:
                        continue
                except Exception:
                    continue
                # Compute local tile for lookup
                try:
                    zone = getattr(cfg, 'zone', 'lobby')
                    off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
                    gx, gy = getattr(cfg, 'anchor_tile', (0, 0))
                    local_tile = (int(gx - off_x), int(gy - off_y))
                except Exception:
                    zone = getattr(cfg, 'zone', 'lobby')
                    local_tile = (0, 0)
                # Fetch instance overrides (if any)
                try:
                    _, _, overrides = find_instance_in_json(t_id, zone, local_tile)
                except Exception:
                    overrides = None
                # Build merged config like placement system
                trigger = dict(updated_template.get('trigger', {})) if isinstance(updated_template, dict) else {}
                policy = dict(updated_template.get('policy', {})) if isinstance(updated_template, dict) else {}
                waves = list(updated_template.get('waves', [])) if isinstance(updated_template, dict) else []
                spawner_type = updated_template.get('spawner_type', getattr(cfg, 'spawner_type', 'invisible')) if isinstance(updated_template, dict) else getattr(cfg, 'spawner_type', 'invisible')
                if isinstance(overrides, dict):
                    for k, v in overrides.items():
                        try:
                            if k.startswith('trigger.'):
                                trigger[k.split('.', 1)[1]] = v
                            elif k.startswith('policy.'):
                                policy[k.split('.', 1)[1]] = v
                            elif k == 'spawner_type':
                                spawner_type = v
                        except Exception:
                            continue
                # Recompute cooldown frames from policy
                try:
                    from roguelike_engine.config import config as _cfg
                    fps = getattr(_cfg, 'FPS', 60)
                    cooldown_s = float(policy.get('cooldown_s', 10.0))
                    cooldown_frames = int(round(cooldown_s * fps))
                except Exception:
                    cooldown_frames = getattr(cfg, 'cooldown_frames', 0)
                # Apply updates in-place
                try:
                    cfg.trigger = trigger
                    cfg.policy = policy
                    if isinstance(waves, list):
                        cfg.waves = waves
                    cfg.spawner_type = spawner_type
                    cfg.cooldown_frames = cooldown_frames
                except Exception:
                    pass
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
        # Allow Instance Properties controller to access game (camera/world)
        try:
            if hasattr(self, 'instance_properties') and hasattr(self.instance_properties, 'set_game'):
                self.instance_properties.set_game(game)
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
            # Cancel hold-to-focus if active
            self.model.hold_focus_active = False
            self.model.hold_focus_target_px = None
            # Clear any global state flags set during hold so camera/input restore
            try:
                world = getattr(getattr(self, 'game', None), 'ecs', None)
                world = getattr(world, 'ecs_world', None)
                if world is not None and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', False)
                    setattr(world.state, 'spawner_hold_focus', False)
            except Exception:
                pass
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
            # If the Visuals Picker overlay is open, delegate to it FIRST so it behaves modally
            try:
                ip = getattr(self, 'instance_properties', None)
                if ip is not None and getattr(getattr(ip, 'model', None), 'visuals_picker_open', False):
                    # Obtain camera if available
                    try:
                        cam = getattr(self, 'game', None)
                        cam = getattr(cam, 'camera', None)
                    except Exception:
                        cam = None
                    handled = False
                    try:
                        handled = bool(ip.handle_visuals_picker_event(event, cam))
                    except Exception:
                        handled = False
                    # Always consume common input types while overlay is open to avoid UI underneath reacting
                    if handled:
                        return True
                    if event.type in (
                        pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION,
                        pygame.MOUSEWHEEL, pygame.KEYDOWN, pygame.KEYUP,
                    ):
                        return True
            except Exception:
                pass
            # Compute/apply UI state and side effects
            state = self._compute_ui_state()
            self._apply_ui_state(state)
            self._update_tutorial_pulses(state)
            self._maybe_refresh_instances_on_first_show(state)
            self._instances_visible_last = bool(self.model.visible and (state.active_tool == 'spawner_list'))
            self._manager_visible_last = bool(state.manager_visible)
            # Route Tutorial panel early so it can consume ESC and clicks inside
            try:
                if hasattr(self, 'tutorial') and self.tutorial.handle_event(event):
                    return True
            except Exception:
                pass
            # Route toolbar first to consume UI interactions
            if hasattr(self, 'spawner_toolbar') and self.spawner_toolbar.handle_event(event):
                return True
            # Instance toolbar should still be interactive during Add Mode (to allow cancel)
            try:
                if getattr(getattr(self.instance_toolbar, 'model', None), 'visible', False):
                    if self.instance_toolbar.handle_event(event):
                        return True
            except Exception:
                pass
            # Manager captures UI next if visible
            if getattr(self.spawner_manager.model, 'visible', False):
                if self.spawner_manager.handle_event(event):
                    return True
            # Instances list captures next if visible (not during Add Mode, Remove Mode, or Placement)
            # Even if hidden due to hold, we still route events so it can receive MOUSEBUTTONUP to end hold
            placing_active = bool(getattr(self.model, 'placing_template_id', None))
            if (
                self.model.visible
                and (active_tool == 'spawner_list')
                and not getattr(self.model, 'add_mode_active', False)
                and not getattr(self.model, 'remove_mode_active', False)
                and not placing_active
            ):
                # Instance toolbar already handled above
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
            state = self._compute_ui_state()
            self._apply_ui_state(state)
            if (self.model.visible and (state.active_tool == 'spawner_list')) and not self._instances_visible_last:
                try:
                    self.spawner_instances.refresh_from_disk()
                except Exception:
                    pass
            self._instances_visible_last = bool(self.model.visible and (state.active_tool == 'spawner_list'))
            # While holding focus, keep camera centered on target
            if state.hold and getattr(self.model, 'hold_focus_target_px', None) is not None:
                try:
                    cam = getattr(self.game, 'camera', None)
                    if cam is not None:
                        tx, ty = self.model.hold_focus_target_px
                        zoom = getattr(cam, 'zoom', 1.0) or 1.0
                        cam.offset_x = float(tx) - (cam.screen_width / (2 * zoom))
                        cam.offset_y = float(ty) - (cam.screen_height / (2 * zoom))
                except Exception:
                    pass
            self.view.render(screen)
            # Render tutorial overlay on top
            try:
                if hasattr(self, 'tutorial'):
                    self.tutorial.render(screen)
            except Exception:
                pass
        except Exception:
            pass

    # Internal helpers ---------------------------------------------------------
    def _begin_place_template(self, template_id: str) -> None:
        """Enter placement mode for the provided spawner template id."""
        try:
            self.model.visible = True
            self.model.placing_template_id = str(template_id)
            # Keep Add Mode flag active to blink the Add button until placement completes/cancels
            # Ensure hold-to-focus is cleared so view doesn't hide overlays
            try:
                self.model.hold_focus_active = False
                self.model.hold_focus_target_px = None
                world = getattr(getattr(self, 'game', None), 'ecs', None)
                world = getattr(world, 'ecs_world', None)
                if world is not None and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_hold_focus', False)
            except Exception:
                pass
            # Hide Templates Manager by clearing active tool
            try:
                tb = getattr(self, 'spawner_toolbar', None)
                if tb and getattr(tb, 'model', None) is not None:
                    tb.model.active_tool = None
            except Exception:
                pass
            # Do not stop blinking here; toolbar model keeps add_mode_active True until placement ends
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
        # Tutorial pulse on selection
        try:
            if inst is not None:
                setattr(self.model, 'tutorial_instance_selected_pulse', True)
        except Exception:
            pass

    # Instance change propagation (live visuals) -----------------------------
    def _on_instance_saved(self, inst: dict, changed_key: Optional[str] = None) -> None:
        """When an instance is saved, relink its visual based on building_id changes.

        Supports:
        - building_id at instance root or under overrides.building_id
        """
        # Quick filter: if a specific key changed, react only to building_id edits
        try:
            if changed_key is not None:
                ck = str(changed_key)
                if ('building_id' not in ck):
                    return
        except Exception:
            pass
        try:
            world = getattr(getattr(self, 'game', None), 'ecs', None)
            world = getattr(world, 'ecs_world', None)
            if not world:
                return
            blds = getattr(world, 'buildings', None) or []
            # Resolve instance id and desired building id
            inst_id = None
            try:
                inst_id = str(inst.get('id')) if inst and inst.get('id') is not None else None
            except Exception:
                inst_id = None
            bld_id = None
            try:
                ov = inst.get('overrides') if isinstance(inst, dict) else None
                if isinstance(ov, dict) and ov.get('building_id') is not None:
                    bld_id = int(ov.get('building_id'))
                elif inst.get('building_id') is not None:
                    bld_id = int(inst.get('building_id'))
            except Exception:
                # keep as None on parse errors
                pass
            if inst_id is None or bld_id is None:
                return
            # Find target building by id
            target = None
            for ob in blds:
                try:
                    if getattr(ob, 'id', None) == bld_id:
                        target = ob
                        break
                except Exception:
                    continue
            if target is None:
                return
            # Tag and link
            try:
                setattr(target, '_is_spawner_visual', True)
                setattr(target, 'spawner_instance_id', inst_id)
                setattr(target, 'spawn_id', inst_id)
                # Record back-link to ECS entity if present
                comps = getattr(world, 'components', {})
                if 'SpawnerConfig' in comps:
                    for eid in world.get_entities_with('SpawnerConfig'):
                        try:
                            cfg = comps['SpawnerConfig'][eid]
                            # Match by instance id if available via existing spawn_id
                            if getattr(target, 'spawn_id', None) == inst_id:
                                setattr(target, '_spawner_eid', eid)
                                setattr(target, '_world_ref', world)
                                break
                        except Exception:
                            continue
            except Exception:
                pass
        except Exception:
            pass
