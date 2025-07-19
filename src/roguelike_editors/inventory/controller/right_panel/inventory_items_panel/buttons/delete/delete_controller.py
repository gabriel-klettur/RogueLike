class DeleteController:
    """
    Controlador para flujo de eliminar ítems en el grid (Delete Item).
    """
    def __init__(self, editor_controller, parent_controller):
        self.editor_controller = editor_controller
        self.parent = parent_controller
        self.model = editor_controller.model.grid_model
        self.editor_model = editor_controller.model
        self.world = editor_controller.world

    def delete_item(self, slot_idx, quantity=None):
        """
        Elimina el ítem del slot dado (hasta la cantidad indicada) y actualiza datos y ECS.
        """
        qty = quantity if quantity is not None else self.model.delete_quantity
        eid = self.editor_model.selected_eid
        cat = self.editor_model.current_category
        side = self.editor_model.editing_side

        if side == 'default':
            if cat == 'player':
                tpl = self.editor_model.default_data.get('player', {})
                slots = tpl.get('slots', [])
                if slot_idx < len(slots):
                    slot = slots[slot_idx]
                    if slot and slot.get('quantity', 0) > qty:
                        slot['quantity'] -= qty
                    else:
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
                slot = slots[slot_idx]
                if slot and slot.get('quantity', 0) > qty:
                    slot['quantity'] -= qty
                else:
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
                slot_comp = inv_comp.slots[slot_idx]
                if hasattr(slot_comp, 'quantity') and slot_comp.quantity > qty:
                    slot_comp.quantity -= qty
                else:
                    inv_comp.slots[slot_idx] = None

        # Resetear flags de modo eliminación
        self.model.show_delete_mode = False
        self.model.show_delete_quantity_input = False
