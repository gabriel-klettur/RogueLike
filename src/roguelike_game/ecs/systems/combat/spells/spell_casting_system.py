"""
Sistema ECS que detecta componentes 'WantsToCastSpell' y, según corresponda,
arranca la máquina de estados de hechizos (CastState y subestados) para NPCs
y jugadores.
"""
from roguelike_game.ecs.systems.fsm.states.cast_state import CastState
from roguelike_game.ecs.systems.fsm.states.player.player_spell_cast_state import PlayerSpellCastState
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.ecs.systems.fsm.states.player.move_state import MoveState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy

from roguelike_engine.utils.benchmark import benchmark
import math
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
import pygame

import logging
logger = logging.getLogger(__name__)

class SpellCastingSystem:
    """
    Sistema que procesa intenciones de hechizo registradas en el ECS:
      • Si la intención pertenece a un NPC (está en 'NPCState'), inicia su sub-FSM de hechizos
        cambiando su estado a CastState.
      • Si pertenece al jugador, genera inmediatamente una fireball dirigida a la posición
        del ratón, sin pasar por sub-FSM.
    """

    def __init__(self, perf_log=None):
        """
        Args:
            perf_log: objeto de logging o benchmarking (opcional), usado por el decorador @benchmark.
        """
        self.perf_log = perf_log

    # Mapping for simple type -> component counting
    _TYPE_TO_COMPONENT = {
        'aura': 'AuraComponent',
        'beam': 'LaserBeamComponent',
        'dash': 'DashComponent',
        'lightning': 'LightningComponent',
        'arcane_flame': 'ArcaneFlameComponent',
        'firework_launch': 'FireworkLaunchComponent',
        'smoke': 'SmokeComponent',
        'smoke_emitter': 'SmokeEmitterComponent',
        'sphere_magic_shield': 'SphereMagicShieldComponent',
        'teleport': 'TeleportComponent',
    }

    def _count_active(self, world, eid, intent, spell_type: str) -> int:
        """Count active instances for the given spell type, preserving current semantics.

        Special cases:
        - projectile: count FireballComponent filtered by spell_key
        - slash: count HitboxComponent owned by the caster
        Others: count all components of mapped type
        """
        if spell_type == 'projectile':
            return sum(1 for comp in world.components.get('FireballComponent', {}).values()
                       if getattr(comp, 'spell_key', '') == intent.spell)
        if spell_type == 'slash':
            return sum(1 for comp in world.components.get('HitboxComponent', {}).values()
                       if getattr(comp, 'owner', None) == eid)
        # Per-caster counting for dash: component vive en el caster
        if spell_type == 'dash':
            return 1 if eid in world.components.get('DashComponent', {}) else 0
        comp_key = self._TYPE_TO_COMPONENT.get(spell_type)
        if comp_key:
            return len(world.components.get(comp_key, {}))
        return 0
    
    def update(self, world, camera=None):
        """
        Recorre todas las entidades que tengan el componente 'WantsToCastSpell'.

        - 'wants' es el diccionario de intenciones de hechizo:
          key: entity_id, value: instancia de WantsToCastSpell
        - 'npcs' es el diccionario de componentes NPCState, se usa para distinguir NPCs de jugador.

        Args:
            world: instancia de ECSWorld, contenedor de entidades y componentes.
            camera: objeto de cámara, usado para convertir coordenadas de pantalla a mundo.
        """
        # Obtener diccionario actual de intenciones de hechizo
        wants = world.components.get('WantsToCastSpell', {})
        npcs = world.components.get('NPCState', {})

        #logger.debug(f" Inicio update: {len(wants)} intenciones detectadas.")

        # Iterar sobre una copia de las llaves, porque vamos a eliminar intenciones mientras iteramos
        for eid in list(wants.keys()):
            intent = wants[eid]
            # Validar max_instances y allow_overlap segun spells.json
            cfg = SPELLS.get(intent.spell, {})
            spell_type = cfg.get('type')
            max_inst = cfg.get('max_instances', 0)
            # Contar instancias activas (centralizado)
            active = self._count_active(world, eid, intent, spell_type)

            if max_inst and active >= max_inst:
                continue
            allow_overlap = cfg.get('allow_overlap', True)
            if not allow_overlap and active > 0:
                continue
            # Verificar coste de maná (si el caster tiene maná)
            try:
                godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and (eid == getattr(world, 'player_entity', None))
                if not godmode:
                    mana_comp = world.components.get('Mana', {}).get(eid)
                    mana_cost = float(getattr(cfg, 'mana_cost', cfg.get('mana_cost', 0)))
                    if mana_comp is not None and mana_cost > 0:
                        if float(mana_comp.current_mana) < mana_cost:
                            # Notificar falta de maná al jugador mediante burbuja
                            try:
                                from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble
                                push_bubble(world, eid, 'No tengo suficiente maná', color=(240, 200, 200), ttl_ms=1800)
                            except Exception:
                                pass
                            # Disparar flash azul en la barra de maná durante 600ms
                            try:
                                import time as _time
                                flash_store = getattr(world, '_mana_flash_until', None)
                                if not isinstance(flash_store, dict):
                                    flash_store = {}
                                    setattr(world, '_mana_flash_until', flash_store)
                                flash_store[eid] = _time.time() + 0.6
                            except Exception:
                                pass
                            # Descartar intención y continuar
                            wants.pop(eid, None)
                            continue
            except Exception:
                # fallback duro: no bloquear por errores de lectura
                pass

            # Evaluar movilidad del hechizo una sola vez
            allow_mov = cfg.get('allow_movement', False)

            # Si el hechizo permite movimiento y es un proyectil, realizar "inline cast" para NPCs
            # sin cambiar el estado global (mantiene ChaseState activo y no corta el movimiento).
            player_eid = getattr(world, 'player_entity', None)
            if eid in npcs and eid != player_eid and allow_mov and spell_type == 'projectile':
                try:
                    # Mana: cobrar aquí porque no pasaremos por ReleaseSpellState
                    godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and (eid == getattr(world, 'player_entity', None))
                    mana_comp = world.components.get('Mana', {}).get(eid)
                    mana_cost = float(getattr(cfg, 'mana_cost', cfg.get('mana_cost', 0)))
                    if not godmode and mana_comp is not None and mana_cost > 0:
                        if float(mana_comp.current_mana) < mana_cost:
                            wants.pop(eid, None)
                            continue
                        mana_comp.current_mana = int(max(0, float(mana_comp.current_mana) - mana_cost))

                    # Spawn center del caster
                    pos_cmp = world.components['Position'][eid]
                    spawn_x, spawn_y = pos_cmp.x, pos_cmp.y
                    sprite_cmp = world.components.get('Sprite', {}).get(eid)
                    if sprite_cmp:
                        w, h = sprite_cmp.image.get_size()
                        spawn_x += w/2; spawn_y += h/2
                    # Objetivo: centro del player
                    player_eid = getattr(world, 'player_entity', None)
                    ppos = world.components['Position'].get(player_eid)
                    if ppos is not None:
                        px, py = ppos.x, ppos.y
                        ps = world.components.get('Sprite', {}).get(player_eid)
                        if ps:
                            pw, ph = ps.image.get_size()
                            px += pw/2; py += ph/2
                    else:
                        # Fallback: mismo punto (evita div/0)
                        px, py = spawn_x + 1, spawn_y
                    dx, dy = px - spawn_x, py - spawn_y
                    length = math.hypot(dx, dy) or 1
                    dx, dy = dx/length, dy/length

                    # Crear proyectil (fireball genérico)
                    fid = world.create_entity()
                    world.components['Position'][fid] = Position(spawn_x, spawn_y)
                    speed = cfg.get('speed', 0)
                    world.components['Velocity'][fid] = Velocity(dx * speed, dy * speed)
                    # Damage base y overrides por meta
                    base_damage = cfg.get('damage', 0)
                    try:
                        meta = getattr(intent, 'meta', None)
                        if isinstance(meta, dict):
                            if isinstance(meta.get('damage'), (int, float)):
                                base_damage = float(meta.get('damage'))
                            else:
                                dmul = meta.get('damage_multiplier')
                                if isinstance(dmul, (int, float)):
                                    base_damage = float(base_damage) * float(dmul)
                    except Exception:
                        pass

                    # vfx scale multiplier: mantener el impacto consistente con el tamaño del proyectil
                    vfx_scale_mul = 1.0
                    try:
                        # Si meta.scale está definido, interpretarlo como escala absoluta del sprite;
                        # convertirlo a multiplicador relativo respecto al scale de la spell
                        meta = getattr(intent, 'meta', None)
                        if isinstance(meta, dict):
                            base_scale = float(cfg.get('scale', 1.0))
                            if isinstance(meta.get('scale'), (int, float)):
                                abs_scale = float(meta.get('scale'))
                                vfx_scale_mul = (abs_scale / base_scale) if base_scale > 0 else 1.0
                            else:
                                mul = meta.get('scale_multiplier')
                                if isinstance(mul, (int, float)):
                                    vfx_scale_mul = float(mul)
                    except Exception:
                        vfx_scale_mul = 1.0

                    # Radio base de colisión configurable por spell, escalado por multiplicador visual
                    try:
                        base_hit_radius = float(cfg.get('hit_radius', 2.0))
                    except Exception:
                        base_hit_radius = 2.0
                    # Detectar si hubo override explícito de hit_radius en meta
                    hit_radius_overridden = False
                    try:
                        meta = getattr(intent, 'meta', None)
                        if isinstance(meta, dict):
                            if isinstance(meta.get('hit_radius'), (int, float)):
                                base_hit_radius = float(meta.get('hit_radius'))
                                hit_radius_overridden = True
                            else:
                                hmul = meta.get('hit_radius_multiplier')
                                if isinstance(hmul, (int, float)):
                                    base_hit_radius = float(base_hit_radius) * float(hmul)
                                    hit_radius_overridden = True
                    except Exception:
                        pass
                    hit_radius = float(base_hit_radius) * float(vfx_scale_mul)

                    world.components['FireballComponent'][fid] = FireballComponent(
                        dx * speed, dy * speed,
                        damage=base_damage,
                        lifespan=cfg.get('lifespan', cfg.get('lifetime', 0)),
                        caster=eid,
                        spell_key=intent.spell,
                        spawn_pos=(spawn_x, spawn_y),
                        vfx_scale_multiplier=vfx_scale_mul,
                        hit_radius=hit_radius
                    )
                    # Sprite/scale si está definido
                    sprite_path = cfg.get('sprite')
                    if sprite_path:
                        try:
                            img = pygame.image.load(sprite_path).convert_alpha()
                            world.components['Sprite'][fid] = Sprite(img)
                            # Base scale from spell cfg (flattened from vfx.sprite.scale)
                            scale_value = cfg.get('scale', 1.0)
                            # Apply per-cast overrides from intent.meta (e.g., scale or scale_multiplier)
                            try:
                                meta = getattr(intent, 'meta', None)
                                if isinstance(meta, dict):
                                    if isinstance(meta.get('scale'), (int, float)):
                                        scale_value = float(meta.get('scale'))
                                    else:
                                        mul = meta.get('scale_multiplier')
                                        if isinstance(mul, (int, float)):
                                            scale_value = float(scale_value) * float(mul)
                            except Exception:
                                pass
                            world.components['Scale'][fid] = Scale(scale=scale_value)
                            # Autoajustar hit_radius al tamaño visual si no hubo override explícito
                            try:
                                if not hit_radius_overridden and img is not None:
                                    iw, ih = img.get_size()
                                    # Aproximamos radio como ~45% del diámetro mayor escalado
                                    visual_radius = 0.45 * float(max(iw, ih)) * float(scale_value)
                                    fb = world.components['FireballComponent'][fid]
                                    fb.hit_radius = max(fb.hit_radius, visual_radius)
                            except Exception:
                                pass
                        except Exception:
                            pass
                    # Consumir intención y saltar siguiente
                    wants.pop(eid, None)
                    logger.debug(f" [InlineCast] NPC {eid} -> fireball fid={fid} pos=({spawn_x:.1f},{spawn_y:.1f}) vel=({dx*speed:.2f},{dy*speed:.2f})")
                    continue
                except Exception:
                    # Si algo falla, caer al flujo normal (con cambio a CastState)
                    pass

            # Permitir castear solo si Idle o (allow_movement y movimiento)
            state_comp = npcs.get(eid)
            if state_comp:
                current = state_comp.fsm.current_state
                # Permitir también castear durante ChaseState cuando el hechizo permite movimiento
                try:
                    current_name = getattr(getattr(current, '__class__', type(current)), '__name__', '')
                except Exception:
                    current_name = ''
                moving_ok = allow_mov and (isinstance(current, MoveState) or current_name == 'ChaseState')
                # salto si no está en IdleState o movimiento permitido
                if not (isinstance(current, IdleState) or moving_ok):
                    # en medio de cast; verificar interruptibilidad
                    if not cfg.get('interruptible', False):
                        # descartar intención si no es interruptible
                        wants.pop(eid, None)
                        continue

            # Si tiene FSM global (NPC o jugador), iniciar sub-FSM de hechizo
            if eid in npcs:
                npc_state = npcs[eid]
                proxy = _EntityProxy(world, eid)
                # Elegir estado inicial según tipo de entidad
                if eid == world.player_entity:
                    new_state = PlayerSpellCastState()
                    # Compute direction and spawn center
                    pos_cmp = world.components['Position'][eid]
                    sprite_cmp = world.components.get('Sprite', {}).get(eid)
                    if sprite_cmp:
                        w, h = sprite_cmp.image.get_size()
                        spawn_x, spawn_y = pos_cmp.x + w/2, pos_cmp.y + h/2
                    else:
                        spawn_x, spawn_y = pos_cmp.x, pos_cmp.y
                    if camera:
                        mx, my = pygame.mouse.get_pos()
                        world_x = mx / camera.zoom + camera.offset_x
                        world_y = my / camera.zoom + camera.offset_y
                    else:
                        mx, my = pygame.mouse.get_pos()
                        world_x, world_y = float(mx), float(my)
                    dx, dy = world_x - spawn_x, world_y - spawn_y
                    length = math.hypot(dx, dy) or 1
                    new_state.spell_fsm.context['direction'] = (dx/length, dy/length)
                    new_state.spell_fsm.context['spawn_pos'] = (spawn_x, spawn_y)
                    # Guardar camera y spell para recalcular aiming dinámico
                    new_state.spell_fsm.context['camera'] = camera
                    new_state.spell_fsm.context['spell'] = intent.spell
                    new_state.spell_fsm.context['automatic'] = cfg.get('automatic', False)
                    new_state.spell_fsm.context['automatic_cast_punish'] = cfg.get('automatic_cast_punish', 1.0)
                else:
                    new_state = CastState()
                    new_state.spell_fsm.context['spell'] = intent.spell
                    new_state.spell_fsm.context['automatic'] = cfg.get('automatic', False)
                    new_state.spell_fsm.context['automatic_cast_punish'] = cfg.get('automatic_cast_punish', 1.0)
                logger.debug(f" Entidad {eid} inicia hechizo '{intent.spell}' via FSM.")
                npc_state.fsm.change_state(new_state, proxy)
            # Limpiar intención
            wants.pop(eid, None)
            logger.debug(f" Intención de hechizo de entidad {eid} eliminada.\n")

        # Nota: pulse la FSM de hechizo (prepare/channel/release/cooldown) en CastState y subestados