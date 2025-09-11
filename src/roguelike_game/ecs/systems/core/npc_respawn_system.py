"""
NpcRespawnSystem
Emite SpawnRequest para NPCs persistidos en el save que pertenezcan
al nivel actual y no estén presentes aún en el ECS. Respeta flags
como 'dead' y reusa el instance_id persistente para consistencia
con inventarios y estado.
"""
from __future__ import annotations
from typing import Set, Tuple, Dict

from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest

import logging
import time
logger = logging.getLogger(__name__)


class NpcRespawnSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._requested: Set[str] = set()
        # Throttling / dedup de logs
        self._last_summary_sig = None
        self._last_summary_t = 0.0
        self._summary_interval = 1.0  # seconds
        self._skip_logged: Dict[Tuple[str, str], float] = {}
        self._skip_log_interval = 3.0  # seconds

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
        # Resumen con throttling
        now = time.time()
        summary_sig = (len(states), len(present_ids), len(self._requested))
        if (now - self._last_summary_t) >= self._summary_interval or summary_sig != self._last_summary_sig:
            try:
                logger.debug(
                    "[NpcRespawn] level=%s states_total=%s present=%s already_requested=%s",
                    current_level, summary_sig[0], summary_sig[1], summary_sig[2]
                )
            except Exception:
                pass
            self._last_summary_t = now
            self._last_summary_sig = summary_sig

        enqueued = 0
        # Crear SpawnRequest para cada NPC faltante de este nivel y no muerto
        for instance_id, st in states.items():
            try:
                if not instance_id:
                    if logger.isEnabledFor(logging.DEBUG):
                        logger.debug("[NpcRespawn] skip: missing instance_id entry=%s", st)
                    continue
                lvl = st.get('level')
                if lvl != current_level:
                    if logger.isEnabledFor(logging.DEBUG):
                        key = (str(instance_id), "level_mismatch")
                        tlast = self._skip_logged.get(key, 0.0)
                        if (now - tlast) >= self._skip_log_interval:
                            logger.debug("[NpcRespawn] skip: level mismatch iid=%s lvl=%s current=%s", instance_id, lvl, current_level)
                            self._skip_logged[key] = now
                    continue
                if bool(st.get('dead')):
                    if logger.isEnabledFor(logging.DEBUG):
                        key = (str(instance_id), "dead")
                        tlast = self._skip_logged.get(key, 0.0)
                        if (now - tlast) >= self._skip_log_interval:
                            logger.debug("[NpcRespawn] skip: dead iid=%s", instance_id)
                            self._skip_logged[key] = now
                    continue
                if instance_id in present_ids or instance_id in self._requested:
                    if logger.isEnabledFor(logging.DEBUG):
                        reason = "present" if instance_id in present_ids else "already_requested"
                        key = (str(instance_id), reason)
                        tlast = self._skip_logged.get(key, 0.0)
                        if (now - tlast) >= self._skip_log_interval:
                            logger.debug("[NpcRespawn] skip: %s iid=%s", reason, instance_id)
                            self._skip_logged[key] = now
                    continue
                tile = st.get('tile')
                proto = st.get('prototype')
                if not isinstance(tile, (list, tuple)) or len(tile) != 2:
                    if logger.isEnabledFor(logging.DEBUG):
                        key = (str(instance_id), "bad_tile")
                        tlast = self._skip_logged.get(key, 0.0)
                        if (now - tlast) >= self._skip_log_interval:
                            logger.debug("[NpcRespawn] skip: bad tile iid=%s tile=%s", instance_id, tile)
                            self._skip_logged[key] = now
                    continue
                if not proto:
                    # Sin prototipo no sabemos qué crear
                    if logger.isEnabledFor(logging.DEBUG):
                        key = (str(instance_id), "missing_proto")
                        tlast = self._skip_logged.get(key, 0.0)
                        if (now - tlast) >= self._skip_log_interval:
                            logger.debug("[NpcRespawn] skip: missing prototype iid=%s", instance_id)
                            self._skip_logged[key] = now
                    continue
                tx = int(tile[0]); ty = int(tile[1])
                # Emitir SpawnRequest con instance_id fijo
                req_eid = world.create_entity()
                world.components.setdefault('SpawnRequest', {})[req_eid] = SpawnRequest(
                    prototype=str(proto), position=(tx, ty), instance_id=str(instance_id)
                )
                self._requested.add(str(instance_id))
                enqueued += 1
                try:
                    logger.info(
                        "[NpcRespawn] Enqueued SpawnRequest iid=%s proto=%s tile=(%s,%s)",
                        instance_id, proto, tx, ty
                    )
                except Exception:
                    pass
            except Exception:
                # No bloquear el juego por un estado defectuoso
                continue
        try:
            if enqueued:
                logger.info("[NpcRespawn] total enqueued=%s for level=%s", enqueued, current_level)
            else:
                # Solo reportar "no requests" cuando toque el resumen
                if (now - self._last_summary_t) < 0.05:  # el resumen fue emitido justo ahora
                    logger.debug("[NpcRespawn] no requests enqueued for level=%s", current_level)
        except Exception:
            pass
