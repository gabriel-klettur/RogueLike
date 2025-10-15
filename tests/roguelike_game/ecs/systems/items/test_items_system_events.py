import types

import roguelike_game.ecs.systems.items.consume_system as cs
from roguelike_game.ecs.components.inventory_component import InventoryComponent


def test_consume_system_event_use_item_resets_flag(monkeypatch):
    # Stub loader to avoid IO
    monkeypatch.setattr(cs, 'load_items', lambda path: {
        'elixir': types.SimpleNamespace(default_params={'mana': 3})
    }, raising=True)
    system = cs.ConsumeSystem()

    player_eid = 1
    inv = InventoryComponent(capacity=2, player_id='p')
    inv.add('elixir', 1)

    input_comp = types.SimpleNamespace(use_item='elixir')
    mana = types.SimpleNamespace(current_mana=0, max_mana=5)

    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {player_eid: object()},
        'InputComponent': {player_eid: input_comp},
        'InventoryComponent': {player_eid: inv},
        'Mana': {player_eid: mana},
    }

    system.update(world)

    # Event flag cleared after handling
    assert input_comp.use_item is None
    # Effect applied
    assert mana.current_mana == 3
