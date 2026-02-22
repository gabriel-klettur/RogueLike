from .base import BaseSpellResolver
from .utils import get_entity_center
from roguelike_game.ecs.systems.rendering.combat.spells.smoke_emitter.model import SmokeEmitterModel
from roguelike_game.ecs.components.abilities.smoke_emitter_component import SmokeEmitterComponent


class SmokeEmitterResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Resolver para emisor de humo
        cx, cy = get_entity_center(world, caster)
        color = tuple(cfg.get('particle_color', (200, 200, 200)))
        emit_rate = int(cfg.get('emit_rate', 2))
        # Advanced optional params with safe defaults
        speed = float(cfg.get('speed', 1.0))
        lifespan = float(cfg.get('lifespan', 100.0))
        size_range = cfg.get('size_range', (8, 16))
        dispersion = float(cfg.get('dispersion', 0.3))
        palette = None
        try:
            cols = cfg.get('colors')
            if isinstance(cols, (list, tuple)) and len(cols) > 0:
                tmp = []
                for c in cols:
                    if isinstance(c, (list, tuple)) and len(c) >= 3:
                        tmp.append((int(c[0]), int(c[1]), int(c[2])))
                palette = tmp if tmp else None
        except Exception:
            palette = None
        gravity = cfg.get('gravity', None)
        if isinstance(gravity, (int, float)):
            gravity = (0.0, float(gravity))
        elif not (isinstance(gravity, (list, tuple)) and len(gravity) >= 2):
            gravity = None
        drag = cfg.get('drag', None)
        if not isinstance(drag, (int, float)):
            drag = None
        blend_mode = cfg.get('blend_mode') if isinstance(cfg.get('blend_mode'), str) else None
        sol = cfg.get('size_over_life') if isinstance(cfg.get('size_over_life'), (list, tuple)) else None
        aol = cfg.get('alpha_over_life') if isinstance(cfg.get('alpha_over_life'), (list, tuple)) else None
        col_ol = cfg.get('color_over_life') if isinstance(cfg.get('color_over_life'), (list, tuple)) else None

        model = SmokeEmitterModel(
            cx,
            cy,
            color,
            emit_rate,
            speed=speed,
            lifespan=lifespan,
            size_range=size_range,
            dispersion=dispersion,
            colors_palette=palette,
            gravity=gravity,
            drag=drag,
            blend_mode=blend_mode,
            size_over_life=sol,
            alpha_over_life=aol,
            color_over_life=col_ol,
        )
        world.components.setdefault('SmokeEmitterComponent', {})[caster] = SmokeEmitterComponent(model)
