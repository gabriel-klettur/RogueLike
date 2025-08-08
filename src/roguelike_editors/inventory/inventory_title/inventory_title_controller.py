from roguelike_editors.inventory.inventory_title.inventory_title_view import InventoryTitleView
from roguelike_editors.inventory.inventory_title.inventory_title_model import InventoryTitleModel

class InventoryTitleController:
    """
    Controller para el panel de título del Inventory Editor.
    """
    def __init__(self, editor_state, model: InventoryTitleModel, font):
        self.editor_state = editor_state
        self.model = model
        self.font = font
        self.view = InventoryTitleView(self, self.model, self.font)

    def handle_event(self, event):
        return False

    def render(self, screen):
        """Renderiza y devuelve el rect del título para layout."""
        return self.view.render(screen)
