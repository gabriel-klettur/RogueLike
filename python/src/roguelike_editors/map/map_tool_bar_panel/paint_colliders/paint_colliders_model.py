import logging

logger = logging.getLogger(__name__)


class PaintCollidersModel:
    """
    Model for the Paint Colliders tool. Manipulates the shared MapEditorState
    to drive the workflow: mode toggle, pending zone, and confirm dialog flags.

    Español: Modelo que opera sobre `MapEditorState` para habilitar/deshabilitar
    el modo, abrir el diálogo de confirmación y limpiar su estado.
    """

    def __init__(self, editor_state):
        self.editor = editor_state

    # --- Mutaciones de modo y diálogo ---
    def enable_mode(self) -> None:
        self.editor.paint_colliders_mode = True

    def disable_mode(self) -> None:
        self.editor.paint_colliders_mode = False

    def disable_other_modes(self) -> None:
        # Asegurar exclusión mutua con otras herramientas
        for attr in (
            "add_zone_mode",
            "delete_zone_mode",
            "paint_tiles_mode",
            "clear_colliders_mode",
        ):
            setattr(self.editor, attr, False)

    def toggle_mode(self) -> bool:
        new_val = not getattr(self.editor, "paint_colliders_mode", False)
        self.editor.paint_colliders_mode = new_val
        if new_val:
            self.disable_other_modes()
        logger.debug(
            f"[Toolbar/PaintCollidersModel] paint_colliders_mode -> {self.editor.paint_colliders_mode}"
        )
        return new_val

    def begin_confirmation(self, zone: str) -> None:
        self.editor.pending_paint_colliders_zone = zone
        self.editor.confirm_paint_colliders = True
        logger.debug(f"[Toolbar/PaintCollidersModel] begin confirmation for zone={zone}")

    def reset_dialog(self) -> None:
        # Usar helper dedicado del estado si existe
        reset_fn = getattr(self.editor, "reset_paint_colliders_dialog", None)
        if callable(reset_fn):
            reset_fn()
        else:
            # Fallback en caso de estado antiguo
            self.editor.confirm_paint_colliders = False
            self.editor.pending_paint_colliders_zone = None
            self.editor.confirm_paint_colliders_yes_rect = None
            self.editor.confirm_paint_colliders_no_rect = None
        logger.debug("[Toolbar/PaintCollidersModel] reset paint_colliders dialog")
