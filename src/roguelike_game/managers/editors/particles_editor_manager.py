import pygame
from roguelike_editors.particles.particles_controller import ParticlesEditorController
from roguelike_editors.particles.services.instances_service import load_particles_instances
from roguelike_editors.particles.services.instances_service import find_nearest_instance as _find_nearest_instance
from roguelike_editors.particles.services.preview_builder import build_preview_for_definition
from roguelike_game.config.particles_config import get_preset
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

import logging
logger = logging.getLogger(__name__)

class ParticlesEditorManager:
    """
    Manager for the Particles Editor: builds controller and exposes its model.
    """
    def __init__(self, game):
        self.game = game
        font = getattr(game, 'font', None)
        self.controller = ParticlesEditorController(font)
        # Provide game reference to controller for camera/world conversions in panel events
        try:
            self.controller.game = game
        except Exception:
            pass
        self.model = self.controller.model
        # Expose state globally if needed in future
        try:
            game.state.particles_editor_state = self.model
        except Exception:
            pass
        # Timestep for drag preview animation
        self._last_ticks = 0
        # Local cache of ad-hoc preview providers if picker providers are not built
        self._local_preview_cache: dict[str, object] = {}

    def handle_event(self, event: pygame.event.Event) -> None:
        self.controller.handle_event(event)

    def draw(self, screen: pygame.Surface) -> None:
        self.controller.draw(screen)
        # Animated drag preview identical to picker
        try:
            model = getattr(self, 'model', None)
            if model and getattr(model, 'visible', False):
                picker = getattr(self.controller, 'particles_picker_controller', None)
                providers = getattr(getattr(picker, 'model', None), 'preview_providers', {}) if picker else {}
                cell_size = None
                try:
                    # Use the same provider size as picker cells (minus padding as picker does)
                    cell_size = max(8, int(picker.model.cell_size) - 8)
                except Exception:
                    cell_size = 56
                now = pygame.time.get_ticks()
                dt_ms = 16 if self._last_ticks == 0 else max(0, min(48, now - self._last_ticks))
                self._last_ticks = now
                # Helper to draw provider centered at screen position
                def _draw_provider_at(pid: str, sx: int, sy: int):
                    if not isinstance(pid, str):
                        return
                    prov = providers.get(pid)
                    # Fallback: build ad-hoc provider from preset if picker providers unavailable
                    if prov is None:
                        try:
                            if pid in self._local_preview_cache:
                                prov = self._local_preview_cache.get(pid)
                            else:
                                p = get_preset(pid)
                                if p is not None:
                                    defn = {
                                        "id": getattr(p, "id", pid),
                                        "name": getattr(p, "name", pid),
                                        "type": getattr(p, "type", ""),
                                        "vfx": getattr(p, "vfx", {}),
                                    }
                                    obj = build_preview_for_definition(defn)
                                    if obj is not None:
                                        def provider(size, dt_ms):
                                            return obj.render(size, dt_ms)
                                        prov = provider
                                        self._local_preview_cache[pid] = prov
                        except Exception:
                            prov = None
                    if prov is None:
                        return
                    try:
                        surf = prov((cell_size, cell_size), dt_ms)
                        if surf is not None:
                            screen.blit(surf, (int(sx - surf.get_width() // 2), int(sy - surf.get_height() // 2)))
                    except Exception:
                        pass
                # Persisted instances are now drawn by the runtime render system; avoid overlay duplicates
                # Case 1: placing new instance (right-drag in add mode)
                if getattr(model, 'drag_place_active', False) and isinstance(getattr(model, 'drag_pid', None), str):
                    try:
                        mx, my = pygame.mouse.get_pos()
                        _draw_provider_at(model.drag_pid, mx, my)
                    except Exception:
                        pass
                # Case 2: moving existing instance (right-drag move)
                elif getattr(model, 'drag_move_active', False) and getattr(model, 'selected_instance_id', None) is not None:
                    # Resolve preset_id for the selected instance
                    try:
                        sel_id = int(model.selected_instance_id)
                        preset_id = None
                        for e in load_particles_instances() or []:
                            try:
                                if int(e.get('id')) == sel_id:
                                    preset_id = str(e.get('preset_id'))
                                    break
                            except Exception:
                                continue
                        if isinstance(preset_id, str):
                            mx, my = pygame.mouse.get_pos()
                            _draw_provider_at(preset_id, mx, my)
                    except Exception:
                        pass
        except Exception:
            pass
        # Overlay: LALT held -> draw cyan ring around ALL instances (skip selected)
        try:
            model = getattr(self, 'model', None)
            if model and getattr(model, 'visible', False):
                # Do not show during drags to reduce clutter
                if not getattr(model, 'drag_place_active', False) and not getattr(model, 'drag_move_active', False):
                    mods = 0
                    try:
                        mods = pygame.key.get_mods()
                    except Exception:
                        mods = 0
                    if mods & pygame.KMOD_LALT:
                        sel_id = getattr(model, 'selected_instance_id', None)
                        cam = getattr(self.game, 'camera', None)
                        zoom = float(getattr(cam, 'zoom', 1.0) or 1.0)
                        ox = float(getattr(cam, 'offset_x', 0.0) or 0.0)
                        oy = float(getattr(cam, 'offset_y', 0.0) or 0.0)
                        radius = int(14 * zoom)
                        if radius < 8:
                            radius = 8
                        cyan_a = (0, 255, 255)
                        cyan_b = (0, 200, 200)
                        for e in load_particles_instances() or []:
                            try:
                                if sel_id is not None and int(e.get('id')) == int(sel_id):
                                    continue  # selection has priority (yellow)
                                zone = str(e.get('zone') or 'no zone')
                                rel_x = int(e.get('rel_x') or 0)
                                rel_y = int(e.get('rel_y') or 0)
                                off_tx, off_ty = global_map_settings.zone_offsets.get(zone, (0, 0))
                                wx = int(off_tx) * TILE_SIZE + int(rel_x)
                                wy = int(off_ty) * TILE_SIZE + int(rel_y)
                                sx = int((float(wx) - ox) * zoom)
                                sy = int((float(wy) - oy) * zoom)
                                pygame.draw.circle(screen, cyan_a, (sx, sy), radius + 2, width=2)
                                pygame.draw.circle(screen, cyan_b, (sx, sy), radius, width=2)
                            except Exception:
                                continue
        except Exception:
            pass
        # Overlay: selection highlight for selected instance (ring only; runtime draws the effect)
        try:
            model = getattr(self, 'model', None)
            if model and getattr(model, 'visible', False):
                sel_id = getattr(model, 'selected_instance_id', None)
                if sel_id is not None:
                    # Find entry and compute world coords
                    entry = None
                    for e in load_particles_instances() or []:
                        try:
                            if int(e.get('id')) == int(sel_id):
                                entry = e
                                break
                        except Exception:
                            continue
                    if isinstance(entry, dict):
                        zone = str(entry.get('zone') or 'no zone')
                        rel_x = int(entry.get('rel_x') or 0)
                        rel_y = int(entry.get('rel_y') or 0)
                        off_tx, off_ty = global_map_settings.zone_offsets.get(zone, (0, 0))
                        wx = int(off_tx) * TILE_SIZE + int(rel_x)
                        wy = int(off_ty) * TILE_SIZE + int(rel_y)
                        # World -> screen
                        cam = getattr(self.game, 'camera', None)
                        zoom = float(getattr(cam, 'zoom', 1.0) or 1.0)
                        ox = float(getattr(cam, 'offset_x', 0.0) or 0.0)
                        oy = float(getattr(cam, 'offset_y', 0.0) or 0.0)
                        sx = int((float(wx) - ox) * zoom)
                        sy = int((float(wy) - oy) * zoom)
                        # Draw ring
                        radius = int(14 * zoom)
                        if radius < 8:
                            radius = 8
                        color_a = (255, 255, 0)
                        color_b = (220, 180, 0)
                        try:
                            pygame.draw.circle(screen, color_a, (sx, sy), radius + 2, width=2)
                            pygame.draw.circle(screen, color_b, (sx, sy), radius, width=2)
                        except Exception:
                            pass
        except Exception:
            pass
        # Overlay: hover highlight (cyan borders) for instance under cursor
        try:
            model = getattr(self, 'model', None)
            if not model or not getattr(model, 'visible', False):
                return
            if getattr(model, 'drag_place_active', False) or getattr(model, 'drag_move_active', False):
                return  # no hover highlight while dragging
            # Screen -> world
            mx, my = pygame.mouse.get_pos()
            cam = getattr(self.game, 'camera', None)
            zoom = float(getattr(cam, 'zoom', 1.0) or 1.0)
            ox = float(getattr(cam, 'offset_x', 0.0) or 0.0)
            oy = float(getattr(cam, 'offset_y', 0.0) or 0.0)
            wx = mx / (zoom if zoom != 0 else 1.0) + ox
            wy = my / (zoom if zoom != 0 else 1.0) + oy
            entry = None
            try:
                # Scale search radius with zoom so hover works when zoomed out/in
                max_d = int(48 / (zoom if zoom > 0 else 1.0))
                if max_d < 24:
                    max_d = 24
                if max_d > 128:
                    max_d = 128
                entry = _find_nearest_instance(float(wx), float(wy), max_dist_px=max_d)
            except Exception:
                entry = None
            wpx = None
            wpy = None
            # Prefer persisted entry
            if isinstance(entry, dict):
                # Do not draw cyan hover if this entry is currently selected (selection has priority)
                try:
                    sel_id = getattr(model, 'selected_instance_id', None)
                    if sel_id is not None and int(entry.get('id')) == int(sel_id):
                        return
                except Exception:
                    pass
                # Compute world position of the hovered persisted instance
                zone = str(entry.get('zone') or 'no zone')
                rel_x = int(entry.get('rel_x') or 0)
                rel_y = int(entry.get('rel_y') or 0)
                off_tx, off_ty = global_map_settings.zone_offsets.get(zone, (0, 0))
                wpx = int(off_tx) * TILE_SIZE + int(rel_x)
                wpy = int(off_ty) * TILE_SIZE + int(rel_y)
            else:
                # Fallback: search nearest runtime ECS particle entity
                try:
                    world = getattr(getattr(self.game, 'ecs', None), 'ecs_world', None)
                except Exception:
                    world = None
                if world is not None:
                    try:
                        pos_map = world.components.get('Position', {}) or {}
                        parts = world.components.get('ParticlePresetComponent', {}) or {}
                        best_e = None
                        best_d2 = None
                        for eid, comp in list(parts.items()):
                            pos = pos_map.get(eid)
                            if pos is None:
                                continue
                            dx = float(getattr(pos, 'x', 0.0)) - float(wx)
                            dy = float(getattr(pos, 'y', 0.0)) - float(wy)
                            d2 = dx*dx + dy*dy
                            if best_d2 is None or d2 < best_d2:
                                best_d2 = d2
                                best_e = eid
                        # Threshold using same scaled radius
                        thr = float(max_d * max_d)
                        if best_e is not None and (best_d2 is None or best_d2 <= thr):
                            pos = pos_map.get(best_e)
                            if pos is not None:
                                # Skip if it's the currently selected entry id (if available on component)
                                try:
                                    sel_id = getattr(model, 'selected_instance_id', None)
                                    comp = parts.get(best_e)
                                    if sel_id is not None and comp is not None and int(getattr(comp, 'entry_id', -1)) == int(sel_id):
                                        return
                                except Exception:
                                    pass
                                wpx = float(getattr(pos, 'x', 0.0))
                                wpy = float(getattr(pos, 'y', 0.0))
                    except Exception:
                        pass
            if wpx is None or wpy is None:
                return
            sx = int((float(wpx) - ox) * zoom)
            sy = int((float(wpy) - oy) * zoom)
            # Cyan double ring
            radius = int(14 * zoom)
            if radius < 8:
                radius = 8
            cyan_a = (0, 255, 255)
            cyan_b = (0, 200, 200)
            try:
                pygame.draw.circle(screen, cyan_a, (sx, sy), radius + 2, width=2)
                pygame.draw.circle(screen, cyan_b, (sx, sy), radius, width=2)
            except Exception:
                pass
        except Exception:
            pass
