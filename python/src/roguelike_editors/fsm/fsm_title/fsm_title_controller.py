from roguelike_editors.fsm.fsm_title.fsm_title_view import FsmTitleView

class FsmTitleController:
    """
    Controller para el panel de título del editor FSM.
    """
    def __init__(self, editor_state, model, font):
        """
        Args:
            editor_state: Instancia del FSM Editor Manager (o estado del editor).
            model: FsmTitleModel.
            font: pygame.font.Font.
        """
        self.editor_state = editor_state
        self.model = model
        self.font = font
        # Crear vista del título
        self.view = FsmTitleView(self, self.model, self.font)

    def handle_event(self, event):
        """
        Manejar eventos (no hay eventos para el título por ahora).
        """
        return False

    def render(self, screen):
        """
        Renderiza y devuelve el rect del título para layout (igual que otros editores).
        """
        return self.view.render(screen)
