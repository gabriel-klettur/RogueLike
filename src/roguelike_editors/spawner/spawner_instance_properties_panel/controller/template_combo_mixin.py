from __future__ import annotations

from typing import List, Optional
from roguelike_editors.spawner.services.persistence import load_spawners_json


class TemplateComboMixin:
    def _load_template_options(self) -> None:
        try:
            data = load_spawners_json()
            ids: List[str] = []
            for sp in data:
                try:
                    sid = str(sp.get('id'))
                    if sid:
                        ids.append(sid)
                except Exception:
                    continue
            ids = sorted(set(ids))
            self.model.template_options = ids
        except Exception:
            self.model.template_options = []

    def get_template_options(self) -> List[str]:
        if not getattr(self.model, 'template_options', None):
            self._load_template_options()
        return list(self.model.template_options)

    def get_current_template_index(self) -> Optional[int]:
        opts = self.get_template_options()
        inst = self.model.selected_instance or {}
        cur = None
        try:
            cur = str(inst.get('template_id'))
        except Exception:
            cur = None
        if cur is None:
            return None
        try:
            return opts.index(cur)
        except ValueError:
            return None

    def select_template_by_index(self, idx: int) -> None:
        opts = self.get_template_options()
        if not (0 <= idx < len(opts)):
            return
        self.set_template_id(opts[idx])

    def set_template_id(self, new_id: str) -> None:
        inst = self.model.selected_instance
        if inst is None:
            return
        try:
            inst['template_id'] = str(new_id)
        except Exception:
            inst['template_id'] = new_id  # type: ignore
        # Persist and refresh
        self._persist_instance()
        self._rows = self._flatten_instance()
