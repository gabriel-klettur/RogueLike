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

        # Shared tools adapters (reuse Buildings Editor logic)
        try:
            self._split_adapter = SimpleNamespace(split_dragging=False, selected_building=None)
            self._split_tool = SplitTool(None, self._split_adapter)
        except Exception:
            self._split_adapter = SimpleNamespace(split_dragging=False, selected_building=None)
            self._split_tool = None
        try:
            self._z_adapter = SimpleNamespace(active_building=None)
            _z_state = getattr(controller, 'z_state', None)
            if _z_state is None or not hasattr(_z_state, 'set'):
                _z_state = SimpleNamespace(set=lambda *args, **kwargs: None)
            self._z_tool_bottom = ZTool(SimpleNamespace(z_state=_z_state), self._z_adapter, target="bottom")
            self._z_tool_top = ZTool(SimpleNamespace(z_state=_z_state), self._z_adapter, target="top")
        except Exception:
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
            except Exception:
                pass
            try:
                self.model.split_drag_active = False
                self.model.split_drag_bid = None
            except Exception:
                pass
            self.panning = False
            self.info_dragging = False
            self._drag_start_entry = None
            try:
                if world and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_editor_hovered_eid', None)
                    setattr(world.state, 'spawner_input_suppressed', False)
                    setattr(world.state, 'spawner_editor_active', False)
            except Exception:
                pass
            # Clear split propagation key to avoid stale propagation next time
            try:
                setattr(self.model, '_split_propagation_key', None)
            except Exception:
                pass
        else:
            # Mark editor as active globally
            try:
                if world and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_editor_active', True)
            except Exception:
                pass

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
                except Exception:
                    handled = False
                return True if handled or event.type in (
                    pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION,
                    pygame.MOUSEWHEEL, pygame.KEYDOWN, pygame.KEYUP
                ) else False
        except Exception:
            pass

        # Split drag END on any mouse button up
        if event.type == pygame.MOUSEBUTTONUP and getattr(self.model, 'split_drag_active', False):
            if split.end_split_drag(ctx, event):
                return True
        # RMB up: stop anchor drag if active
        if event.type == pygame.MOUSEBUTTONUP and event.button == 3 and getattr(self.model, 'dragging', False):
            # Stop dragging the spawner anchor and re-enable input
            self.model.dragging = False
            self.model.dragging_eid = None
            try:
                if hasattr(ctx.world, 'state'):
                    setattr(ctx.world.state, 'spawner_input_suppressed', False)
            except Exception:
                pass
            return True
        # Split drag MOTION while active
        if event.type == pygame.MOUSEMOTION and getattr(self.model, 'split_drag_active', False) and getattr(self.model, 'split_drag_bid', None) is not None:
            if split.update_split_drag(ctx, event):
                return True

        # Resize MOTION while active
        if event.type == pygame.MOUSEMOTION and getattr(self.model, 'resizing_visual', False):
            if rz.update_resize_motion(ctx, event):
                return True

        # LMB handling: overlay buttons (Default/Resize/Remove) and selection
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            ip = getattr(self.controller, 'instance_properties', None)
            mx, my = event.pos
            # Compute handle rects for the currently selected building (like Building Editor)
            sel_bid = None
            try:
                vmodel = getattr(getattr(ip, 'model', None), 'visuals', None) if ip else None
                sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
            except Exception:
                sel_bid = None
            world_ob = None
            if sel_bid is not None:
                try:
                    world_ob = ip.visuals._find_building_entity_by_id(int(sel_bid)) if ip and hasattr(ip, 'visuals') else None
                except Exception:
                    world_ob = None
                if world_ob is None:
                    from .utils import find_building_in_world_by_id
                    world_ob = find_building_in_world_by_id(ctx.world, int(sel_bid))
            if world_ob is not None:
                from .utils import compute_spawner_handle_rects
                rects = compute_spawner_handle_rects(ctx.camera, world_ob)
                del_rect = rects.get('delete')
                rst_rect = rects.get('reset')
                rz_rect = rects.get('resize')
                # Default (reset size)
                if rst_rect is not None and rst_rect.collidepoint(mx, my):
                    if self._reset_selected_building_size(sel_bid):
                        return True
                # Resize: begin resize mode for selected building
                if rz_rect is not None and rz_rect.collidepoint(mx, my):
                    if rz.start_resize(ctx, event):
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
                            except Exception:
                                pass
                            out.append(e)
                        if changed:
                            _write_bi(out)
                        # 2) Remove any visuals refs in spawners_instances.json pointing to this building id
                        try:
                            from roguelike_editors.spawner.services.persistence import remove_visual_refs_by_building_id as _rm_vis
                            _rm_vis(int(sel_bid))
                        except Exception:
                            pass
                        # 3) Remove from live world/entities using existing helper
                        try:
                            if ip and hasattr(ip, 'visuals') and hasattr(ip.visuals, '_remove_building_entity_by_id'):
                                ip.visuals._remove_building_entity_by_id(int(sel_bid))
                        except Exception:
                            pass
                        # 4) Clear selection
                        try:
                            if ip and hasattr(ip, 'visuals') and hasattr(ip.visuals, 'model'):
                                ip.visuals.model.selected_building_id = None
                        except Exception:
                            pass
                        return True
                    except Exception:
                        pass
            # Else: selection under cursor (LMB-only selection)
            try:
                if ip is not None and hasattr(ip, 'visuals'):
                    ob = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
                    if ob is not None:
                        bid = getattr(ob, 'id', None)
                        if bid is not None:
                            ip.visuals.model.selected_building_id = int(bid)
                            return True
            except Exception:
                pass

        # RMB handling: split drag start, reset size, or selection
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            if getattr(self.model, 'remove_mode_active', False) or getattr(self.model, 'placing_template_id', None):
                return False
            view = getattr(self.controller, 'view', None)
            ip = getattr(self.controller, 'instance_properties', None)
            mx, my = event.pos
            split_rect = getattr(view, '_last_split_handle_rect', None) if view is not None else None
            rst_rect = getattr(view, '_last_selected_reset_rect', None) if view is not None else None
            # 1) Split handle: begin split drag
            if split_rect is not None and pygame.Rect(split_rect).collidepoint(mx, my):
                sel_bid = None
                try:
                    vmodel = getattr(getattr(ip, 'model', None), 'visuals', None) if ip else None
                    sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
                except Exception:
                    sel_bid = None
                target_bid = sel_bid
                try:
                    if target_bid is None and ip is not None and hasattr(ip, 'visuals'):
                        ob_pick = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
                        if ob_pick is not None and getattr(ob_pick, 'id', None) is not None:
                            target_bid = int(getattr(ob_pick, 'id'))
                            try:
                                ip.visuals.model.selected_building_id = int(target_bid)
                            except Exception:
                                pass
                except Exception:
                    pass
                if target_bid is not None:
                    if split.begin_split_drag(ctx, int(target_bid), event):
                        return True
            # 2) Otherwise: start anchor drag for currently selected building's spawner
            try:
                ip = getattr(self.controller, 'instance_properties', None)
                vmodel = getattr(getattr(ip, 'model', None), 'visuals', None) if ip else None
                sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
            except Exception:
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
                    except Exception:
                        pass
                    return True

        # Spawner anchor drag MOTION
        if event.type == pygame.MOUSEMOTION and getattr(self.model, 'dragging', False) and getattr(self.model, 'dragging_eid', None) is not None:
            if anchor.update_anchor_drag_motion(ctx, event):
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
            except Exception:
                ob = None
            if ob is not None:
                try:
                    ob.reset_to_original_size()
                except Exception:
                    pass
                # Persist: drop overrides.scale
                try:
                    data = svc_load_buildings_instances()
                except Exception:
                    data = []
                changed = False
                for e in data or []:
                    try:
                        if int(e.get('id')) != int(sel_bid):
                            continue
                    except Exception:
                        continue
                    ov = e.get('overrides') or {}
                    if isinstance(ov, dict) and 'scale' in ov:
                        try:
                            ov.pop('scale', None)
                            if not ov:
                                try:
                                    e.pop('overrides', None)
                                except Exception:
                                    e['overrides'] = {}
                            else:
                                e['overrides'] = ov
                            changed = True
                        except Exception:
                            pass
                    break
                if changed:
                    try:
                        svc_write_buildings_instances(data)
                    except Exception:
                        pass
                return True
        except Exception:
            pass
        return False
