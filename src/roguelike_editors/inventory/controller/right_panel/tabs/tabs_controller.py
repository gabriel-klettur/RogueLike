class TabsController:
    """
    Controlador para manejar pestañas 'Show Default' y 'Show Active' en el panel derecho.
    """
    def __init__(self, editor_controller, parent_controller):
        self.editor_controller = editor_controller
        self.parent = parent_controller
        self.model = editor_controller.model

    def show_default(self):
        """
        Cambia la vista a JSON por defecto.
        """
        self.model.editing_side = 'default'

    def show_active(self):
        """
        Cambia la vista a JSON activo.
        """
        self.model.editing_side = 'active'
