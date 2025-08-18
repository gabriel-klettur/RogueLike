from __future__ import annotations
import logging

from roguelike_editors.spawner.services.persistence import (
    load_spawners_json,
    write_spawners_json,
    load_instances_json,
    write_instances_json,
)

LOGGER = logging.getLogger("roguelike_editors.spawner.spawner_templates_panel.list_templates.delete.controller")


class ListTemplatesDeleteController:
    def ask_confirm_delete(self, parent_controller, index: int) -> None:
        """Open confirmation modal for the template at the given row index."""
        try:
            model = parent_controller.model
            dmodel = parent_controller.delete_model
            templates = getattr(parent_controller, "_templates", [])
            if not (0 <= index < len(templates)):
                return
            tpl = templates[index]
            tpl_id = str(tpl.get("id"))
            dmodel.confirm_visible = True
            dmodel.confirm_target_index = int(index)
            dmodel.confirm_target_id = tpl_id
            # Count instances that reference this template to warn about cascade delete
            try:
                inst = load_instances_json()
                count = sum(1 for it in inst if str(it.get('template_id')) == tpl_id)
            except Exception:
                count = 0
            if count > 0:
                dmodel.confirm_text = (
                    f"¿Eliminar template '{tpl_id}'?\n"
                    f"Se eliminarán también {count} instancia(s) que dependen de este template.\n"
                    f"Esta acción no se puede deshacer."
                )
            else:
                dmodel.confirm_text = (
                    f"¿Eliminar template '{tpl_id}'?\n"
                    f"Esta acción no se puede deshacer."
                )
        except Exception:
            # Ensure modal not half-open
            dmodel = parent_controller.delete_model
            dmodel.confirm_visible = False
            dmodel.confirm_target_index = None
            dmodel.confirm_target_id = None
            dmodel.confirm_text = ""

    def confirm_yes(self, parent_controller) -> None:
        """Execute deletion of the targeted template and close modal."""
        dmodel = parent_controller.delete_model
        target_id = getattr(dmodel, "confirm_target_id", None)
        if not target_id:
            self.confirm_no(parent_controller)
            return
        try:
            # 1) Remove the template from spawners.json
            data = load_spawners_json()
            new_list = [t for t in data if str(t.get("id")) != target_id]
            write_spawners_json(new_list)
            # 2) Cascade delete: remove all instances that reference this template
            try:
                inst = load_instances_json()
            except Exception:
                inst = []
            kept = [it for it in inst if str(it.get('template_id')) != target_id]
            removed_count = max(0, len(inst) - len(kept))
            if removed_count > 0:
                write_instances_json(kept)
            # Refresh UI
            prev_idx = getattr(parent_controller.model, "selected_index", None)
            parent_controller.refresh_from_disk()
            # Adjust selection similar to FSM
            model = parent_controller.model
            if prev_idx is not None:
                try:
                    # If we deleted the selected, move to previous/nearest valid
                    if 0 <= int(prev_idx) < len(parent_controller._templates):
                        if str(parent_controller._templates[int(prev_idx)].get("id")) == target_id:
                            new_sel = min(int(prev_idx), max(0, len(model.items) - 1))
                            model.selected_index = new_sel if model.items else None
                        else:
                            old_pos = int(prev_idx)
                            model.selected_index = min(old_pos, max(0, len(model.items) - 1)) if model.items else None
                    else:
                        model.selected_index = model.selected_index if model.items else None
                except Exception:
                    model.selected_index = model.selected_index if model.items else None
            else:
                model.selected_index = model.selected_index if model.items else None
            dmodel.last_deleted_id = target_id
            dmodel.last_deleted_instances_count = removed_count
            # Notify parent list controller observers (e.g., to refresh instances panel)
            try:
                cb = getattr(parent_controller, 'on_after_delete_template', None)
                if cb:
                    cb(target_id, removed_count)
            except Exception:
                pass
        except Exception as ex:
            LOGGER.exception("[SpawnerTemplates] delete failed for id=%s: %s", target_id, ex)
        finally:
            self.confirm_no(parent_controller)

    def confirm_no(self, parent_controller) -> None:
        """Dismiss the confirmation modal and clear target info."""
        dmodel = parent_controller.delete_model
        dmodel.confirm_visible = False
        dmodel.confirm_target_index = None
        dmodel.confirm_target_id = None
        dmodel.confirm_text = ""


__all__ = ["ListTemplatesDeleteController"]
