import logging

logger = logging.getLogger(__name__)


class AddZoneModel:
    """
    Model for the Add Zone tool. It manipulates the shared MapEditorState
    to drive the add-zone workflow (mode toggle, pending coords, confirm dialog).
    """

    def __init__(self, editor_state):
        self.editor = editor_state

    # --- Mode + dialog state mutations ---
    def enable_mode(self):
        self.editor.add_zone_mode = True

    def disable_mode(self):
        self.editor.add_zone_mode = False

    def disable_other_modes(self):
        # Ensure mutual exclusion with other tools
        for attr in (
            "delete_zone_mode",
            "paint_tiles_mode",
            "clear_colliders_mode",
            "paint_colliders_mode",
        ):
            setattr(self.editor, attr, False)

    def toggle_mode(self):
        new_val = not getattr(self.editor, "add_zone_mode", False)
        self.editor.add_zone_mode = new_val
        if new_val:
            self.disable_other_modes()
        logger.debug(f"[Toolbar/AddZoneModel] add_zone_mode -> {self.editor.add_zone_mode}")
        return new_val

    def begin_placement(self, tx: int, ty: int):
        self.editor.pending_add_zone_coords = (tx, ty)
        self.editor.confirm_add_zone = True
        logger.debug(f"[Toolbar/AddZoneModel] begin placement at tx={tx} ty={ty}")

    def reset_dialog(self):
        # Prefer the dedicated helper if available on state
        reset_fn = getattr(self.editor, "reset_add_zone_dialog", None)
        if callable(reset_fn):
            reset_fn()
        else:
            # Fallback
            self.editor.confirm_add_zone = False
            self.editor.pending_add_zone_coords = None
            self.editor.confirm_add_yes_rect = None
            self.editor.confirm_add_no_rect = None
        logger.debug("[Toolbar/AddZoneModel] reset add_zone dialog")
