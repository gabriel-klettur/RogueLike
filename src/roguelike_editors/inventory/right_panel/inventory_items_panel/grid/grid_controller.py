class GridController:
    """
    Controlador para lógica de grid de inventario (guardado y tabs).
    """
    def __init__(self, editor_controller, parent_controller):
        self.editor_controller = editor_controller
        self.parent = parent_controller
        self.editor_model = editor_controller.model

    def _save_default(self):
        """
        Delega al controlador de guardado para default.
        """
        return self.parent.save_controller.save_default()

    def _save_active(self):
        """
        Delega al controlador de guardado para active.
        """
        return self.parent.save_controller.save_active()
