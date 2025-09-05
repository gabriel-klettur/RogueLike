"""
NpcRespawnSystem
Emite SpawnRequest para NPCs persistidos en el save que pertenezcan
al nivel actual y no estén presentes aún en el ECS. Respeta flags
como 'dead' y reusa el instance_id persistente para consistencia
con inventarios y estado.
"""
from __future__ import annotations
from typing import Set, Tuple

from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest

import logging
logger = logging.getLogger(__name__)


class NpcRespawnSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._requested: Set[str] = set()

    def update(self, world, *args):
        # Fuente de estados guardados: MapManager._local_state['npc_states']
        try:
            mgr = world.map_manager
            states = getattr(mgr, "_local_state", {}).get("npc_states", {}) or {}
            current_level = getattr(mgr, "name", None)
            if not states or not current_level:
                return
        except Exception:
            return

        comps = world.components
        inst_store = comps.get('MonsterInstanceComponent', {}) or {}
        # Conjunto de instance_ids ya presentes en el mundo
        present_ids: Set[str] = set()
        for eid, inst in inst_store.items():
            iid = getattr(inst, 'instance_id', None)
            if iid:
                present_ids.add(str(iid))

        # Crear SpawnRequest para cada NPC faltante de este nivel y no muerto
        for instance_id, st in states.items():
            try:
                if not instance_id:
                    continue
                lvl = st.get('level')
                if lvl != current_level:
                    continue
                if bool(st.get('dead')):
                    continue
                if instance_id in present_ids or instance_id in self._requested:
                    continue
                tile = st.get('tile')
                proto = st.get('prototype')
                if not isinstance(tile, (list, tuple)) or len(tile) != 2:
                    continue
                if not proto:
                    # Sin prototipo no sabemos qué crear
                    continue
                tx = int(tile[0]); ty = int(tile[1])
                # Emitir SpawnRequest con instance_id fijo
                req_eid = world.create_entity()
                world.components.setdefault('SpawnRequest', {})[req_eid] = SpawnRequest(
                    prototype=str(proto), position=(tx, ty), instance_id=str(instance_id)
                )
                self._requested.add(str(instance_id))
            except Exception:
                # No bloquear el juego por un estado defectuoso
                continue
