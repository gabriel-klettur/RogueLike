"""
NpcRestoreSystem
Aplica, una sola vez por NPC, la posición (tile->pixel) y la vida actuales
restauradas desde el guardado. Lee los estados desde
world.map_manager._local_state['npc_states'] y los filtra por nivel.

Se basa en MonsterInstanceComponent.instance_id para casar con las claves del save.
"""
from __future__ import annotations
from typing import Set, Tuple

import logging
import time
logger = logging.getLogger(__name__)


class NpcRestoreSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Evitar re-aplicar repetidamente
        self._applied: Set[str] = set()
        # Throttling del resumen de debug
        self._last_summary_sig = None
        self._last_summary_t = 0.0
        self._summary_interval = 5.0  # seconds
        # Heartbeat para emitir el resumen aunque no cambie el estado
        self._heartbeat_interval = 30.0  # seconds between identical summaries

    def update(self, world, *args):
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
        npc_tags = comps.get('NPCTagComponent', {}) or {}
        pos_store = comps.get('Position', {}) or {}
        health_store = comps.get('Health', {}) or {}

        # Helper: tile -> pixel usando el MapManager
        def tile_to_pixel(tile_xy: Tuple[int, int]) -> Tuple[int, int] | None:
            try:
                return mgr.get_spawn_pixel((int(tile_xy[0]), int(tile_xy[1])))
            except Exception:
                return None

        # Resumen con throttling y heartbeat
        now = time.time()
        summary_sig = (len(states), len(self._applied))
        if summary_sig != self._last_summary_sig or (now - self._last_summary_t) >= self._heartbeat_interval:
            try:
                logger.debug(
                    "[NpcRestore] level=%s states_total=%s already_applied=%s",
                    current_level, summary_sig[0], summary_sig[1]
                )
            except Exception:
                pass
            self._last_summary_t = now
            self._last_summary_sig = summary_sig

        applied_count = 0
        for eid in list(npc_tags.keys()):
            inst = inst_store.get(eid)
            if not inst:
                continue
            instance_id = getattr(inst, 'instance_id', None)
            if not instance_id or instance_id in self._applied:
                continue
            st = states.get(str(instance_id)) or {}
            # Filtrar por nivel
            if (st.get('level') or current_level) != current_level:
                continue

            applied_any = False

            # Aplicar posición si hay tile
            tile = st.get('tile')
            if isinstance(tile, (list, tuple)) and len(tile) == 2:
                try:
                    px_py = tile_to_pixel((tile[0], tile[1]))
                    if px_py is not None:
                        pos = pos_store.get(eid)
                        if pos is not None:
                            pos.x, pos.y = int(px_py[0]), int(px_py[1])
                            applied_any = True
                except Exception:
                    pass

            # Aplicar HP si hay componente Health
            if eid in health_store:
                try:
                    hp_cmp = health_store.get(eid)
                    hp_val = st.get('hp')
                    if hp_val is not None:
                        try:
                            hp_int = int(hp_val)
                        except Exception:
                            hp_int = hp_val
                        # Clamp básico
                        max_hp = getattr(hp_cmp, 'max_hp', None)
                        if isinstance(hp_int, int) and isinstance(max_hp, int) and max_hp > 0:
                            hp_int = max(0, min(hp_int, max_hp))
                        hp_cmp.current_hp = hp_int
                        applied_any = True
                except Exception:
                    pass

            if applied_any:
                self._applied.add(str(instance_id))
                try:
                    logger.info(
                        "[NpcRestore] Applied state to npc instance_id=%s pos_tile=%s hp=%s",
                        instance_id, st.get('tile'), st.get('hp')
                    )
                except Exception:
                    pass
                applied_count += 1
        try:
            if applied_count:
                logger.info("[NpcRestore] total applied=%s for level=%s", applied_count, current_level)
        except Exception:
            pass
