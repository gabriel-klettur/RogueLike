import math
from typing import Any, Dict

from .base import BaseSpellResolver
from .utils import get_entity_center, mouse_world, direction_from_to
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.wall_segment_component import WallSegmentComponent
import pygame
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale


class WallResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta: Dict[str, Any], cfg, camera):
        # 1) Posición de spawn: ratón en mundo
        try:
            mx, my = mouse_world(camera)
            spawn_x, spawn_y = float(mx), float(my)
        except Exception:
            # Fallback al centro del caster
            try:
                cx, cy = get_entity_center(world, caster)
                spawn_x, spawn_y = float(cx), float(cy)
            except Exception:
                p = world.components.get('Position', {}).get(caster)
                spawn_x, spawn_y = (float(p.x), float(p.y)) if p else (0.0, 0.0)

        # 2) Elegir orientación 90° respecto al vector caster->ratón (perpendicular SIEMPRE)
        try:
            cx, cy = get_entity_center(world, caster)
        except Exception:
            pos_c = world.components.get('Position', {}).get(caster)
            cx, cy = (float(pos_c.x), float(pos_c.y)) if pos_c else (spawn_x, spawn_y)
        dx, dy = float(spawn_x - cx), float(spawn_y - cy)
        # Si el vector hacia el ratón es más horizontal (|dx| >= |dy|), el muro es vertical; si es más vertical, el muro es horizontal
        orient = 'vertical' if abs(dx) >= abs(dy) else 'horizontal'

        # 3) Efecto: parámetros con defaults adecuados
        try:
            effect = getattr(cfg, 'extra', {}).get('effect', {}) or {}
        except Exception:
            effect = {}
        duration = float(effect.get('duration', cfg.get('duration', 0.0) or 0.0))
        hp = float(effect.get('hp', 50.0))
        # width/height: tamaño en espacio local (eje X=ancho largo, eje Y=alto corto)
        base_w = float(effect.get('width', 200.0))
        base_h = float(effect.get('height', 50.0))
        blocks_projectiles = bool(effect.get('blocks_projectiles', True))
        blocks_units = bool(effect.get('blocks_units', True))
        # Ángulo perpendicular al vector caster->ratón (en grados)
        import math
        angle_deg = 0.0
        try:
            angle_deg = (math.degrees(math.atan2(dy, dx)) + 90.0) % 360.0
        except Exception:
            pass

        # 5) Crear único segmento AABB en el ratón
        eid = world.create_entity()
        world.components.setdefault('Position', {})[eid] = Position(spawn_x, spawn_y)
        world.components.setdefault('WallSegmentComponent', {})[eid] = WallSegmentComponent(
            width=base_w,
            height=base_h,
            hp=hp,
            duration=duration,
            blocks_projectiles=blocks_projectiles,
            blocks_units=blocks_units,
            owner=caster,
            spell_key=getattr(cfg, 'key', ''),
            orient=orient,
            angle_deg=angle_deg,
        )
        # 6) Si hay sprite configurado en spells.json, adjuntarlo junto con Scale
        try:
            sprite_path = cfg.get('sprite') if hasattr(cfg, 'get') else getattr(cfg, 'sprite', None)
            if isinstance(sprite_path, str) and sprite_path:
                sp = Sprite(sprite_path)
                # Intentar leer un offset de rotación opcional desde spells.json: vfx.sprite.rotation_offset_deg
                try:
                    vfx_obj = getattr(cfg, 'vfx', None)
                    if not isinstance(vfx_obj, dict):
                        vfx_obj = getattr(cfg, 'extra', {}).get('vfx')
                    rot_off = 0.0
                    rev_dur = None
                    if isinstance(vfx_obj, dict):
                        spr_cfg = vfx_obj.get('sprite') or {}
                        val = spr_cfg.get('rotation_offset_deg')
                        if isinstance(val, (int, float)):
                            rot_off = float(val)
                        val2 = spr_cfg.get('reveal_duration_sec')
                        if isinstance(val2, (int, float)):
                            rev_dur = float(val2)
                    setattr(sp, 'rotation_offset_deg', rot_off)
                    if rev_dur is not None:
                        setattr(sp, 'reveal_duration_sec', rev_dur)
                except Exception:
                    pass
                # Flag de flip vertical: si el spawn está por debajo de la línea horizontal del jugador
                try:
                    # usamos cy calculado antes (centro del caster)
                    setattr(sp, 'flip_y', bool(spawn_y > cy))
                except Exception:
                    pass
                world.components.setdefault('Sprite', {})[eid] = sp
                try:
                    sc = float(getattr(cfg, 'scale', 1.0))
                except Exception:
                    sc = 1.0
                world.components.setdefault('Scale', {})[eid] = Scale(sc)
        except Exception:
            # Ignorar fallos al cargar sprite para no bloquear el gameplay del muro
            pass
        return [eid]
