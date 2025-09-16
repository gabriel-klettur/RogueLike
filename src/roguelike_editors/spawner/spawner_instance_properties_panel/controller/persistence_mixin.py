from __future__ import annotations

from typing import Dict, Any, Optional
import ast

from roguelike_editors.spawner.services.persistence import (
    load_instances_json,
    write_instances_json,
    find_instance_in_json,
    find_instance_by_id,
    generate_instance_id,
)


class PersistenceMixin:
    # --- Utils ---------------------------------------------------------------
    def _parse_value(self, text: str, key_path: str):
        t = (text or "").strip()
        low = t.lower()
        if low == 'true':
            return True
        if low == 'false':
            return False
        if low in ('null', 'none'):
            return None
        # number
        try:
            if t.startswith('0') and t != '0' and not t.startswith('0.'):
                raise ValueError()
            if '.' in t:
                return float(t)
            return int(t)
        except (ValueError, TypeError):
            pass
        # JSON/list/dict
        if (t.startswith('[') and t.endswith(']')) or (t.startswith('{') and t.endswith('}')):
            try:
                import json
                return json.loads(t)
            except (ValueError, TypeError):
                try:
                    return ast.literal_eval(t)
                except (ValueError, SyntaxError):
                    pass
        return text

    def _apply_edit(self, key_path: str, value) -> None:
        inst = self.model.selected_instance
        if inst is None:
            return
        # Special-case tile.0 and tile.1 to force list length and int
        if key_path.startswith('tile.'):
            try:
                idx = int(key_path.split('.')[-1])
            except (ValueError, TypeError):
                idx = None
            if idx is not None:
                tile = inst.get('tile')
                if not isinstance(tile, list):
                    tile = [0, 0]
                while len(tile) <= idx:
                    tile.append(0)
                try:
                    tile[idx] = int(value)
                except (ValueError, TypeError):
                    try:
                        tile[idx] = int(float(value))
                    except (ValueError, TypeError):
                        pass

    def _set_by_path(self, root: Dict[str, Any] | None, path: str, value) -> None:
        if root is None:
            return
        parts = path.split('.') if path else []
        cur: Any = root
        for i, part in enumerate(parts):
            is_last = (i == len(parts) - 1)
            idx: Optional[int] = None
            try:
                idx = int(part)
            except (TypeError, ValueError):
                idx = None

            if idx is not None and isinstance(cur, list):
                if is_last:
                    cur[idx] = value
                else:
                    # If next is out of bounds, extend with dicts
                    while len(cur) <= idx:
                        cur.append({})
                    cur = cur[idx]
            else:
                if is_last:
                    if isinstance(cur, dict):
                        cur[part] = value
                else:
                    if isinstance(cur, dict):
                        nxt = cur.get(part)
                        if nxt is None:
                            nxt = {} if not parts[i+1].isdigit() else []
                            cur[part] = nxt
                        cur = nxt

    def _persist_instance(self) -> None:
        inst = self.model.selected_instance
        if inst is None:
            return
        try:
            self._log_debug_rl("persist_about", f"[InstanceProps] _persist_instance: about to persist id={inst.get('id')} visuals={inst.get('visuals')}")
        except (AttributeError, TypeError, ValueError):
            pass
        # Ensure the in-memory visuals map from the model is applied before persisting
        try:
            visuals_model = getattr(self.model, 'visuals', None)
            if isinstance(visuals_model, dict):
                # Normalize visuals to new format {instance_id, template_id}
                self._ensure_buildings_index()
                idx = self._building_index or {}
                norm: dict[str, dict] = {}
                for k, v in (visuals_model or {}).items():
                    try:
                        if isinstance(v, dict):
                            # Ensure keys and fill missing template_id
                            vid = None
                            try:
                                vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                            except (TypeError, ValueError, AttributeError):
                                vid = None
                            tpl = v.get('template_id')
                            if tpl is None and vid is not None and vid in idx:
                                tpl = idx.get(vid)
                            entry = {'instance_id': vid if vid is not None else v, 'template_id': tpl}
                            # Preserve offset if present and non-zero (drop [0,0] to avoid JSON noise)
                            try:
                                off = v.get('offset')
                                if isinstance(off, (list, tuple)) and len(off) == 2:
                                    dx, dy = int(off[0]), int(off[1])
                                    if dx != 0 or dy != 0:
                                        entry['offset'] = [dx, dy]  # type: ignore[index]
                                # Preserve scale if present and valid (>0)
                                sc = v.get('scale')
                                if isinstance(sc, (list, tuple)) and len(sc) == 2:
                                    try:
                                        sw, sh = int(sc[0]), int(sc[1])
                                        if sw > 0 and sh > 0:
                                            entry['scale'] = [sw, sh]  # type: ignore[index]
                                    except (TypeError, ValueError):
                                        pass
                            except Exception:
                                pass
                            norm[str(k)] = entry
                        else:
                            vid = int(v)
                            tpl = idx.get(vid)
                            norm[str(k)] = {'instance_id': vid, 'template_id': tpl}
                    except (AttributeError, TypeError, ValueError):
                        # Keep as-is if cannot normalize
                        norm[str(k)] = {'instance_id': v, 'template_id': None}
                try:
                    self._log_debug_rl("persist_norm", f"[InstanceProps] _persist_instance: computed norm_visuals={norm}")
                except (AttributeError, TypeError, ValueError):
                    pass
                # Guard: avoid wiping visuals unintentionally when model.visuals is empty transiently
                if norm:
                    if inst.get('visuals') != norm:
                        inst['visuals'] = norm
                else:
                    # If norm is empty but instance already has visuals with entries, keep them
                    try:
                        cur_vis = inst.get('visuals')
                        if isinstance(cur_vis, dict) and len(cur_vis) > 0:
                            try:
                                self._log_debug_rl("persist_keep_nonempty", "[InstanceProps] _persist_instance: norm empty, KEEP existing visuals (non-empty)")
                            except (AttributeError, TypeError, ValueError):
                                pass
                        else:
                            inst['visuals'] = {}
                    except (AttributeError, TypeError, ValueError):
                        inst['visuals'] = {}
        except (AttributeError, TypeError, ValueError):
            try:
                visuals_model = getattr(self.model, 'visuals', None)
                if isinstance(visuals_model, dict):
                    inst['visuals'] = visuals_model
            except (AttributeError, TypeError, ValueError):
                pass
        # Reload data fresh
        data = load_instances_json()

        # Compute identities
        cur_id = None
        try:
            cur_id = str(inst.get('id')) if inst.get('id') is not None else None
        except (AttributeError, TypeError, ValueError):
            cur_id = None
        cur_key = None
        try:
            tpl = str(inst.get('template_id'))
            zone = str(inst.get('zone'))
            tile = tuple(inst.get('tile', [0, 0]))
            cur_key = (tpl, zone, (int(tile[0]), int(tile[1])))
        except (AttributeError, TypeError, ValueError):
            cur_key = None

        # Determine target index prioritizing original id, then index+key, then key search
        target_idx: Optional[int] = None
        # 1) If we have an original id, replace that exact entry
        if self.model.original_id:
            data_by_id, idx_by_id, _ = find_instance_by_id(self.model.original_id)
            if data_by_id is not None:
                data = data_by_id
            if idx_by_id is not None:
                target_idx = idx_by_id
        # 2) If not found yet, try validating stored index with original key
        if target_idx is None:
            idx = self.model.selected_index
            if idx is not None and 0 <= idx < len(data):
                ok = True
                try:
                    if self.model.original_key is not None:
                        e = data[idx]
                        ek = (str(e.get('template_id')), str(e.get('zone')), (int(e.get('tile', [0, 0])[0]), int(e.get('tile', [0, 0])[1])))
                        ok = (ek == self.model.original_key)
                except (AttributeError, TypeError, ValueError):
                    ok = False
                if ok:
                    target_idx = idx

        # 3) Try original key lookup
        if target_idx is None and self.model.original_key is not None:
            tpl0, zone0, local0 = self.model.original_key
            data2, found_idx, _ = find_instance_in_json(tpl0, zone0, local0)
            if data2 is not None:
                data = data2
            if found_idx is not None:
                target_idx = found_idx
        # 4) As last resort, try current identity search
        if target_idx is None and cur_key is not None:
            for i, e in enumerate(data):
                try:
                    ek = (str(e.get('template_id')), str(e.get('zone')), (int(e.get('tile', [0, 0])[0]), int(e.get('tile', [0, 0])[1])))
                    if ek == cur_key:
                        target_idx = i
                        break
                except (AttributeError, TypeError, ValueError):
                    continue
        try:
            self._log_debug_rl("persist_resolve", f"[InstanceProps] _persist_instance: resolve target_idx={target_idx} original_id={self.model.original_id} selected_index={self.model.selected_index} original_key={self.model.original_key} cur_key={cur_key}")
        except (AttributeError, TypeError, ValueError):
            pass

        # Ensure a unique 'id' for the instance (handle rename conflicts)
        existing_ids = {str(e.get('id')) for e in data if e.get('id')}
        if target_idx is not None:
            # Exclude current target from conflict set
            try:
                existing_ids.discard(str(data[target_idx].get('id')))
            except (AttributeError, TypeError, ValueError):
                pass

        desired_id = cur_id
        if not desired_id or desired_id in existing_ids:
            inst['id'] = generate_instance_id(inst, existing_ids)
        # Persist replace/append
        if target_idx is not None:
            data[target_idx] = inst
        else:
            data.append(inst)
        write_instances_json(data)
        # After writing, debounce sanitize to avoid immediate repair/cleanup loop
        try:
            now = self._now_ms()
            # Give a short window for indexes to refresh and UI to settle
            self._sanitize_block_until_ms = max(getattr(self, '_sanitize_block_until_ms', 0), now + 600)
        except Exception:
            pass
        # Verify round-trip persisted visuals; if lost accidentally, rewrite once with model snapshot
        try:
            check, idx_check, _ = find_instance_by_id(str(inst.get('id')))
            if idx_check is not None:
                on_disk = check[idx_check].get('visuals')
                desired = inst.get('visuals')
                if isinstance(desired, dict) and desired and (not isinstance(on_disk, dict) or len(on_disk or {}) < len(desired)):
                    check[idx_check]['visuals'] = desired
                    write_instances_json(check)
                    try:
                        self._log.warning("[InstanceProps] _persist_instance: on-disk visuals were smaller/empty; rewrote with in-memory snapshot")
                    except (AttributeError, TypeError, ValueError):
                        pass
        except (AttributeError, TypeError, ValueError, OSError):
            pass
        try:
            self._log_debug_rl("persist_wrote", f"[InstanceProps] _persist_instance: wrote instance id={inst.get('id')} with visuals keys={list((inst.get('visuals') or {}).keys()) if isinstance(inst.get('visuals'), dict) else inst.get('visuals')}")
        except (AttributeError, TypeError, ValueError):
            pass
        # Update original ids/keys for subsequent edits
        self.model.original_id = str(inst.get('id')) if inst and inst.get('id') is not None else None
        self.model.original_key = cur_key
        # Notify UI to refresh instances list if requested
        try:
            if self.on_persist is not None:
                self.on_persist()
        except AttributeError:
            pass
        # Notify editor about saved instance with context (changed key)
        try:
            if self.on_instance_saved is not None:
                self.on_instance_saved(inst, getattr(self, '_last_edit_key', None))
        except AttributeError:
            pass
        # Clear last edit key after notifying
        self._last_edit_key = None
