from __future__ import annotations

from typing import Any


def get_transfer_system(world: Any):
    """Return InventoryTransferSystem instance from world, creating if missing."""
    for s in getattr(world, 'update_systems', []):
        if type(s).__name__ == 'InventoryTransferSystem':
            return s
    from roguelike_game.ecs.systems.inventory.inventory_transfer_system import (
        InventoryTransferSystem,
    )

    inst = InventoryTransferSystem()
    world.update_systems.append(inst)
    return inst
