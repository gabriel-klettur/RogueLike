from __future__ import annotations

from typing import Dict, Any, Optional

import pygame
from roguelike_editors.spawner.services.persistence import find_instance_by_id


class VisualsSanitizeMixin:
    def _sanitize_visuals_instances(self) -> None:
        """Remove visuals entries whose instance id does not exist in buildings_instances.json.
        Rule: if a visuals state has no Template (would display as 'N/A'), then it must not have an Instance.
        Persists the spawner instance if any removals occur and rebuilds rows.
        """
        # Ensure we are checking against a fresh buildings index
        self._ensure_buildings_index()
        # Ensure we have valid building template ids to allow template-only mappings
        self._ensure_building_templates()
        # Prefer disk truth: do not recreate mappings that were already removed on disk
        disk_visuals_keys: set[str] = set()
        try:
            sid = getattr(self.model, 'original_id', None)
            if sid:
                data, idx, _ = find_instance_by_id(str(sid))
                if idx is not None:
                    vis_disk = data[idx].get('visuals')
                    if isinstance(vis_disk, dict):
                        disk_visuals_keys = {str(k) for k in vis_disk.keys()}
        except (AttributeError, TypeError, ValueError, OSError):
            disk_visuals_keys = set()
        # Skip sanitization during debounce window
        try:
            import pygame as _pg
            now = int(_pg.time.get_ticks() or 0)
        except (ImportError, AttributeError, TypeError, ValueError):
            now = 0
        if getattr(self, '_sanitize_block_until_ms', 0) and now < self._sanitize_block_until_ms:
            try:
                self._log_debug_rl("sanitize_skip_debounce", f"[InstanceProps] sanitize_visuals: SKIP (debounce) now={now} until={self._sanitize_block_until_ms}")
            except (AttributeError, TypeError, ValueError):
                pass
            return
        visuals = dict(getattr(self.model, 'visuals', {}) or {})
        if not visuals:
            return
        idx = self._building_index or {}
        # Helper: consider a building present if it's in JSON index OR currently spawned in world
        def _building_exists(bid: int) -> bool:
            try:
                if int(bid) in idx:
                    return True
            except (AttributeError, TypeError, ValueError):
                pass
            try:
                if self._find_building_entity_by_id(int(bid)) is not None:
                    return True
            except (AttributeError, TypeError, ValueError):
                pass
            return False
        valid_tpls = self._building_template_ids or set()
        removed_any = False
        repaired_any = False
        for k in list(visuals.keys()):
            v = visuals.get(k)
            if v is None:
                continue
            # If the mapping is no longer present on disk for this instance, drop it without repair
            try:
                if disk_visuals_keys and (str(k) not in disk_visuals_keys):
                    try:
                        self._log_info_rl(f"sanitize_drop_{k}", f"[InstanceProps] sanitize_visuals: dropping state='{k}' (absent on disk)")
                    except (AttributeError, TypeError, ValueError):
                        pass
                    visuals.pop(k, None)
                    removed_any = True
                    continue
            except (AttributeError, TypeError, ValueError):
                pass
            vid = None
            vtpl = None
            try:
                if isinstance(v, dict):
                    vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                    try:
                        vtpl = int(v.get('template_id')) if v.get('template_id') is not None else None
                    except (AttributeError, TypeError, ValueError):
                        vtpl = None
                else:
                    vid = int(v)
            except (AttributeError, TypeError, ValueError):
                # Non-integer/invalid mapping is invalid
                vid = None
            # Keep or repair mapping
            repaired = False
            if vid is not None and _building_exists(int(vid)):
                keep = True
            elif isinstance(v, dict) and vtpl is not None and vtpl in valid_tpls:
                # Auto-repair by creating/reusing an instance for this template
                try:
                    try:
                        if not hasattr(self.model, 'visuals_pending_templates') or self.model.visuals_pending_templates is None:
                            self.model.visuals_pending_templates = {}
                    except (AttributeError, TypeError):
                        pass
                    self.model.visuals_pending_templates[str(k)] = str(int(vtpl))
                    new_id = self.add_building_instance_for_visual(str(k), reveal=False)
                    if new_id is not None:
                        visuals[str(k)] = {'instance_id': int(new_id), 'template_id': int(vtpl)}
                        repaired = True
                        repaired_any = True
                        try:
                            self._log_info_rl(f"sanitize_repair_{k}", f"[InstanceProps] sanitize_visuals: repaired state='{k}' -> instance_id={new_id} tpl={vtpl}")
                        except (AttributeError, TypeError, ValueError):
                            pass
                    else:
                        repaired = False
                except (AttributeError, TypeError, ValueError, OSError):
                    repaired = False
                keep = repaired
            else:
                keep = False
            if not keep and not repaired:
                try:
                    reason = 'invalid' if vid is None else 'missing in buildings and no valid template_id'
                    self._log_warning_rl(f"sanitize_remove_{k}", f"[InstanceProps] sanitize_visuals: removing state='{k}' reason={reason} value={v}")
                except (AttributeError, TypeError, ValueError):
                    pass
                visuals.pop(k, None)
                removed_any = True
        if removed_any or repaired_any:
            # Apply and persist cleanup
            self.model.visuals = visuals
            try:
                if self.model.selected_instance is not None:
                    self.model.selected_instance['visuals'] = visuals
            except AttributeError:
                pass
            self._persist_instance()
            # Rebuild to refresh UI
            self._build_visuals_rows()
            try:
                self._log_info_rl("sanitize_persisted", f"[InstanceProps] sanitize_visuals: persisted cleanup/repairs keys={list(visuals.keys())}")
            except (AttributeError, TypeError, ValueError):
                pass
