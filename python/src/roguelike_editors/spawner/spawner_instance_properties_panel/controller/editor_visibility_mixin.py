from __future__ import annotations

from typing import Optional


class EditorVisibilityMixin:
    def _get_world(self):
        # Delegated to visuals
        return getattr(self.visuals, '_get_world')()

    def _iter_building_entities(self):
        # Delegated to visuals
        yield from self.visuals._iter_building_entities()

    def _find_building_entity_by_id(self, bid: int):
        return self.visuals._find_building_entity_by_id(int(bid))

    def _ensure_building_loaded(self, bid: int) -> None:
        # Delegated to visuals
        self.visuals._ensure_building_loaded(int(bid))

    def _set_building_visible(self, bid: int, visible: bool) -> None:
        # Delegated to visuals
        self.visuals._set_building_visible(int(bid), bool(visible))

    def _tag_and_reveal_building(self, bid: int, state_key: str) -> None:
        # Delegated to visuals
        self.visuals.tag_and_reveal_building(int(bid), str(state_key))

    def is_visual_building_visible(self, state_key: str) -> bool:
        return self.visuals.is_building_visible_for_state(str(state_key))

    def toggle_visual_building_visibility(self, state_key: str) -> None:
        self.visuals.toggle_building_visibility_for_state(str(state_key))

    def _remove_building_entity_by_id(self, bid: int) -> bool:
        """Hard-remove a Building object with the given id from the running world/editor.
        Returns True if any object was removed.
        """
        removed_any = False
        # Remove from ECS world list
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
        # Remove from editor/game registry
        try:
            ents = getattr(self, 'game', None)
            ents = getattr(ents, 'entities', None)
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
            pass
        # Best-effort: clear any cached visibility flags
        try:
            if int(bid) in self.visuals.model.editor_visibility:
                self.visuals.model.editor_visibility.pop(int(bid), None)
        except (AttributeError, TypeError, ValueError):
            pass
        return removed_any
