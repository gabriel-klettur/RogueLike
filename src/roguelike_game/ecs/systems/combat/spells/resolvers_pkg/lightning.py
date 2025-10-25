import pygame
from .base import BaseSpellResolver
from .utils import mouse_world
from roguelike_game.ecs.components.abilities.lightning_component import LightningComponent
from roguelike_game.config.particles_config import get_preset
import math


class LightningResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Instanciar LightningComponent en el caster
        start = spawn_meta.get('spawn_pos', (0, 0))
        wx, wy = mouse_world(camera)
        # Optional: resolve particles preset and palette/colors from cfg.vfx
        preset_id = None
        try:
            vfx_attr = getattr(cfg, 'vfx', None)
            if isinstance(vfx_attr, str):
                preset_id = vfx_attr
            elif isinstance(getattr(cfg, 'extra', {}).get('vfx'), dict):
                vfx_obj = getattr(cfg, 'extra', {}).get('vfx')
                pid = vfx_obj.get('preset')
                if isinstance(pid, str):
                    preset_id = pid
        except Exception:
            preset_id = None
        palette = None
        particle_size = 2
        particle_lifespan = 1
        emit_rate = 2
        particle_speed = 0.0
        dispersion_rad = 0.0
        size_min = None
        size_max = None
        try:
            if isinstance(preset_id, str):
                p = get_preset(preset_id)
                if p and isinstance(getattr(p, 'vfx', None), dict):
                    parts = p.vfx.get('particles') if isinstance(p.vfx.get('particles'), dict) else None
                    if isinstance(parts, dict):
                        cols = parts.get('colors')
                        if isinstance(cols, (list, tuple)) and cols:
                            palette = [tuple(int(c[i]) for i in range(3)) for c in cols if isinstance(c, (list, tuple)) and len(c) >= 3]
                        er = parts.get('emit_rate')
                        if isinstance(er, int) and er > 0:
                            emit_rate = er
                        spd = parts.get('speed')
                        if isinstance(spd, (int, float)):
                            particle_speed = float(spd)
                        disp = parts.get('dispersion')
                        if isinstance(disp, (int, float)):
                            # Interpret dispersion from degrees to radians if large, otherwise keep as small rad
                            dispersion_rad = math.radians(disp) if abs(disp) > 0.5 else float(disp)
                        sz = parts.get('size')
                        if isinstance(sz, (int, float)):
                            particle_size = max(1, int(sz))
                        else:
                            sr = parts.get('size_range')
                            if isinstance(sr, (list, tuple)) and len(sr) >= 2:
                                try:
                                    a, b = float(sr[0]), float(sr[1])
                                    particle_size = max(1, int((a + b) / 2.0))
                                    size_min = max(1, int(min(a, b)))
                                    size_max = max(1, int(max(a, b)))
                                except Exception:
                                    pass
                        life = parts.get('lifespan')
                        if isinstance(life, (int, float)):
                            particle_lifespan = max(1, int(life))
                        # Advanced particle params for lightning particles
                        particle_blend_mode = parts.get('blend_mode') if isinstance(parts.get('blend_mode'), str) else None
                        particle_size_over_life = parts.get('size_over_life') if isinstance(parts.get('size_over_life'), (list, tuple)) else None
                        particle_alpha_over_life = parts.get('alpha_over_life') if isinstance(parts.get('alpha_over_life'), (list, tuple)) else None
                        particle_color_over_life = parts.get('color_over_life') if isinstance(parts.get('color_over_life'), (list, tuple)) else None
                        gval = parts.get('gravity')
                        if isinstance(gval, (int, float)):
                            particle_gravity = (0.0, float(gval))
                        elif isinstance(gval, (list, tuple)) and len(gval) >= 2:
                            particle_gravity = (float(gval[0]), float(gval[1]))
                        else:
                            particle_gravity = None
                        dval = parts.get('drag')
                        particle_drag = float(dval) if isinstance(dval, (int, float)) else None
        except Exception:
            palette = None
            particle_size = 2
            particle_lifespan = 1
            emit_rate = 2
            particle_speed = 0.0
            dispersion_rad = 0.0
            size_min = None
            size_max = None
            particle_blend_mode = None
            particle_size_over_life = None
            particle_alpha_over_life = None
            particle_color_over_life = None
            particle_gravity = None
            particle_drag = None
        comp = LightningComponent(start, (wx, wy),
                                   cfg.get('segments', 10),
                                   cfg.get('offset', 0),
                                   cfg.get('lifetime', 0),
                                   preset_id=preset_id,
                                   colors_palette=palette,
                                   particle_size=particle_size,
                                   particle_lifespan=particle_lifespan,
                                   particle_emit_rate=emit_rate,
                                   particle_speed=particle_speed,
                                   particle_dispersion=dispersion_rad,
                                   size_min=size_min,
                                   size_max=size_max,
                                   particle_blend_mode=particle_blend_mode,
                                   particle_size_over_life=particle_size_over_life,
                                   particle_alpha_over_life=particle_alpha_over_life,
                                   particle_color_over_life=particle_color_over_life,
                                   particle_gravity=particle_gravity,
                                   particle_drag=particle_drag)
        world.components.setdefault('LightningComponent', {})[caster] = comp
