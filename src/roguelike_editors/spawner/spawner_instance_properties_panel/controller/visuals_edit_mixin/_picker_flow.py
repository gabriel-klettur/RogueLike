from __future__ import annotations

from typing import Any, Optional


def set_visual_template_via_picker_flow(owner: Any, state_key: str, new_tpl_id: int) -> None:
    try:
        owner._ensure_building_templates()
        if (getattr(owner, "_building_template_ids", None) or set()) and new_tpl_id not in (
            getattr(owner, "_building_template_ids", None) or set()
        ):
            return
    except (AttributeError, TypeError, ValueError):
        pass

    rows = owner.get_visuals_rows()
    cur_inst_int: Optional[int] = None
    for st, inst_str, _tpl in rows:
        if st == state_key:
            try:
                cur_inst_int = (
                    int(float(str(inst_str))) if str(inst_str).strip() != "" and str(inst_str).upper() != "N/A" else None
                )
            except (ValueError, TypeError):
                cur_inst_int = None
            break
    try:
        owner._log.debug(
            f"[InstanceProps] set_visual_template_via_picker: state={state_key} tpl={new_tpl_id} cur_inst={cur_inst_int}"
        )
    except Exception:
        pass

    try:
        owner._ensure_buildings_index()
        _exists_on_disk = bool((getattr(owner, "_building_index", None) or {}).get(int(cur_inst_int)) is not None) if cur_inst_int is not None else False
    except (AttributeError, TypeError, ValueError):
        _exists_on_disk = False
    try:
        _exists_in_world = owner._find_building_entity_by_id(int(cur_inst_int)) is not None if cur_inst_int is not None else False
    except (AttributeError, TypeError, ValueError):
        _exists_in_world = False

    if cur_inst_int is not None and not (_exists_on_disk or _exists_in_world):
        try:
            owner._log.warning(
                f"[InstanceProps] set_visual_template_via_picker: instance_id {cur_inst_int} no existe (disco/mundo). Se creará uno nuevo."
            )
        except Exception:
            pass
        cur_inst_int = None

    if cur_inst_int is not None and new_tpl_id is not None:
        desired = int(new_tpl_id)
        if (getattr(owner, "_building_index", None) or {}).get(cur_inst_int, None) == str(desired):
            return
        if owner._count_instance_refs_in_visuals(cur_inst_int) <= 1:
            data = owner._load_buildings_instances()
            changed = False
            for e in data:
                try:
                    if int(e.get("id")) == cur_inst_int:
                        e["template_id"] = int(desired)
                        changed = True
                        break
                except (AttributeError, TypeError, ValueError):
                    continue
            if changed:
                owner._write_buildings_instances(data)
                owner._building_index = None
                owner._ensure_buildings_index()
                try:
                    sid = None
                    try:
                        inst_sel = owner.model.selected_instance
                        if isinstance(inst_sel, dict) and inst_sel.get("id") is not None:
                            sid = str(inst_sel.get("id"))
                    except (AttributeError, TypeError, ValueError):
                        sid = None
                    if sid:
                        for e in data:
                            try:
                                if int(e.get("id")) == cur_inst_int:
                                    e["spawn_id"] = sid
                                    e["spawner_instance_id"] = sid
                                    ov = e.get("overrides") or {}
                                    ov["_is_spawner_visual"] = True
                                    ov["spawner_instance_id"] = sid
                                    e["overrides"] = ov
                                    break
                            except (AttributeError, TypeError, ValueError):
                                continue
                        owner._write_buildings_instances(data)
                except (AttributeError, TypeError, ValueError):
                    pass

                visuals = getattr(owner.model, "visuals", {}) or {}
                key_map = getattr(owner.model, "visuals_key_map", {}) or {}
                json_key = key_map.get(state_key, state_key)
                visuals[json_key] = {"instance_id": cur_inst_int, "template_id": int(desired)}
                owner.model.visuals = visuals
                try:
                    inst = owner.model.selected_instance
                    if isinstance(inst, dict):
                        ov = dict(inst.get("overrides") or {})
                        ov["visible_in_game"] = True
                        inst["overrides"] = ov
                except (AttributeError, TypeError, ValueError):
                    pass
                try:
                    if owner.model.selected_instance is not None:
                        owner.model.selected_instance["visuals"] = visuals
                except AttributeError:
                    pass
                owner._build_visuals_rows()
                try:
                    owner._persist_instance()
                except (AttributeError, TypeError, ValueError):
                    pass
                try:
                    owner._tag_and_reveal_building(int(cur_inst_int), state_key)
                except (AttributeError, TypeError, ValueError):
                    pass
                try:
                    owner._log.info(
                        f"[InstanceProps] Updated instance {cur_inst_int} -> template {desired}"
                    )
                except Exception:
                    pass
                try:
                    for r in (getattr(owner.model, "visuals_rows", None) or []):
                        if r[0] == state_key:
                            owner._log.debug(f"[InstanceProps] Row after update: {r}")
                            break
                except (AttributeError, TypeError, ValueError):
                    pass
        else:
            new_id = owner._clone_instance_with_new_template(cur_inst_int, int(desired))
            if new_id is not None:
                visuals = getattr(owner.model, "visuals", {}) or {}
                key_map = getattr(owner.model, "visuals_key_map", {}) or {}
                json_key = key_map.get(state_key, state_key)
                visuals[json_key] = {"instance_id": new_id, "template_id": int(desired)}
                owner.model.visuals = visuals
                try:
                    inst = owner.model.selected_instance
                    if isinstance(inst, dict):
                        ov = dict(inst.get("overrides") or {})
                        ov["visible_in_game"] = True
                        inst["overrides"] = ov
                except (AttributeError, TypeError, ValueError):
                    pass
                try:
                    if owner.model.selected_instance is not None:
                        owner.model.selected_instance["visuals"] = visuals
                except AttributeError:
                    pass
                owner._persist_instance()
                owner._ensure_buildings_index()
                owner._build_visuals_rows()
                try:
                    owner._tag_and_reveal_building(int(new_id), state_key)
                except (AttributeError, TypeError, ValueError):
                    pass
                try:
                    owner._log.info(
                        f"[InstanceProps] Cloned instance {cur_inst_int} -> new_id {new_id} tpl {desired} for state {state_key}"
                    )
                except Exception:
                    pass
                try:
                    for r in (getattr(owner.model, "visuals_rows", None) or []):
                        if r[0] == state_key:
                            owner._log.debug(f"[InstanceProps] Row after clone: {r}")
                            break
                except (AttributeError, TypeError, ValueError):
                    pass
        return

    if cur_inst_int is None and new_tpl_id is not None:
        desired = int(new_tpl_id)
        try:
            if not hasattr(owner.model, 'visuals_pending_templates') or getattr(owner.model, 'visuals_pending_templates') is None:
                owner.model.visuals_pending_templates = {}
            owner.model.visuals_pending_templates[str(state_key)] = str(desired)
        except (AttributeError, TypeError, ValueError):
            pass
        new_id = owner.add_building_instance_for_visual(state_key, reveal=False)
        try:
            owner._log.info(
                f"[InstanceProps] Created new centered instance {new_id} for state {state_key} tpl {desired}"
            )
        except Exception:
            pass
        try:
            for r in (getattr(owner.model, "visuals_rows", None) or []):
                if r[0] == state_key:
                    owner._log.debug(f"[InstanceProps] Row after create: {r}")
                    break
        except Exception:
            pass
        try:
            owner._reload_selected_from_json()
        except (AttributeError, OSError, ValueError, TypeError):
            pass

    try:
        owner._show_toast(f"Template aplicado: {int(new_tpl_id)} → {state_key}")
    except (AttributeError, TypeError, ValueError):
        pass
    return
