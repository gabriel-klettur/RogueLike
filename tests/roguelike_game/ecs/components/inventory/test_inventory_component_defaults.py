import dataclasses

from roguelike_game.ecs.components.inventory_component import InventoryComponent


def test_inventory_defaults_capacity_and_slots():
    inv = InventoryComponent()
    assert inv.capacity == 20
    assert inv.player_id is None
    assert isinstance(inv.slots, list)
    assert len(inv.slots) == 20
    assert all(s is None for s in inv.slots)


def test_inventory_add_has_remove_and_serialize():
    inv = InventoryComponent(capacity=3, player_id="p1")
    # Add creates a new stack in an empty slot
    assert inv.add("potion", 3) is True
    assert inv.has("potion", 3) is True
    # Removing partially keeps stack
    assert inv.remove("potion", 2) is True
    assert inv.has("potion", 1) is True
    # Removing remaining clears slot
    assert inv.remove("potion", 1) is True
    assert inv.has("potion", 1) is False

    data = inv.serialize()
    assert data["player_id"] == "p1"
    assert data["capacity"] == 3
    assert isinstance(data["slots"], list)
    assert len(data["slots"]) == 3
