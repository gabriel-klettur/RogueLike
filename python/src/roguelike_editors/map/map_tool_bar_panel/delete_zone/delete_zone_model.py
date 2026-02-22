import logging

logger = logging.getLogger(__name__)


class DeleteZoneModel:
    """
    Model for the Delete Zone tool. Manipulates the shared MapEditorState
    to drive the delete-zone workflow (mode toggle, pending zone, confirm dialog).
    """

    def __init__(self, editor_state):
        self.editor = editor_state

    # --- Mode + dialog state mutations ---
    def enable_mode(self):
        self.editor.delete_zone_mode = True

    def disable_mode(self):
        self.editor.delete_zone_mode = False

    def disable_other_modes(self):
        # Ensure mutual exclusion with other tools
        for attr in (
            "add_zone_mode",
            "paint_tiles_mode",
            "clear_colliders_mode",
            "paint_colliders_mode",
        ):
            setattr(self.editor, attr, False)

    def toggle_mode(self) -> bool:
        new_val = not getattr(self.editor, "delete_zone_mode", False)
        self.editor.delete_zone_mode = new_val
        if new_val:
            self.disable_other_modes()
        logger.debug(f"[Toolbar/DeleteZoneModel] delete_zone_mode -> {self.editor.delete_zone_mode}")
        return new_val

    def begin_delete(self, zone_name: str) -> None:
        self.editor.pending_delete_zone = zone_name
        self.editor.confirm_delete_zone = True
        logger.debug(f"[Toolbar/DeleteZoneModel] begin delete for zone={zone_name}")

    def reset_dialog(self) -> None:
        reset_fn = getattr(self.editor, "reset_delete_dialog", None)
        if callable(reset_fn):
            reset_fn()
        else:
            self.editor.confirm_delete_zone = False
            self.editor.pending_delete_zone = None
            self.editor.confirm_yes_rect = None
            self.editor.confirm_no_rect = None
        logger.debug("[Toolbar/DeleteZoneModel] reset delete_zone dialog")
