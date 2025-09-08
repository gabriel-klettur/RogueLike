import pygame
from logging import getLogger
import logging
from roguelike_engine.config.config_camera import ALLOWED_ZOOMS, next_allowed_zoom

logger = getLogger(__name__)

def handle_mouse(event, state, camera, clock, map, entities, *, mmb_pan_enabled: bool = False):
    
    if event.type == pygame.MOUSEWHEEL:
        try:
            mx, my = pygame.mouse.get_pos()
            z = float(getattr(camera, 'zoom', 1.0)) or 1.0
            # World coords under cursor before zoom
            wx = mx / z + float(getattr(camera, 'offset_x', 0.0) or 0.0)
            wy = my / z + float(getattr(camera, 'offset_y', 0.0) or 0.0)
            allowed = ALLOWED_ZOOMS
            new_z = next_allowed_zoom(z, +1, allowed) if event.y > 0 else next_allowed_zoom(z, -1, allowed)
            if abs(new_z - z) > 1e-9:
                camera.zoom = new_z
                # Keep stable world point under cursor
                camera.offset_x = wx - mx / camera.zoom
                camera.offset_y = wy - my / camera.zoom
                logger.debug("[Mouse] Zoom wheel z=%.3f -> %.3f at (%d,%d) -> cam_off=(%.2f,%.2f)", z, new_z, mx, my, camera.offset_x, camera.offset_y)
        except Exception:
            # Silent fail to avoid crashing input loop
            pass
    elif event.type == pygame.MOUSEBUTTONDOWN:
        if event.button == 3:
            # Right-click dash handled by ECS InputSystem; no legacy action
            pass
        elif event.button == 2:
            # Begin MMB camera panning
            if mmb_pan_enabled:
                try:
                    state.mmb_panning = True
                    state.mmb_start = getattr(event, 'pos', pygame.mouse.get_pos())
                    state.mmb_cam_start = (camera.offset_x, camera.offset_y)
                    logger.debug("[Mouse] MMB DOWN start pan at pos=%s cam_start=(%.2f,%.2f)", state.mmb_start, camera.offset_x, camera.offset_y)
                except Exception:
                    # Fallbacks if state/camera are not fully formed
                    pass
    elif event.type == pygame.MOUSEMOTION:
        # If context no longer allows MMB panning, cancel immediately
        if getattr(state, 'mmb_panning', False) and not mmb_pan_enabled:
            try:
                state.mmb_panning = False
                logger.debug("[Mouse] MMB MOVE cancel pan (context disabled)")
            except Exception:
                pass
        # Update camera while panning with MMB
        if getattr(state, 'mmb_panning', False):
            try:
                mx, my = getattr(event, 'pos', pygame.mouse.get_pos())
                sx, sy = getattr(state, 'mmb_start', (mx, my))
                cx, cy = getattr(state, 'mmb_cam_start', (camera.offset_x, camera.offset_y))
                z = getattr(camera, 'zoom', 1.0) or 1.0
                dx = mx - sx
                dy = my - sy
                camera.offset_x = cx - dx / z
                camera.offset_y = cy - dy / z
                logger.debug("[Mouse] MMB MOVE pos=(%d,%d) dx=%.1f dy=%.1f zoom=%.2f -> cam=(%.2f,%.2f)", mx, my, dx, dy, z, camera.offset_x, camera.offset_y)
            except Exception:
                pass
    elif event.type == pygame.MOUSEBUTTONUP:
        if event.button == 2:
            # End MMB camera panning
            if getattr(state, 'mmb_panning', False):
                try:
                    state.mmb_panning = False
                    logger.debug("[Mouse] MMB UP end pan")
                    # Defer camera follow a few frames to avoid instant recenter after release
                    try:
                        # Only apply when panning was enabled in this context
                        if mmb_pan_enabled:
                            # keep the max in case another system already set a larger defer
                            current = int(getattr(state, 'defer_follow_frames', 0) or 0)
                            state.defer_follow_frames = max(current, 12)
                    except Exception:
                        pass
                except Exception:
                    pass