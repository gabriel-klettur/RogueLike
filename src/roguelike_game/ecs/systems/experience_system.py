import os
from roguelike_game.ecs.components.experience_component import ExperienceComponent
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.item_models import load_items
import json


class ExperienceSystem:
    """
    Sistema ECS que procesa ítems con experiencia en el inventario
    y actualiza el componente de experiencia.
    """
    def __init__(self, perf_log=None, items_path=None):
        self.perf_log = perf_log
        if items_path is None:
            items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
        self.items = load_items(items_path)
        # Setup XP persistence
        self.save_path = os.path.join(os.getcwd(), 'data', 'lvls', 'experience.json')
        os.makedirs(os.path.dirname(self.save_path), exist_ok=True)
        try:
            with open(self.save_path, 'r', encoding='utf-8') as f:
                self.active_xp = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            self.active_xp = {}
            with open(self.save_path, 'w', encoding='utf-8') as f:
                json.dump(self.active_xp, f, indent=2)
        self.initialized = False
        self.dirty = False

    def update(self, world, *args):
        comps = world.components
        xp_comps = comps.get('ExperienceComponent', {})
        inv_comps = comps.get('InventoryComponent', {})
        # Cargar XP persistida al primer update
        if not getattr(self, 'initialized', False):
            for eid_str, data in self.active_xp.items():
                try:
                    eid = int(eid_str)
                except (ValueError, TypeError):
                    continue
                xp_c = xp_comps.get(eid)
                if xp_c:
                    xp_c.xp = data.get('xp', xp_c.xp)
                    xp_c.level = data.get('level', xp_c.level)
                    xp_c.xp_to_next_level = data.get('xp_to_next_level', xp_c.xp_to_next_level)
            self.initialized = True
        self.dirty = False
        # Procesar XP de orbes en inventario
        for eid, xp_comp in xp_comps.items():
            inv = inv_comps.get(eid)
            if not inv:
                continue
            for stack in list(inv.slots):
                if not stack:
                    continue
                item_id = stack.item_id
                model = self.items.get(item_id)
                if model and getattr(model, 'experience', None):
                    qty = stack.quantity
                    exp_value = model.experience or 0
                    total_exp = qty * exp_value
                    xp_comp.xp += total_exp
                    self.dirty = True
                    inv.remove(item_id, qty)
                    # Subir de nivel si es necesario
                    while xp_comp.xp >= xp_comp.xp_to_next_level:
                        xp_comp.xp -= xp_comp.xp_to_next_level
                        xp_comp.level += 1

        # Guardar XP si hubo cambios
        if self.dirty:
            self._persist_xp(xp_comps)

    def _persist_xp(self, xp_comps):
        # Persistir XP a JSON
        data = {}
        for eid, xp in xp_comps.items():
            data[str(eid)] = {
                'xp': xp.xp,
                'level': xp.level,
                'xp_to_next_level': xp.xp_to_next_level,
            }
        with open(self.save_path, 'w', encoding='utf-8') as f:
            json.dump(data, f, indent=2)
