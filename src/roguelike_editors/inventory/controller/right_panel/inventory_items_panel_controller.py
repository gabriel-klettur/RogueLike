
import os
import json
from roguelike_game.ecs.components.item_models import ItemStack

class InventoryItemsPanelController:
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
        # Prepare items for selection panel
        default_items = self.model.available_items
        active_map = self.editor_model.active_data.get('map', {})
        ground_items = [f"{entry.get('item_id')} x{entry.get('quantity')}" for entry in active_map.values()]
        # Open item selection panel with default and ground items
        self.editor_controller.view.item_panel_controller.open(default_items, ground_items)
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
        print(f"[DEBUG InvGrid] confirm_quantity eid={eid}, cat={cat}, side={editing_side}, item={self.model.selected_item}, qty={quantity}")
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
            # Determinar clave JSON: instance_id para monstruos, entity id para otros
            if cat == 'monsters':
                inst_comp = self.world.components.get('MonsterInstanceComponent', {}).get(eid)
                key = inst_comp.instance_id if inst_comp else str(eid)
            else:
                key = str(eid)
            entry = active_map.get(key, {})
            print(f"[DEBUG InvGrid] active_data before for key={key}: {entry}")
            slots = entry.get('slots', [])
            print(f"[DEBUG InvGrid] slots before modification: {slots}")
            # Insertar en primer hueco libre
            for idx_slot, slot in enumerate(slots):
                if slot is None:
                    slots[idx_slot] = {'item': self.model.selected_item, 'quantity': quantity}
                    break
            else:
                # Si no hay hueco, añadir al final
                slots.append({'item': self.model.selected_item, 'quantity': quantity})
            entry['slots'] = slots
            print(f"[DEBUG InvGrid] entry['slots'] updated to: {slots}")
            active_map[key] = entry
            # Actualizar InventoryComponent slots para que el NPC tenga el ítem al morir
            # Map JSON key to ECS entity id for updating InventoryComponent
            if cat == 'monsters':
                inst_map = self.world.components.get('MonsterInstanceComponent', {})
                numeric_eid = next((e for e, comp in inst_map.items() if comp.instance_id == key), None)
                print(f"[DEBUG InvGrid] mapped instance_id {key} to numeric eid {numeric_eid}")
            else:
                numeric_eid = int(key)
            inv_comp = self.world.components.get('InventoryComponent', {}).get(numeric_eid)
            if inv_comp:
                for idx_comp, slot_comp in enumerate(inv_comp.slots):
                    if not slot_comp:
                        inv_comp.slots[idx_comp] = ItemStack(self.model.selected_item, quantity)
                        break
                else:
                    inv_comp.slots.append(ItemStack(self.model.selected_item, quantity))
                    print(f"[DEBUG InvGrid] inv_comp.slots after update: {[ (s.item_id, s.quantity) for s in inv_comp.slots if s ]}")
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

    def delete_item(self, slot_idx):
        """
        Elimina el ítem del slot dado y actualiza datos y ECS.
        """
        eid = self.editor_model.selected_eid
        cat = self.editor_model.current_category
        side = self.editor_model.editing_side
        # Acceder a slots según modo
        if side == 'default':
            if cat == 'player':
                tpl = self.editor_model.default_data.get('player', {})
                slots = tpl.get('slots', [])
                if slot_idx < len(slots):
                    slots[slot_idx] = None
                    tpl['slots'] = slots
            elif cat == 'monsters':
                active_entry = self.editor_model.active_data.get('monsters', {}).get(str(eid), {})
                template_id = active_entry.get('template_id')
                for def_entry in self.editor_model.default_data.get('monsters', {}).values():
                    if def_entry.get('template_id') == template_id:
                        inv_list = def_entry.get('inventory', [])
                        if slot_idx < len(inv_list):
                            inv_list.pop(slot_idx)
                            def_entry['inventory'] = inv_list
                        break
        elif side == 'active':
            active_map = self.editor_model.active_data.get(cat, {})
            if cat == 'monsters':
                inst_comp = self.world.components.get('MonsterInstanceComponent', {}).get(eid)
                key = inst_comp.instance_id if inst_comp else str(eid)
            else:
                key = str(eid)
            entry = active_map.get(key, {})
            slots = entry.get('slots', [])
            if slot_idx < len(slots):
                slots[slot_idx] = None
                entry['slots'] = slots
                active_map[key] = entry
            # Actualizar componente ECS
            if cat == 'monsters':
                inst_map = self.world.components.get('MonsterInstanceComponent', {})
                numeric_eid = next((e for e, comp in inst_map.items() if comp.instance_id == key), None)
            else:
                numeric_eid = int(key)
            inv_comp = self.world.components.get('InventoryComponent', {}).get(numeric_eid)
            if inv_comp and slot_idx < len(inv_comp.slots):
                inv_comp.slots[slot_idx] = None
        # Desactivar modo delete
        self.model.show_delete_mode = False

    def _save_default(self):
        cat = self.editor_model.current_category
        path = self.editor_controller.paths[cat]['default']
        try:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, 'w', encoding='utf-8') as f:
                json.dump(self.editor_model.default_data.get(cat, {}), f, indent=2)
            self.editor_controller.logger.info(f"Default inventory for '{cat}' saved to {path}")
        except Exception as e:
            self.editor_controller.logger.error(f"Error saving default inventory for '{cat}' to {path}: {e}")

    def _save_active(self):
        cat = self.editor_model.current_category
        path = self.editor_controller.paths[cat]['active']
        try:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, 'w', encoding='utf-8') as f:
                json.dump(self.editor_model.active_data.get(cat, {}), f, indent=2)
            self.editor_controller.logger.info(f"Active inventory for '{cat}' saved to {path}")
        except Exception as e:
            self.editor_controller.logger.error(f"Error saving active inventory for '{cat}' to {path}: {e}")