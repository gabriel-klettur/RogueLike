import pytest
from types import SimpleNamespace
from roguelike_engine.console.contexts.inventory import InventoryContext


def test_inventory_context_absent_inventory():
    # Game sin InventoryComponent
    ecs_world = SimpleNamespace(player_entity=1, components={})
    game = SimpleNamespace(ecs=SimpleNamespace(ecs_world=ecs_world), items=set())

    ctx = InventoryContext(game)
    assert ctx.list() == "Inventario no disponible"
    assert ctx.add_direct("potion_small", 1) == "Inventario no disponible"
