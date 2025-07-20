class ListController:
    """
    Controlador para el manejo de la lista de elementos del panel izquierdo.
    """
    
    def __init__(self, editor_controller, panel_model):
        self.editor_controller = editor_controller
        self.panel_model = panel_model
        # Debug flag: ensure prints only once per panel open
        self.debug_printed = False
    
    def select_entity(self, eid):
        """
        Seleccionar entidad (actualiza modelo de panel y modelo de editor).
        """
        self.panel_model.selected_eid = eid
        self.editor_controller.model.selected_eid = eid
    
    def get_items_list(self):
        """
        Construir lista de elementos para la categoría actual usando active_data.
        """
        ed_model = self.editor_controller.model
        data = ed_model.active_data.get(self.panel_model.current_category, {})
        
        items = []
        if self.panel_model.current_category == 'player':
            items = self._get_player_items(data)
        elif self.panel_model.current_category == 'monsters':
            items = self._get_monsters_items(data)
        else:
            items = self._get_other_items(data)
        
        return items
    
    def _get_player_items(self, data):
        """
        Obtiene los items del jugador.
        """
        items = []
        for entry in data.values() if isinstance(data, dict) else []:
            for slot in entry.get('slots', []):
                if slot:
                    items.append(f"{slot.get('item')} x{slot.get('quantity')}")
        return items
    
    def _get_monsters_items(self, data):
        """
        Obtiene los items de los monstruos.
        """
        items = []
        
        # Position & instance maps
        inst_map = self.editor_controller.world.components.get('MonsterInstanceComponent', {})
        pos_map = self.editor_controller.world.components.get('Position', {})
        
        # Default template mapping
        default_data = self.editor_controller.model.default_data.get('monsters', {})
        template_map = {tmpl.get('template_id'): tmpl for tmpl in default_data.values()}
        
        if not getattr(self, 'debug_printed', False):
            self._debug_monsters_data(data, pos_map, inst_map)
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
            template_name = next((name for name, def_entry in default_monsters.items() 
                                if def_entry.get('template_id') == tpl), None)
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
            slot_texts = [f"{slot.get('item')} x{slot.get('quantity')}" 
                         for slot in entry.get('slots', []) if slot]
            if slot_texts:
                items.append("  Items: " + ", ".join(slot_texts))
        
        return items
    
    def _get_other_items(self, data):
        """
        Obtiene items para otras categorías.
        """
        items = []
        for entry in data.values() if isinstance(data, dict) else []:
            pos = entry.get('position', {})
            items.append(f"{entry.get('item_id')} x{entry.get('quantity')} "
                        f"@({pos.get('x'):.1f},{pos.get('y'):.1f})")
        return items
    
    def _debug_monsters_data(self, data, pos_map, inst_map):
        """
        Imprime información de debug para los monstruos.
        """
        # DEBUG: dump data and component maps once per panel open
        print(f"[DEBUG ListController] monster data keys: {list(data.keys())}")
        print(f"[DEBUG ListController] Position entity ids: {list(pos_map.keys())}")
        print(f"[DEBUG ListController] MonsterInstance entity ids: {list(inst_map.keys())}")
        
        for mon_id, entry in data.items() if isinstance(data, dict) else []:
            print(f"[ListController] Monster Eid={mon_id}: {entry}")
        
        # DEBUG: mapping summary
        missing_inst = []
        missing_pos = []
        for mon_id, entry in data.items() if isinstance(data, dict) else []:
            eid_int = next((eid for eid, comp in inst_map.items() 
                          if getattr(comp, 'instance_id', None) == mon_id), None)
            if eid_int is None:
                missing_inst.append(mon_id)
            elif eid_int not in pos_map:
                missing_pos.append(mon_id)
        
        if missing_inst:
            print(f"[DEBUG ListController] mon_ids with no ECS instance: {missing_inst}")
        if missing_pos:
            print(f"[DEBUG ListController] mon_ids with no Position component: {missing_pos}")
