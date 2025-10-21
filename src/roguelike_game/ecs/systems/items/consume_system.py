
from roguelike_game.managers.items.loader import ItemsLoader

import logging
logger = logging.getLogger(__name__)

def load_items(_path: str | None = None):
    """Compatibility wrapper for tests to stub items catalog.

    Delegates to ItemsLoader().load() and returns only the items dict.
    The `_path` is ignored for backward compatibility with tests.
    """
    items, _assets = ItemsLoader().load()
    return items

class ConsumeSystem:
    """
    Sistema ECS que maneja uso de consumibles (curación, stat buffs).
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Cargar definiciones de ítems (permite monkeypatch de cs.load_items en tests)
        self.items = load_items(None)

    def update(self, world, *args):
        components = world.components
        
        # Placeholder: lógica para aplicar efectos al jugador
        # Ejemplo: iterar sobre aplicaciones pendientes de HealingComponent o BuffComponent
        player_tags = components.get('PlayerTagComponent', {})
        if not player_tags:
            return
        player_eid = next(iter(player_tags))
        
        # TODO: implementar lógica de consumo basada en eventos de uso de ítems
        # Manejar consumo de ítems
        inp = components.get('InputComponent', {}).get(player_eid)
        if not inp or not getattr(inp, 'use_item', None):
            return
        item_id = inp.use_item
        logger.debug(f"[ConsumeSystem] use_item={item_id}")
        inv = components.get('InventoryComponent', {}).get(player_eid)
        logger.debug(f"[ConsumeSystem] attempt remove 1 x {item_id}")
        if inv and inv.remove(item_id, 1):
            logger.debug(f"[ConsumeSystem] removed 1 x {item_id}")
            model = self.items.get(item_id)
            params = getattr(model, 'default_params', {}) or {}
            for key, val in params.items():
                if key == 'healing':
                    hp_comp = components.get('Health', {}).get(player_eid)
                    if hp_comp:
                        hp_comp.current_hp = min(hp_comp.max_hp, hp_comp.current_hp + val)
                        logger.debug(f"[ConsumeSystem] applied healing {val}, new HP = {hp_comp.current_hp}/{hp_comp.max_hp}")
                elif key == 'mana':
                    mana_comp = components.get('Mana', {}).get(player_eid)
                    if mana_comp:
                        mana_comp.current_mana = min(mana_comp.max_mana, mana_comp.current_mana + val)
                        logger.debug(f"[ConsumeSystem] applied mana {val}, new Mana = {mana_comp.current_mana}/{mana_comp.max_mana}")
                elif key == 'energy':
                    energy_comp = components.get('Energy', {}).get(player_eid)
                    if energy_comp:
                        energy_comp.current_energy = min(energy_comp.max_energy, energy_comp.current_energy + val)
                        logger.debug(f"[ConsumeSystem] applied energy {val}, new Energy = {energy_comp.current_energy}/{energy_comp.max_energy}")
                elif key == 'hunger':
                    hunger_comp = components.get('Hunger', {}).get(player_eid)
                    if hunger_comp:
                        hunger_comp.current_hunger = min(hunger_comp.max_hunger, hunger_comp.current_hunger + val)
                        logger.debug(f"[ConsumeSystem] applied hunger {val}, new Hunger = {hunger_comp.current_hunger}/{hunger_comp.max_hunger}")
                else:
                    logger.debug(f"[ConsumeSystem] unknown effect param: {key}={val}")
        inp.use_item = None
        logger.debug("[ConsumeSystem] inp.use_item reset")
