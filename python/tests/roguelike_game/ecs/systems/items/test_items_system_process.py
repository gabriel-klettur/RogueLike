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


def test_teleport_system_cross_world_invokes_swap_and_refresh(monkeypatch):
    """Cross-world teleport debe invocar swap_world_and_spawn y refrescar partículas.

    Además debe marcar invalidate_spatial_index en el mundo ECS para que el
    índice espacial se reconstruya con el nuevo mapa y edificios.
    """

    system = ts.TeleportSystem()

    # Forzar current_world a 'base' durante el test
    from roguelike_engine.config.map_config import global_map_settings

    prev_world = getattr(global_map_settings, "current_world", "base")
    try:
        global_map_settings.current_world = "base"

        refresh_calls = {"n": 0}

        def fake_refresh(world):  # noqa: ANN001
            refresh_calls["n"] += 1

        # Parchear el refresco de partículas dentro del módulo teleport_system
        monkeypatch.setattr(ts, "_refresh_particles_from_world", fake_refresh, raising=True)

        class DummyMapManager:
            def __init__(self):
                self.calls = []

            def swap_world_and_spawn(self, world_id, tile_pos):
                self.calls.append((world_id, tile_pos))

        class DummyWorld:
            def __init__(self):
                self.components = {
                    "PlayerTagComponent": {1: object()},
                    "Position": {
                        1: types.SimpleNamespace(x=0, y=0),
                        2: types.SimpleNamespace(x=ts.TILE_SIZE // 2, y=0),
                    },
                    "TeleportComponent": {
                        2: types.SimpleNamespace(
                            dest_world="chaos_world",
                            dest_zone=None,
                            dest_x=5,
                            dest_y=6,
                        )
                    },
                }
                self.map_manager = DummyMapManager()
                self.invalidate_calls = 0

            def invalidate_spatial_index(self):
                self.invalidate_calls += 1

        world = DummyWorld()
        system.update(world)

        # swap_world_and_spawn debe haberse llamado una vez con el mundo y tile esperados
        assert world.map_manager.calls == [("chaos_world", (5, 6))]
        # Debe refrescar partículas exactamente una vez
        assert refresh_calls["n"] == 1
        # Debe marcar el índice espacial como sucio una vez
        assert world.invalidate_calls == 1
    finally:
        global_map_settings.current_world = prev_world
