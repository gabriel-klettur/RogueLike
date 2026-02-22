import types

import roguelike_game.ecs.systems.items.consume_system as cs
import roguelike_game.ecs.systems.items.teleport_system as ts
from roguelike_game.ecs.components.inventory_component import InventoryComponent


def test_consume_system_no_player_tags_noop(monkeypatch):
    # Avoid file IO
    monkeypatch.setattr(cs, 'load_items', lambda path: {}, raising=True)
    system = cs.ConsumeSystem()

    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {},  # no players
        'InputComponent': {},
        'InventoryComponent': {},
        'Health': {},
    }
    # Should not raise or modify state
    system.update(world)


def test_consume_system_missing_inventory_or_input(monkeypatch):
    monkeypatch.setattr(cs, 'load_items', lambda path: {
        'potion': types.SimpleNamespace(default_params={'healing': 5})
    }, raising=True)
    system = cs.ConsumeSystem()
    player_eid = 1

    # Case: no InputComponent entry
    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {player_eid: object()},
        'InputComponent': {},
        'InventoryComponent': {player_eid: InventoryComponent(capacity=2, player_id='p')},
        'Health': {player_eid: types.SimpleNamespace(current_hp=1, max_hp=10)},
    }
    system.update(world)  # returns early

    # Case: input present but no inventory -> returns after remove fails
    invless = types.SimpleNamespace()
    invless.components = {
        'PlayerTagComponent': {player_eid: object()},
        'InputComponent': {player_eid: types.SimpleNamespace(use_item='potion')},
        'InventoryComponent': {},
        'Health': {player_eid: types.SimpleNamespace(current_hp=1, max_hp=10)},
    }
    system.update(invless)


def test_consume_system_unknown_effect_key_is_ignored(monkeypatch):
    monkeypatch.setattr(cs, 'load_items', lambda path: {
        'weird': types.SimpleNamespace(default_params={'unknown_key': 123})
    }, raising=True)
    system = cs.ConsumeSystem()
    player_eid = 1
    inv = InventoryComponent(capacity=2, player_id='p')
    inv.add('weird', 1)

    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {player_eid: object()},
        'InputComponent': {player_eid: types.SimpleNamespace(use_item='weird')},
        'InventoryComponent': {player_eid: inv},
        'Health': {player_eid: types.SimpleNamespace(current_hp=5, max_hp=10)},
    }
    system.update(world)
    # Inventory item consumed even if effect key is unknown; no crash
    assert inv.has('weird', 1) is False


def test_teleport_system_no_teleports_or_no_player_is_noop():
    system = ts.TeleportSystem()

    # No teleports
    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {1: object()},
        'Position': {1: types.SimpleNamespace(x=0, y=0)},
        'TeleportComponent': {},
    }
    system.update(world)

    # No player
    world2 = types.SimpleNamespace()
    world2.components = {
        'PlayerTagComponent': {},
        'Position': {},
        'TeleportComponent': {},
    }
    system.update(world2)


def test_teleport_system_missing_positions_is_noop():
    system = ts.TeleportSystem()
    player_eid = 1
    portal_eid = 2

    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {player_eid: object()},
        'Position': {player_eid: None},  # missing player position
        'TeleportComponent': {portal_eid: types.SimpleNamespace(dest_map='m', dest_x=0, dest_y=0)},
    }
    system.update(world)
