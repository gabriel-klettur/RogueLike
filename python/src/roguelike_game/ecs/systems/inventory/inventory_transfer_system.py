import os
import json
from roguelike_engine.config import config

from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.core.identity import Faction

import logging
logger = logging.getLogger(__name__)

class InventoryTransferSystem:

    def __init__(self,
                 active_monster_path: str | None = None,
                 active_player_path: str | None = None,
                 active_neutral_path: str | None = None):
        base = config.DATA_DIR
        self.active_monster_path = active_monster_path or os.path.join(base, 'inventory', 'active', 'inventory_monsters.json')
        self.active_player_path = active_player_path or os.path.join(base, 'inventory', 'active', 'inventory_player.json')
        self.active_neutral_path = active_neutral_path or os.path.join(base, 'inventory', 'active', 'inventory_neutrals.json')
        self.world = None

    def update(self, world, *args):
        self.world = world
        return

    def transfer(self, world, item_id: str, quantity: int, source_eid: int, target_eid: int) -> None:
        self.world = world
        comps = world.components
        invs = comps.get('InventoryComponent', {})
        source_inv = invs.get(source_eid)
        target_inv = invs.get(target_eid)
        if not source_inv or not target_inv:
            raise ValueError(f"Invalid source or target entity for transfer: {source_eid} -> {target_eid}")
        # Check availability
        if not source_inv.has(item_id, quantity):
            raise ValueError(f"Source eid={source_eid} does not have {quantity}x{item_id}")
        # Perform atomic transfer
        source_inv.remove(item_id, quantity)
        added = target_inv.add(item_id, quantity)
        if not added:
            # Rollback on failure
            source_inv.add(item_id, quantity)
            raise ValueError(f"Transfer failed, target eid={target_eid} has no space for {quantity}x{item_id}")
        # Persist inventories
        self._persist_inventory(source_eid, source_inv)
        self._persist_inventory(target_eid, target_inv)
        # Dispatch event (for UI/logs)
        logger.debug(f"[TransferEvent] Transferred {quantity}x{item_id} from eid={source_eid} to eid={target_eid}")

    def _persist_inventory(self, eid: int, inv: InventoryComponent) -> None:
        comps = getattr(self.world, 'components', {})
        inst = comps.get('MonsterInstanceComponent', {}).get(eid)
        identity = comps.get('Identity', {}).get(eid)
        player_tags = comps.get('PlayerTagComponent', {})

        # Clave de persistencia: instance_id si existe, si no eid como str
        if inst:
            key = getattr(inst, 'instance_id', None) or str(eid)
        else:
            key = str(eid)

        # Seleccionar archivo activo según tipo/facción
        save_path = self.active_monster_path
        try:
            if eid in player_tags:
                save_path = self.active_player_path
            else:
                # Si tiene identidad y es neutral, usar neutrals
                if identity is not None and getattr(identity, 'faction', None) == Faction.NEUTRAL:
                    save_path = self.active_neutral_path
        except Exception:
            # Fallback robusto: mantener save_path por defecto
            pass

        # Leer, actualizar y escribir solo si existe la entrada
        try:
            with open(save_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            data = {}
        if key in data:
            data[key]['slots'] = inv.serialize().get('slots')
            with open(save_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=2)
