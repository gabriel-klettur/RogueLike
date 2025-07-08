import pytest
from roguelike_game.ecs.components.inventory_component import InventoryComponent


def test_inventory_add_has_remove():
    inv = InventoryComponent(capacity=3)
    # Add items
    assert inv.add("gold", 5)
    assert inv.has("gold", 5)
    # Add another item
    assert inv.add("wood", 2)
    assert inv.has("wood", 2)
    # Partial remove
    assert inv.remove("gold", 3)
    assert inv.has("gold", 2)
    # Remove remaining
    assert inv.remove("gold", 2)
    assert not inv.has("gold", 1)
    # Removing non-existent fails
    assert not inv.remove("gold", 1)
    # Fill inventory capacity
    inv2 = InventoryComponent(capacity=1)
    assert inv2.add("apple", 1)
    assert not inv2.add("banana", 1)


def test_serialize():
    inv = InventoryComponent(capacity=2, player_id="player1")
    inv.add("gold", 10)
    inv.add("wood", 3)
    data = inv.serialize()
    assert data["player_id"] == "player1"
    assert data["capacity"] == 2
    assert data["slots"][0] == {"item": "gold", "quantity": 10}
    assert data["slots"][1] == {"item": "wood", "quantity": 3}
