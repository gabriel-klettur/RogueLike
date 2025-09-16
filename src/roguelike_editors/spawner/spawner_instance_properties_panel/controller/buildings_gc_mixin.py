from __future__ import annotations

from typing import List, Dict, Any, Optional

from ..services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    write_buildings_instances as svc_write_buildings_instances,
    load_buildings_templates as svc_load_buildings_templates,
)


class BuildingsGCMixin:
    def _load_buildings_instances(self) -> List[Dict[str, Any]]:
        return svc_load_buildings_instances()

    def _write_buildings_instances(self, data: List[Dict[str, Any]]) -> None:
        svc_write_buildings_instances(data)
        # Post-write GC to ensure consistency, but throttle to avoid tight loops
        try:
            now = self._now_ms()
            if not getattr(self, '_last_post_write_gc_ms', 0) or (now - self._last_post_write_gc_ms) > 1500:
                self._last_post_write_gc_ms = now
                self._gc_invalid_building_instances()
        except (AttributeError, TypeError, ValueError):
            pass

    def _count_instance_refs_in_visuals(self, inst_id: int) -> int:
        visuals = getattr(self.model, 'visuals', {}) or {}
        cnt = 0
        for _k, v in visuals.items():
            try:
                if isinstance(v, dict):
                    val = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                else:
                    val = int(v)
                if val == inst_id:
                    cnt += 1
            except (AttributeError, TypeError, ValueError):
                continue
        return cnt

    def _find_existing_visual_instance_by_template(self, template_id: int) -> Optional[int]:
        """(Disabled) Reuse is not allowed: each state must have its own building instance.
        Always return None so creation flow clones/creates a new instance.
        """
        return None

    def _gc_invalid_building_instances(self) -> None:
        """Remove entries from buildings_instances.json with invalid id or template_id.
        - Drops entries where 'id' is missing or non-integer
        - Drops entries where 'template_id' is missing, non-integer, or not present in templates
        - Deduplicates entries by (zone, rel_x, rel_y, template_id) keeping the best candidate
        Persists cleaned list and refreshes building index if any were removed.
        """
        # Ensure template ids
        try:
            self._ensure_building_templates()
        except Exception:
            pass
        valid_tpls = getattr(self, '_building_template_ids', set()) or set()
        data = self._load_buildings_instances()
        if not data:
            return
        kept = []
        removed = False
        # First filter invalids
        for e in data:
            try:
                eid = int(e.get('id'))
                tid = int(e.get('template_id'))
            except (AttributeError, TypeError, ValueError):
                removed = True
                continue
            if tid not in valid_tpls:
                removed = True
                continue
            kept.append(e)
        # Then deduplicate by (zone,rel_x,rel_y,template_id) for NORMAL entries only.
        # Entries linked to spawners are PROTECTED and not deduplicated here.
        try:
            def _key(e: dict) -> str:
                try:
                    zone = str(e.get('zone') or 'lobby')
                    rx = int(e.get('rel_x') or 0)
                    ry = int(e.get('rel_y') or 0)
                    tid = int(e.get('template_id') or -1)
                    return f"{zone}|{rx}|{ry}|{tid}"
                except (AttributeError, TypeError, ValueError):
                    return str(id(e))
            def _is_spawner_linked(e: dict) -> bool:
                try:
                    ov = e.get('overrides') if isinstance(e, dict) else None
                    if isinstance(ov, dict) and (ov.get('_is_spawner_visual') or ov.get('spawner_instance_id')):
                        return True
                    if str(e.get('spawner_instance_id') or '') or str(e.get('spawn_id') or ''):
                        return True
                except Exception:
                    pass
                return False
            protected = [e for e in kept if _is_spawner_linked(e)]
            normal = [e for e in kept if not _is_spawner_linked(e)]
            seen: dict[str, dict] = {}
            def _score_normal(e: dict) -> int:
                try:
                    return -int(e.get('id') or 0)
                except Exception:
                    return 0
            for e in normal:
                k = _key(e)
                cur = seen.get(k)
                if cur is None or _score_normal(e) > _score_normal(cur):
                    seen[k] = e
            dedup_normal = list(seen.values())
            # Drop normals that collide with any protected key
            pkeys = { _key(e) for e in protected }
            dedup_normal = [e for e in dedup_normal if _key(e) not in pkeys]
            deduped = protected + dedup_normal
            if len(deduped) != len(kept):
                kept = deduped
                removed = True
        except (AttributeError, TypeError, ValueError):
            pass
        if removed:
            try:
                self._log_warning_rl("gc_dedup", f"[InstanceProps] GC/Dedup buildings_instances: before={len(data)} after={len(kept)} removed={len(data)-len(kept)}", 1500)
            except (AttributeError, TypeError, ValueError):
                pass
            self._write_buildings_instances(kept)
            # Refresh index to reflect removals
            try:
                self._building_index = None
                self._ensure_buildings_index()
            except Exception:
                pass
