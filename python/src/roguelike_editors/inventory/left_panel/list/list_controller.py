import logging
logger = logging.getLogger(__name__)

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
        Construir lista de elementos para la categoría actual usando active_data o default_data
        según el lado de edición seleccionado.
        """
        ed_model = self.editor_controller.model
        category = self.panel_model.current_category
        # Alias: tratar 'hostile' como 'monsters'
        effective_category = 'monsters' if category in ('monsters', 'hostile') else category
        use_default = getattr(ed_model, 'editing_side', 'active') == 'default' and effective_category in ('player', 'monsters')
        # Mostrar PLANTILLAS (templates) completas cuando está seleccionada la vista Default
        if use_default and effective_category == 'monsters':
            default_monsters = ed_model.default_data.get('monsters', {}) or {}
            return self._get_monster_templates_items(default_monsters)
        if use_default and effective_category == 'player':
            default_player = ed_model.default_data.get('player', {}) or {}
            return self._get_player_template_items(default_player)

        # Caso normal: datos activos
        data = ed_model.active_data.get(effective_category, {})
        if effective_category == 'player':
            items = self._get_player_items(data)
        elif effective_category == 'monsters':
            items = self._get_monsters_items(data, use_default=False)
        else:
            items = self._get_other_items(data)

        return items

    def _get_monster_templates_items(self, default_monsters: dict):
        """
        Construye una lista de TODOS los templates de monstruos desde defaults
        (inventory_monsters.json), en un formato similar al listado activo:
        - Línea raíz: "<nombre_template> | Template: <template_id>"
        - Línea items: "  Items: <item> x<min>, ..."
        """
        items = []
        if not isinstance(default_monsters, dict):
            return items
        for name, entry in default_monsters.items():
            tpl = (entry or {}).get('template_id', '')
            items.append(f"{name} | Template: {tpl}")
            inv_list = (entry or {}).get('inventory', []) or []
            slot_texts = [f"{e.get('item')} x{e.get('min', 0)}" for e in inv_list]
            if slot_texts:
                items.append("  Items: " + ", ".join(slot_texts))
        return items

    def _get_player_template_items(self, default_player: dict):
        """
        Construye una lista con los templates por defecto del Player desde defaults
        (inventory_player.json).
        - Si hay 'classes':
          - Línea raíz por clase: "Class: <name> | Capacity: <cap>"
          - Línea items: "  Items: <item> x<quantity>, ..."
        - Si es formato legacy (sin 'classes'):
          - Línea raíz: "Player | Template: <player_id>"
          - Línea items: "  Items: <item> x<quantity>, ..."
        """
        items = []
        if not isinstance(default_player, dict) or not default_player:
            return items
        classes = default_player.get('classes')
        if isinstance(classes, dict) and classes:
            for cls_name, tpl in classes.items():
                cap = tpl.get('capacity')
                cap_txt = f" | Capacity: {cap}" if isinstance(cap, int) else ""
                items.append(f"Class: {cls_name}{cap_txt}")
                slots = tpl.get('slots', []) or []
                slot_texts = [f"{s.get('item')} x{s.get('quantity', 0)}" for s in slots if s]
                if slot_texts:
                    items.append("  Items: " + ", ".join(slot_texts))
        else:
            pid = default_player.get('player_id', '')
            items.append(f"Player | Template: {pid}")
            slots = default_player.get('slots', []) or []
            slot_texts = [f"{s.get('item')} x{s.get('quantity', 0)}" for s in slots if s]
            if slot_texts:
                items.append("  Items: " + ", ".join(slot_texts))
            # Info extra opcional: capacidad
            cap = default_player.get('capacity')
            if isinstance(cap, int):
                items.append(f"  Capacity: {cap}")
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
    
    def _get_monsters_items(self, data, use_default=False):
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
            
            # Items: activos o por defecto según use_default
            if use_default:
                # Buscar plantilla y mostrar inventario por defecto (min)
                tpl_entry = template_map.get(tpl)
                inv_list = tpl_entry.get('inventory', []) if tpl_entry else []
                slot_texts = [f"{e.get('item')} x{e.get('min', 0)}" for e in inv_list]
            else:
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
        logger.debug(f"[DEBUG ListController] monster data keys: {list(data.keys())}")
        logger.debug(f"[DEBUG ListController] Position entity ids: {list(pos_map.keys())}")
        logger.debug(f"[DEBUG ListController] MonsterInstance entity ids: {list(inst_map.keys())}")
        
        for mon_id, entry in data.items() if isinstance(data, dict) else []:
            logger.debug(f"[ListController] Monster Eid={mon_id}: {entry}")
        
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
            logger.debug(f"[DEBUG ListController] mon_ids with no ECS instance: {missing_inst}")
        if missing_pos:
            logger.debug(f"[DEBUG ListController] mon_ids with no Position component: {missing_pos}")
