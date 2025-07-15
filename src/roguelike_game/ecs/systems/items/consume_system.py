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
            healing = None
            if model and hasattr(model, 'default_params'):
                healing = getattr(model, 'default_params').get('healing')
            if healing is None and model and getattr(model, 'effect', '').startswith('heal_'):
                try:
                    healing = int(model.effect.split('_')[1])
                except ValueError:
                    healing = 0
            print(f"[DEBUG][ConsumeSystem] healing amount = {healing}")
            if healing:
                hp_comp = components.get('Health', {}).get(player_eid)
                if hp_comp:
                    hp_comp.current_hp = min(hp_comp.max_hp, hp_comp.current_hp + healing)
                print(f"[DEBUG][ConsumeSystem] new HP = {hp_comp.current_hp}/{hp_comp.max_hp}")
        inp.use_item = None
        print("[DEBUG][ConsumeSystem] inp.use_item reset")
