from __future__ import annotations

from typing import Optional
import logging

from roguelike_editors.spawner.spawner_instance_properties_panel.visuals.visuals_model import VisualsModel
from roguelike_editors.spawner.spawner_instance_properties_panel.visuals.visuals_view import VisualsView
from roguelike_editors.spawner.spawner_instance_properties_panel.visuals.visuals_events import VisualsEvents
from roguelike_editors.spawner.spawner_instance_properties_panel.visuals.services import (
    mapping as mapping_svc,
    world as world_svc,
    building_loader as loader_svc,
    camera as camera_svc,
    tagging as tagging_svc,
    visibility as visibility_svc,
    hit_test as hit_test_svc,
)

logger = logging.getLogger(__name__)


class VisualsController:
    """Feature controller for the Visuals table inside Instance Properties.

    It owns its own MVC (model/view/events) but delegates data mutations and
    persistence to the parent InstancePropertiesController.
    """

    def __init__(self, parent_controller) -> None:
        # Keep a dynamic reference to the parent controller without importing it here
        self.parent = parent_controller
        self.model = VisualsModel()
        self.view = VisualsView(self)
        self.events = VisualsEvents()

    # --- Convenience accessors to parent data --------------------------------
    def get_visuals_rows(self):
        return self.parent.get_visuals_rows()

    def get_visuals(self) -> dict:
        return getattr(self.parent.model, 'visuals', {}) or {}

    def get_visuals_key_map(self) -> dict:
        return getattr(self.parent.model, 'visuals_key_map', {}) or {}

    def get_text_input(self):
        return self.parent.get_text_input()

    # --- Visuals mapping helpers --------------------------------------------
    def _resolve_json_key_for_state(self, state_key: str) -> str:
        """Given a display state name (TitleCase), resolve the actual JSON key used."""
        return mapping_svc.resolve_json_key_for_state(self, state_key)

    def _get_mapping_entry_for_state(self, state_key: str):
        """Return the raw mapping value from model.visuals for the given state (dict | int | None)."""
        return mapping_svc.get_mapping_entry_for_state(self, state_key)

    def _get_instance_id_for_state(self, state_key: str) -> int | None:
        """Extract instance_id as int from the mapping of a state, if present and valid."""
        return mapping_svc.get_instance_id_for_state(self, state_key)

    def _get_template_id_for_state(self, state_key: str) -> int | None:
        """Best-effort template_id resolution for a state: prefer explicit mapping, fallback to building index."""
        return mapping_svc.get_template_id_for_state(self, state_key)

    # --- World/buildings helpers ---------------------------------------------
    def _get_world(self):
        return world_svc.get_world(self)

    def _iter_building_entities(self):
        yield from world_svc.iter_building_entities(self)

    def _find_building_entity_by_id(self, bid: int):
        return world_svc.find_building_entity_by_id(self, int(bid))

    def _get_selected_spawner_id(self) -> str | None:
        return world_svc.get_selected_spawner_id(self)

    def _find_visual_entity_for_state(self, state_key: str):
        """Best-effort resolver for the visual entity of a given state."""
        return world_svc.find_visual_entity_for_state(self, state_key)

    def center_camera_on_state(self, state_key: str) -> None:
        """Center camera on the building instance mapped to the given visuals state key."""
        return camera_svc.center_camera_on_state(self, state_key)

    # JSON helpers now live in services

    def _ensure_building_loaded(self, bid: int) -> None:
        """Ensure building with id 'bid' is loaded into the world (editor-only)."""
        return loader_svc.ensure_building_loaded(self, int(bid))

    def _set_building_visible(self, bid: int, visible: bool) -> None:
        visibility_svc.set_building_visible(self, int(bid), bool(visible))

    def tag_and_reveal_building(self, bid: int, state_key: str) -> None:
        tagging_svc.tag_and_reveal_building(self, int(bid), str(state_key))

    def tag_building_for_state(self, bid: int, state_key: str, *, visible: bool = True, center: bool = False) -> None:
        """Tag a building as a spawner visual for a given state."""
        tagging_svc.tag_building_for_state(self, int(bid), str(state_key), visible=bool(visible), center=bool(center))

    def reveal_all_mapped_buildings(self) -> None:
        """Ensure all mapped buildings are present, tagged, and visible as per editor cache."""
        visuals = dict(getattr(self.parent.model, 'visuals', {}) or {})
        vis_cache = getattr(self.model, 'editor_visibility', {}) or {}
        ipc = self.parent
        for state_key, entry in visuals.items():
            bid = None
            tpl_id = None
            try:
                if isinstance(entry, dict):
                    raw_bid = entry.get('instance_id') or entry.get('id') or entry.get('building_instance_id')
                    bid = int(raw_bid) if raw_bid is not None else None
                    tpl_id = int(entry.get('template_id')) if entry.get('template_id') is not None else None
                else:
                    bid = int(entry)
            except Exception:
                bid = None
            ob = None
            if bid is not None:
                ob = world_svc.find_building_entity_by_id(self, int(bid))
                if ob is None:
                    try:
                        loader_svc.ensure_building_loaded(self, int(bid))
                        ob = world_svc.find_building_entity_by_id(self, int(bid))
                    except Exception:
                        ob = None
            if ob is None and tpl_id is not None:
                try:
                    if not hasattr(ipc.model, 'visuals_pending_templates') or ipc.model.visuals_pending_templates is None:
                        ipc.model.visuals_pending_templates = {}
                except Exception:
                    ipc.model.visuals_pending_templates = {}
                try:
                    ipc.model.visuals_pending_templates[str(state_key)] = str(int(tpl_id))
                except Exception:
                    ipc.model.visuals_pending_templates[str(state_key)] = str(tpl_id)
                new_id = None
                try:
                    new_id = ipc.add_building_instance_for_visual(str(state_key), reveal=False)
                except Exception:
                    new_id = None
                if new_id is not None:
                    visuals[str(state_key)] = {'instance_id': int(new_id), 'template_id': int(tpl_id)}
                    bid = int(new_id)
            if bid is None:
                continue
            visible = bool(vis_cache.get(int(bid), True))
            tagging_svc.tag_building_for_state(self, int(bid), str(state_key), visible=visible, center=False)
        try:
            ipc.model.visuals = visuals
            if isinstance(ipc.model.selected_instance, dict):
                ipc.model.selected_instance['visuals'] = visuals
        except Exception:
            pass

    def is_building_visible_for_state(self, state_key: str) -> bool:
        return visibility_svc.is_visible_for_state(self, state_key)

    def toggle_building_visibility_for_state(self, state_key: str) -> None:
        """Toggle only the editor rendering visibility for the building mapped to state_key."""
        visibility_svc.toggle_for_state(self, state_key)

    # --- Hit-testing ----------------------------------------------------------
    def pick_visual_building_under_cursor(self, mx: int, my: int):
        """Return the visual building entity under cursor for the selected spawner, or None."""
        return hit_test_svc.pick_building_under_cursor(self, int(mx), int(my))

    def _is_spawner_visual_building_id(self, bid: int) -> bool:
        """Return True if building id appears linked to any spawner by persisted data."""
        return hit_test_svc.is_spawner_visual_building_id(self, int(bid))

    def open_picker(self, state_key: str) -> None:
        """Delegates to parent to open the Visuals Picker for the given state."""
        self.parent.open_visuals_picker_for_state(state_key)

    # ... (rest of the code remains the same)

    # --- Delegations to parent InstancePropertiesController ------------------
    def begin_edit_visual(self, state_key: str) -> None:
        """Begin inline editing of the Template cell for the given visuals state.

        VisualsEvents expects this symbol on the visuals controller; delegate
        to the parent controller which owns the edit TextInput and commit/cancel
        logic. This enables clicking the Template cell to start editing.
        """
        try:
            self.parent.begin_edit_visual(state_key)
        except AttributeError:
            # Defensive: if parent does not expose the method, ignore gracefully
            logger.debug("VisualsController.begin_edit_visual: parent missing method", exc_info=True)

    def clear_visual_for_state(self, state_key: str) -> None:
        """Clear the mapping for a given visuals state (invoked by Clear 'X' button).

        Delegates to the parent controller which performs persistence, optional
        strict cleanup of orphaned building instances, and UI refresh.
        """
        try:
            self.parent.clear_visual_for_state(state_key)
        except AttributeError:
            logger.debug("VisualsController.clear_visual_for_state: parent missing method", exc_info=True)

    # --- Hard removal helper --------------------------------------------------
    def _remove_building_entity_by_id(self, bid: int) -> bool:
        """Remove any Building object with id 'bid' from the live world and editor lists.
        Returns True if any object was removed.
        """
        removed_any = False
        # ECS world list
        try:
            world = self._get_world()
            if world is not None and hasattr(world, 'buildings') and isinstance(world.buildings, list):
                arr = world.buildings
                for i in range(len(arr) - 1, -1, -1):
                    try:
                        if getattr(arr[i], 'id', None) == int(bid):
                            arr.pop(i)
                            removed_any = True
                    except (AttributeError, TypeError, ValueError):
                        continue
        except AttributeError:
            pass

        # Editor/game registry if present
        try:
            ents = getattr(self.parent.game, 'entities', None)
            if ents is not None and hasattr(ents, 'buildings') and isinstance(ents.buildings, list):
                arr2 = ents.buildings
                for i in range(len(arr2) - 1, -1, -1):
                    try:
                        if getattr(arr2[i], 'id', None) == int(bid):
                            arr2.pop(i)
                            removed_any = True
                    except (AttributeError, TypeError, ValueError):
                        continue
        except AttributeError:
            logger.debug("VisualsController._remove_building_entity_by_id: error scanning editor/game registry", exc_info=True)

        return removed_any


__all__ = ["VisualsController"]
