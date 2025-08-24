from __future__ import annotations
import logging

from roguelike_editors.fsm.services.fsm_persistence import (
    default_sets_path,
    load_sets,
    save_sets,
)
from roguelike_editors.fsm.services.fsm_runtime_bridge import reload as bridge_reload

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_sets_panel.delete.controller")


class SetsPanelDeleteController:
    def ask_confirm_delete(self, parent_controller, index: int) -> None:
        """Open confirmation modal for the set at the given row index."""
        try:
            model = parent_controller.model
            dmodel = parent_controller.delete_model
            if not (0 <= index < len(model.items)):
                return
            set_id = model.items[index]
            dmodel.confirm_visible = True
            dmodel.confirm_target_index = int(index)
            dmodel.confirm_target_id = set_id
            dmodel.confirm_text = f"Delete set '{set_id}'?\nThis action cannot be undone."
        except Exception:
            # Ensure modal not half-open
            dmodel = parent_controller.delete_model
            dmodel.confirm_visible = False
            dmodel.confirm_target_index = None
            dmodel.confirm_target_id = None
            dmodel.confirm_text = ""

    def confirm_yes(self, parent_controller) -> None:
        """Execute deletion of the targeted set and close modal."""
        model = parent_controller.model
        dmodel = parent_controller.delete_model
        target_id = getattr(dmodel, 'confirm_target_id', None)
        if not target_id:
            self.confirm_no(parent_controller)
            return
        try:
            data = load_sets(default_sets_path())
            sets = data.get('sets') or []
            # Remove by id
            new_sets = [s for s in sets if s.get('id') != target_id]
            data['sets'] = new_sets
            save_sets(data, default_sets_path())
            try:
                bridge_reload()
            except Exception:
                pass
            # Update UI list and selection
            prev_idx = getattr(model, 'selected_index', None)
            parent_controller._refresh_items_from_disk()
            # Adjust selection: if deleting the selected, move to previous index
            if prev_idx is not None:
                try:
                    if 0 <= int(prev_idx) < len(model.items) and model.items[int(prev_idx)] == target_id:
                        new_sel = min(int(prev_idx), max(0, len(model.items) - 1))
                        model.selected_index = new_sel if model.items else None
                    else:
                        try:
                            old_pos = int(prev_idx)
                            model.selected_index = min(old_pos, max(0, len(model.items) - 1)) if model.items else None
                        except Exception:
                            pass
                except Exception:
                    model.selected_index = model.selected_index if model.items else None
            else:
                model.selected_index = model.selected_index if model.items else None
            # Record last deleted id for UX/telemetry if needed
            dmodel.last_deleted_id = target_id
        except Exception as ex:
            LOGGER.exception("[SetsPanel] delete failed for set_id=%s: %s", target_id, ex)
        finally:
            self.confirm_no(parent_controller)

    def confirm_no(self, parent_controller) -> None:
        """Dismiss the confirmation modal and clear target info."""
        dmodel = parent_controller.delete_model
        dmodel.confirm_visible = False
        dmodel.confirm_target_index = None
        dmodel.confirm_target_id = None
        dmodel.confirm_text = ""


__all__ = ["SetsPanelDeleteController"]
