import pygame
from .base import BaseSpellResolver
from .utils import mouse_world
from roguelike_game.ecs.components.abilities.lightning_component import LightningComponent
from roguelike_game.config.particles_config import get_preset


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
        try:
            if isinstance(preset_id, str):
                p = get_preset(preset_id)
                if p and isinstance(getattr(p, 'vfx', None), dict):
                    parts = p.vfx.get('particles') if isinstance(p.vfx.get('particles'), dict) else None
                    if isinstance(parts, dict):
                        cols = parts.get('colors')
                        if isinstance(cols, (list, tuple)) and cols:
                            palette = [tuple(int(c[i]) for i in range(3)) for c in cols if isinstance(c, (list, tuple)) and len(c) >= 3]
        except Exception:
            palette = None
        comp = LightningComponent(start, (wx, wy),
                                   cfg.get('segments', 10),
                                   cfg.get('offset', 0),
                                   cfg.get('lifetime', 0),
                                   preset_id=preset_id,
                                   colors_palette=palette,
                                   particle_size=2,
                                   particle_lifespan=1)
        world.components.setdefault('LightningComponent', {})[caster] = comp
