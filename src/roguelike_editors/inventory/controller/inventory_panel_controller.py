from roguelike_editors.inventory.model.inventory_panel_model import InventoryPanelModel

class InventoryPanelController:
    """
    Controlador para la selección de entidades (tabs + listado).
    """
    def __init__(self, editor_controller, model: InventoryPanelModel):
        self.editor_controller = editor_controller
        self.model = model
        # Debug flag: ensure prints only once per panel open
        self.debug_printed = False

    def change_category(self, category: str):
        # Cambiar categoría de listado
        self.model.current_category = category
        # Al cambiar categoría, resetear selección
        self.model.selected_eid = None
        self.editor_controller.model.current_category = category

    def select_entity(self, eid):
        # Seleccionar entidad (actualiza modelo de panel y modelo de editor)
        self.model.selected_eid = eid
        self.editor_controller.model.selected_eid = eid

    def get_items_list(self):
        # Construir lista de elementos para la categoría actual usando active_data
        ed_model = self.editor_controller.model
        data = ed_model.active_data.get(self.model.current_category, {})
        items = []
        if self.model.current_category == 'player':
            for entry in data.values() if isinstance(data, dict) else []:
                for slot in entry.get('slots', []):
                    if slot:
                        items.append(f"{slot.get('item')} x{slot.get('quantity')}")
        elif self.model.current_category == 'monsters':
            # Position & instance maps
            inst_map = self.editor_controller.world.components.get('MonsterInstanceComponent', {})
            pos_map = self.editor_controller.world.components.get('Position', {})
            if not getattr(self, 'debug_printed', False):
                # DEBUG: dump data and component maps once per panel open
                print(f"[DEBUG InventoryPanel] monster data keys: {list(data.keys())}")
                print(f"[DEBUG InventoryPanel] Position entity ids: {list(pos_map.keys())}")
                print(f"[DEBUG InventoryPanel] MonsterInstance entity ids: {list(inst_map.keys())}")
                for mon_id, entry in data.items() if isinstance(data, dict) else []:
                    print(f"[InventoryPanel] Monster Eid={mon_id}: {entry}")
                # DEBUG: mapping summary
                missing_inst = []
                missing_pos = []
                for mon_id, entry in data.items() if isinstance(data, dict) else []:
                    eid_int = next((eid for eid, comp in inst_map.items() if getattr(comp, 'instance_id', None) == mon_id), None)
                    if eid_int is None:
                        missing_inst.append(mon_id)
                    elif eid_int not in pos_map:
                        missing_pos.append(mon_id)
                if missing_inst:
                    print(f"[DEBUG InventoryPanel] mon_ids with no ECS instance: {missing_inst}")
                if missing_pos:
                    print(f"[DEBUG InventoryPanel] mon_ids with no Position component: {missing_pos}")
                self.debug_printed = True
            for eid_int, inst_comp in inst_map.items():
                mon_id = inst_comp.instance_id
                entry = data.get(mon_id, {})
                
                    

                # First line: EID and template
                tpl = entry.get('template_id', '')
                line1 = f"{mon_id}" + (f" | Template: {tpl}" if tpl else "")
                items.append(line1)

                # Second line: position and other metadata
                meta_parts = []
                pos_comp = pos_map.get(eid_int)
                if pos_comp:
                    meta_parts.append(f"Pos: ({pos_comp.x:.1f}, {pos_comp.y:.1f})")
                else:
                    # Fallback to JSON-stored position
                    pos = entry.get('position', {})
                    if pos:
                        meta_parts.append(f"Pos: ({pos.get('x', 0):.1f}, {pos.get('y', 0):.1f})")

                # Additional metadata
                for key, value in entry.items():
                    if key not in ('template_id', 'position', 'slots') and not key.startswith('_'):
                        meta_parts.append(f"{key.capitalize()}: {value}")
                if meta_parts:
                    items.append("  " + " | ".join(meta_parts))

                # Third line: slots
                slot_texts = [f"{slot.get('item')} x{slot.get('quantity')}" for slot in entry.get('slots', []) if slot]
                if slot_texts:
                    items.append("  Slots: " + ", ".join(slot_texts))
        else:
            for entry in data.values() if isinstance(data, dict) else []:
                pos = entry.get('position', {})
                items.append(f"{entry.get('item_id')} x{entry.get('quantity')} @({pos.get('x'):.1f},{pos.get('y'):.1f})")
        return items
