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
        cat = self.editor_model.current_category
        editing_side = self.editor_model.editing_side
        if editing_side == 'default':
            if cat == 'player':
                default_player = self.editor_model.default_data.get('player', {})
                slots = default_player.get('slots', [])
                for idx, slot in enumerate(slots):
                    if not slot:
                        slots[idx] = {'item': self.model.selected_item, 'quantity': quantity}
                        break
                default_player['slots'] = slots
            elif cat == 'monsters':
                active_entry = self.editor_model.active_data.get('monsters', {}).get(str(eid), {})
                template_id = active_entry.get('template_id')
                for tpl_name, def_entry in self.editor_model.default_data.get('monsters', {}).items():
                    if def_entry.get('template_id') == template_id:
                        inv_list = def_entry.get('inventory', [])
                        inv_list.append({'item': self.model.selected_item, 'min': quantity, 'max': quantity, 'chance': 1.0})
                        def_entry['inventory'] = inv_list
                        break
            # resetear estado
            self.model.show_item_list = False
            self.model.show_quantity_input = False
            self.model.selected_item = None
            self.model.quantity = 1
            return
        # Handle active side: update active_data
        if editing_side == 'active':
            active_map = self.editor_model.active_data.get(cat, {})
            entry = active_map.get(str(eid), {})
            slots = entry.get('slots', [])
            # Insertar en primer hueco libre
            for idx_slot, slot in enumerate(slots):
                if slot is None:
                    slots[idx_slot] = {'item': self.model.selected_item, 'quantity': quantity}
                    break
            else:
                # Si no hay hueco, añadir al final
                slots.append({'item': self.model.selected_item, 'quantity': quantity})
            entry['slots'] = slots
            active_map[str(eid)] = entry
            # Reset state
            self.model.show_item_list = False
            self.model.show_quantity_input = False
            self.model.selected_item = None
            self.model.quantity = 1
            return
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