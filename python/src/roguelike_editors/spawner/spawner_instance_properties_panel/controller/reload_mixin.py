from __future__ import annotations

from typing import Optional, Dict, Any, Tuple, List

from roguelike_editors.spawner.services.persistence import find_instance_by_id


class ReloadMixin:
    def _reload_selected_from_json(self) -> None:
        """Reload the current selected instance from spawners_instances.json by original_id.
        Keeps selection index updated and refreshes visuals and rows.
        """
        sid = getattr(self.model, 'original_id', None)
        if not sid:
            return
        try:
            data, idx, _ = find_instance_by_id(str(sid))
            if idx is None:
                return
            inst = data[idx]
            self.model.selected_instance = inst
            self.model.selected_index = idx
            visuals = {}
            try:
                if isinstance(inst.get('visuals'), dict):
                    visuals = dict(inst.get('visuals') or {})
            except (AttributeError, TypeError, ValueError):
                visuals = {}
            self.model.visuals = visuals
            # Rebuild rows from fresh disk state
            self._ensure_buildings_index()
            self._build_visuals_rows()
            self._log.debug(f"[InstanceProps] _reload_selected_from_json: idx={idx} visuals={visuals}")
        except (AttributeError, TypeError, ValueError, OSError):
            pass
