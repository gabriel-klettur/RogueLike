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
            # Resolver beam: registra LaserBeamComponent según cfg
            resolver = SPELL_RESOLVERS.get('beam')
            resolver.resolve(world, entity.id, ctx, cfg, ctx.get('camera'))
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
        if not lock:
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
        fid = world.create_entity()
        # Mantener spawn_pos como centro de la fireball
        world.components['Position'][fid] = Position(spawn_x, spawn_y)
        speed = cfg.get('speed', 0)
        world.components['Velocity'][fid] = Velocity(dx * speed, dy * speed)
        world.components['FireballComponent'][fid] = FireballComponent(
            dx * speed, dy * speed,
            damage=cfg.get('damage', 0),
            lifespan=cfg.get('lifespan', 0),
            caster=entity.id,
            spell_key=spell_key,
            spawn_pos=(spawn_x, spawn_y)
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
            img = pygame.image.load(sprite_path).convert_alpha()
            world.components['Sprite'][fid] = Sprite(img)
            world.components['Scale'][fid] = Scale(scale=cfg.get('scale', 1.0))
        ctx['fireball_id'] = fid

    def execute(self, entity, dt):
        # Transición inmediata a fase de resolución
        from roguelike_game.ecs.systems.fsm.states.spell.resolve_spell_state import ResolveSpellState
        self.fsm.change_state(ResolveSpellState(), entity)

    def exit(self, entity):
        pass