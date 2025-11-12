from __future__ import annotations

import math
import random


def spawn_at_screen(ctl, pos: tuple[int, int]) -> None:
    try:
        from roguelike_engine.rendering.lighting import get_global_lighting
        from roguelike_engine.rendering.lighting.light_types import Light
        from roguelike_editors.lighting.services.light_instances_service import append_instance as persist_light_instance
        mx, my = int(pos[0]), int(pos[1])
        cam = getattr(ctl.game, "camera", None)
        if cam is not None:
            z = float(getattr(cam, "zoom", 1.0) or 1.0)
            ox = round(getattr(cam, "offset_x", 0.0) * z) / z
            oy = round(getattr(cam, "offset_y", 0.0) * z) / z
            wx = (mx / z) + ox
            wy = (my / z) + oy
        else:
            wx, wy = float(mx), float(my)
        stp = ctl.presets_state
        lm = get_global_lighting()
        lm.set_enabled(True)
        if not lm.should_render():
            try:
                lm.set_quality("lights_low")
            except Exception:
                pass
        lm.add(
            Light(
                x=wx,
                y=wy,
                radius=int(getattr(stp, "spawn_radius", 160)),
                color=tuple(getattr(stp, "spawn_color", (255, 200, 140))),
                intensity=float(getattr(stp, "spawn_intensity", 1.0)),
                falloff=float(getattr(stp, "spawn_falloff", 2.0)),
                flicker_amp=float(getattr(stp, "spawn_flicker_amp", 0.15)),
                flicker_speed=float(getattr(stp, "spawn_flicker_speed", 2.5)),
                flicker_phase_rad=random.Random().uniform(0.0, 2.0 * math.pi),
                center_scale=float(getattr(stp, "spawn_center_scale", 1.0)),
            )
        )
        try:
            preset_id = str(getattr(stp, "spawn_preset", "Custom"))
            params = {
                "radius": int(getattr(stp, "spawn_radius", 160)),
                "color": tuple(getattr(stp, "spawn_color", (255, 200, 140))),
                "intensity": float(getattr(stp, "spawn_intensity", 1.0)),
                "falloff": float(getattr(stp, "spawn_falloff", 2.0)),
                "flicker_amp": float(getattr(stp, "spawn_flicker_amp", 0.15)),
                "flicker_speed": float(getattr(stp, "spawn_flicker_speed", 2.5)),
                "center_scale": float(getattr(stp, "spawn_center_scale", 1.0)),
            }
            persist_light_instance(preset_id, float(wx), float(wy), params=params)
        except Exception:
            pass
    except Exception:
        pass


def cycle_overlay_preset_color(ctl, *, prev: bool) -> None:
    st = ctl.model
    try:
        pid = getattr(st, "_hovered_preset_id", None)
        if not pid and getattr(st, "selected_light_id", None) is not None:
            sel_id = int(st.selected_light_id)
            from roguelike_editors.lighting.services.light_instances_service import load_light_instances
            for e in (load_light_instances() or []):
                try:
                    if int(e.get("id")) == sel_id:
                        pid = str(e.get("preset_id") or "")
                        break
                except Exception:
                    continue
        if not pid:
            return
        palette = [
            (255, 200, 140), (255, 255, 255), (120, 200, 255), (255, 120, 120), (120, 255, 160), (200, 140, 255), (255, 240, 120)
        ]
        pal_map = getattr(st, "overlay_palette", {}) or {}
        cur = pal_map.get(pid)
        try:
            idx = palette.index(cur) if cur in palette else -1
        except Exception:
            idx = -1
        if prev:
            idx = (idx - 1) % len(palette)
        else:
            idx = (idx + 1) % len(palette)
        pal_map[pid] = palette[idx]
        st.overlay_palette = pal_map
    except Exception:
        pass
