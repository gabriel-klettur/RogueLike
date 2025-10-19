import types

import roguelike_game.ecs.systems.items.consume_system as cs
import roguelike_game.ecs.systems.items.teleport_system as ts
from roguelike_game.ecs.components.inventory_component import InventoryComponent


def test_consume_system_uses_item_and_heals(monkeypatch):
    # Stub load_items to avoid filesystem
    monkeypatch.setattr(cs, 'load_items', lambda path: {
        'potion': types.SimpleNamespace(default_params={'healing': 5})
    }, raising=True)

    system = cs.ConsumeSystem()

    # World with player, input to use potion, inventory, and health
    player_eid = 1
    inv = InventoryComponent(capacity=3, player_id='p')
    inv.add('potion', 2)

    health = types.SimpleNamespace(current_hp=7, max_hp=10)
    input_comp = types.SimpleNamespace(use_item='potion')

    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {player_eid: object()},
        'InputComponent': {player_eid: input_comp},
        'InventoryComponent': {player_eid: inv},
        'Health': {player_eid: health},
    }

    system.update(world)

    # Health increased and input reset
    assert health.current_hp == 10  # healed by 5 but clamped to max 10
    assert input_comp.use_item is None
    # Inventory decreased by 1
    assert inv.has('potion', 2) is False
    assert inv.has('potion', 1) is True


def test_teleport_system_detects_nearby_portal(monkeypatch):
    # Create system
    system = ts.TeleportSystem()

    # Player at (0,0), portal within TILE_SIZE distance
    player_eid = 1
    portal_eid = 2
    Position = types.SimpleNamespace  # simple(x=, y=)
    Teleport = types.SimpleNamespace  # dest_map, dest_x, dest_y

    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {player_eid: object()},
        'Position': {
            player_eid: Position(x=0, y=0),
            portal_eid: Position(x=ts.TILE_SIZE//2, y=0),
        },
        'TeleportComponent': {portal_eid: Teleport(dest_map='next', dest_x=5, dest_y=6)},
    }

    # Should not raise; behavior is logging + early break when collision
    system.update(world)
