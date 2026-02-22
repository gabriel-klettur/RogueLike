from __future__ import annotations

import pygame
from .actions import cycle_overlay_preset_color
from roguelike_editors.lighting.services.light_instances_service import delete_instances


def click_ui(ctl, pos: tuple[int, int]) -> bool:
    try:
        from roguelike_engine.rendering.lighting import get_global_lighting
        from roguelike_engine.rendering.lighting.daynight import get_global_daynight
    except Exception:
        return False

    st = ctl.model
    x, y = pos

    if isinstance(getattr(st, "_btn_ambient", None), pygame.Rect) and st._btn_ambient.collidepoint(x, y):
        try:
            dn = get_global_daynight()
            dn.enabled = not dn.enabled
        except Exception:
            pass
        return True

    if isinstance(getattr(st, "_btn_lights", None), pygame.Rect) and st._btn_lights.collidepoint(x, y):
        try:
            lm = get_global_lighting()
            lm.set_enabled(not lm.enabled)
        except Exception:
            pass
        return True

    if isinstance(getattr(st, "_btn_spawn", None), pygame.Rect) and st._btn_spawn.collidepoint(x, y):
        st.spawn_mode = not bool(getattr(st, "spawn_mode", False))
        if st.spawn_mode:
            try:
                lm = get_global_lighting()
                lm.set_enabled(True)
                if not lm.should_render():
                    lm.set_quality("lights_low")
            except Exception:
                pass
        return True

    if isinstance(getattr(st, "_btn_clear", None), pygame.Rect) and st._btn_clear.collidepoint(x, y):
        try:
            get_global_lighting().clear_debug_lights()
        except Exception:
            pass
        return True

    if hasattr(st, "_btn_occlusion") and isinstance(getattr(st, "_btn_occlusion", None), pygame.Rect) and st._btn_occlusion.collidepoint(x, y):
        try:
            lm = get_global_lighting()
            lm.set_tile_occlusion(not lm.tile_occlusion_enabled())
        except Exception:
            pass
        return True

    if hasattr(st, "_btn_shadows") and isinstance(getattr(st, "_btn_shadows", None), pygame.Rect) and st._btn_shadows.collidepoint(x, y):
        try:
            lm = get_global_lighting()
            lm.set_shadow_polygons(not lm.shadow_polygons_enabled())
        except Exception:
            pass
        return True

    if hasattr(st, "_btn_overlay") and isinstance(getattr(st, "_btn_overlay", None), pygame.Rect) and st._btn_overlay.collidepoint(x, y):
        try:
            st.overlay_visible = not bool(getattr(st, "overlay_visible", True))
        except Exception:
            pass
        return True

    if hasattr(st, "_btn_labels") and isinstance(getattr(st, "_btn_labels", None), pygame.Rect) and st._btn_labels.collidepoint(x, y):
        try:
            st.overlay_labels = not bool(getattr(st, "overlay_labels", True))
        except Exception:
            pass
        return True

    if hasattr(st, "_btn_delete_selected") and isinstance(getattr(st, "_btn_delete_selected", None), pygame.Rect) and st._btn_delete_selected.collidepoint(x, y):
        try:
            ids = list(getattr(st, "selected_light_ids", set()) or [])
            if ids:
                delete_instances(ids)
                try:
                    lm = get_global_lighting()
                    for i in ids:
                        lm.remove_by_id(f"persist:{int(i)}")
                except Exception:
                    pass
                try:
                    st.selected_light_ids.clear()
                    st.selected_light_id = None
                except Exception:
                    pass
        except Exception:
            pass
        return True

    if hasattr(st, "_btn_palette_prev") and isinstance(getattr(st, "_btn_palette_prev", None), pygame.Rect) and st._btn_palette_prev.collidepoint(x, y):
        cycle_overlay_preset_color(ctl, prev=True)
        return True
    if hasattr(st, "_btn_palette_next") and isinstance(getattr(st, "_btn_palette_next", None), pygame.Rect) and st._btn_palette_next.collidepoint(x, y):
        cycle_overlay_preset_color(ctl, prev=False)
        return True

    try:
        lm = get_global_lighting()
    except Exception:
        lm = None

    if lm is not None:
        if hasattr(st, "_btn_lrs_minus") and isinstance(getattr(st, "_btn_lrs_minus", None), pygame.Rect) and st._btn_lrs_minus.collidepoint(x, y):
            lm.set_low_res_scale(max(1, lm.current_low_res_scale() - 1)); return True
        if hasattr(st, "_btn_lrs_plus") and isinstance(getattr(st, "_btn_lrs_plus", None), pygame.Rect) and st._btn_lrs_plus.collidepoint(x, y):
            lm.set_low_res_scale(min(8, lm.current_low_res_scale() + 1)); return True
        if hasattr(st, "_btn_ml_minus") and isinstance(getattr(st, "_btn_ml_minus", None), pygame.Rect) and st._btn_ml_minus.collidepoint(x, y):
            lm.set_max_lights(max(0, lm.current_max_lights() - 1)); return True
        if hasattr(st, "_btn_ml_plus") and isinstance(getattr(st, "_btn_ml_plus", None), pygame.Rect) and st._btn_ml_plus.collidepoint(x, y):
            lm.set_max_lights(min(256, lm.current_max_lights() + 1)); return True
        if hasattr(st, "_btn_mr_minus") and isinstance(getattr(st, "_btn_mr_minus", None), pygame.Rect) and st._btn_mr_minus.collidepoint(x, y):
            lm.set_max_radius(max(16, lm.current_max_radius() - 8)); return True
        if hasattr(st, "_btn_mr_plus") and isinstance(getattr(st, "_btn_mr_plus", None), pygame.Rect) and st._btn_mr_plus.collidepoint(x, y):
            lm.set_max_radius(min(2048, lm.current_max_radius() + 8)); return True
        if hasattr(st, "_btn_sh_hero_minus") and isinstance(getattr(st, "_btn_sh_hero_minus", None), pygame.Rect) and st._btn_sh_hero_minus.collidepoint(x, y):
            lm.set_shadow_hero_count(max(0, lm.get_shadow_hero_count() - 1)); return True
        if hasattr(st, "_btn_sh_hero_plus") and isinstance(getattr(st, "_btn_sh_hero_plus", None), pygame.Rect) and st._btn_sh_hero_plus.collidepoint(x, y):
            lm.set_shadow_hero_count(min(2, lm.get_shadow_hero_count() + 1)); return True
        if hasattr(st, "_btn_sh_rays_minus") and isinstance(getattr(st, "_btn_sh_rays_minus", None), pygame.Rect) and st._btn_sh_rays_minus.collidepoint(x, y):
            lm.set_shadow_rays(max(8, lm.get_shadow_rays() - 8)); return True
        if hasattr(st, "_btn_sh_rays_plus") and isinstance(getattr(st, "_btn_sh_rays_plus", None), pygame.Rect) and st._btn_sh_rays_plus.collidepoint(x, y):
            lm.set_shadow_rays(min(256, lm.get_shadow_rays() + 8)); return True

    return False
