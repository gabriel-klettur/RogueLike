from roguelike_editors.entities.entities_title.entities_title_view import EntitiesTitleView

class EntitiesTitleController:
    """
    Controller para el panel de título de entidades.
    """
    def __init__(self, editor_state, model, font):
        """
        Args:
            editor_state: Instancia de EntitiesEditorManager.
            model: EntitiesTitleModel.
            font: pygame.font.Font.
        """
        self.editor_state = editor_state
        self.model = model
        self.font = font
        # Crear vista del título
        self.view = EntitiesTitleView(self, self.model, self.font)

    def handle_event(self, event):
        """
        Manejar eventos (no hay eventos para el título por ahora).
        """
        return False

    def render(self, screen):
        """
        Renderiza y devuelve el rect del título para layout (unificado con InventoryTitleController).
        """
        return self.view.render(screen)