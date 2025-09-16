from __future__ import annotations

from typing import List, Tuple


class RowsMixin:
    def _flatten_instance(self) -> List[Tuple[str, str]]:
        data = self.model.selected_instance or {}
        # Present a stable order: id, template_id, zone, tile, overrides.*
        flat: List[Tuple[str, str]] = []
        try:
            flat.append(("id", str(data.get('id'))))
        except (AttributeError, TypeError, ValueError):
            pass
        try:
            # Simple fields
            flat.append(("template_id", str(data.get('template_id'))))
        except (AttributeError, TypeError, ValueError):
            pass
        try:
            flat.append(("zone", str(data.get('zone'))))
        except (AttributeError, TypeError, ValueError):
            pass
        try:
            tile = data.get('tile', [0, 0])
            flat.append(("tile.0", str(tile[0] if isinstance(tile, (list, tuple)) and len(tile) > 0 else 0)))
            flat.append(("tile.1", str(tile[1] if isinstance(tile, (list, tuple)) and len(tile) > 1 else 0)))
        except (AttributeError, TypeError, ValueError):
            pass
        # Overrides tree
        try:
            ov = data.get('overrides')
            if isinstance(ov, dict):
                for k, v in self.view._flatten(ov, prefix="overrides"):  # reuse view flattener
                    flat.append((k, v))
        except (AttributeError, TypeError, ValueError):
            pass
        return flat

    def get_rows(self) -> List[Tuple[str, str]]:
        return list(self._rows)
