from roguelike_editors.spawner.spawner_title.spawner_title_view import SpawnerTitleView

class SpawnerTitleController:
    """
    Controller para el panel de título del Spawner Editor.
    """
    def __init__(self, editor_state, model, font):
        self.editor_state = editor_state
        self.model = model
        self.font = font
        self.view = SpawnerTitleView(self, self.model, self.font)

    def handle_event(self, event):
        return False

    def render(self, screen):
        return self.view.render(screen)
