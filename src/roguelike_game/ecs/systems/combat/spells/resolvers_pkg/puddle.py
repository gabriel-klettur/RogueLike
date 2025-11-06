import logging
import pygame
from typing import Any, Dict, List, Optional

from .base import BaseSpellResolver
from .utils import get_entity_center, mouse_world
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.abilities.puddle_component import PuddleComponent


logger = logging.getLogger(__name__)
try:
    logger.setLevel(logging.INFO)
except Exception:
    pass


class PuddleResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta: Dict[str, Any], cfg, camera):
        # Determinar posición de spawn (prioridad: meta.spawn_pos -> meta.target=='player' -> cfg.spawn_at -> centro caster)
        used_source = 'unknown'
        try:
            # 1) Si el meta trae spawn_pos explícito, usarlo siempre
            if isinstance(spawn_meta, dict) and 'spawn_pos' in spawn_meta:
                sx, sy = spawn_meta.get('spawn_pos', (0, 0))
                spawn_x, spawn_y = float(sx), float(sy)
                used_source = 'meta.spawn_pos'
            # 2) Si meta indica target=player, usar centro del jugador
            elif isinstance(spawn_meta, dict) and str(spawn_meta.get('target', '')).lower() == 'player':
                peid = getattr(world, 'player_entity', None)
                if peid is not None:
                    px, py = get_entity_center(world, peid)
                    spawn_x, spawn_y = float(px), float(py)
                    used_source = 'meta.target=player'
                else:
                    cx, cy = get_entity_center(world, caster)
                    spawn_x, spawn_y = float(cx), float(cy)
                    used_source = 'fallback.caster_no_player'
            else:
                # 3) Consultar spawn_at desde cfg
                try:
                    effect_tmp = getattr(cfg, 'extra', {}).get('effect', {}) or {}
                except Exception:
                    effect_tmp = {}
                spawn_at = effect_tmp.get('spawn_at', cfg.get('spawn_at', 'caster'))
                spawn_at = str(spawn_at or 'caster').lower()
                if spawn_at == 'mouse':
                    wx, wy = mouse_world(camera)
                    spawn_x, spawn_y = float(wx), float(wy)
                    used_source = 'cfg.mouse'
                else:
                    cx, cy = get_entity_center(world, caster)
                    spawn_x, spawn_y = float(cx), float(cy)
                    used_source = 'cfg.caster'
        except Exception:
            # Fallback simple
            pos_cmp = world.components.get('Position', {}).get(caster)
            if pos_cmp is not None:
                spawn_x, spawn_y = float(pos_cmp.x), float(pos_cmp.y)
            else:
                spawn_x, spawn_y = 0.0, 0.0
            used_source = 'exception.fallback'

        # Parametrización: campos aplanados + extra.effect (tolerante)
        effect = {}
        try:
            effect = getattr(cfg, 'extra', {}).get('effect', {}) or {}
        except Exception:
            effect = {}
        radius = float(cfg.get('radius', effect.get('radius', 80)))
        duration = float(cfg.get('duration', effect.get('duration', 5.0)))
        # tick_period aún no está en el aplanado estándar: leer desde effect o usar default
        tick_period = float(effect.get('tick_period', 0.25))
        damage = float(cfg.get('damage', effect.get('damage', 0)))
        heal = float(effect.get('heal', 0))
        status = effect.get('status')
        move_speed_mult = float(effect.get('move_speed_mult', 1.0))
        element = effect.get('element')

        # Color base desde vfx.particles.color o paleta (opcional)
        color = None
        alpha = 80
        try:
            vfx = getattr(cfg, 'extra', {}).get('vfx', {}) or {}
            parts = vfx.get('particles', {}) or {}
            if isinstance(parts.get('color'), (list, tuple)):
                color = tuple(parts['color'])
            elif isinstance(parts.get('colors'), (list, tuple)) and parts['colors']:
                color = tuple(parts['colors'][0])
            if isinstance(vfx.get('alpha'), (int, float)):
                alpha = int(vfx['alpha'])
        except Exception:
            pass

        # Crear entidad puddle
        eid = world.create_entity()
        world.components.setdefault('Position', {})[eid] = Position(spawn_x, spawn_y)

        # Sprite estático o secuencia de sprites con tiempos
        seq_frames: List[Any] = []
        seq_times: List[float] = []
        scale_value: float = float(cfg.get('scale', 1.0))
        sprite_path: Optional[str] = None
        try:
            vfx = getattr(cfg, 'extra', {}).get('vfx', {}) or {}
        except Exception:
            vfx = {}
        # Secuencia
        if isinstance(vfx, dict):
            seq = vfx.get('sprite_sequence') or vfx.get('sequence') or {}
            if isinstance(seq, dict):
                frames_list = seq.get('frames')
                times_list = seq.get('times')
                if isinstance(frames_list, (list, tuple)) and frames_list:
                    for p in frames_list:
                        if isinstance(p, str) and p:
                            try:
                                seq_frames.append(pygame.image.load(p).convert_alpha())
                            except Exception:
                                pass
                if isinstance(times_list, (list, tuple)) and times_list:
                    try:
                        seq_times = [float(t) for t in times_list]
                    except Exception:
                        seq_times = []
                if isinstance(seq.get('scale'), (int, float)):
                    scale_value = float(seq.get('scale'))
        # Si no hay secuencia válida, intentar sprite único
        if not seq_frames:
            sprite_path = cfg.get('sprite', None)
            if not sprite_path:
                try:
                    sp = vfx.get('sprite') if isinstance(vfx, dict) else None
                    if isinstance(sp, dict) and isinstance(sp.get('path'), str):
                        sprite_path = sp.get('path')
                        if isinstance(sp.get('scale'), (int, float)):
                            scale_value = float(sp.get('scale'))
                except Exception:
                    sprite_path = None
            if sprite_path:
                try:
                    img = pygame.image.load(sprite_path).convert_alpha()
                    world.components.setdefault('Sprite', {})[eid] = Sprite(img)
                except Exception:
                    pass
        else:
            try:
                # Aplicar primer frame como Sprite inicial
                world.components.setdefault('Sprite', {})[eid] = Sprite(seq_frames[0])
            except Exception:
                pass
        # Escala visual
        try:
            world.components.setdefault('Scale', {})[eid] = Scale(scale=scale_value)
        except Exception:
            pass

        # (wire/outline eliminado por requerimiento)

        # Registrar componente de lógica
        # Expiración por colisión (player)
        expire_on_player_collision = False
        try:
            expire_on_player_collision = bool(effect.get('expire_on_player_collision', cfg.get('expire_on_player_collision', False)))
        except Exception:
            expire_on_player_collision = False

        # Ajustar radius automáticamente si se solicita o si no es válido
        try:
            auto_radius = bool(effect.get('auto_radius', cfg.get('auto_radius', False)))
        except Exception:
            auto_radius = False
        try:
            if auto_radius or radius <= 0:
                max_dim = 0.0
                if seq_frames:
                    for surf in seq_frames:
                        try:
                            w, h = surf.get_size()
                            max_dim = max(max_dim, float(max(w, h)))
                        except Exception:
                            pass
                else:
                    spr_cmp = world.components.get('Sprite', {}).get(eid)
                    if spr_cmp and hasattr(spr_cmp, 'image'):
                        try:
                            w, h = spr_cmp.image.get_size()
                            max_dim = max(float(w), float(h))
                        except Exception:
                            max_dim = 0.0
                if max_dim > 0.0:
                    radius = 0.45 * max_dim * float(scale_value or 1.0)
        except Exception:
            pass

        world.components.setdefault('PuddleComponent', {})[eid] = PuddleComponent(
            radius=radius,
            duration=duration,
            tick_period=tick_period,
            damage=damage,
            heal=heal,
            status=status,
            move_speed_mult=move_speed_mult,
            element=element,
            color=color,
            alpha=alpha,
            owner=caster,
            spell_key=getattr(cfg, 'key', ''),
            sequence_frames=seq_frames if seq_frames else None,
            sequence_times=seq_times if seq_times else None,
            hold_last_frame=True,
            expire_on_player_collision=expire_on_player_collision,
        )

        try:
            if isinstance(spawn_meta, dict):
                meta_brief = {k: v for k, v in spawn_meta.items() if k in ('spawn_pos', 'target')}
            else:
                meta_brief = None
            logger.info(
                "[PuddleResolver] src=%s caster=%s eid=%s pos=(%.1f,%.1f) meta=%s",
                used_source, caster, eid, spawn_x, spawn_y, str(meta_brief)
            )
        except Exception:
            pass
