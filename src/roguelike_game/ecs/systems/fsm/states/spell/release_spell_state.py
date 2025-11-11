from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent

from roguelike_game.config.spells_config import SPELLS
import pygame
import math
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.abilities.aura_component import AuraComponent
from roguelike_game.ecs.systems.combat.spells.resolvers import SPELL_RESOLVERS
from roguelike_game.ecs.utils.position_utils import compute_entity_center
import logging
logger = logging.getLogger(__name__)

def _load_image_safe(sprite_path):
    """Load an image and convert alpha only if a video mode exists.

    This prevents pygame.error: 'No video mode has been set' during headless tests.
    """
    try:
        img = pygame.image.load(sprite_path)
    except Exception:
        return None
    try:
        if pygame.display.get_init() and pygame.display.get_surface() is not None:
            try:
                return img.convert_alpha()
            except Exception:
                # Fall back to raw surface if conversion fails
                return img
        else:
            # No display surface available: return raw surface
            return img
    except Exception:
        return img

class ReleaseSpellState(State):
    def enter(self, entity):
        # Cargar configuración según hechizo actual
        ctx = self.fsm.context
        spell_key = ctx.get('spell')
        cfg = SPELLS.get(spell_key, {})
        # Consumir maná del caster si corresponde (evitar doble cobro si ya se cobró en SpellCastingSystem)
        try:
            world = entity.world

            godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and (entity.id == getattr(world, 'player_entity', None))
            if not godmode and not ctx.get('__mana_charged__', False):
                mana_cost = float(getattr(cfg, 'mana_cost', cfg.get('mana_cost', 0)))
                if mana_cost > 0:
                    mana_dict = world.components.get('Mana', {})
                    mana_comp = mana_dict.get(entity.id)
                    if mana_comp is not None:
                        new_val = int(max(0, float(mana_comp.current_mana) - mana_cost))
                        mana_comp.current_mana = new_val
        except Exception:
            # No bloquear el casteo por errores de maná
            pass
        spell_type = cfg.get('type')
        if spell_type == 'sphere_magic_shield':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('sphere_magic_shield')
            resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return
        if spell_type == 'teleport':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('teleport')
            resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return
        if spell_type == 'aura':
            world = entity.world
            # Use resolver so that flattened vfx/particles params and effect.buff are applied
            resolver = SPELL_RESOLVERS.get('aura')
            if resolver is not None:
                resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return
        if spell_type == 'slash':
            world = entity.world
            # Resolver slash: crea hitbox y partículas según cfg
            resolver = SPELL_RESOLVERS.get('slash')
            resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            # Audio: elegir aleatoriamente un clash de espada
            try:
                aq = world.components.setdefault('AudioEventQueue', [])
                aq.append({
                    'type': 'play_sfx',
                    'choices': [
                        'sword_clash_1','sword_clash_2','sword_clash_3','sword_clash_4','sword_clash_5',
                        'sword_clash_6','sword_clash_7','sword_clash_8','sword_clash_9','sword_clash_10'
                    ],
                    'group': 'sfx'
                })
            except Exception:
                pass
            return
        if spell_type == 'dash':
            world = entity.world
            # Resolver dash: registra DashComponent según cfg
            resolver = SPELL_RESOLVERS.get('dash')
            resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return
        if spell_type == 'beam':
            world = entity.world
            # En release: asegurar que el láser se detiene limpiamente (hold-to-fire)
            try:
                world.components.get('LaserBeamComponent', {}).pop(entity.id, None)
            except Exception:
                pass
            return
        if spell_type == 'lightning':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('lightning')
            resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return
        if spell_type == 'chain_lightning':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('chain_lightning')
            if resolver is not None:
                resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return
        if spell_type == 'vortex_field':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('vortex_field')
            if resolver is not None:
                resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return
        if spell_type == 'arcane_flame':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('arcane_flame')
            resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return
        if spell_type == 'firework_launch':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('firework_launch')
            resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return

        if spell_type == 'smoke':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('smoke')
            resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return
        if spell_type == 'smoke_emitter':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('smoke_emitter')
            resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return
        if spell_type == 'puddle':
            world = entity.world
            # Si no hay spawn_pos definido (por ejemplo, autocast de NPC), fijarlo al centro del Player
            try:
                has_spawn = isinstance(ctx.get('spawn_pos'), (tuple, list)) and len(ctx.get('spawn_pos')) == 2
            except Exception:
                has_spawn = False
            if not has_spawn and entity.id != getattr(world, 'player_entity', None):
                try:
                    player_id = getattr(world, 'player_entity', None)
                    if player_id is not None:
                        pos_map = world.components.get('Position', {})
                        spr_map = world.components.get('Sprite', {})
                        scl_map = world.components.get('Scale', {})
                        ppos = pos_map.get(player_id)
                        if ppos is not None:
                            pspr = spr_map.get(player_id)
                            pscl = scl_map.get(player_id)
                            if pspr is not None:
                                cen = compute_entity_center(ppos, pspr, pscl)
                                ctx['spawn_pos'] = (float(cen.x), float(cen.y))
                            else:
                                ctx['spawn_pos'] = (float(ppos.x), float(ppos.y))
                except Exception:
                    pass
            resolver = SPELL_RESOLVERS.get('puddle')
            if resolver is not None:
                resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return
        if spell_type == 'mine':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('mine')
            if resolver is not None:
                resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return

        if spell_type == 'boomerang':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('boomerang')
            if resolver is not None:
                resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return

        if spell_type == 'meteor_shower':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('meteor_shower')
            if resolver is not None:
                resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return

        if spell_type == 'summon':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('summon')
            if resolver is not None:
                resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return

        if spell_type == 'totem':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('totem')
            if resolver is not None:
                resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return

        if spell_type == 'wall':
            world = entity.world
            resolver = SPELL_RESOLVERS.get('wall')
            if resolver is not None:
                resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
            return

        # Avoid default projectile fallback for cone_breath: effect handled during channel
        if spell_type == 'cone_breath':
            return

        # Evitar crear más instancias si se alcanzó el máximo en spells.json para proyectiles
        if spell_type == 'projectile':
            max_inst = cfg.get('max_instances', 0)
            if max_inst:
                active = sum(1 for comp in entity.world.components.get('FireballComponent', {}).values()
                             if getattr(comp, 'spell_key', '') == spell_key)
                if active >= max_inst:
                    return
        world = entity.world
        # Proyectiles spawnean desde el centro del caster
        if cfg.get('type') == 'projectile':
            pos_cmp = world.components['Position'][entity.id]
            sprite_cmp = world.components.get('Sprite', {}).get(entity.id)
            if sprite_cmp:
                w, h = sprite_cmp.image.get_size()
                spawn_x, spawn_y = pos_cmp.x + w/2, pos_cmp.y + h/2
            else:
                spawn_x, spawn_y = pos_cmp.x, pos_cmp.y
        else:
            spawn_x, spawn_y = ctx.get('spawn_pos', (0, 0))
        # Recalcular dirección si no lock_cast_direction (permitir override por contexto NPC)
        lock = cfg.get('lock_cast_direction', True)
        try:
            ctx = self.fsm.context
            if bool(ctx.get('force_lock_direction', False)):
                lock = True
        except Exception:
            pass
        # Prefer explicit direction from context if provided; only sample mouse when unlocked and no direction
        has_ctx_dir = False
        try:
            d = ctx.get('direction', None)
            has_ctx_dir = isinstance(d, (tuple, list)) and len(d) >= 2
        except Exception:
            has_ctx_dir = False
        if not lock and not has_ctx_dir:
            camera = ctx.get('camera')
            mx, my = pygame.mouse.get_pos()
            if camera:
                world_x = mx / camera.zoom + camera.offset_x
                world_y = my / camera.zoom + camera.offset_y
            else:
                world_x, world_y = mx, my
            dx, dy = world_x - spawn_x, world_y - spawn_y
            length = math.hypot(dx, dy) or 1
            dx, dy = dx/length, dy/length
        else:
            dx, dy = ctx.get('direction', (1, 0))
        # Normalize and safeguard direction against zero-length vectors
        try:
            _len = math.hypot(dx, dy)
        except Exception:
            _len = 0.0
        if _len <= 1e-12:
            dx, dy = 1.0, 0.0
        else:
            dx, dy = dx / _len, dy / _len
        # Escala efectiva del sprite del proyectil (permitir overrides por entrada/cast)
        eff_scale = cfg.get('scale', 1.0)
        try:
            smul = float(ctx.get('scale_multiplier', cfg.get('scale_multiplier', 1.0)) or 1.0)
        except Exception:
            smul = 1.0
        try:
            eff_scale = float(eff_scale) * float(smul)
        except Exception:
            pass
        # Radio de impacto efectivo del proyectil (permite override por entrada y multiplicador)
        try:
            base_hit_radius = float(ctx.get('hit_radius', cfg.get('hit_radius', 2.0)) or 2.0)
        except Exception:
            base_hit_radius = 2.0
        try:
            hitmul = float(ctx.get('hit_radius_multiplier', cfg.get('hit_radius_multiplier', 1.0)) or 1.0)
        except Exception:
            hitmul = 1.0
        try:
            eff_hit_radius = max(1.0, float(base_hit_radius) * float(hitmul))
        except Exception:
            eff_hit_radius = max(1.0, float(base_hit_radius))
        # Soporte para ráfaga radial: si se especifica burst_directions o radial_count, generar múltiples proyectiles y salir
        if cfg.get('type') == 'projectile':
            try:
                # Offset hacia delante reutilizando central_forward_offset (opcional)
                try:
                    burst_fwd = float(ctx.get('central_forward_offset', cfg.get('central_forward_offset', 0.0)) or 0.0)
                except Exception:
                    burst_fwd = 0.0
                dirs = ctx.get('burst_directions')
                radial_cnt = 0
                if not isinstance(dirs, list):
                    try:
                        radial_cnt = int(ctx.get('radial_count', cfg.get('radial_count', 0)) or 0)
                    except Exception:
                        radial_cnt = 0
                    if radial_cnt and radial_cnt >= 3:
                        start_deg = 0.0
                        try:
                            start_deg = float(ctx.get('radial_start_deg', cfg.get('radial_start_deg', 0.0)) or 0.0)
                        except Exception:
                            start_deg = 0.0
                        step = 360.0 / float(radial_cnt)
                        dirs = []
                        for k in range(radial_cnt):
                            ang = math.radians(start_deg + k * step)
                            dirs.append((math.cos(ang), math.sin(ang)))
                # Si tenemos una lista de direcciones, disparar y retornar
                if isinstance(dirs, list) and dirs:
                    # Resolve projectile speed robustly (config -> ctx -> safe default)
                    try:
                        speed = float(cfg.get('speed', 0) or 0)
                    except Exception:
                        speed = 0.0
                    if speed <= 0.0:
                        try:
                            speed = float(ctx.get('speed', 0) or 0)
                        except Exception:
                            speed = 0.0
                    if speed <= 0.0:
                        speed = 1.0
                    sprite_path = cfg.get('sprite')
                    img = None
                    if sprite_path:
                        try:
                            img = _load_image_safe(sprite_path)
                        except Exception:
                            img = None
                    last_id = None
                    for dvec in dirs:
                        try:
                            ux, uy = float(dvec[0]), float(dvec[1])
                        except Exception:
                            continue
                        # Normalizar por seguridad
                        mag = math.hypot(ux, uy) or 1.0
                        ux, uy = ux / mag, uy / mag
                        sx = spawn_x + ux * burst_fwd
                        sy = spawn_y + uy * burst_fwd
                        eidp = world.create_entity()
                        world.components['Position'][eidp] = Position(sx, sy)
                        world.components['Velocity'][eidp] = Velocity(ux * speed, uy * speed)
                        world.components['FireballComponent'][eidp] = FireballComponent(
                            ux * speed, uy * speed,
                            damage=cfg.get('damage', 0),
                            lifespan=cfg.get('lifespan', 0),
                            caster=entity.id,
                            spell_key=spell_key,
                            spawn_pos=(sx, sy),
                            vfx_scale_multiplier=smul,
                            hit_radius=eff_hit_radius,
                        )
                        if img is not None:
                            try:
                                world.components['Sprite'][eidp] = Sprite(img)
                                world.components['Scale'][eidp] = Scale(scale=eff_scale)
                            except Exception:
                                pass
                        last_id = eidp
                    if last_id is not None:
                        ctx['fireball_id'] = last_id
                    return
            except Exception:
                pass
        # Offset hacia delante para la fireball central (opcional)
        try:
            central_fwd = float(ctx.get('central_forward_offset', cfg.get('central_forward_offset', 0.0)) or 0.0)
        except Exception:
            central_fwd = 0.0
        c_spawn_x = spawn_x + dx * central_fwd
        c_spawn_y = spawn_y + dy * central_fwd
        fid = world.create_entity()
        # Mantener spawn_pos real de la fireball central
        world.components['Position'][fid] = Position(c_spawn_x, c_spawn_y)
        # Resolve projectile speed robustly (config -> ctx -> safe default)
        try:
            speed = float(cfg.get('speed', 0) or 0)
        except Exception:
            speed = 0.0
        if speed <= 0.0:
            try:
                speed = float(ctx.get('speed', 0) or 0)
            except Exception:
                speed = 0.0
        if speed <= 0.0:
            speed = 1.0
        world.components['Velocity'][fid] = Velocity(dx * speed, dy * speed)
        world.components['FireballComponent'][fid] = FireballComponent(
            dx * speed, dy * speed,
            damage=cfg.get('damage', 0),
            lifespan=cfg.get('lifespan', 0),
            caster=entity.id,
            spell_key=spell_key,
            spawn_pos=(c_spawn_x, c_spawn_y),
            vfx_scale_multiplier=smul,
            hit_radius=eff_hit_radius,
        )
        try:
            logger.debug(
                "[ReleaseSpellState] Spawn fireball eid=%s spell=%s pos=(%.1f,%.1f) vel=(%.2f,%.2f) preset=%s",
                fid, spell_key, spawn_x, spawn_y, dx * speed, dy * speed, str(bool(cfg.get('vfx')))
            )
        except Exception:
            pass

        # Audio: disparo de fireball
        try:
            aq = world.components.setdefault('AudioEventQueue', [])
            aq.append({'type': 'play_sfx', 'sfx_id': 'fireball', 'group': 'sfx'})
        except Exception:
            pass
        # Añadir sprite y aplicar scale si existe ruta
        sprite_path = cfg.get('sprite')
        if sprite_path:
            try:
                img = _load_image_safe(sprite_path)
                if img is not None:
                    world.components['Sprite'][fid] = Sprite(img)
                    world.components['Scale'][fid] = Scale(scale=eff_scale)
            except Exception:
                pass
        # Disparo paralelo: generar dos proyectiles adicionales a izquierda/derecha de la central
        try:
            par_count = int(ctx.get('parallel_count', cfg.get('parallel_count', 1)) or 1)
        except Exception:
            par_count = 1
        if par_count > 1 and cfg.get('type') == 'projectile':
            try:
                spacing = float(ctx.get('parallel_spacing', cfg.get('parallel_spacing', 16.0)) or 16.0)
            except Exception:
                spacing = 16.0
            # Offset hacia delante opcional para las laterales (por defecto 0)
            try:
                sides_fwd = float(ctx.get('sides_forward_offset', cfg.get('sides_forward_offset', 0.0)) or 0.0)
            except Exception:
                sides_fwd = 0.0
            # Vector perpendicular normalizado a (dx,dy)
            px, py = -dy, dx
            plen = math.hypot(px, py) or 1.0
            px, py = px / plen, py / plen
            for side in (-1, 1):
                ex = spawn_x + px * spacing * side + dx * sides_fwd
                ey = spawn_y + py * spacing * side + dy * sides_fwd
                eid2 = world.create_entity()
                world.components['Position'][eid2] = Position(ex, ey)
                # Resolve speed locally to avoid depending on outer scope
                try:
                    _sp = float(cfg.get('speed', 0) or 0)
                except Exception:
                    _sp = 0.0
                if _sp <= 0.0:
                    try:
                        _sp = float(ctx.get('speed', 0) or 0)
                    except Exception:
                        _sp = 0.0
                if _sp <= 0.0:
                    _sp = 1.0
                world.components['Velocity'][eid2] = Velocity(dx * _sp, dy * _sp)
                world.components['FireballComponent'][eid2] = FireballComponent(
                    dx * _sp, dy * _sp,
                    damage=cfg.get('damage', 0),
                    lifespan=cfg.get('lifespan', 0),
                    caster=entity.id,
                    spell_key=spell_key,
                    spawn_pos=(ex, ey),
                    vfx_scale_multiplier=smul,
                    hit_radius=eff_hit_radius,
                )
                if sprite_path:
                    try:
                        # Reusar misma imagen ya cargada para evitar IO repetido
                        world.components['Sprite'][eid2] = Sprite(img)
                        world.components['Scale'][eid2] = Scale(scale=eff_scale)
                    except Exception:
                        pass
        ctx['fireball_id'] = fid

    def execute(self, entity, dt):
        # Transición inmediata a fase de resolución
        from roguelike_game.ecs.systems.fsm.states.spell.resolve_spell_state import ResolveSpellState
        self.fsm.change_state(ResolveSpellState(), entity)

    def exit(self, entity):
        pass