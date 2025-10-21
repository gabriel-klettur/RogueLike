from roguelike_game.ecs.components.experience_component import ExperienceComponent
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.managers.items.loader import ItemsLoader


class ExperienceSystem:
    """
    Sistema ECS que procesa ítems con experiencia en el inventario
    y actualiza el componente de experiencia.
    """
    def __init__(self, perf_log=None, items_path=None):
        self.perf_log = perf_log
        # Load items from SQLite
        self.items, _assets = ItemsLoader().load()
        # Persistencia eliminada: ahora el XP/Nivel se guarda en el archivo de partida (meta)
        # mediante ShutdownManager y se restaura al cargar la partida desde el menú.
        self.initialized = True
        self.dirty = False

    def update(self, world, *args):
        comps = world.components
        xp_comps = comps.get('ExperienceComponent', {})
        inv_comps = comps.get('InventoryComponent', {})
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
        # Persistencia a disco eliminada: se captura en el guardado de mundo
