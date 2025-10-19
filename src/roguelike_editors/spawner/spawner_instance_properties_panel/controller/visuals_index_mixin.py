from __future__ import annotations

from typing import List, Dict, Any, Set

from ..services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    load_buildings_templates as svc_load_buildings_templates,
)


class VisualsIndexMixin:
    def _ensure_buildings_index(self) -> None:
        if getattr(self, '_building_index', None) is not None:
            return
        try:
            arr = svc_load_buildings_instances()
            idx: Dict[int, str] = {}
            if isinstance(arr, list):
                for e in arr:
                    try:
                        bid = int(e.get('id'))
                        tid = str(e.get('template_id'))
                        idx[bid] = tid
                    except (AttributeError, TypeError, ValueError):
                        continue
        except (OSError, ValueError, TypeError, AttributeError):
            idx = {}
        self._building_index = idx

    def _ensure_building_templates(self) -> None:
        if getattr(self, '_building_template_ids', None) is not None:
            return
        ids: Set[int] = set()
        try:
            arr = svc_load_buildings_templates()
            if isinstance(arr, list):
                for e in arr:
                    try:
                        tid = int(e.get('id'))
                        ids.add(tid)
                    except (AttributeError, TypeError, ValueError):
                        continue
        except (OSError, ValueError, TypeError, AttributeError):
            ids = set()
        self._building_template_ids = ids
