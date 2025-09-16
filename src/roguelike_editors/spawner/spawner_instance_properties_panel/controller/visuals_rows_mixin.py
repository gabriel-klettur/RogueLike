from __future__ import annotations

from typing import List, Tuple


class VisualsRowsMixin:
    def get_visuals_rows(self) -> List[Tuple[str, str, str]]:
        """Expose prebuilt visuals rows for the view (state, instance_id, template_id).
        The view and visuals view call this; missing it causes AttributeError and an empty panel.
        """
        try:
            rows = getattr(self.model, 'visuals_rows', None)
            if isinstance(rows, list):
                return list(rows)
        except Exception:
            pass
        return []

    def _build_visuals_rows(self) -> None:
        visuals = getattr(self.model, 'visuals', {}) or {}
        # Log visuals only when they change (avoid per-frame spam)
        try:
            # Build a stable signature
            if isinstance(visuals, dict):
                sig = tuple(sorted((str(k), str(v)) for k, v in visuals.items()))
            else:
                sig = (str(visuals),)
            if sig != self._last_visuals_log_sig:
                self._last_visuals_log_sig = sig
                self._log.debug(f"[InstanceProps] visuals updated: {visuals}")
        except (TypeError, ValueError):
            pass
        idx = self._building_index or {}
        rows: List[tuple[str, str, str]] = []

        # Canonical state order to display always (TitleCase)
        canonical_states: List[str] = [
            'AwaitTrigger',
            'SpawningWave',
            'WaitCooldown',
            'WaitClear',
            'WaitRestart',
            'Finished',
        ]

        def _to_snake(title: str) -> str:
            s = str(title or '')
            out = []
            for i, ch in enumerate(s):
                if ch.isupper() and i > 0:
                    out.append('_')
                out.append(ch.lower())
            return ''.join(out)

        def _candidates_for(canon: str) -> List[str]:
            snake = _to_snake(canon)
            return [
                str(canon),                  # TitleCase
                snake,                        # snake_case
                snake.replace('_', ''),       # condensed snake
                str(canon).lower(),           # lowercase title
            ]

        matched_keys: set[str] = set()
        # Map displayed canonical state -> actual key present in JSON (or None if missing)
        key_map: dict[str, str] = {}

        # 1) Emit rows for the canonical states, even if missing
        for canon in canonical_states:
            inst_val = None
            chosen_key = None
            for key in _candidates_for(canon):
                if key in visuals:
                    inst_val = visuals.get(key)
                    chosen_key = key
                    matched_keys.add(key)
                    break
            # Resolve instance id and template label
            inst_str = ''
            tpl_str = 'N/A'
            try:
                if inst_val is not None:
                    # Support new format: dict with instance/template
                    if isinstance(inst_val, dict):
                        try:
                            inst_int = int(inst_val.get('instance_id') or inst_val.get('id') or inst_val.get('building_instance_id'))
                        except (AttributeError, TypeError, ValueError):
                            inst_int = None
                        tpl_from_val = inst_val.get('template_id') if isinstance(inst_val, dict) else None
                        if inst_int is not None:
                            inst_str = str(inst_int)
                        else:
                            inst_str = ''
                        if tpl_from_val is not None:
                            tpl_str = str(tpl_from_val)
                        elif inst_int is not None and inst_int in idx:
                            tpl_str = idx.get(inst_int, 'N/A')
                    else:
                        try:
                            inst_int = int(inst_val)
                        except (TypeError, ValueError):
                            inst_int = None
                        inst_str = str(inst_val)
                        if inst_int is not None and inst_int in idx:
                            tpl_str = idx.get(inst_int, 'N/A')
            except (AttributeError, TypeError, ValueError):
                pass
            # Record mapping for later editing/commit operations
            try:
                if chosen_key is not None:
                    key_map[str(canon)] = str(chosen_key)
            except (AttributeError, TypeError, ValueError):
                pass
            rows.append((str(canon), inst_str, tpl_str))

        # 2) Append any extra custom states present in JSON that are not in canonical list
        try:
            for state, inst_id in visuals.items():
                if state in matched_keys:
                    continue
                # Skip if this state is equivalent to a canonical one (e.g., snake vs TitleCase)
                is_equiv = False
                for canon in canonical_states:
                    if state in _candidates_for(canon):
                        is_equiv = True
                        break
                if is_equiv:
                    continue
                # Compute template label
                inst_int = None
                inst_str = ''
                tpl_str = 'N/A'
                try:
                    if isinstance(inst_id, dict):
                        try:
                            inst_int = int(inst_id.get('instance_id') or inst_id.get('id') or inst_id.get('building_instance_id'))
                        except (AttributeError, TypeError, ValueError):
                            inst_int = None
                        if inst_int is not None:
                            inst_str = str(inst_int)
                        tpl_from_val = inst_id.get('template_id')
                        if tpl_from_val is not None:
                            tpl_str = str(tpl_from_val)
                        elif inst_int is not None and inst_int in idx:
                            tpl_str = idx.get(inst_int, 'N/A')
                    else:
                        try:
                            inst_int = int(inst_id)
                        except (TypeError, ValueError):
                            inst_int = None
                        inst_str = str(inst_id)
                        if inst_int is not None and inst_int in idx:
                            tpl_str = idx.get(inst_int, 'N/A')
                except (AttributeError, TypeError, ValueError):
                    pass
                rows.append((str(state), inst_str, tpl_str))
        except (AttributeError, TypeError, ValueError):
            pass

        self.model.visuals_rows = rows
        # Expose the display->JSON key mapping for event handlers/commits
        try:
            self.model.visuals_key_map = key_map
        except (AttributeError, TypeError, ValueError):
            pass
