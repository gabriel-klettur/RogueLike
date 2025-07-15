from roguelike_game.ecs.components.items.healing_component import HealingComponent
from roguelike_game.ecs.components.items.buff_component import BuffComponent
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.transform.position import Position
import os
from roguelike_game.ecs.components.item_models import load_items

class ConsumeSystem:
    """
    Sistema ECS que maneja uso de consumibles (curación, stat buffs).
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Cargar definiciones de ítems para consumo
        items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
        self.items = load_items(items_path)

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
        print(f"[DEBUG][ConsumeSystem] use_item={item_id}")
        inv = components.get('InventoryComponent', {}).get(player_eid)
        print(f"[DEBUG][ConsumeSystem] attempt remove 1 x {item_id}")
        if inv and inv.remove(item_id, 1):
            print(f"[DEBUG][ConsumeSystem] removed 1 x {item_id}")
            model = self.items.get(item_id)
            params = getattr(model, 'default_params', {}) or {}
            for key, val in params.items():
                if key == 'healing':
                    hp_comp = components.get('Health', {}).get(player_eid)
                    if hp_comp:
                        hp_comp.current_hp = min(hp_comp.max_hp, hp_comp.current_hp + val)
                        print(f"[DEBUG][ConsumeSystem] applied healing {val}, new HP = {hp_comp.current_hp}/{hp_comp.max_hp}")
                elif key == 'mana':
                    mana_comp = components.get('Mana', {}).get(player_eid)
                    if mana_comp:
                        mana_comp.current_mana = min(mana_comp.max_mana, mana_comp.current_mana + val)
                        print(f"[DEBUG][ConsumeSystem] applied mana {val}, new Mana = {mana_comp.current_mana}/{mana_comp.max_mana}")
                elif key == 'energy':
                    energy_comp = components.get('Energy', {}).get(player_eid)
                    if energy_comp:
                        energy_comp.current_energy = min(energy_comp.max_energy, energy_comp.current_energy + val)
                        print(f"[DEBUG][ConsumeSystem] applied energy {val}, new Energy = {energy_comp.current_energy}/{energy_comp.max_energy}")
                elif key == 'hunger':
                    hunger_comp = components.get('Hunger', {}).get(player_eid)
                    if hunger_comp:
                        hunger_comp.current_hunger = min(hunger_comp.max_hunger, hunger_comp.current_hunger + val)
                        print(f"[DEBUG][ConsumeSystem] applied hunger {val}, new Hunger = {hunger_comp.current_hunger}/{hunger_comp.max_hunger}")
                else:
                    print(f"[DEBUG][ConsumeSystem] unknown effect param: {key}={val}")
        inp.use_item = None
        print("[DEBUG][ConsumeSystem] inp.use_item reset")
