from __future__ import annotations

from typing import Optional, Any
from types import SimpleNamespace
import pygame
import logging

from .types import EditorCtx
from .utils import safe_get_world, safe_get_camera
from . import split_drag as split
from . import selection as sel
from . import anchor_drag as anchor
from . import resize as rz
from . import confirmations as conf
from ..services.picking import pick_spawner_under_cursor
from ..services import zone_for_global_tile
from ..services.persistence import find_instance_in_json, persist_drop, load_instances_json, write_instances_json
from roguelike_engine.config.config_tiles import TILE_SIZE

# Tools from Buildings Editor reused by the Spawner Editor
from roguelike_editors.buildings.tools.split_z_tool.split_tool import SplitTool
from roguelike_editors.buildings.tools.z_tool.z_tool import ZTool
from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    write_buildings_instances as svc_write_buildings_instances,
)


logger = logging.getLogger(__name__)


class SpawnerEditorEventHandler:
    """Minimal Spawner Editor event handler that delegates to modular event functions.

    - Builds a small EditorCtx (world/camera/controller/tool adapters)
    - Routes each event to split/selection/anchor/resize/confirmation functions
    - Maintains editor flags like visibility and input suppression
    """

    def __init__(self, controller: 'SpawnerEditorController'):
        self.controller = controller
        self.model = controller.model
        self.font = controller.font
        self.game = controller.game
        # Split-drag state sampled MOTION1 once per drag
        self._split_drag_first_logged: bool = False
        # Info overlay / panning flags kept for parity (can extend later)
        self.info_dragging: bool = False
        self.info_drag_offset: tuple[int, int] = (0, 0)
        self.panning: bool = False
        self.pan_start: tuple[int, int] = (0, 0)
        self.pan_offset_start: tuple[float, float] = (0.0, 0.0)
        # Snapshot used by zone confirmation flow
        self._drag_start_entry: Optional[dict] = None
        # Visual moving (RMB-drag) helpers
        self._moving_visual_delta_world: tuple[int, int] | None = None

        # Shared tools adapters (reuse Buildings Editor logic)
        try:
            self._split_adapter = SimpleNamespace(split_dragging=False, selected_building=None)
            self._split_tool = SplitTool(None, self._split_adapter)
        except (AttributeError, TypeError):
            self._split_adapter = SimpleNamespace(split_dragging=False, selected_building=None)
            self._split_tool = None
        try:
            self._z_adapter = SimpleNamespace(active_building=None)
            _z_state = getattr(controller, 'z_state', None)
            if _z_state is None or not hasattr(_z_state, 'set'):
                _z_state = SimpleNamespace(set=lambda *args, **kwargs: None)
            self._z_tool_bottom = ZTool(SimpleNamespace(z_state=_z_state), self._z_adapter, target="bottom")
            self._z_tool_top = ZTool(SimpleNamespace(z_state=_z_state), self._z_adapter, target="top")
        except (AttributeError, TypeError):
            self._z_adapter = SimpleNamespace(active_building=None)
            self._z_tool_bottom = None
            self._z_tool_top = None

    # Public API ---------------------------------------------------------------
    def set_game(self, game) -> None:
        self.game = game

    def _make_ctx(self) -> EditorCtx:
        world = safe_get_world(getattr(self, 'game', None))
        camera = safe_get_camera(getattr(self, 'game', None))
        return EditorCtx(
            controller=self.controller,
            model=self.model,
            game=self.game,
            world=world,
            camera=camera,
            split_tool=self._split_tool,
            split_adapter=self._split_adapter,
            logger=logger,
        )

    def toggle_visible(self) -> None:
        """Toggle visibility and manage side effects like input suppression and drag cancel."""
        self.model.visible = not self.model.visible
        ctx = self._make_ctx()
        world = ctx.world
        if not self.model.visible:
            # Stop any ongoing drags and clear hover/selection
            self.model.dragging = False
            self.model.dragging_eid = None
            self.model.hovered_eid = None
            try:
                self.model.resizing_visual = False
                self.model.resizing_visual_bid = None
            except AttributeError:
                logger.debug("toggle_visible: failed to reset resizing flags", exc_info=True)
            try:
                self.model.split_drag_active = False
                self.model.split_drag_bid = None
            except AttributeError:
                logger.debug("toggle_visible: failed to reset split-drag flags", exc_info=True)
            self.panning = False
            self.info_dragging = False
            self._drag_start_entry = None
            try:
                if world and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_editor_hovered_eid', None)
                    setattr(world.state, 'spawner_selected_eid', None)
                    setattr(world.state, 'spawner_input_suppressed', False)
                    setattr(world.state, 'spawner_editor_active', False)
            except AttributeError:
                logger.debug("toggle_visible: failed to clear world.state flags", exc_info=True)
            # Clear split propagation key to avoid stale propagation next time
            try:
                setattr(self.model, '_split_propagation_key', None)
            except AttributeError:
                logger.debug("toggle_visible: failed to clear _split_propagation_key", exc_info=True)
        else:
            # Mark editor as active globally
            try:
                if world and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_editor_active', True)
            except AttributeError:
                logger.debug("toggle_visible: failed to set world.state.spawner_editor_active", exc_info=True)
            # Reveal all mapped visuals for the currently selected instance (if any)
            try:
                ip = getattr(self.controller, 'instance_properties', None)
                if ip is not None and hasattr(ip, 'visuals'):
                    # Only when a spawner instance is selected
                    sel_inst = getattr(getattr(ip, 'model', None), 'selected_instance', None)
                    if isinstance(sel_inst, dict):
                        ip.visuals.reveal_all_mapped_buildings()
            except Exception:
                logger.debug("toggle_visible: reveal_all_mapped_buildings failed on open", exc_info=True)

    # Orchestrated event dispatcher ------------------------------------------
    def handle_event(self, event: pygame.event.Event) -> bool:
        if not self.model.visible or not self.game:
            return False
        ctx = self._make_ctx()
        world, camera = ctx.world, ctx.camera
        if not world or not camera:
            return False

        # Visuals Picker overlay has priority and blocks gameplay
        try:
            ip = getattr(self.controller, 'instance_properties', None)
            if ip is not None and getattr(getattr(ip, 'model', None), 'visuals_picker_open', False):
                handled = False
                try:
                    handled = bool(ip.handle_visuals_picker_event(event, camera))
                except (AttributeError, TypeError, ValueError):
                    logger.debug("handle_event: visuals_picker_event handler failed", exc_info=True)
                    handled = False
                return True if handled or event.type in (
                    pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION,
                    pygame.MOUSEWHEEL, pygame.KEYDOWN, pygame.KEYUP
                ) else False
        except (AttributeError, TypeError):
            logger.debug("handle_event: exception while routing to visuals picker", exc_info=True)

        # Split drag END on any mouse button up
        if event.type == pygame.MOUSEBUTTONUP and getattr(self.model, 'split_drag_active', False):
            if split.end_split_drag(ctx, event):
                return True
        # RMB up: finish moving a visual building (persist offset and building rel position)
        if event.type == pygame.MOUSEBUTTONUP and event.button == 3 and getattr(self.model, 'moving_visual', False):
            bid = getattr(self.model, 'moving_visual_bid', None)
            self.model.moving_visual = False
            try:
                ip = getattr(self.controller, 'instance_properties', None)
                ob = None
                if bid is not None and ip is not None and hasattr(ip, 'visuals'):
                    try:
                        ob = ip.visuals._find_building_entity_by_id(int(bid))
                    except Exception:
                        ob = None
                # Clear drag guard on the world object
                try:
                    if ob is not None:
                        setattr(ob, '_spawner_visual_dragging', False)
                except Exception:
                    pass
                # Persist offset in spawners_instances.json (relative to spawner center)
                try:
                    sel_inst = getattr(getattr(self.controller.instance_properties, 'model', None), 'selected_instance', None)
                except Exception:
                    sel_inst = None
                if ob is not None and sel_inst is not None and isinstance(sel_inst, dict):
                    try:
                        # Resolve spawner EID for this building (tagged during runtime sync)
                        sp_eid = getattr(ob, '_spawner_eid', None)
                        cfg = world.components['SpawnerConfig'][sp_eid] if sp_eid is not None else None
                        zone = getattr(cfg, 'zone', None) or getattr(ob, 'zone', None) or 'lobby'
                        off_x, off_y = (0, 0)
                        try:
                            from roguelike_engine.config.map_config import global_map_settings as _gms
                            off_x, off_y = _gms.zone_offsets.get(str(zone), (0, 0))
                        except Exception:
                            off_x, off_y = (0, 0)
                        # Anchor center in zone-relative px
                        ax, ay = (0, 0)
                        try:
                            tx, ty = cfg.anchor_tile
                            ax = int((int(tx) - int(off_x)) * TILE_SIZE + TILE_SIZE // 2)
                            ay = int((int(ty) - int(off_y)) * TILE_SIZE + TILE_SIZE // 2)
                        except Exception:
                            # Fallback: compute from selected instance tile
                            try:
                                t = sel_inst.get('tile', [0, 0])
                                ax = int(int(t[0]) * TILE_SIZE + TILE_SIZE // 2)
                                ay = int(int(t[1]) * TILE_SIZE + TILE_SIZE // 2)
                            except Exception:
                                ax, ay = (0, 0)
                        # Compute offset = building.rel - anchor_center (zone-relative px)
                        dx = int(getattr(ob, 'rel_x', getattr(getattr(ob, 'model', ob), 'rel_x', 0)) or 0) - ax
                        dy = int(getattr(ob, 'rel_y', getattr(getattr(ob, 'model', ob), 'rel_y', 0)) or 0) - ay
                        # Update visuals mapping entry that points to this building id
                        arr = load_instances_json()
                        # Identify instance on disk by id
                        cur_id = str(sel_inst.get('id')) if sel_inst.get('id') is not None else None
                        for inst in arr:
                            try:
                                if str(inst.get('id')) != cur_id:
                                    continue
                                vis = inst.get('visuals') if isinstance(inst.get('visuals'), dict) else {}
                                changed = False
                                for k, v in vis.items():
                                    try:
                                        vid = None
                                        if isinstance(v, dict):
                                            vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                                        else:
                                            vid = int(v)
                                    except Exception:
                                        vid = None
                                    if vid is not None and int(vid) == int(bid):
                                        # ensure dict form and write/remove offset as needed
                                        if not isinstance(v, dict):
                                            entry = {'instance_id': vid, 'template_id': None}
                                            if int(dx) != 0 or int(dy) != 0:
                                                entry['offset'] = [int(dx), int(dy)]  # type: ignore[index]
                                            vis[k] = entry
                                        else:
                                            vv = dict(v)
                                            if int(dx) != 0 or int(dy) != 0:
                                                vv['offset'] = [int(dx), int(dy)]
                                            else:
                                                # Drop zero offsets to keep JSON clean
                                                try:
                                                    vv.pop('offset', None)
                                                except Exception:
                                                    pass
                                            vis[k] = vv
                                        inst['visuals'] = vis
                                        changed = True
                                        break
                                if changed:
                                    write_instances_json(arr)
                                    # Also update cfg.visuals_offsets_px in-memory for this mapping key
                                    try:
                                        if cfg is not None:
                                            if getattr(cfg, 'visuals_offsets_px', None) is None:
                                                cfg.visuals_offsets_px = {}
                                            key_l = str(k).strip().lower()
                                            cfg.visuals_offsets_px[key_l] = (int(dx), int(dy))
                                    except Exception:
                                        pass
                                    break
                            except Exception:
                                continue
                        # Persist buildings_instances rel_x/rel_y for this building id
                        try:
                            data = svc_load_buildings_instances()
                        except OSError:
                            data = []
                        changed2 = False
                        for e in data or []:
                            try:
                                if int(e.get('id')) != int(bid):
                                    continue
                            except Exception:
                                continue
                            try:
                                e['rel_x'] = int(getattr(ob, 'rel_x', getattr(getattr(ob, 'model', ob), 'rel_x', 0)) or 0)
                                e['rel_y'] = int(getattr(ob, 'rel_y', getattr(getattr(ob, 'model', ob), 'rel_y', 0)) or 0)
                                e['zone'] = str(zone)
                                changed2 = True
                            except Exception:
                                pass
                            break
                        if changed2:
                            try:
                                svc_write_buildings_instances(data)
                            except OSError:
                                pass
                    except Exception:
                        logger.debug("RMB up: visual persist failed", exc_info=True)
                # Clear world input suppression now that move finished
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', False)
                except Exception:
                    pass
                # Clear delta cache
                self._moving_visual_delta_world = None
            except Exception:
                logger.debug("RMB up: finalize moving_visual failed", exc_info=True)
            return True
        # RMB up: stop anchor drag if active and persist movement (or ask for zone confirm)
        if event.type == pygame.MOUSEBUTTONUP and event.button == 3 and getattr(self.model, 'dragging', False):
            eid = getattr(self.model, 'dragging_eid', None)
            self.model.dragging = False
            self.model.dragging_eid = None
            try:
                if isinstance(eid, int) and eid in world.components.get('SpawnerConfig', {}):
                    cfg = world.components['SpawnerConfig'][eid]
                    tx, ty = cfg.anchor_tile
                    proposed_zone = zone_for_global_tile(int(tx), int(ty))
                    # Snapshot captured at drag start
                    snapshot = getattr(self, '_drag_start_entry', None) or {}
                    orig_zone = snapshot.get('zone') or getattr(cfg, 'zone', None)
                    orig_local = snapshot.get('local_tile') or snapshot.get('orig_local')
                    if proposed_zone and str(proposed_zone) != str(orig_zone):
                        # Ask confirmation before moving across zones
                        try:
                            self.model.pending_zone_confirm = {
                                'eid': eid,
                                'orig_zone': orig_zone,
                                'proposed_zone': proposed_zone,
                                'orig_local': orig_local,
                            }
                            if hasattr(world, 'state'):
                                setattr(world.state, 'spawner_input_suppressed', True)
                        except Exception:
                            pass
                    else:
                        # Persist movement in same zone
                        try:
                            persist_drop(world, eid, snapshot, orig_zone=orig_zone)
                        except Exception:
                            logger.debug("RMB up: persist_drop failed", exc_info=True)
                        # Refresh instances list to reflect new tile
                        try:
                            self.controller.spawner_instances.refresh_from_disk()
                        except Exception:
                            pass
                        # Clear suppression and snapshot
                        try:
                            if hasattr(world, 'state'):
                                setattr(world.state, 'spawner_input_suppressed', False)
                        except Exception:
                            pass
                        self._drag_start_entry = None
            except Exception:
                logger.debug("RMB up: error finalizing anchor drag", exc_info=True)
            return True
        # Split drag MOTION while active
        if event.type == pygame.MOUSEMOTION and getattr(self.model, 'split_drag_active', False) and getattr(self.model, 'split_drag_bid', None) is not None:
            if split.update_split_drag(ctx, event):
                return True

        # Hover: detect spawner anchor under cursor when not dragging/resizing/splitting
        if event.type == pygame.MOUSEMOTION and not getattr(self.model, 'dragging', False) and not getattr(self.model, 'resizing_visual', False) and not getattr(self.model, 'split_drag_active', False):
            try:
                mx, my = event.pos
                eid = pick_spawner_under_cursor(world, camera, int(mx), int(my))
                # Mirror to model and world.state for renderer
                try:
                    self.model.hovered_eid = eid
                except Exception:
                    pass
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_editor_hovered_eid', eid)
                except Exception:
                    pass
            except Exception:
                logger.debug("handle_event: hover pick failed", exc_info=True)

        # Resize MOTION while active
        if event.type == pygame.MOUSEMOTION and getattr(self.model, 'resizing_visual', False):
            if rz.update_resize_motion(ctx, event):
                return True

        # LMB handling: prioritize building UI handles (Delete/Reset/Resize) for the currently selected building.
        # If not clicking a handle, allow selecting a spawner by clicking near its anchor (spawner gets priority over general building selection).
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            ip = getattr(self.controller, 'instance_properties', None)
            mx, my = event.pos
            # 0) Building overlay handles for the currently selected building (Delete/Reset/Resize)
            from .utils import get_selected_building_id
            sel_bid = get_selected_building_id(ip)
            world_ob = None
            try:
                logger.debug("[SpawnerEditor] LMB down at (%s,%s); sel_bid=%s", mx, my, sel_bid)
            except Exception:
                pass
            if sel_bid is not None:
                try:
                    world_ob = ip.visuals._find_building_entity_by_id(int(sel_bid)) if ip and hasattr(ip, 'visuals') else None
                except (AttributeError, TypeError, ValueError):
                    world_ob = None
                if world_ob is None:
                    from .utils import find_building_in_world_by_id
                    world_ob = find_building_in_world_by_id(ctx.world, int(sel_bid))
            try:
                logger.debug("[SpawnerEditor] LMB sel_bid=%s world_ob_resolved=%s", sel_bid, world_ob is not None)
            except Exception:
                pass
            # Also detect a building under cursor to allow handle clicks even if not yet selected
            ob_under = None
            try:
                if ip is not None and hasattr(ip, 'visuals'):
                    ob_under = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
            except Exception:
                ob_under = None
            # If we have no selected world_ob, but have a building under cursor, use it for handle hit-testing
            if world_ob is None and ob_under is not None:
                world_ob = ob_under
            if world_ob is not None:
                # Prefer view-cached rects from the last render pass to match exactly what was drawn
                view = getattr(self.controller, 'view', None)
                # Debug: check if click falls inside any cached panel rects (may explain swallowed events upstream)
                try:
                    if view is not None:
                        props = getattr(view, '_last_properties_rect', None)
                        insts = getattr(view, '_last_instances_rect', None)
                        mgr = getattr(view, '_last_manager_rect', None)
                        tb = getattr(view, '_last_toolbar_rect', None)
                        itb = getattr(view, '_last_instance_toolbar_rect', None)
                        def _hit(r):
                            try:
                                import pygame as _pg
                                return bool(r and _pg.Rect(r).collidepoint(mx, my))
                            except Exception:
                                return False
                        logger.debug("[SpawnerEditor] UI collisions: props=%s insts=%s mgr=%s tb=%s itb=%s", _hit(props), _hit(insts), _hit(mgr), _hit(tb), _hit(itb))
                except Exception:
                    pass
                del_rect = getattr(view, '_last_selected_delete_rect', None) if view is not None else None
                rst_rect = getattr(view, '_last_selected_reset_rect', None) if view is not None else None
                rz_rect = getattr(view, '_last_selected_resize_rect', None) if view is not None else None
                # Fallback to computing rects if the view cache is missing
                if del_rect is None or rst_rect is None or rz_rect is None:
                    from .utils import compute_spawner_handle_rects
                    rects = compute_spawner_handle_rects(ctx.camera, world_ob)
                    del_rect = del_rect or rects.get('delete')
                    rst_rect = rst_rect or rects.get('reset')
                    rz_rect = rz_rect or rects.get('resize')
                try:
                    logger.debug("[SpawnerEditor] LMB handles present: del=%s rst=%s rz=%s", del_rect is not None, rst_rect is not None, rz_rect is not None)
                except Exception:
                    pass
                try:
                    logger.debug("[SpawnerEditor] LMB handle hit-tests: del=%s rst=%s rz=%s", bool(del_rect and del_rect.collidepoint(mx, my)), bool(rst_rect and rst_rect.collidepoint(mx, my)), bool(rz_rect and rz_rect.collidepoint(mx, my)))
                except Exception:
                    pass
                # Default (reset size)
                if rst_rect is not None and rst_rect.collidepoint(mx, my):
                    if self._reset_selected_building_size(sel_bid):
                        return True
                # Resize: begin resize mode for selected building or for the building under cursor
                if rz_rect is not None and rz_rect.collidepoint(mx, my):
                    # If no selection yet but we have a building under cursor, select it first (respect visibility/instance)
                    if sel_bid is None and ob_under is not None:
                        try:
                            hidden = bool(getattr(ob_under, 'editor_hidden', False))
                        except Exception:
                            hidden = False
                        same_instance = True
                        try:
                            sel_inst = getattr(getattr(ip, 'model', None), 'selected_instance', None)
                            sel_sid = str(sel_inst.get('id')) if isinstance(sel_inst, dict) and sel_inst.get('id') is not None else None
                            ob_sid = str(getattr(ob_under, 'spawner_instance_id', getattr(ob_under, 'spawn_id', '')))
                            if sel_sid is not None:
                                same_instance = (ob_sid == sel_sid)
                        except Exception:
                            same_instance = True
                        if (not hidden) and same_instance:
                            try:
                                bid = getattr(ob_under, 'id', None)
                                if bid is not None and hasattr(ip, 'visuals') and hasattr(ip.visuals, 'model'):
                                    ip.visuals.model.selected_building_id = int(bid)
                                    sel_bid = int(bid)
                                    logger.debug("[SpawnerEditor] LMB autoselected building on resize click: bid=%s", bid)
                            except Exception:
                                pass
                    started = False
                    try:
                        started = bool(rz.start_resize(ctx, event))
                    except Exception:
                        started = False
                    try:
                        logger.debug("[SpawnerEditor] LMB start resize: sel_bid=%s started=%s", sel_bid, started)
                    except Exception:
                        pass
                    if started:
                        return True
                # Remove: delete the selected building instance (parity with Building Editor)
                if del_rect is not None and del_rect.collidepoint(mx, my):
                    try:
                        # 1) Remove from buildings_instances.json
                        from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
                            load_buildings_instances as _load_bi,
                            write_buildings_instances as _write_bi,
                        )
                        data = _load_bi()
                        changed = False
                        out = []
                        for e in data or []:
                            try:
                                if int(e.get('id')) == int(sel_bid):
                                    changed = True
                                    continue
                            except (TypeError, ValueError):
                                pass
                            out.append(e)
                        if changed:
                            _write_bi(out)
                        # 2) Remove any visuals refs in spawners_instances.json pointing to this building id
                        try:
                            from roguelike_editors.spawner.services.persistence import remove_visual_refs_by_building_id as _rm_vis
                            _rm_vis(int(sel_bid))
                        except (ImportError, OSError, TypeError, ValueError):
                            logger.debug("handle_event: failed to remove visuals refs for building id", exc_info=True)
                        # 3) Remove from live world/entities using existing helper
                        try:
                            if ip and hasattr(ip, 'visuals') and hasattr(ip.visuals, '_remove_building_entity_by_id'):
                                ip.visuals._remove_building_entity_by_id(int(sel_bid))
                        except (AttributeError, TypeError, ValueError):
                            logger.debug("handle_event: failed to remove building entity from live world", exc_info=True)
                        # 4) Clear selection
                        try:
                            if ip and hasattr(ip, 'visuals') and hasattr(ip.visuals, 'model'):
                                ip.visuals.model.selected_building_id = None
                        except AttributeError:
                            logger.debug("handle_event: failed to clear selected_building_id", exc_info=True)
                        return True
                    except (AttributeError, OSError, TypeError, ValueError):
                        logger.debug("handle_event: delete selected building flow failed", exc_info=True)
            # 0b) If no building selected yet, prioritize selecting a building under cursor before spawner anchor
            if sel_bid is None and ip is not None and hasattr(ip, 'visuals'):
                try:
                    ob = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
                except Exception:
                    ob = None
                if ob is not None:
                    # Only allow selecting if visible and matches selected spawner instance
                    try:
                        hidden = bool(getattr(ob, 'editor_hidden', False))
                    except Exception:
                        hidden = False
                    same_instance = True
                    try:
                        sel_inst = getattr(getattr(ip, 'model', None), 'selected_instance', None)
                        sel_sid = str(sel_inst.get('id')) if isinstance(sel_inst, dict) and sel_inst.get('id') is not None else None
                        ob_sid = str(getattr(ob, 'spawner_instance_id', getattr(ob, 'spawn_id', '')))
                        if sel_sid is not None:
                            same_instance = (ob_sid == sel_sid)
                    except Exception:
                        same_instance = True
                    if (not hidden) and same_instance:
                        try:
                            bid = getattr(ob, 'id', None)
                            if bid is not None:
                                ip.visuals.model.selected_building_id = int(bid)
                                logger.debug("[SpawnerEditor] LMB selected building via early-pick: bid=%s", bid)
                                return True
                        except Exception:
                            pass

            # 1) Spawner anchor selection (only if not clicking a handle)
            try:
                eid = pick_spawner_under_cursor(world, camera, int(mx), int(my))
                if eid is not None:
                    try:
                        self.model.selected_eid = eid
                    except Exception:
                        pass
                    try:
                        if hasattr(world, 'state'):
                            setattr(world.state, 'spawner_selected_eid', eid)
                    except Exception:
                        pass
                    # Also clear any selected building when selecting a spawner
                    try:
                        if ip is not None and hasattr(ip, 'visuals') and hasattr(ip.visuals, 'model'):
                            ip.visuals.model.selected_building_id = None
                    except Exception:
                        pass
                    # Priority: consume event so buildings do not receive hover/click
                    return True
            except Exception:
                logger.debug("handle_event: spawner anchor selection failed", exc_info=True)
            # If not clicking a spawner anchor, clear spawner selection (lose focus)
            try:
                if getattr(self.model, 'selected_eid', None) is not None:
                    self.model.selected_eid = None
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_selected_eid', None)
            except Exception:
                pass
            # Else: selection under cursor (LMB-only selection)
            try:
                if ip is not None and hasattr(ip, 'visuals'):
                    ob = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
                    if ob is not None:
                        # Only allow selecting if visible in editor (not hidden) and belongs to the selected spawner instance
                        try:
                            hidden = bool(getattr(ob, 'editor_hidden', False))
                        except Exception:
                            hidden = False
                        same_instance = True
                        try:
                            sel_inst = getattr(getattr(ip, 'model', None), 'selected_instance', None)
                            sel_sid = str(sel_inst.get('id')) if isinstance(sel_inst, dict) and sel_inst.get('id') is not None else None
                            ob_sid = str(getattr(ob, 'spawner_instance_id', getattr(ob, 'spawn_id', '')))
                            if sel_sid is not None:
                                same_instance = (ob_sid == sel_sid)
                        except Exception:
                            same_instance = True
                        if (not hidden) and same_instance:
                            bid = getattr(ob, 'id', None)
                            if bid is not None:
                                ip.visuals.model.selected_building_id = int(bid)
                                return True
                        # If hidden or mismatched instance, do not change selection
                    else:
                        # Clicked empty space (not spawner, not building): clear building selection
                        try:
                            ip.visuals.model.selected_building_id = None
                        except Exception:
                            pass
            except (AttributeError, TypeError, ValueError):
                logger.debug("handle_event: failed picking building under cursor for selection", exc_info=True)

        # RMB handling: give spawner anchor priority over buildings as well
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            try:
                mx, my = event.pos
                eid = pick_spawner_under_cursor(world, camera, int(mx), int(my))
                if eid is not None:
                    try:
                        self.model.selected_eid = eid
                    except Exception:
                        pass
                    try:
                        if hasattr(world, 'state'):
                            setattr(world.state, 'spawner_selected_eid', eid)
                    except Exception:
                        pass
                    # Clear building selection when selecting a spawner with RMB too
                    try:
                        ip = getattr(self.controller, 'instance_properties', None)
                        if ip is not None and hasattr(ip, 'visuals') and hasattr(ip.visuals, 'model'):
                            ip.visuals.model.selected_building_id = None
                    except Exception:
                        pass
                    # Begin anchor drag on spawner center (RMB)
                    try:
                        self.model.dragging = True
                        self.model.dragging_eid = eid
                        if hasattr(world, 'state'):
                            setattr(world.state, 'spawner_input_suppressed', True)
                    except Exception:
                        logger.debug("handle_event: failed to start spawner anchor drag", exc_info=True)
                    # Capture snapshot for persistence at drop
                    try:
                        cfg = world.components['SpawnerConfig'][eid]
                        zone = getattr(cfg, 'zone', None)
                        tx, ty = cfg.anchor_tile
                        # compute local from zone
                        orig_zone = zone
                        off = (0, 0)
                        try:
                            from roguelike_engine.config.map_config import global_map_settings as _gms
                            off = _gms.zone_offsets.get(zone, (0, 0)) if zone else (0, 0)
                        except Exception:
                            off = (0, 0)
                        local = (int(tx - off[0]), int(ty - off[1]))
                        # Try to resolve id/overrides from JSON
                        inst_list, idx_found, overrides = find_instance_in_json(str(getattr(cfg, 'template_id', '')), str(zone), tuple(local))
                        inst_id = None
                        try:
                            if idx_found is not None:
                                inst_id = inst_list[idx_found].get('id')
                        except Exception:
                            inst_id = None
                        self._drag_start_entry = {
                            'id': inst_id,
                            'zone': zone,
                            'orig_zone': orig_zone,
                            'local_tile': local,
                            'orig_local': local,
                            'overrides': overrides if isinstance(overrides, dict) else None,
                        }
                    except Exception:
                        logger.debug("handle_event: failed to capture drag snapshot", exc_info=True)
                    # Consume RMB to avoid interacting with building handles when clicking spawner anchor
                    return True
            except Exception:
                logger.debug("handle_event: spawner anchor RMB selection failed", exc_info=True)
            # Not clicking a spawner anchor: clear spawner selection before other interactions
            try:
                if getattr(self.model, 'selected_eid', None) is not None:
                    self.model.selected_eid = None
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_selected_eid', None)
            except Exception:
                pass
            if getattr(self.model, 'remove_mode_active', False) or getattr(self.model, 'placing_template_id', None):
                return False
            view = getattr(self.controller, 'view', None)
            ip = getattr(self.controller, 'instance_properties', None)
            mx, my = event.pos
            # Before split/anchor drag: begin moving a visual if clicked over it
            try:
                if ip is not None and hasattr(ip, 'visuals'):
                    ob = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
                else:
                    ob = None
            except Exception:
                ob = None
            if ob is not None and getattr(ip.visuals.model, 'selected_building_id', None) is not None:
                try:
                    # Require that the building under cursor is the SAME as the one already selected
                    # Do NOT auto-select on RMB; only allow moving previously selected and visible visuals
                    sel_bid = None
                    try:
                        sel_bid = int(getattr(ip.visuals.model, 'selected_building_id') or -1)
                    except Exception:
                        sel_bid = None
                    bid = None
                    try:
                        bid = int(getattr(ob, 'id'))
                    except Exception:
                        bid = None
                    if sel_bid is not None and bid is not None and int(sel_bid) == int(bid):
                        # Check not hidden in editor (visibility toggled from State-Instancia-Template table)
                        hidden = False
                        try:
                            hidden = bool(getattr(ob, 'editor_hidden', False))
                        except Exception:
                            hidden = False
                        # Ensure it belongs to the currently selected spawner instance (when one is selected)
                        same_instance = True
                        try:
                            sel_inst = getattr(getattr(ip, 'model', None), 'selected_instance', None)
                            sel_sid = str(sel_inst.get('id')) if isinstance(sel_inst, dict) and sel_inst.get('id') is not None else None
                            ob_sid = str(getattr(ob, 'spawner_instance_id', getattr(ob, 'spawn_id', '')))
                            if sel_sid is not None:
                                same_instance = (ob_sid == sel_sid)
                        except Exception:
                            same_instance = True
                        if (not hidden) and same_instance:
                            # Begin move
                            self.model.moving_visual = True
                            self.model.moving_visual_bid = bid
                            # Mark object to prevent runtime override during drag
                            try:
                                setattr(ob, '_spawner_visual_dragging', True)
                            except Exception:
                                pass
                            # Suppress gameplay input while moving visual
                            try:
                                if hasattr(world, 'state'):
                                    setattr(world.state, 'spawner_input_suppressed', True)
                            except Exception:
                                pass
                            # Capture mouse-to-object delta in world px
                            try:
                                z = getattr(camera, 'zoom', 1.0) or 1.0
                                wx = int(mx / z + camera.offset_x)
                                wy = int(my / z + camera.offset_y)
                                self._moving_visual_delta_world = (int(ob.x) - wx, int(ob.y) - wy)
                            except Exception:
                                self._moving_visual_delta_world = (0, 0)
                            return True
                        # Else: either hidden or does not belong to selected spawner; do not start move
                    # If clicked a different building than selected, do not auto-select or move on RMB
                except Exception:
                    logger.debug("handle_event: failed to evaluate moving visual guards", exc_info=True)
            split_rect = getattr(view, '_last_split_handle_rect', None) if view is not None else None
            rst_rect = getattr(view, '_last_selected_reset_rect', None) if view is not None else None
            # 1) Split handle: begin split drag
            if split_rect is not None and pygame.Rect(split_rect).collidepoint(mx, my):
                sel_bid = None
                try:
                    vmodel = getattr(getattr(ip, 'visuals', None), 'model', None) if ip else None
                    sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
                except (AttributeError, TypeError):
                    sel_bid = None
                target_bid = sel_bid
                try:
                    if target_bid is None and ip is not None and hasattr(ip, 'visuals'):
                        ob_pick = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
                        if ob_pick is not None and getattr(ob_pick, 'id', None) is not None:
                            target_bid = int(getattr(ob_pick, 'id'))
                            try:
                                ip.visuals.model.selected_building_id = int(target_bid)
                            except (AttributeError, TypeError, ValueError):
                                logger.debug("handle_event: failed to set selected_building_id during split-start", exc_info=True)
                except (AttributeError, TypeError, ValueError):
                    logger.debug("handle_event: error while determining target_bid for split drag", exc_info=True)
                if target_bid is not None:
                    if split.begin_split_drag(ctx, int(target_bid), event):
                        return True
            # 2) Otherwise: start anchor drag for currently selected building's spawner
            try:
                ip = getattr(self.controller, 'instance_properties', None)
                vmodel = getattr(getattr(ip, 'visuals', None), 'model', None) if ip else None
                sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
            except (AttributeError, TypeError):
                sel_bid = None
            if sel_bid is not None:
                # Resolve world building and spawner eid
                world_ob = None
                try:
                    world_ob = ip.visuals._find_building_entity_by_id(int(sel_bid)) if ip and hasattr(ip, 'visuals') else None
                except Exception:
                    world_ob = None
                if world_ob is None:
                    from .utils import find_building_in_world_by_id
                    world_ob = find_building_in_world_by_id(ctx.world, int(sel_bid))
                sp_eid = getattr(world_ob, '_spawner_eid', None) if world_ob is not None else None
                if sp_eid is not None:
                    try:
                        self.model.dragging = True
                        self.model.dragging_eid = sp_eid
                        if hasattr(ctx.world, 'state'):
                            setattr(ctx.world.state, 'spawner_input_suppressed', True)
                    except AttributeError:
                        logger.debug("handle_event: failed to start anchor drag (set flags)", exc_info=True)
                    # Capture snapshot for persistence when starting drag via building-selected path
                    try:
                        cfg = world.components['SpawnerConfig'][sp_eid]
                        zone = getattr(cfg, 'zone', None)
                        tx, ty = cfg.anchor_tile
                        from roguelike_engine.config.map_config import global_map_settings as _gms
                        off = _gms.zone_offsets.get(zone, (0, 0)) if zone else (0, 0)
                        local = (int(tx - off[0]), int(ty - off[1]))
                        inst_list, idx_found, overrides = find_instance_in_json(str(getattr(cfg, 'template_id', '')), str(zone), tuple(local))
                        inst_id = None
                        try:
                            if idx_found is not None:
                                inst_id = inst_list[idx_found].get('id')
                        except Exception:
                            inst_id = None
                        self._drag_start_entry = {
                            'id': inst_id,
                            'zone': zone,
                            'orig_zone': zone,
                            'local_tile': local,
                            'orig_local': local,
                            'overrides': overrides if isinstance(overrides, dict) else None,
                        }
                    except Exception:
                        logger.debug("handle_event: failed to capture drag snapshot (building path)", exc_info=True)
                    return True

        # Spawner anchor drag MOTION
        if event.type == pygame.MOUSEMOTION and getattr(self.model, 'dragging', False) and getattr(self.model, 'dragging_eid', None) is not None:
            if anchor.update_anchor_drag_motion(ctx, event):
                return True
        # Visual building move MOTION (RMB drag)
        if event.type == pygame.MOUSEMOTION and getattr(self.model, 'moving_visual', False) and getattr(self.model, 'moving_visual_bid', None) is not None:
            try:
                ip = getattr(self.controller, 'instance_properties', None)
                ob = None
                bid = int(self.model.moving_visual_bid)
                if ip is not None and hasattr(ip, 'visuals'):
                    try:
                        ob = ip.visuals._find_building_entity_by_id(bid)
                    except Exception:
                        ob = None
                if ob is not None:
                    # Compute new world top-left from mouse + delta
                    mx, my = event.pos
                    z = getattr(camera, 'zoom', 1.0) or 1.0
                    wx = int(mx / z + camera.offset_x)
                    wy = int(my / z + camera.offset_y)
                    dx, dy = self._moving_visual_delta_world or (0, 0)
                    world_x = int(wx + dx)
                    world_y = int(wy + dy)
                    # Convert to zone-relative px for rel_x/rel_y
                    zone = getattr(ob, 'zone', None)
                    if zone is None:
                        try:
                            zone = getattr(getattr(ob, 'model', ob), 'zone', None)
                        except Exception:
                            zone = None
                    if not zone:
                        zone = 'lobby'
                    try:
                        from roguelike_engine.config.map_config import global_map_settings as _gms
                        off_x, off_y = _gms.zone_offsets.get(str(zone), (0, 0))
                    except Exception:
                        off_x, off_y = (0, 0)
                    rel_x = int(world_x - int(off_x) * TILE_SIZE)
                    rel_y = int(world_y - int(off_y) * TILE_SIZE)
                    try:
                        setattr(ob, 'rel_x', rel_x)
                        setattr(ob, 'rel_y', rel_y)
                    except Exception:
                        pass
            except Exception:
                logger.debug("handle_event: moving visual motion failed", exc_info=True)
            return True

        # LMB up: finish resize
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1 and getattr(self.model, 'resizing_visual', False):
            if rz.finish_resize(ctx, event):
                return True

        # Confirmations
        if event.type == pygame.KEYDOWN and getattr(self.model, 'pending_zone_confirm', None):
            if conf.handle_zone_confirm(ctx, event):
                return True
        if event.type == pygame.KEYDOWN and getattr(self.model, 'pending_delete_confirm', None):
            if conf.handle_delete_confirm(ctx, event):
                return True

        return False

    # Local helper (kept here; small and specific) ---------------------------
    def _reset_selected_building_size(self, sel_bid: Optional[int]) -> bool:
        if sel_bid is None:
            return False
        try:
            ip = getattr(self.controller, 'instance_properties', None)
            ob = None
            try:
                if ip is not None and hasattr(ip, 'visuals'):
                    ob = ip.visuals._find_building_entity_by_id(int(sel_bid))
            except (AttributeError, TypeError, ValueError):
                ob = None
            if ob is not None:
                try:
                    ob.reset_to_original_size()
                except AttributeError:
                    logger.debug("_reset_selected_building_size: failed to reset entity size", exc_info=True)
                # Persist: drop overrides.scale
                try:
                    data = svc_load_buildings_instances()
                except OSError:
                    data = []
                changed = False
                for e in data or []:
                    try:
                        if int(e.get('id')) != int(sel_bid):
                            continue
                    except (TypeError, ValueError):
                        continue
                    ov = e.get('overrides') or {}
                    if isinstance(ov, dict) and 'scale' in ov:
                        try:
                            ov.pop('scale', None)
                            if not ov:
                                try:
                                    e.pop('overrides', None)
                                except KeyError:
                                    logger.debug("_reset_selected_building_size: failed to pop overrides; setting empty dict", exc_info=True)
                                    e['overrides'] = {}
                            else:
                                e['overrides'] = ov
                            changed = True
                        except (AttributeError, KeyError, TypeError):
                            logger.debug("_reset_selected_building_size: failed updating overrides dict", exc_info=True)
                    break
                if changed:
                    try:
                        svc_write_buildings_instances(data)
                    except OSError:
                        logger.debug("_reset_selected_building_size: failed persisting buildings_instances after reset", exc_info=True)
                # Also remove any per-visuals scale stored under spawners_instances.json for this selected instance
                try:
                    from roguelike_editors.spawner.services.persistence import load_instances_json as _sp_load, write_instances_json as _sp_write
                except Exception:
                    _sp_load = _sp_write = None
                try:
                    if _sp_load is not None and _sp_write is not None:
                        inst_list = _sp_load()
                        changed_vis = False
                        # Identify currently selected spawner instance id
                        sel_inst = getattr(getattr(self.controller.instance_properties, 'model', None), 'selected_instance', None)
                        target_id = str(sel_inst.get('id')) if isinstance(sel_inst, dict) and sel_inst.get('id') is not None else None
                        if target_id is not None:
                            for inst in inst_list or []:
                                try:
                                    if str(inst.get('id')) != target_id:
                                        continue
                                    vis = inst.get('visuals') if isinstance(inst.get('visuals'), dict) else {}
                                    # Iterate all state mappings and remove 'scale' for entries pointing to this building id
                                    for k, v in list(vis.items()):
                                        try:
                                            if isinstance(v, dict):
                                                vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                                            else:
                                                vid = int(v)
                                        except Exception:
                                            vid = None
                                        if vid is not None and int(vid) == int(sel_bid):
                                            if isinstance(v, dict) and 'scale' in v:
                                                vv = dict(v)
                                                try:
                                                    vv.pop('scale', None)
                                                except Exception:
                                                    pass
                                                vis[k] = vv
                                                inst['visuals'] = vis
                                                changed_vis = True
                                    break
                                except Exception:
                                    continue
                            if changed_vis:
                                try:
                                    _sp_write(inst_list)
                                except OSError:
                                    logger.debug("_reset_selected_building_size: failed persisting spawners_instances visuals after reset", exc_info=True)
                        # Update in-memory mapping as well
                        try:
                            if isinstance(getattr(self.controller.instance_properties.model, 'visuals', None), dict):
                                vm = dict(self.controller.instance_properties.model.visuals)
                                for k, v in list(vm.items()):
                                    try:
                                        if isinstance(v, dict):
                                            vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                                        else:
                                            vid = int(v)
                                    except Exception:
                                        vid = None
                                    if vid is not None and int(vid) == int(sel_bid) and isinstance(v, dict) and 'scale' in v:
                                        vv = dict(v)
                                        try:
                                            vv.pop('scale', None)
                                        except Exception:
                                            pass
                                        vm[k] = vv
                                self.controller.instance_properties.model.visuals = vm
                                if isinstance(self.controller.instance_properties.model.selected_instance, dict):
                                    self.controller.instance_properties.model.selected_instance['visuals'] = vm
                        except Exception:
                            logger.debug("_reset_selected_building_size: failed updating in-memory visuals map after reset", exc_info=True)
                except Exception:
                    logger.debug("_reset_selected_building_size: error while clearing visuals scale", exc_info=True)
                return True
        except (AttributeError, TypeError, ValueError):
            logger.debug("_reset_selected_building_size: unexpected error", exc_info=True)
        return False
