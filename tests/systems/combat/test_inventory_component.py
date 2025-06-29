# Path: tests/systems/combat/test_inventory_component.py
import pytest
from roguelike_game.ecs.components.combat.inventory import InventoryComponent

def test_inventory_initial_empty():
    inv = InventoryComponent()
    assert inv.items == []

def test_inventory_add_remove_items():
    inv = InventoryComponent()
    inv.items.append('sword')
    assert 'sword' in inv.items
    inv.items.remove('sword')
    assert 'sword' not in inv.items