import pygame

class InventoryGridController:
    """
    Controlador para manejar el flujo de añadir/eliminar ítems en el grid.
    """
    def __init__(self, editor_controller):
        self.editor_controller = editor_controller
        self.model = editor_controller.model.grid_model
        self.editor_model = editor_controller.model
        self.world = editor_controller.world

    def load_available_items(self):
        """
        Carga lista de todos los ítems disponibles desde el EditorView.
        """
        # usa el diccionario view.items (item_id -> modelo)
        self.model.available_items = list(self.editor_controller.view.items.keys())

    def start_add_item(self):
        """
        Inicia flujo de añadir ítem: muestra lista de ítems.
        """
        self.load_available_items()
        # Open MVC item selection panel
        self.editor_controller.view.item_panel_controller.open(self.model.available_items)
        self.model.show_item_list = True
        self.model.show_quantity_input = False
        self.model.selected_item = None
        self.model.quantity = 1

    def select_item(self, item_id):
        """
        Selecciona un ítem y pasa a input de cantidad.
        """
        self.model.selected_item = item_id
        self.model.show_quantity_input = True

    def confirm_quantity(self, quantity):
        """
        Confirma cantidad y añade el ítem al InventoryComponent.
        """
        eid = self.editor_model.selected_eid
        inv_comp = self.world.components.get('InventoryComponent', {}).get(eid)
        if inv_comp and self.model.selected_item:
            # encuentra primer slot vacío
            for idx, slot in enumerate(inv_comp.slots):
                if not slot:
                    inv_comp.slots[idx] = {'item': self.model.selected_item, 'quantity': quantity}
                    break
        # resetear estado
        self.model.show_item_list = False
        self.model.show_quantity_input = False
        self.model.selected_item = None
        self.model.quantity = 1