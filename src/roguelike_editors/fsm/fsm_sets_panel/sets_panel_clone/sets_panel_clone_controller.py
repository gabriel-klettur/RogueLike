from __future__ import annotations
from typing import Optional
import logging
import copy

from roguelike_editors.fsm.services.fsm_persistence.fsm_persistence import (
    default_sets_path,
    load_sets,
    save_sets,
)
from roguelike_editors.fsm.services.fsm_id import new_id
from roguelike_editors.fsm.services.fsm_runtime_bridge import reload as bridge_reload

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_sets_panel.clone.controller")


class SetsPanelCloneController:
    def clone_by_index(self, parent_controller, index: int) -> Optional[str]:
        """
        Clone the set at 'index' in sets.json, persist it, refresh parent list,
        and select the new set in the parent model. Returns the new set id on success.
        """
        try:
            # Reset feedback
            try:
                parent_controller.clone_model.error = None
                parent_controller.clone_model.last_cloned_id = None
            except Exception:
                pass
            data = load_sets(default_sets_path())
            sets = data.get('sets') or []
            if not (0 <= index < len(sets)):
                return None
            src = sets[index]
            # Build new unique id based on original id prefix
            existing_ids = {s.get('id') for s in sets if isinstance(s, dict)}
            base = str(src.get('id') or 'Set')
            new_set_id = new_id(base, set(existing_ids))
            # Deep copy
            dst = copy.deepcopy(src)
            dst['id'] = new_set_id
            # Friendly label
            try:
                label = dst.get('label') or base
                dst['label'] = f"{label} (copy)"
            except Exception:
                pass
            sets.append(dst)
            data['sets'] = sets
            # Persist
            save_sets(data, default_sets_path())
            try:
                bridge_reload()
            except Exception:
                pass
            # Refresh UI list and select new item in parent
            parent_controller._refresh_items_from_disk()
            try:
                parent_controller.model.selected_index = parent_controller.model.items.index(new_set_id)
            except ValueError:
                parent_controller.model.selected_index = max(0, len(parent_controller.model.items) - 1)
            # Feedback in clone model
            try:
                parent_controller.clone_model.last_cloned_id = new_set_id
            except Exception:
                pass
            return new_set_id
        except Exception as ex:
            LOGGER.exception("[SetsPanelClone] clone failed for index=%s: %s", index, ex)
            try:
                parent_controller.clone_model.error = str(ex)
            except Exception:
                pass
            return None


__all__ = ["SetsPanelCloneController"]
