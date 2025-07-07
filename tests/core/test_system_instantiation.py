import os
import pytest
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem


def test_inventory_pickup_system_instantiation(world):
    """
    Ensure ECSWorld instantiates InventoryPickupSystem with default paths as strings.
    """
    # Find pickup system in update_systems
    pickup_systems = [s for s in world.update_systems if isinstance(s, InventoryPickupSystem)]
    assert len(pickup_systems) == 1, "Expected exactly one InventoryPickupSystem"
    inst = pickup_systems[0]
    # Paths should be strings
    assert isinstance(inst.active_monster_path, str), "active_monster_path should be a string"
    assert isinstance(inst.active_player_path, str), "active_player_path should be a string"
    # Default paths end with correct filenames
    assert inst.active_monster_path.endswith(os.path.join('data', 'inventory_monsters.json'))
    assert inst.active_player_path.endswith(os.path.join('data', 'inventory_player.json'))
    # Drop manager path
    assert inst.drop_manager.path.endswith(os.path.join('data', 'inventory_map.json'))
