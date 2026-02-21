import logging
logger = logging.getLogger(__name__)
from roguelike_game.ecs.components.item_models import ItemStack

class AddItemController:
    """
    Controlador para flujo de añadir ítems en el grid (Add Item).
    """
    def __init__(self, editor_controller, parent_controller):
        self.editor_controller = editor_controller
        self.parent = parent_controller
        self.model = parent_controller.model
        self.editor_model = editor_controller.model
        self.world = editor_controller.world

    def load_available_items(self):
        """
        Carga lista de todos los ítems disponibles desde el EditorView.
        """
        self.model.available_items = list(self.editor_controller.view.items.keys())

    def start_add_item(self):
        """
        Inicia flujo de añadir ítem: muestra lista de ítems.
        """
        self.load_available_items()
        default_items = self.model.available_items
        active_map = self.editor_model.active_data.get('map', {})
        ground_items = [f"{entry.get('item_id')} x{entry.get('quantity')}" for entry in active_map.values()]
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
        logger.debug(f"[DEBUG InvGrid] confirm_quantity eid={eid}, cat={cat}, side={editing_side}, item={self.model.selected_item}, qty={quantity}")
        # Default side: JSON default templates
        if editing_side == 'default':
            if cat == 'player':
                default_player = self.editor_model.default_data.get('player', {}) or {}
                classes = default_player.get('classes')
                if isinstance(classes, dict) and classes:
                    sel_cls = getattr(self.editor_model, 'selected_default_player_class', None)
                    target_cls = sel_cls if sel_cls in classes else next(iter(classes.keys()))
                    tpl = classes.get(target_cls, {})
                    slots = tpl.get('slots', []) or []
                    cap = tpl.get('capacity')
                    for slot in slots:
                        if slot and slot.get('item') == self.model.selected_item:
                            slot['quantity'] = slot.get('quantity', 0) + quantity
                            break
                    else:
                        for idx, slot in enumerate(slots):
                            if not slot:
                                slots[idx] = {'item': self.model.selected_item, 'quantity': quantity}
                                break
                        else:
                            # Si hay capacidad definida, no excederla
                            if isinstance(cap, int) and cap > 0 and len(slots) >= cap:
                                logger.debug(f"[DEBUG InvGrid] capacidad alcanzada para clase {target_cls}: {cap}")
                            else:
                                slots.append({'item': self.model.selected_item, 'quantity': quantity})
                    tpl['slots'] = slots
                    classes[target_cls] = tpl
                    default_player['classes'] = classes
                else:
                    slots = default_player.get('slots', [])
                    cap = default_player.get('capacity')
                    for slot in slots:
                        if slot and slot.get('item') == self.model.selected_item:
                            slot['quantity'] = slot.get('quantity', 0) + quantity
                            break
                    else:
                        for idx, slot in enumerate(slots):
                            if not slot:
                                slots[idx] = {'item': self.model.selected_item, 'quantity': quantity}
                                break
                        else:
                            if isinstance(cap, int) and cap > 0 and len(slots) >= cap:
                                logger.debug(f"[DEBUG InvGrid] capacidad alcanzada (legacy player): {cap}")
                            else:
                                slots.append({'item': self.model.selected_item, 'quantity': quantity})
                    default_player['slots'] = slots
            elif cat in ('monsters', 'hostile'):
                # Determine target template_id: prefer explicit selection from left list
                sel_tid = getattr(self.editor_model, 'selected_default_template_id', None)
                template_id = None
                if sel_tid:
                    template_id = sel_tid
                else:
                    active_entry = self.editor_model.active_data.get('monsters', {}).get(str(eid), {})
                    template_id = active_entry.get('template_id')
                if template_id:
                    for def_entry in self.editor_model.default_data.get('monsters', {}).values():
                        if def_entry.get('template_id') == template_id:
                            inv_list = def_entry.get('inventory', [])
                            for entry in inv_list:
                                if entry.get('item') == self.model.selected_item:
                                    entry['min'] = entry.get('min', 0) + quantity
                                    entry['max'] = entry.get('max', 0) + quantity
                                    break
                            else:
                                inv_list.append({'item': self.model.selected_item, 'min': quantity, 'max': quantity, 'chance': 1.0})
                            def_entry['inventory'] = inv_list
                            break
            self.model.show_item_list = False
            self.model.show_quantity_input = False
            self.model.selected_item = None
            self.model.quantity = 1
            return
        # Active side: JSON and ECS
        if editing_side == 'active':
            active_map = self.editor_model.active_data.get(cat, {})
            if cat in ('monsters', 'hostile'):
                inst_comp = self.world.components.get('MonsterInstanceComponent', {}).get(eid)
                key = inst_comp.instance_id if inst_comp else str(eid)
            else:
                key = str(eid)
            entry = active_map.get(key, {})
            slots = entry.get('slots', [])
            for slot in slots:
                if slot and slot.get('item') == self.model.selected_item:
                    slot['quantity'] = slot.get('quantity', 0) + quantity
                    break
            else:
                for idx_slot, slot in enumerate(slots):
                    if slot is None:
                        slots[idx_slot] = {'item': self.model.selected_item, 'quantity': quantity}
                        break
                else:
                    slots.append({'item': self.model.selected_item, 'quantity': quantity})
            entry['slots'] = slots
            active_map[key] = entry
            if cat in ('monsters', 'hostile'):
                inst_map = self.world.components.get('MonsterInstanceComponent', {})
                numeric_eid = next((e for e, comp in inst_map.items() if comp.instance_id == key), None)
            else:
                numeric_eid = int(key)
            inv_comp = self.world.components.get('InventoryComponent', {}).get(numeric_eid)
            if inv_comp:
                merged = False
                for idx_comp, slot_comp in enumerate(inv_comp.slots):
                    if slot_comp and slot_comp.item_id == self.model.selected_item:
                        slot_comp.quantity += quantity
                        merged = True
                        break
                if not merged:
                    for idx_comp, slot_comp in enumerate(inv_comp.slots):
                        if not slot_comp:
                            inv_comp.slots[idx_comp] = ItemStack(self.model.selected_item, quantity)
                            break
                    else:
                        inv_comp.slots.append(ItemStack(self.model.selected_item, quantity))
            # Actualizar ground items en active_data para map
            if cat == 'map':
                map_data = self.editor_model.active_data.get('map', {})
                for key_map, entry_map in list(map_data.items()):
                    if entry_map.get('item_id') == self.model.selected_item:
                        q0 = entry_map.get('quantity', 0)
                        new_q = q0 - quantity
                        if new_q > 0:
                            entry_map['quantity'] = new_q
                        else:
                            map_data.pop(key_map)
                        break
                self.editor_model.active_data['map'] = map_data
            self.model.show_item_list = False
            self.model.show_quantity_input = False
            self.model.selected_item = None
            self.model.quantity = 1
            return
        inv_comp = self.world.components.get('InventoryComponent', {}).get(eid)
        if inv_comp and self.model.selected_item:
            for idx, slot in enumerate(inv_comp.slots):
                if not slot:
                    inv_comp.slots[idx] = {'item': self.model.selected_item, 'quantity': quantity}
                    break
        self.model.show_item_list = False
        self.model.show_quantity_input = False
        self.model.selected_item = None
        self.model.quantity = 1
