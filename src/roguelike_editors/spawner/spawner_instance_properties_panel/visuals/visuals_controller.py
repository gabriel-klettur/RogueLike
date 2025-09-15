from __future__ import annotations

from typing import Optional, Any, List, Dict
import logging
from roguelike_engine.buildings.factory import build_from_config
from ..services.buildings_service import (
    load_buildings_instances,
    write_buildings_instances,
    get_template_image_path,
)
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

from .visuals_model import VisualsModel
from .visuals_view import VisualsView
from .visuals_events import VisualsEvents
from roguelike_editors.spawner.services import load_instances_json as load_spawners_instances_json

logger = logging.getLogger(__name__)


class VisualsController:
    """Feature controller for the Visuals table inside Instance Properties.

    It owns its own MVC (model/view/events) but delegates data mutations and
    persistence to the parent InstancePropertiesController.
    """

    def __init__(self, parent_controller) -> None:
        # Keep a dynamic reference to the parent controller without importing it here
        self.parent = parent_controller
        self.model = VisualsModel()
        self.view = VisualsView(self)
        self.events = VisualsEvents()

    # --- Convenience accessors to parent data --------------------------------
    def get_visuals_rows(self):
        return self.parent.get_visuals_rows()

    def get_visuals(self) -> dict:
        return getattr(self.parent.model, 'visuals', {}) or {}

    def get_visuals_key_map(self) -> dict:
        return getattr(self.parent.model, 'visuals_key_map', {}) or {}

    def get_text_input(self):
        return self.parent.get_text_input()

    # --- Visuals mapping helpers --------------------------------------------
    def _resolve_json_key_for_state(self, state_key: str) -> str:
        """Given a display state name (TitleCase), resolve the actual JSON key used."""
        try:
            key_map = getattr(self.parent.model, 'visuals_key_map', {}) or {}
            return str(key_map.get(state_key, state_key))
        except (AttributeError, TypeError, KeyError):
            return str(state_key)

    def _get_mapping_entry_for_state(self, state_key: str):
        """Return the raw mapping value from model.visuals for the given state (dict | int | None)."""
        try:
            visuals = getattr(self.parent.model, 'visuals', {}) or {}
        except (AttributeError, TypeError):
            logger.debug("VisualsController.pick_visual_building_under_cursor: failed to read visuals mapping", exc_info=True)
            visuals = {}
        try:
            return visuals.get(self._resolve_json_key_for_state(state_key))
        except (AttributeError, TypeError, KeyError):
            logger.debug("VisualsController.pick_visual_building_under_cursor: error computing screen bounds", exc_info=True)
            return None

    def _get_instance_id_for_state(self, state_key: str) -> int | None:
        """Extract instance_id as int from the mapping of a state, if present and valid."""
        raw = self._get_mapping_entry_for_state(state_key)
        try:
            if raw is None:
                return None
            if isinstance(raw, dict):
                return int(raw.get('instance_id') or raw.get('id') or raw.get('building_instance_id'))
            return int(raw)
        except (ValueError, TypeError, AttributeError, KeyError):
            return None

    def _get_template_id_for_state(self, state_key: str) -> int | None:
        """Best-effort template_id resolution for a state: prefer explicit mapping, fallback to building index."""
        raw = self._get_mapping_entry_for_state(state_key)
        try:
            if isinstance(raw, dict) and raw.get('template_id') is not None:
                return int(raw.get('template_id'))
        except (ValueError, TypeError, AttributeError, KeyError):
            logger.debug("VisualsController._get_template_id_for_state: failed to read template_id from mapping", exc_info=True)
        # Fallback via buildings index if instance_id present
        try:
            bid = self._get_instance_id_for_state(state_key)
            idx = getattr(self.parent, '_building_index', {}) or {}
            if bid is not None and int(bid) in idx:
                tid_str = idx.get(int(bid))
                return int(tid_str) if tid_str is not None else None
        except (AttributeError, TypeError, ValueError, KeyError):
            logger.debug("VisualsController._get_template_id_for_state: failed to fallback via building index", exc_info=True)
        return None

    # --- World/buildings helpers ---------------------------------------------
    def _get_world(self):
        try:
            return getattr(getattr(self.parent.game, 'ecs', None), 'ecs_world', None)
        except AttributeError:
            return None

    def _iter_building_entities(self):
        world = self._get_world()
        try:
            for ob in getattr(world, 'buildings', []) or []:
                yield ob
        except AttributeError:
            return

    def _find_building_entity_by_id(self, bid: int):
        for ob in self._iter_building_entities():
            try:
                if getattr(ob, 'id', None) == int(bid):
                    return ob
            except (AttributeError, TypeError, ValueError):
                logger.debug("VisualsController.pick_visual_building_under_cursor: error iterating tagged entities", exc_info=True)
                continue
        # Try to load on demand if not found
        try:
            self._ensure_building_loaded(int(bid))
            for ob in self._iter_building_entities():
                try:
                    if getattr(ob, 'id', None) == int(bid):
                        return ob
                except (AttributeError, TypeError, ValueError):
                    logger.debug("VisualsController._find_visual_entity_for_state: error scanning tagged world entities", exc_info=True)
                    continue
        except (AttributeError, TypeError, ValueError):
            pass

    def _get_selected_spawner_id(self) -> str | None:
        try:
            inst = getattr(self.parent.model, 'selected_instance', None)
            if isinstance(inst, dict) and inst.get('id') is not None:
                return str(inst.get('id'))
        except (AttributeError, TypeError, ValueError):
            return None
        return None

    def _find_visual_entity_for_state(self, state_key: str):
        """Best-effort resolver for the visual entity of a given state.
        Priority:
        1) World object tagged as spawner visual for this spawner and state
        2) Fallback to instance_id mapping if present
        """
        sid = self._get_selected_spawner_id()
        # 1) Try tags (_is_spawner_visual + spawner_instance_id + state_key)
        if sid is not None:
            for ob in self._iter_building_entities():
                try:
                    if not getattr(ob, '_is_spawner_visual', False):
                        continue
                    if str(getattr(ob, 'spawner_instance_id', getattr(ob, 'spawn_id', ''))) != str(sid):
                        continue
                    if str(getattr(ob, 'spawner_state_key', '')) == str(state_key):
                        return ob
                except (ValueError, TypeError, KeyError):
                    continue
        # 2) Fallback by instance_id from visuals mapping
        try:
            visuals = getattr(self.parent.model, 'visuals', {}) or {}
            key_map = getattr(self.parent.model, 'visuals_key_map', {}) or {}
            json_key = key_map.get(state_key, state_key)
            raw = visuals.get(json_key)
            if raw is not None:
                if isinstance(raw, dict):
                    bid = int(raw.get('instance_id') or raw.get('id') or raw.get('building_instance_id'))
                else:
                    bid = int(raw)
                return self._find_building_entity_by_id(int(bid))
        except (AttributeError, TypeError, ValueError, KeyError):
            pass
        return None

    def center_camera_on_state(self, state_key: str) -> None:
        """Center camera on the building instance mapped to the given visuals state key.
        Best-effort: ensures the building is loaded, then computes world pixel position
        from buildings_instances.json using zone offsets and rel_x/rel_y.
        """
        # Prefer the live world entity if present (robust across restarts)
        try:
            cam = getattr(self.parent.game, 'camera', None)
            if cam is None:
                return None
            ob = self._find_visual_entity_for_state(state_key)
            if ob is not None:
                try:
                    zone = getattr(ob, 'zone', None)
                    if zone is None:
                        zone = getattr(getattr(ob, 'model', ob), 'zone', None)
                    if not zone:
                        zone = 'lobby'
                    rx = getattr(getattr(ob, 'model', ob), 'rel_x', None)
                    ry = getattr(getattr(ob, 'model', ob), 'rel_y', None)
                    if rx is None or ry is None:
                        # Fallback to JSON if coords are missing
                        raise RuntimeError('missing rel coords on entity')
                    off = global_map_settings.zone_offsets.get(str(zone), (0, 0))
                    bx = int(off[0] * TILE_SIZE) + int(rx)
                    by = int(off[1] * TILE_SIZE) + int(ry)
                    zoom = getattr(cam, 'zoom', 1.0) or 1.0
                    cam.offset_x = float(bx) - (cam.screen_width / (2 * zoom))
                    cam.offset_y = float(by) - (cam.screen_height / (2 * zoom))
                    return None
                except (AttributeError, TypeError, ValueError):
                    logger.debug("VisualsController.center_camera_on_state: failed centering using live entity", exc_info=True)
        except (AttributeError, TypeError, ValueError):
            logger.debug("VisualsController.center_camera_on_state: unexpected error (live entity path)", exc_info=True)
        # Fallback: try to center using the instance id mapping and JSON
        bid = self._get_instance_id_for_state(state_key)
        if bid is None:
            return None
        try:
            self._ensure_building_loaded(int(bid))
        except Exception:
            pass
        try:
            cam = getattr(self.parent.game, 'camera', None)
            if cam is None:
                return None
            bx = by = None
            bzone = 'lobby'
            for e in load_buildings_instances():
                try:
                    if int(e.get('id')) == int(bid):
                        bzone = str(e.get('zone') or 'lobby')
                        rx = int(e.get('rel_x') or 0)
                        ry = int(e.get('rel_y') or 0)
                        off = global_map_settings.zone_offsets.get(bzone, (0, 0))
                        bx = int(off[0] * TILE_SIZE) + int(rx)
                        by = int(off[1] * TILE_SIZE) + int(ry)
                        break
                except Exception:
                    continue
            if bx is not None and by is not None:
                zoom = getattr(cam, 'zoom', 1.0) or 1.0
                cam.offset_x = float(bx) - (cam.screen_width / (2 * zoom))
                cam.offset_y = float(by) - (cam.screen_height / (2 * zoom))
        except Exception:
            pass
        return None

    # JSON helpers now live in services

    def _ensure_building_loaded(self, bid: int) -> None:
        """If building with id 'bid' is not present in world.buildings, load it
        from instances/templates and append it. Editor-only best-effort."""
        world = self._get_world()
        if world is None:
            return
        # Already loaded in world?
        try:
            for ob in getattr(world, 'buildings', []) or []:
                try:
                    if int(getattr(ob, 'id', -1)) == int(bid):
                        return
                except (TypeError, ValueError):
                    continue
        except AttributeError:
            pass
        # Lookup in JSON for this building id
        inst_entry: Dict[str, Any] | None = None
        try:
            for e in load_buildings_instances():
                try:
                    if int(e.get('id')) == int(bid):
                        inst_entry = e
                        break
                except (TypeError, ValueError, AttributeError):
                    continue
        except Exception:
            inst_entry = None
        if not inst_entry:
            return
        # Build config for factory
        cfg: dict[str, Any] = {}
        try:
            cfg['image_path'] = get_template_image_path(int(inst_entry.get('template_id')))
            cfg['rel_x'] = int(inst_entry.get('rel_x', 0) or 0)
            cfg['rel_y'] = int(inst_entry.get('rel_y', 0) or 0)
            if inst_entry.get('zone') is not None:
                cfg['zone'] = str(inst_entry.get('zone'))
            ov = inst_entry.get('overrides') or {}
            if isinstance(ov, dict):
                if isinstance(ov.get('scale'), (list, tuple)) and len(ov.get('scale')) == 2:
                    cfg['scale'] = (int(ov['scale'][0]), int(ov['scale'][1]))
                if 'z_bottom' in ov:
                    cfg['z_bottom'] = int(ov['z_bottom'])
                if 'z_top' in ov:
                    cfg['z_top'] = int(ov['z_top'])
        except (TypeError, ValueError, AttributeError):
            pass
        if not cfg.get('image_path'):
            return
        # Create Building and append to world
        try:
            cam = getattr(self.parent.game, 'camera', None)
            b = build_from_config(cfg, camera=cam)
            try:
                setattr(b, 'id', int(bid))
            except (TypeError, ValueError, AttributeError):
                pass
            try:
                setattr(b, 'visible', True)
                setattr(b, 'editor_hidden', False)
                setattr(b, 'runtime_hidden', False)
            except AttributeError:
                pass
            try:
                if not hasattr(world, 'buildings') or world.buildings is None:
                    setattr(world, 'buildings', [])
                world.buildings.append(b)
            except AttributeError:
                pass
            try:
                ents = getattr(self.parent.game, 'entities', None)
                if ents is not None and hasattr(ents, 'buildings') and ents.buildings is not None:
                    ents.buildings.append(b)
            except AttributeError:
                pass
        except Exception:
            pass

    def _set_building_visible(self, bid: int, visible: bool) -> None:
        # Cache intended editor visibility
        self.model.editor_visibility[int(bid)] = bool(visible)
        ob = self._find_building_entity_by_id(int(bid))
        if ob is not None:
            # Do NOT touch runtime 'visible' flag; restrict to editor-only flag so gameplay is unaffected
            try:
                setattr(ob, 'editor_hidden', not bool(visible))
            except Exception:
                pass

    def tag_and_reveal_building(self, bid: int, state_key: str) -> None:
        ob = self._find_building_entity_by_id(int(bid))
        if ob is None:
            # Attempt to load the building instance into the world and retry
            try:
                self._ensure_building_loaded(int(bid))
            except Exception:
                pass
            ob = self._find_building_entity_by_id(int(bid))
            if ob is None:
                return
        try:
            setattr(ob, '_is_spawner_visual', True)
        except Exception:
            pass
        try:
            inst = self.parent.model.selected_instance or {}
            sid = str(inst.get('id')) if inst.get('id') is not None else None
            if sid is not None:
                setattr(ob, 'spawner_instance_id', sid)
                setattr(ob, 'spawn_id', sid)
        except Exception:
            pass
        try:
            setattr(ob, 'spawner_state_key', str(state_key))
        except Exception:
            pass
        # Link back to ECS entity if present (best-effort)
        try:
            world = self._get_world()
            comps = getattr(world, 'components', {}) if world else {}
            if world and 'SpawnerConfig' in comps:
                for eid in world.get_entities_with('SpawnerConfig'):
                    try:
                        cfg = comps['SpawnerConfig'][eid]
                        # Match by spawner instance id if available
                        if getattr(ob, 'spawn_id', None) == str(getattr(cfg, 'instance_id', getattr(cfg, 'template_id', ''))):
                            setattr(ob, '_spawner_eid', eid)
                            setattr(ob, '_world_ref', world)
                            break
                    except Exception:
                        continue
        except Exception:
            pass
        self._set_building_visible(int(bid), True)
        # Center camera on the revealed building for user feedback
        try:
            cam = getattr(self.parent.game, 'camera', None)
            if cam is not None:
                # Find the building instance entry to compute world pixel position
                bx = by = None
                bzone = 'lobby'
                for e in load_buildings_instances():
                    try:
                        if int(e.get('id')) == int(bid):
                            bzone = str(e.get('zone') or 'lobby')
                            rx = int(e.get('rel_x') or 0)
                            ry = int(e.get('rel_y') or 0)
                            off = global_map_settings.zone_offsets.get(bzone, (0, 0))
                            bx = int(off[0] * TILE_SIZE) + int(rx)
                            by = int(off[1] * TILE_SIZE) + int(ry)
                            break
                    except Exception:
                        continue
                if bx is not None and by is not None:
                    zoom = getattr(cam, 'zoom', 1.0) or 1.0
                    cam.offset_x = float(bx) - (cam.screen_width / (2 * zoom))
                    cam.offset_y = float(by) - (cam.screen_height / (2 * zoom))
        except Exception:
            pass

    def tag_building_for_state(self, bid: int, state_key: str, *, visible: bool = True, center: bool = False) -> None:
        """Tag a building as a spawner visual for a given state without forcing camera centering by default.

        - Ensures the building entity exists in the world (loads if needed).
        - Tags linkage fields (_is_spawner_visual, spawn_id/spawner_instance_id, spawner_state_key, _spawner_eid).
        - Applies editor visibility via the editor_hidden flag using _set_building_visible.
        - Optionally recenters camera if center=True.
        """
        ob = self._find_building_entity_by_id(int(bid))
        if ob is None:
            try:
                self._ensure_building_loaded(int(bid))
            except Exception:
                pass
            ob = self._find_building_entity_by_id(int(bid))
            if ob is None:
                return
        # Basic tags for editor/runtime linking and debug
        try:
            setattr(ob, '_is_spawner_visual', True)
        except Exception:
            pass
        try:
            inst = self.parent.model.selected_instance or {}
            sid = str(inst.get('id')) if inst.get('id') is not None else None
            if sid is not None:
                setattr(ob, 'spawner_instance_id', sid)
                setattr(ob, 'spawn_id', sid)
        except Exception:
            pass
        try:
            setattr(ob, 'spawner_state_key', str(state_key))
        except Exception:
            pass
        # Link to spawner eid if available
        try:
            world = self._get_world()
            comps = getattr(world, 'components', {}) if world else {}
            if world and 'SpawnerConfig' in comps:
                for eid in world.get_entities_with('SpawnerConfig'):
                    try:
                        cfg = comps['SpawnerConfig'][eid]
                        if getattr(ob, 'spawn_id', None) == str(getattr(cfg, 'instance_id', getattr(cfg, 'template_id', ''))):
                            setattr(ob, '_spawner_eid', eid)
                            setattr(ob, '_world_ref', world)
                            break
                    except Exception:
                        continue
        except Exception:
            pass
        # Apply editor visibility only (do not touch runtime_hidden here)
        try:
            self._set_building_visible(int(bid), bool(visible))
        except Exception:
            pass
        # Position the building relative to the spawner's anchor immediately using visuals offset (editor quality-of-life)
        try:
            # Do not override while dragging this visual in the editor
            if not bool(getattr(ob, '_spawner_visual_dragging', False)):
                inst = getattr(self.parent.model, 'selected_instance', None)
                if isinstance(inst, dict):
                    zone = str(inst.get('zone') or 'lobby')
                    # Ensure same zone
                    try:
                        if getattr(ob, 'zone', None) != zone:
                            setattr(ob, 'zone', zone)
                    except Exception:
                        pass
                    # Compute anchor center in zone-relative pixels
                    tile = inst.get('tile') or (0, 0)
                    try:
                        tx, ty = int(tile[0]), int(tile[1])
                    except Exception:
                        tx, ty = 0, 0
                    anchor_cx = int(tx * TILE_SIZE + TILE_SIZE // 2)
                    anchor_cy = int(ty * TILE_SIZE + TILE_SIZE // 2)
                    # Resolve per-state offset from visuals mapping (if any)
                    off_dx, off_dy = 0, 0
                    try:
                        raw = self._get_mapping_entry_for_state(state_key)
                        if isinstance(raw, dict):
                            off = raw.get('offset')
                            if isinstance(off, (list, tuple)) and len(off) == 2:
                                off_dx = int(off[0])
                                off_dy = int(off[1])
                    except Exception:
                        off_dx = off_dy = 0
                    try:
                        setattr(ob, 'rel_x', int(anchor_cx + off_dx))
                        setattr(ob, 'rel_y', int(anchor_cy + off_dy))
                    except Exception:
                        pass
                    # Persist updated placement to buildings_instances.json so it sticks across reloads
                    try:
                        arr = load_buildings_instances()
                        changed = False
                        for ee in arr:
                            try:
                                if int(ee.get('id')) == int(bid):
                                    if str(ee.get('zone') or 'lobby') != str(zone):
                                        ee['zone'] = str(zone)
                                        changed = True
                                    if int(ee.get('rel_x') or 0) != int(anchor_cx + off_dx):
                                        ee['rel_x'] = int(anchor_cx + off_dx)
                                        changed = True
                                    if int(ee.get('rel_y') or 0) != int(anchor_cy + off_dy):
                                        ee['rel_y'] = int(anchor_cy + off_dy)
                                        changed = True
                                    break
                            except Exception:
                                continue
                        if changed:
                            write_buildings_instances(arr)
                    except Exception:
                        pass
        except Exception:
            pass
        # Optional camera center
        if center:
            try:
                cam = getattr(self.parent.game, 'camera', None)
                if cam is not None:
                    bx = by = None
                    bzone = 'lobby'
                    for e in load_buildings_instances():
                        try:
                            if int(e.get('id')) == int(bid):
                                bzone = str(e.get('zone') or 'lobby')
                                rx = int(e.get('rel_x') or 0)
                                ry = int(e.get('rel_y') or 0)
                                off = global_map_settings.zone_offsets.get(bzone, (0, 0))
                                bx = int(off[0] * TILE_SIZE) + int(rx)
                                by = int(off[1] * TILE_SIZE) + int(ry)
                                break
                        except Exception:
                            continue
                    if bx is not None and by is not None:
                        zoom = getattr(cam, 'zoom', 1.0) or 1.0
                        cam.offset_x = float(bx) - (cam.screen_width / (2 * zoom))
                        cam.offset_y = float(by) - (cam.screen_height / (2 * zoom))
            except Exception:
                pass

    def reveal_all_mapped_buildings(self) -> None:
        """Ensure that every building referenced by the current instance visuals mapping is
        present in the world, tagged to this spawner, and editor-visible according to the
        per-building `editor_visibility` cache (defaulting to True when absent).
        """
        visuals = dict(getattr(self.parent.model, 'visuals', {}) or {})
        vis_cache = getattr(self.model, 'editor_visibility', {}) or {}
        # Ensure we have access to controller helpers
        ipc = self.parent
        for state_key, entry in visuals.items():
            bid = None
            tpl_id = None
            try:
                if isinstance(entry, dict):
                    bid = entry.get('instance_id') or entry.get('id') or entry.get('building_instance_id')
                    bid = int(bid) if bid is not None else None
                    try:
                        tpl_id = int(entry.get('template_id')) if entry.get('template_id') is not None else None
                    except Exception:
                        tpl_id = None
                else:
                    bid = int(entry)
            except Exception:
                bid = None
            # If the mapping points to a non-existent instance but has template_id, auto-create it
            ob = None
            if bid is not None:
                try:
                    ob = self._find_building_entity_by_id(int(bid))
                    if ob is None:
                        self._ensure_building_loaded(int(bid))
                        ob = self._find_building_entity_by_id(int(bid))
                except Exception:
                    ob = None
            if ob is None and tpl_id is not None:
                # Prime pending template for this state and create a new instance
                try:
                    if not hasattr(ipc.model, 'visuals_pending_templates') or ipc.model.visuals_pending_templates is None:
                        ipc.model.visuals_pending_templates = {}
                except Exception:
                    ipc.model.visuals_pending_templates = {}
                try:
                    ipc.model.visuals_pending_templates[str(state_key)] = str(int(tpl_id))
                except Exception:
                    ipc.model.visuals_pending_templates[str(state_key)] = str(tpl_id)
                new_id = None
                try:
                    new_id = ipc.add_building_instance_for_visual(str(state_key), reveal=False)
                except Exception:
                    new_id = None
                if new_id is not None:
                    # Update local visuals map for immediate use (controller persists inside helper)
                    visuals[str(state_key)] = {'instance_id': int(new_id), 'template_id': int(tpl_id)}
                    bid = int(new_id)
            if bid is None:
                continue
            # Default visible True unless user toggled it off in this editor session
            visible = bool(vis_cache.get(int(bid), True))
            self.tag_building_for_state(int(bid), str(state_key), visible=visible, center=False)
        # Reflect any mapping updates back on the model for consistency
        try:
            ipc.model.visuals = visuals
            if isinstance(ipc.model.selected_instance, dict):
                ipc.model.selected_instance['visuals'] = visuals
        except Exception:
            pass

    def is_building_visible_for_state(self, state_key: str) -> bool:
        # Prefer reading from the live entity (robust across restarts)
        ob = self._find_visual_entity_for_state(state_key)
        if ob is not None:
            try:
                hidden = bool(getattr(ob, 'editor_hidden', False))
                vis = bool(getattr(ob, 'visible', True)) and not hidden
                # Keep cache in model if id is available
                try:
                    bid = getattr(ob, 'id', None)
                    if bid is not None:
                        self.model.editor_visibility[int(bid)] = vis
                except Exception:
                    pass
                return vis
            except Exception:
                return True
        # Fallback to cached visibility by instance id
        bid_int = self._get_instance_id_for_state(state_key)
        if bid_int is None:
            return True
        return bool(self.model.editor_visibility.get(int(bid_int), True))

    def toggle_building_visibility_for_state(self, state_key: str) -> None:
        """Toggle only the editor rendering visibility for the building mapped to state_key.
        Robust even if the instance id is stale because global saver skipped it.
        """
        ob = self._find_visual_entity_for_state(state_key)
        if ob is not None:
            try:
                # Decide current effective visibility
                cur = (not bool(getattr(ob, 'editor_hidden', False)))
                new_vis = not cur
                try:
                    setattr(ob, 'editor_hidden', not bool(new_vis))
                except Exception:
                    pass
                try:
                    bid = getattr(ob, 'id', None)
                    if bid is not None:
                        self.model.editor_visibility[int(bid)] = bool(new_vis)
                except Exception:
                    pass
                return
            except Exception:
                pass
        # Fallback by instance id
        bid_int = self._get_instance_id_for_state(state_key)
        if bid_int is None:
            return
        cur = bool(self.model.editor_visibility.get(int(bid_int), True))
        self._set_building_visible(int(bid_int), not cur)

    # --- Hit-testing ----------------------------------------------------------
    def pick_visual_building_under_cursor(self, mx: int, my: int):
        """Return the building entity under the cursor that is linked to the currently
        selected spawner instance (via tags or visuals mapping). Returns the entity or None.

        Preference order:
        1) Entities tagged as spawner visuals for this spawner (fast path)
        2) Entities whose id appears in the current visuals mapping
        """
        cam = getattr(self.parent.game, 'camera', None)
        if cam is None:
            return None
        sid = self._get_selected_spawner_id()
        # Helper to compute on-screen rect bounds without pygame.Rect
        def _screen_bounds(ob) -> tuple[int, int, int, int] | None:
            try:
                # World pixel coords
                x = getattr(ob, 'x', getattr(getattr(ob, 'model', ob), 'x', None))
                y = getattr(ob, 'y', getattr(getattr(ob, 'model', ob), 'y', None))
                img = getattr(ob, 'image', getattr(getattr(ob, 'model', ob), 'image', None))
                if x is None or y is None or img is None:
                    return None
                w, h = img.get_size()
                # Apply camera transform
                sx, sy = cam.apply((x, y))
                sw, sh = cam.scale((w, h))
                return int(sx), int(sy), int(sw), int(sh)
            except (AttributeError, TypeError, ValueError):
                return None

        # 1) Prefer tagged entities for this spawner id (or any tagged if no selection)
        for ob in self._iter_building_entities():
            try:
                if not getattr(ob, '_is_spawner_visual', False):
                    continue
                if sid is not None:
                    if str(getattr(ob, 'spawner_instance_id', getattr(ob, 'spawn_id', ''))) != str(sid):
                        continue
                b = _screen_bounds(ob)
                if b is None:
                    continue
                sx, sy, sw, sh = b
                if sx <= mx <= sx + sw and sy <= my <= sy + sh:
                    return ob
            except (AttributeError, TypeError, ValueError):
                continue

        # 2) Fallback: check by ids in visuals mapping (ensure loaded before hit-test)
        try:
            visuals = getattr(self.parent.model, 'visuals', {}) or {}
        except (AttributeError, TypeError):
            visuals = {}

        ids: list[int] = []
        for v in visuals.values():
            try:
                if isinstance(v, dict):
                    bid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                else:
                    bid = int(v)
                ids.append(bid)
            except (AttributeError, TypeError, ValueError):
                continue

        # Try each id explicitly so we can ensure it's present for hit-testing
        for bid in ids:
            try:
                ob = self._find_building_entity_by_id(int(bid))
                if ob is None:
                    # Attempt to load on demand
                    self._ensure_building_loaded(int(bid))
                    ob = self._find_building_entity_by_id(int(bid))
                if ob is None:
                    continue
                b = _screen_bounds(ob)
                if b is None:
                    continue
                sx, sy, sw, sh = b
                if sx <= mx <= sx + sw and sy <= my <= sy + sh:
                    return ob
            except (AttributeError, TypeError, ValueError):
                continue

        # 3) Last fallback: check any building under cursor that is spawner-linked by disk data
        for ob in self._iter_building_entities():
            try:
                b = _screen_bounds(ob)
                if b is None:
                    continue
                sx, sy, sw, sh = b
                if not (sx <= mx <= sx + sw and sy <= my <= sy + sh):
                    continue
                bid = getattr(ob, 'id', None)
                if bid is None:
                    continue
                if self._is_spawner_visual_building_id(int(bid)):
                    return ob
            except (AttributeError, TypeError, ValueError):
                logger.debug("VisualsController.pick_visual_building_under_cursor: error scanning world entities for last fallback", exc_info=True)
                continue

        return None

    def _is_spawner_visual_building_id(self, bid: int) -> bool:
        """Return True if building id appears linked to any spawner (by JSON).
        Checks buildings_instances.json overrides and spawners_instances.json visuals.
        """
        # 1) buildings_instances.json overrides
        try:
            for e in load_buildings_instances():
                try:
                    if int(e.get('id')) != int(bid):
                        continue
                    ov = e.get('overrides') or {}
                    if isinstance(ov, dict):
                        if bool(ov.get('_is_spawner_visual', False)):
                            return True
                    # also consider root-level ids
                    if e.get('spawner_instance_id') is not None or e.get('spawn_id') is not None:
                        return True
                    break
                except (AttributeError, TypeError, ValueError):
                    continue
        except (OSError, AttributeError, TypeError, ValueError):
            pass

        # 2) spawners_instances.json visuals mapping
        try:
            arr = load_spawners_instances_json() or []
        except (OSError, AttributeError, TypeError, ValueError):
            logger.debug("VisualsController._is_spawner_visual_building_id: failed to load spawners_instances.json", exc_info=True)
            arr = []

        try:
            for inst in arr:
                try:
                    vis = inst.get('visuals') if isinstance(inst, dict) else None
                    if not isinstance(vis, dict):
                        continue
                    for v in vis.values():
                        try:
                            if isinstance(v, dict):
                                vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                            else:
                                vid = int(v)
                        except (AttributeError, TypeError, ValueError):
                            continue
                        if vid == int(bid):
                            return True
                except (AttributeError, TypeError, ValueError):
                    logger.debug("VisualsController._is_spawner_visual_building_id: error scanning visuals mapping", exc_info=True)
                    continue
        except (AttributeError, TypeError, ValueError):
            logger.debug("VisualsController._is_spawner_visual_building_id: unexpected error scanning spawners_instances", exc_info=True)

        return False

    def open_picker(self, state_key: str) -> None:
        """Delegates to parent to open the Visuals Picker for the given state."""
        self.parent.open_visuals_picker_for_state(state_key)

    # ... (rest of the code remains the same)

    # --- Delegations to parent InstancePropertiesController ------------------
    def begin_edit_visual(self, state_key: str) -> None:
        """Begin inline editing of the Template cell for the given visuals state.

        VisualsEvents expects this symbol on the visuals controller; delegate
        to the parent controller which owns the edit TextInput and commit/cancel
        logic. This enables clicking the Template cell to start editing.
        """
        try:
            self.parent.begin_edit_visual(state_key)
        except AttributeError:
            # Defensive: if parent does not expose the method, ignore gracefully
            logger.debug("VisualsController.begin_edit_visual: parent missing method", exc_info=True)

    def clear_visual_for_state(self, state_key: str) -> None:
        """Clear the mapping for a given visuals state (invoked by Clear 'X' button).

        Delegates to the parent controller which performs persistence, optional
        strict cleanup of orphaned building instances, and UI refresh.
        """
        try:
            self.parent.clear_visual_for_state(state_key)
        except AttributeError:
            logger.debug("VisualsController.clear_visual_for_state: parent missing method", exc_info=True)

    # --- Hard removal helper --------------------------------------------------
    def _remove_building_entity_by_id(self, bid: int) -> bool:
        """Remove any Building object with id 'bid' from the live world and editor lists.
        Returns True if any object was removed.
        """
        removed_any = False
        # ECS world list
        try:
            world = self._get_world()
            if world is not None and hasattr(world, 'buildings') and isinstance(world.buildings, list):
                arr = world.buildings
                for i in range(len(arr) - 1, -1, -1):
                    try:
                        if getattr(arr[i], 'id', None) == int(bid):
                            arr.pop(i)
                            removed_any = True
                    except (AttributeError, TypeError, ValueError):
                        continue
        except AttributeError:
            pass

        # Editor/game registry if present
        try:
            ents = getattr(self.parent.game, 'entities', None)
            if ents is not None and hasattr(ents, 'buildings') and isinstance(ents.buildings, list):
                arr2 = ents.buildings
                for i in range(len(arr2) - 1, -1, -1):
                    try:
                        if getattr(arr2[i], 'id', None) == int(bid):
                            arr2.pop(i)
                            removed_any = True
                    except (AttributeError, TypeError, ValueError):
                        continue
        except AttributeError:
            logger.debug("VisualsController._remove_building_entity_by_id: error scanning editor/game registry", exc_info=True)

        return removed_any


__all__ = ["VisualsController"]
