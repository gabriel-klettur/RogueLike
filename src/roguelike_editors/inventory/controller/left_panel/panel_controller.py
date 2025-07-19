from roguelike_editors.inventory.model.left_panel.inventory_panel_model import InventoryPanelModel
from .tabs import TabsController
from .list import ListController


class PanelController:
    """
    Controlador principal del panel izquierdo que delega a controladores especializados.
    """
    
    def __init__(self, editor_controller, model: InventoryPanelModel):
        self.editor_controller = editor_controller
        self.model = model
        
        # Inicializar controladores especializados
        self.tabs_controller = TabsController(editor_controller, model)
        self.list_controller = ListController(editor_controller, model)
    
    def change_category(self, category: str):
        """
        Delegar cambio de categoría al controlador de tabs.
        """
        self.tabs_controller.change_category(category)
    
    def select_entity(self, eid):
        """
        Delegar selección de entidad al controlador de lista.
        """
        self.list_controller.select_entity(eid)
    
    def get_items_list(self):
        """
        Delegar obtención de lista al controlador de lista.
        """
        return self.list_controller.get_items_list()
