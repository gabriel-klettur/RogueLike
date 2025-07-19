from roguelike_editors.inventory.controller.right_panel.buttons.add_item.add_item_controller import AddItemController
from roguelike_editors.inventory.controller.right_panel.buttons.delete.delete_controller import DeleteController
from roguelike_editors.inventory.controller.right_panel.buttons.save.save_controller import SaveController
from roguelike_editors.inventory.controller.right_panel.grid.grid_controller import GridController
from roguelike_editors.inventory.controller.right_panel.tabs.tabs_controller import TabsController


class InventoryItemsPanelController:
    """
    Controlador principal del panel derecho que delega en subcontroladores:
    add_item, delete, save, grid y tabs.
    """
    def __init__(self, editor_controller):
        self.editor_controller = editor_controller
        # Subcontroladores especializados
        self.add_controller = AddItemController(editor_controller, self)
        self.delete_controller = DeleteController(editor_controller, self)
        self.save_controller = SaveController(editor_controller, self)
        self.grid_controller = GridController(editor_controller, self)
        self.tabs_controller = TabsController(editor_controller, self)
        # Modelo y world
        self.model = editor_controller.model.grid_model
        self.editor_model = editor_controller.model
        self.world = editor_controller.world

    def load_available_items(self):
        return self.add_controller.load_available_items()

    def start_add_item(self):
        return self.add_controller.start_add_item()

    def select_item(self, item_id):
        return self.add_controller.select_item(item_id)

    def confirm_quantity(self, quantity):
        return self.add_controller.confirm_quantity(quantity)

    def delete_item(self, slot_idx, quantity=None):
        return self.delete_controller.delete_item(slot_idx, quantity)

    def save_default(self):
        return self.save_controller.save_default()

    def save_active(self):
        return self.save_controller.save_active()

    # Métodos de compatibilidad para editor_events
    _save_default = save_default
    _save_active = save_active
