from __future__ import annotations

from typing import Optional, Dict, Any, List, Tuple
import pygame
from roguelike_ui.widgets.text_input.text_input import TextInput
from roguelike_engine.config.config_tiles import TILE_SIZE
from ..services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    write_buildings_instances as svc_write_buildings_instances,
    load_buildings_templates as svc_load_buildings_templates,
    get_template_image_path as svc_get_template_image_path,
)


class VisualsEditMixin:
    # --- Validation helpers --------------------------------------------------
    def _parse_int(self, t: str) -> Optional[int]:
        """Parse an integer from string safely. Returns None if invalid."""
        try:
            s = (t or "").strip()
            if s == "":
                return None
            return int(s)
        except (ValueError, TypeError):
            return None

    def _validate_template_text(self, text: str) -> tuple[bool, Optional[str], Optional[int]]:
        """Return (is_valid, error_msg, parsed_id). Empty text returns (True, None, None)."""
        t = (text or '').strip()
        if t == '':
            return True, None, None
        tpl_id = self._parse_int(t)
        if tpl_id is None:
            return False, "Debe ser un número de template", None
        self._ensure_building_templates()
        valid_ids = self._building_template_ids or set()
        if tpl_id not in valid_ids:
            return False, "Template no existe", tpl_id
        return True, None, tpl_id

    def get_visual_input_validation(self, state_key: str) -> tuple[bool, Optional[str]]:
        """Check current text being edited for a given state."""
        txt = (self.model.visuals_pending_templates or {}).get(state_key, '')
        if getattr(self.model, 'visuals_editing_state', None) == state_key:
            vti = getattr(self.visuals.model, 'text_input', None)
            if vti is not None:
                try:
                    txt = vti.text
                except AttributeError:
                    pass
        ok, msg, _ = self._validate_template_text(txt)
        return ok, msg

    # --- Visuals editing API -------------------------------------------------
    def begin_edit_visual(self, state_key: str) -> None:
        # Keep the displayed state key for UI matching
        self.model.visuals_editing_state = str(state_key)
        # Pre-fill pending template with current template text
        cur_tpl = 'N/A'
        try:
            rows = self.get_visuals_rows()
            for st, _iid, tpl in rows:
                if st == state_key:
                    cur_tpl = str(tpl)
                    break
        except (AttributeError, TypeError, ValueError):
            pass
        # If template is N/A, start empty
        if cur_tpl.upper() == 'N/A':
            cur_tpl = ''
        # Ensure dict exists
        try:
            if not hasattr(self.model, 'visuals_pending_templates') or getattr(self.model, 'visuals_pending_templates') is None:
                self.model.visuals_pending_templates = {}
        except AttributeError:
            self.model.visuals_pending_templates = {}
        self.model.visuals_pending_templates[str(state_key)] = cur_tpl
        # Activate visuals's dedicated text input
        vti = getattr(self.visuals.model, 'text_input', None)
        if vti is None:
            font = pygame.font.SysFont(None, 18)
            vti = TextInput(font)
            self.visuals.model.text_input = vti
        vti.activate(cur_tpl, select_all=True)
        # Ensure OS text input is started for proper TEXTINPUT events
        try:
            import pygame as _pg
            _pg.key.start_text_input()
        except (ImportError, AttributeError, pygame.error):
            pass

    def cancel_edit_visual(self) -> None:
        self.model.visuals_editing_state = None
        try:
            import pygame as _pg
            _pg.key.stop_text_input()
        except (ImportError, AttributeError, pygame.error):
            pass

    def commit_visual_edit_if_finished(self) -> bool:
        display_state = getattr(self.model, 'visuals_editing_state', None)
        if not display_state:
            return False
        vti = getattr(self.visuals.model, 'text_input', None)
        if vti is None or vti.active:
            return False
        new_txt = vti.text if vti else ''
        self.model.visuals_pending_templates[display_state] = new_txt
        ok, msg, new_tpl_id = self._validate_template_text(new_txt)
        if not ok and new_txt.strip() != '':
            try:
                if vti is not None:
                    vti.activate(new_txt, select_all=False)
            except (AttributeError, TypeError, ValueError):
                pass
            return True
        # If empty text -> clear mapping for this state
        if new_txt.strip() == '':
            try:
                key_map = getattr(self.model, 'visuals_key_map', {}) or {}
                json_key = key_map.get(display_state, display_state)
                visuals = dict(getattr(self.model, 'visuals', {}) or {})
                if json_key in visuals:
                    visuals.pop(json_key, None)
                    self.model.visuals = visuals
                    try:
                        if self.model.selected_instance is not None:
                            self.model.selected_instance['visuals'] = visuals
                    except AttributeError:
                        pass
                    # Persist and refresh
                    self._persist_instance()
                    self._build_visuals_rows()
            except (AttributeError, TypeError, ValueError):
                pass
            # Exit edit mode and stop OS text input
            self.model.visuals_editing_state = None
            try:
                import pygame as _pg
                _pg.key.stop_text_input()
            except (ImportError, AttributeError, pygame.error):
                pass
            return True
        # Apply the new template id using the same helper as the picker flow
        try:
            if new_tpl_id is not None:
                # Debounce sanitization during create/reuse/update to avoid losing visuals before persist
                try:
                    import pygame as _pg
                    self._sanitize_block_until_ms = int((_pg.time.get_ticks() or 0) + 600)
                except (ImportError, AttributeError, TypeError, ValueError):
                    self._sanitize_block_until_ms = 0
                self.set_visual_template_via_picker(str(display_state), int(new_tpl_id))
        except (AttributeError, TypeError, ValueError):
            pass
        # Exit edit mode and stop OS text input
        self.model.visuals_editing_state = None
        try:
            import pygame as _pg
            _pg.key.stop_text_input()
        except (ImportError, AttributeError, pygame.error):
            pass
        return True

    def set_visual_template_via_picker(self, state_key: str, new_tpl_id: int) -> None:
        """Apply a template selection coming from the visuals picker overlay.
        Mirrors the logic used by inline text commit and the '+' flow, reusing existing
        helpers to update or clone building instances as needed, then rewires the visuals map
        and persists the spawner instance.
        """
        try:
            # Validate template id is known
            self._ensure_building_templates()
            if (self._building_template_ids or set()) and new_tpl_id not in (self._building_template_ids or set()):
                return
        except (AttributeError, TypeError, ValueError):
            pass
        # Current visuals row info
        rows = self.get_visuals_rows()
        cur_inst_int: Optional[int] = None
        for st, inst_str, _tpl in rows:
            if st == state_key:
                try:
                    cur_inst_int = int(float(str(inst_str))) if str(inst_str).strip() != '' and str(inst_str).upper() != 'N/A' else None
                except (ValueError, TypeError):
                    cur_inst_int = None
                break
        self._log.debug(f"[InstanceProps] set_visual_template_via_picker: state={state_key} tpl={new_tpl_id} cur_inst={cur_inst_int}")
        # Validación extra
        try:
            self._ensure_buildings_index()
            _exists_on_disk = bool((self._building_index or {}).get(int(cur_inst_int)) is not None) if cur_inst_int is not None else False
        except (AttributeError, TypeError, ValueError):
            _exists_on_disk = False
        try:
            _exists_in_world = self._find_building_entity_by_id(int(cur_inst_int)) is not None if cur_inst_int is not None else False
        except (AttributeError, TypeError, ValueError):
            _exists_in_world = False
        if cur_inst_int is not None and not (_exists_on_disk or _exists_in_world):
            try:
                self._log.warning(f"[InstanceProps] set_visual_template_via_picker: instance_id {cur_inst_int} no existe (disco/mundo). Se creará uno nuevo.")
            except (AttributeError, TypeError, ValueError):
                pass
            cur_inst_int = None
        # If there was an instance id and user provided a valid template id
        if cur_inst_int is not None and new_tpl_id is not None:
            # Check if current instance already has the desired template -> nothing to do
            desired = int(new_tpl_id)
            if (self._building_index or {}).get(cur_inst_int, None) == str(desired):
                return
            # If this instance is used only by this state: update its template in-place
            if self._count_instance_refs_in_visuals(cur_inst_int) <= 1:
                # Load instances json and patch
                data = self._load_buildings_instances()
                changed = False
                for e in data:
                    try:
                        if int(e.get('id')) == cur_inst_int:
                            e['template_id'] = int(desired)
                            changed = True
                            break
                    except (AttributeError, TypeError, ValueError):
                        continue
                if changed:
                    self._write_buildings_instances(data)
                    # refresh index/rows
                    self._building_index = None
                    self._ensure_buildings_index()
                    # Also ensure the updated entry carries root-level spawn identifiers
                    try:
                        sid = None
                        try:
                            inst_sel = self.model.selected_instance
                            if isinstance(inst_sel, dict) and inst_sel.get('id') is not None:
                                sid = str(inst_sel.get('id'))
                        except (AttributeError, TypeError, ValueError):
                            sid = None
                        if sid:
                            for e in data:
                                try:
                                    if int(e.get('id')) == cur_inst_int:
                                        e['spawn_id'] = sid
                                        e['spawner_instance_id'] = sid
                                        # mirror in overrides tag
                                        ov = e.get('overrides') or {}
                                        ov['_is_spawner_visual'] = True
                                        ov['spawner_instance_id'] = sid
                                        e['overrides'] = ov
                                        break
                                except (AttributeError, TypeError, ValueError):
                                    continue
                            self._write_buildings_instances(data)
                    except (AttributeError, TypeError, ValueError):
                        pass
                    # Ensure visuals mapping stores template as well
                    visuals = getattr(self.model, 'visuals', {}) or {}
                    key_map = getattr(self.model, 'visuals_key_map', {}) or {}
                    json_key = key_map.get(state_key, state_key)
                    visuals[json_key] = {'instance_id': cur_inst_int, 'template_id': int(desired)}
                    self.model.visuals = visuals
                    # Ensure runtime will render spawner visuals
                    try:
                        inst = self.model.selected_instance
                        if isinstance(inst, dict):
                            ov = dict(inst.get('overrides') or {})
                            ov['visible_in_game'] = True
                            inst['overrides'] = ov
                    except (AttributeError, TypeError, ValueError):
                        pass
                    try:
                        if self.model.selected_instance is not None:
                            self.model.selected_instance['visuals'] = visuals
                    except AttributeError:
                        pass
                    self._build_visuals_rows()
                    # Persist spawner instance as well to keep ids/keys consistent
                    try:
                        self._persist_instance()
                    except (AttributeError, TypeError, ValueError):
                        pass
                    # Reveal the updated/assigned building in editor immediately
                    try:
                        self._tag_and_reveal_building(int(cur_inst_int), state_key)
                    except (AttributeError, TypeError, ValueError):
                        pass
                    self._log.info(f"[InstanceProps] Updated instance {cur_inst_int} -> template {desired}")
                    try:
                        # Log current row for state
                        for r in (self.model.visuals_rows or []):
                            if r[0] == state_key:
                                self._log.debug(f"[InstanceProps] Row after update: {r}")
                                break
                    except (AttributeError, TypeError, ValueError):
                        pass
            else:
                # Shared by multiple states: clone and rewire only this state
                new_id = self._clone_instance_with_new_template(cur_inst_int, int(desired))
                if new_id is not None:
                    visuals = getattr(self.model, 'visuals', {}) or {}
                    key_map = getattr(self.model, 'visuals_key_map', {}) or {}
                    json_key = key_map.get(state_key, state_key)
                    visuals[json_key] = {'instance_id': new_id, 'template_id': int(desired)}
                    self.model.visuals = visuals
                    # Ensure runtime will render spawner visuals
                    try:
                        inst = self.model.selected_instance
                        if isinstance(inst, dict):
                            ov = dict(inst.get('overrides') or {})
                            ov['visible_in_game'] = True
                            inst['overrides'] = ov
                    except (AttributeError, TypeError, ValueError):
                        pass
                    try:
                        if self.model.selected_instance is not None:
                            self.model.selected_instance['visuals'] = visuals
                    except AttributeError:
                        pass
                    self._persist_instance()
                    # Rebuild views/indexes
                    self._ensure_buildings_index()
                    self._build_visuals_rows()
                    # Reveal newly cloned building in editor
                    try:
                        self._tag_and_reveal_building(int(new_id), state_key)
                    except (AttributeError, TypeError, ValueError):
                        pass
                    self._log.info(f"[InstanceProps] Cloned instance {cur_inst_int} -> new_id {new_id} tpl {desired} for state {state_key}")
                    try:
                        for r in (self.model.visuals_rows or []):
                            if r[0] == state_key:
                                self._log.debug(f"[InstanceProps] Row after clone: {r}")
                                break
                    except (AttributeError, TypeError, ValueError):
                        pass
            return
        # If there was no instance id and user provided a valid template id: ALWAYS create centered
        if cur_inst_int is None and new_tpl_id is not None:
            desired = int(new_tpl_id)
            # Prime pending input so add_building_instance_for_visual uses it
            try:
                if not hasattr(self.model, 'visuals_pending_templates') or getattr(self.model, 'visuals_pending_templates') is None:
                    self.model.visuals_pending_templates = {}
                # Use string keys consistently (state_key can be TitleCase)
                self.model.visuals_pending_templates[str(state_key)] = str(desired)
            except (AttributeError, TypeError, ValueError):
                pass
            # Create a new instance centered on the owning spawner
            new_id = self.add_building_instance_for_visual(state_key, reveal=False)
            self._log.info(f"[InstanceProps] Created new centered instance {new_id} for state {state_key} tpl {desired}")
            try:
                for r in (self.model.visuals_rows or []):
                    if r[0] == state_key:
                        self._log.debug(f"[InstanceProps] Row after create: {r}")
                        break
            except Exception:
                pass
            # Reload disk snapshot (add_building_instance_for_visual already attempts it)
            try:
                self._reload_selected_from_json()
            except (AttributeError, OSError, ValueError, TypeError):
                pass
        # also toast here as a safety (if picker flow ends here)
        try:
            self._show_toast(f"Template aplicado: {int(new_tpl_id)} → {state_key}")
        except (AttributeError, TypeError, ValueError):
            pass
        return

    def add_building_instance_for_visual(self, state_key: str, reveal: bool = True) -> Optional[int]:
        # Need a template id: prefer current text input if editing this state
        txt = (self.model.visuals_pending_templates or {}).get(state_key, '')
        if getattr(self.model, 'visuals_editing_state', None) == state_key:
            vti = getattr(self.visuals.model, 'text_input', None)
            if vti is not None:
                try:
                    txt = vti.text
                except AttributeError:
                    pass
        ok, msg, tpl_id = self._validate_template_text(txt)
        if tpl_id is None or not ok:
            return None
        # Load buildings instances and compute next id
        data = self._load_buildings_instances()
        next_id = 1
        try:
            ids = [int(e.get('id')) for e in data if e.get('id') is not None]
            if ids:
                next_id = max(ids) + 1
        except Exception:
            pass
        # Determine zone and rel_x/rel_y from the selected spawner instance tile (zone-local)
        zone = None
        local_tile = (0, 0)
        try:
            zone = str((self.model.selected_instance or {}).get('zone'))
        except (AttributeError, TypeError, ValueError):
            zone = None
        try:
            t = (self.model.selected_instance or {}).get('tile', (0, 0))
            if isinstance(t, (list, tuple)) and len(t) >= 2:
                local_tile = (int(t[0]), int(t[1]))
        except (AttributeError, TypeError, ValueError):
            local_tile = (0, 0)
        if not zone:
            zone = 'lobby'
        # Convert the zone-local tile to pixels relative to the zone origin
        try:
            rel_x = int(local_tile[0] * TILE_SIZE)
            rel_y = int(local_tile[1] * TILE_SIZE)
        except (TypeError, ValueError):
            rel_x = 0
            rel_y = 0
        # Attempt to reuse an existing instance in the same spot and template to avoid duplicates
        try:
            zone_norm = zone
            desired_tid = int(tpl_id)
            best_id = None
            best_score = (-1, -1)
            for e in data:
                try:
                    if int(e.get('template_id')) != desired_tid:
                        continue
                    if str(e.get('zone') or 'lobby') != str(zone_norm):
                        continue
                    if int(e.get('rel_x') or 0) != int(local_tile[0] * TILE_SIZE):
                        continue
                    if int(e.get('rel_y') or 0) != int(local_tile[1] * TILE_SIZE):
                        continue
                    ov = e.get('overrides') if isinstance(e, dict) else {}
                    # Score: prefer tagged spawner visuals tied to this spawner, then any tagged, then lowest id
                    tied = 0
                    try:
                        sid = str((self.model.selected_instance or {}).get('id')) if (self.model.selected_instance or {}).get('id') is not None else None
                        if sid and (str(e.get('spawner_instance_id')) == sid or str((ov or {}).get('spawner_instance_id')) == sid):
                            tied = 1
                    except (AttributeError, TypeError, ValueError):
                        tied = 0
                    is_tag = 1 if (isinstance(ov, dict) and bool(ov.get('_is_spawner_visual'))) else 0
                    score = (tied, is_tag)
                    if score > best_score:
                        best_score = score
                        best_id = int(e.get('id'))
                except (AttributeError, TypeError, ValueError):
                    continue
            if best_id is not None:
                visuals = getattr(self.model, 'visuals', {}) or {}
                key_map = getattr(self.model, 'visuals_key_map', {}) or {}
                json_key = key_map.get(state_key, state_key)
                visuals[json_key] = {'instance_id': best_id, 'template_id': int(tpl_id)}
                self.model.visuals = visuals
                try:
                    if self.model.selected_instance is not None:
                        self.model.selected_instance['visuals'] = visuals
                except AttributeError:
                    pass
                self._persist_instance()
                # Refresh indexes/rows
                self._building_index = None
                self._ensure_buildings_index()
                self._build_visuals_rows()
                if reveal:
                    try:
                        self._tag_and_reveal_building(int(best_id), state_key)
                    except (AttributeError, TypeError, ValueError):
                        pass
                return best_id
        except (AttributeError, TypeError, ValueError):
            pass
        try:
            self._log.debug(f"[InstanceProps] No reusable instance found -> creating new (zone={zone}, tile={local_tile}, tpl={tpl_id})")
        except (AttributeError, TypeError, ValueError):
            pass
        # Center the new building on the spawner center using scaled image bounding rect
        try:
            spawn_cx = int(rel_x + (TILE_SIZE // 2))
            spawn_cy = int(rel_y + (TILE_SIZE // 2))
            # Determine desired visual size (width,height)
            w: int | None = None
            h: int | None = None
            brx = bry = 0
            brw = brh = None
            anchor_mode = 'content_center'
            # Prefer templates original_scale
            try:
                for tentry in svc_load_buildings_templates():
                    try:
                        if int(tentry.get('id')) == int(tpl_id):
                            oscale = tentry.get('original_scale')
                            try:
                                am = str(tentry.get('anchor_mode') or '')
                                if am:
                                    anchor_mode = am
                            except (AttributeError, TypeError, ValueError):
                                pass
                            if isinstance(oscale, (list, tuple)) and len(oscale) >= 2:
                                w = int(oscale[0]); h = int(oscale[1])
                                # Compute bounding box at that scale
                                try:
                                    img_path = svc_get_template_image_path(int(tpl_id))
                                    if img_path:
                                        import pygame as _pg
                                        raw = _pg.image.load(img_path)
                                        surf = _pg.transform.scale(raw, (int(w), int(h)))
                                        br = surf.get_bounding_rect(min_alpha=1)
                                        brx, bry, brw, brh = br.x, br.y, br.w, br.h
                                except (AttributeError, TypeError, ValueError, pygame.error):
                                    brw = brh = None
                            break
                    except (AttributeError, TypeError, ValueError):
                        continue
            except (AttributeError, TypeError, ValueError, OSError):
                pass
            # Fallback: probe image size and apply same auto-downscale rule (>512 -> quarter)
            if w is None or h is None:
                try:
                    img_path = svc_get_template_image_path(int(tpl_id))
                    if img_path:
                        import pygame as _pg
                        raw = _pg.image.load(img_path)
                        iw, ih = raw.get_size()
                        if iw > 512 or ih > 512:
                            iw //= 4; ih //= 4
                        w, h = int(iw), int(ih)
                        # Bounding rect from scaled surface
                        try:
                            surf = _pg.transform.scale(raw, (int(w), int(h)))
                            br = surf.get_bounding_rect(min_alpha=1)
                            brx, bry, brw, brh = br.x, br.y, br.w, br.h
                        except (AttributeError, TypeError, ValueError, pygame.error):
                            brw = brh = None
                except (AttributeError, TypeError, ValueError, OSError, pygame.error):
                    w = None; h = None
            if w is not None and h is not None and w > 0 and h > 0:
                if anchor_mode == 'base_center' and brw is not None and brh is not None and brw > 0 and brh > 0:
                    # Align bottom-center of visible content to spawn center
                    rel_x = int(spawn_cx - (brx + brw // 2))
                    rel_y = int(spawn_cy - (bry + brh))
                elif brw is not None and brh is not None and brw > 0 and brh > 0 and anchor_mode == 'content_center':
                    # Align bounding-rect center to spawn center
                    rel_x = int(spawn_cx - (brx + brw // 2))
                    rel_y = int(spawn_cy - (bry + brh // 2))
                else:
                    # Fallback: align image geometric center
                    rel_x = int(spawn_cx - (w // 2))
                    rel_y = int(spawn_cy - (h // 2))
        except (AttributeError, TypeError, ValueError):
            pass
        entry = {
            'id': next_id,
            'template_id': tpl_id,
            'zone': zone,
            'rel_x': int(rel_x),
            'rel_y': int(rel_y),
        }
        # Tag as spawner visual to protect from global building saves
        try:
            if isinstance(self.model.selected_instance, dict):
                sid = str(self.model.selected_instance.get('id')) if self.model.selected_instance.get('id') is not None else None
            else:
                sid = None
            entry['overrides'] = entry.get('overrides') or {}
            entry['overrides']['_is_spawner_visual'] = True
            if sid:
                entry['overrides']['spawner_instance_id'] = sid
            # Persist computed scale if available so runtime size matches centering
            try:
                if 'overrides' in entry:
                    if locals().get('w') is not None and locals().get('h') is not None and int(locals()['w']) > 0 and int(locals()['h']) > 0:
                        entry['overrides']['scale'] = [int(locals()['w']), int(locals()['h'])]
            except (AttributeError, TypeError, ValueError):
                pass
            # Also include root-level identifiers so save_buildings_split preserves this instance id
            try:
                if sid:
                    entry['spawn_id'] = str(sid)
                    entry['spawner_instance_id'] = str(sid)
            except (AttributeError, TypeError, ValueError):
                pass
        except (AttributeError, TypeError, ValueError):
            pass
        data.append(entry)
        self._write_buildings_instances(data)
        # Post-create correction: recenter using actual scaled image's alpha bounding rect
        try:
            try:
                self.visuals._ensure_building_loaded(int(next_id))
            except (AttributeError, TypeError, ValueError):
                pass
            ob = None
            try:
                ob = self.visuals._find_building_entity_by_id(int(next_id))
            except (AttributeError, TypeError, ValueError):
                ob = None
            if ob is not None:
                surf = getattr(getattr(ob, 'model', None), 'image', None)
                br = None
                try:
                    if surf is not None:
                        br = surf.get_bounding_rect(min_alpha=1)
                except (AttributeError, TypeError, ValueError):
                    br = None
                if br is not None and br.w > 0 and br.h > 0:
                    spawn_cx = int((local_tile[0] * TILE_SIZE) + (TILE_SIZE // 2))
                    spawn_cy = int((local_tile[1] * TILE_SIZE) + (TILE_SIZE // 2))
                    corr_rx = int(spawn_cx - (br.x + br.w // 2))
                    corr_ry = int(spawn_cy - (br.y + br.h // 2))
                    for e in data:
                        try:
                            if int(e.get('id')) == int(next_id):
                                e['rel_x'] = corr_rx
                                e['rel_y'] = corr_ry
                                break
                        except (AttributeError, TypeError, ValueError):
                            continue
                    self._write_buildings_instances(data)
                    try:
                        setattr(getattr(ob, 'model', ob), 'rel_x', corr_rx)
                        setattr(getattr(ob, 'model', ob), 'rel_y', corr_ry)
                    except (AttributeError, TypeError, ValueError):
                        pass
        except (AttributeError, TypeError, ValueError):
            pass
        # Update visuals mapping and persist spawner instance
        visuals = getattr(self.model, 'visuals', {}) or {}
        key_map = getattr(self.model, 'visuals_key_map', {}) or {}
        json_key = key_map.get(state_key, state_key)
        visuals[json_key] = {'instance_id': next_id, 'template_id': int(tpl_id)}
        try:
            self._log.debug(f"[InstanceProps] add_building_instance_for_visual: set visuals[{json_key}]={next_id}")
        except (AttributeError, TypeError, ValueError):
            pass
        self.model.visuals = visuals
        # Ensure runtime will render spawner visuals
        try:
            inst = self.model.selected_instance
            if isinstance(inst, dict):
                ov = dict(inst.get('overrides') or {})
                ov['visible_in_game'] = True
                inst['overrides'] = ov
        except (AttributeError, TypeError, ValueError):
            pass
        try:
            if self.model.selected_instance is not None:
                self.model.selected_instance['visuals'] = visuals
        except AttributeError:
            pass
        try:
            self._log.debug(f"[InstanceProps] add_building_instance_for_visual: model.visuals now={self.model.visuals}")
        except (AttributeError, TypeError, ValueError):
            pass
        self._persist_instance()
        # Reload from disk to be 100% sure it persisted and update UI model
        try:
            self._reload_selected_from_json()
        except (AttributeError, OSError, ValueError, TypeError):
            pass
        # Refresh indexes/rows
        self._building_index = None
        self._ensure_buildings_index()
        self._build_visuals_rows()
        # Exit edit mode
        self.model.visuals_editing_state = None
        # Ensure it is visible and tagged for editing (optional)
        if reveal:
            try:
                self._tag_and_reveal_building(int(next_id), state_key)
            except (AttributeError, TypeError, ValueError):
                pass
        return next_id

    def clear_visual_for_state(self, state_key: str) -> None:
        """Remove the visual mapping for a given state and clean JSON files.
        - Removes visuals[state_key] from the selected spawner instance and persists
          to data/spawners/spawners_instances.json
        - If that mapping referenced a building instance id, and the corresponding
          buildings_instances.json entry is tagged as a spawner visual for this
          spawner instance, remove it from data/buildings/buildings_instances.json
        - Hides the building in the world (editor visibility) if present
        """
        visuals = dict(getattr(self.model, 'visuals', {}) or {})
        if not visuals:
            return
        key_map = getattr(self.model, 'visuals_key_map', {}) or {}
        json_key = key_map.get(state_key, state_key)
        v = visuals.get(json_key)
        if v is None:
            return
        # Parse building instance id if present
        bid = None
        try:
            if isinstance(v, dict):
                bid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
            else:
                bid = int(v)
        except (AttributeError, TypeError, ValueError):
            bid = None
        # Remove mapping and persist spawner instance
        visuals.pop(json_key, None)
        self.model.visuals = visuals
        try:
            if self.model.selected_instance is not None:
                self.model.selected_instance['visuals'] = visuals
        except AttributeError:
            pass
        # Persist to spawners_instances.json
        self._persist_instance()
        # Reload from disk to ensure UI reflects persisted state
        try:
            self._reload_selected_from_json()
        except (AttributeError, OSError, ValueError, TypeError):
            pass
        # Attempt to remove the building instance from buildings_instances.json if it is ours
        if bid is not None:
            # Strict mode safety: if no other spawner instance references this building id,
            # we can delete it even if it lacks tagging/root linkage.
            referenced_elsewhere = False
            try:
                from roguelike_editors.spawner.services.persistence import load_instances_json
                all_inst = load_instances_json()
                for _inst in all_inst or []:
                    try:
                        vis = _inst.get('visuals')
                        if not isinstance(vis, dict) or not vis:
                            continue
                        for _k, _v in list(vis.items()):
                            try:
                                if isinstance(_v, dict):
                                    _vid = _v.get('instance_id') or _v.get('id') or _v.get('building_instance_id')
                                    _vid = int(_vid) if _vid is not None else None
                                else:
                                    _vid = int(_v)
                            except Exception:
                                _vid = None
                            if _vid is not None and int(_vid) == int(bid):
                                referenced_elsewhere = True
                                break
                        if referenced_elsewhere:
                            break
                    except Exception:
                        continue
            except Exception:
                referenced_elsewhere = False
            data = self._load_buildings_instances()
            sid = None
            try:
                inst = self.model.selected_instance or {}
                sid = str(inst.get('id')) if inst.get('id') is not None else None
            except Exception:
                sid = None
            kept = []
            removed = False
            for e in data:
                try:
                    eid = int(e.get('id'))
                except Exception:
                    kept.append(e)
                    continue
                if eid != int(bid):
                    kept.append(e)
                    continue
                # Match only tagged spawner visuals for this spawner instance
                ov = e.get('overrides') if isinstance(e, dict) else None
                is_tagged = False
                try:
                    if isinstance(ov, dict) and ov.get('_is_spawner_visual') and (sid is None or str(ov.get('spawner_instance_id')) == str(sid)):
                        is_tagged = True
                except Exception:
                    is_tagged = False
                # Strict mode: also consider root-level identifiers as linkage to this spawner
                is_root_linked = False
                try:
                    if sid is not None and (str(e.get('spawner_instance_id')) == str(sid) or str(e.get('spawn_id')) == str(sid)):
                        is_root_linked = True
                except Exception:
                    is_root_linked = False
                # Remove if explicitly tagged, or in strict mode if root linkage matches,
                # or if in strict mode and no other spawner references this building id anymore
                if is_tagged or (bool(getattr(self, 'strict_visuals_cleanup', False)) and (is_root_linked or not referenced_elsewhere)):
                    removed = True
                    continue  # drop this entry
                # If not tagged, keep it to avoid deleting user buildings
                kept.append(e)
            if removed:
                self._write_buildings_instances(kept)
                # Refresh index for subsequent checks
                self._building_index = None
                self._ensure_buildings_index()
                # Remove building from world/editor entirely
                try:
                    self._remove_building_entity_by_id(int(bid))
                except Exception:
                    pass
            # Rebuild rows/UI (already reloaded from disk above)
            self._build_visuals_rows()
            try:
                self._log.info(f"[InstanceProps] Cleared visual for state={state_key}; removed_building_id={bid}")
            except Exception:
                pass
