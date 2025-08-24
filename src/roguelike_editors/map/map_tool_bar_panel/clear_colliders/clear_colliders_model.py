import logging

logger = logging.getLogger(__name__)


class ClearCollidersModel:
    """
    Model for the Clear Colliders tool. Manipulates the shared MapEditorState
    to drive the workflow: mode toggle, pending zone, and confirm dialog flags.
    """

    def __init__(self, editor_state):
        self.editor = editor_state

    # --- Mode + dialog state mutations ---
    def enable_mode(self):
        self.editor.clear_colliders_mode = True

    def disable_mode(self):
        self.editor.clear_colliders_mode = False

    def disable_other_modes(self):
        # Ensure mutual exclusion with other tools
        for attr in (
            "add_zone_mode",
            "delete_zone_mode",
            "paint_tiles_mode",
            "paint_colliders_mode",
        ):
            setattr(self.editor, attr, False)

    def toggle_mode(self) -> bool:
        new_val = not getattr(self.editor, "clear_colliders_mode", False)
        self.editor.clear_colliders_mode = new_val
        if new_val:
            self.disable_other_modes()
        logger.debug(f"[Toolbar/ClearCollidersModel] clear_colliders_mode -> {self.editor.clear_colliders_mode}")
        return new_val

    def begin_confirmation(self, zone: str):
        self.editor.pending_clear_colliders_zone = zone
        self.editor.confirm_clear_colliders = True
        logger.debug(f"[Toolbar/ClearCollidersModel] begin confirmation for zone={zone}")

    def reset_dialog(self):
        # Prefer dedicated helper if available on state
        reset_fn = getattr(self.editor, "reset_clear_colliders_dialog", None)
        if callable(reset_fn):
            reset_fn()
        else:
            # Fallback fields in editor state
            self.editor.confirm_clear_colliders = False
            self.editor.pending_clear_colliders_zone = None
            self.editor.confirm_clear_colliders_yes_rect = None
            self.editor.confirm_clear_colliders_no_rect = None
        logger.debug("[Toolbar/ClearCollidersModel] reset clear_colliders dialog")
