from roguelike_editors.inventory.model.left_panel.inventory_panel_model import InventoryPanelModel
import os
from roguelike_ui.services.json_persistence import load_from_json

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
        # Si cambiamos a 'monsters', recargar datos activos desde JSON
        if category == 'monsters':
            active_path = self.editor_controller.paths['monsters']['active']
            try:
                self.editor_controller.model.active_data['monsters'] = load_from_json(active_path)
            except Exception as e:
                print("[InventoryPanel] Error recargando inventory_monsters.json:", e)
            # Resetear debug para nuevas impresiones de diagnóstico
            self.debug_printed = False
            self.editor_controller.model.editing_side = 'active'
            # Auto-seleccionar primer monstruo para mostrar sus items en el grid
            monsters = self.editor_controller.model.active_data.get('monsters', {})
            first_mon = next(iter(monsters.keys()), None)
            if first_mon:
                self.select_entity(first_mon)
        elif category == 'player':
            self.editor_controller.model.editing_side = 'active'
            player_eid = getattr(self.editor_controller.world, 'player_entity', None)
            if player_eid is not None:
                self.select_entity(player_eid)

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
            # Default template mapping
            default_data = self.editor_controller.model.default_data.get('monsters', {})
            template_map = {tmpl.get('template_id'): tmpl for tmpl in default_data.values()}
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
                # Mostrar nombre de monstruo de plantillas predeterminadas
                default_monsters = self.editor_controller.model.default_data.get('monsters', {})
                template_name = next((name for name, def_entry in default_monsters.items() if def_entry.get('template_id') == tpl), None)
                if template_name:
                    items.append(f"  Name: {template_name}")


                # Position
                pos_comp = pos_map.get(eid_int)
                if pos_comp:
                    items.append(f"  Pos: ({pos_comp.x:.1f}, {pos_comp.y:.1f})")
                else:
                    pos = entry.get('position', {})
                    if pos:
                        items.append(f"  Pos: ({pos.get('x', 0):.1f}, {pos.get('y', 0):.1f})")

                # Active items
                slot_texts = [f"{slot.get('item')} x{slot.get('quantity')}" for slot in entry.get('slots', []) if slot]
                if slot_texts:
                    items.append("  Items: " + ", ".join(slot_texts))
        else:
            for entry in data.values() if isinstance(data, dict) else []:
                pos = entry.get('position', {})
                items.append(f"{entry.get('item_id')} x{entry.get('quantity')} @({pos.get('x'):.1f},{pos.get('y'):.1f})")
        return items
