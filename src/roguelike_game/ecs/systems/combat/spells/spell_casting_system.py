"""
Sistema ECS que detecta componentes 'WantsToCastSpell' y, según corresponda,
arranca la máquina de estados de hechizos (CastState y subestados) para NPCs
y jugadores.
"""
from roguelike_game.ecs.fsm.states.cast_state import CastState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy
from roguelike_game.ecs.fsm.states.aggro_state import AggroState
from roguelike_game.ecs.components.fsm.npc_state import NPCState
from roguelike_game.ecs.fsm.fsm import FiniteStateMachine
import pygame
import time
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.systems.combat.spells.resolvers import SPELL_RESOLVERS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_engine.utils.benchmark import benchmark


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
        # Cola de spawn diferido para el jugador
        self.pending_spawns = []
        # Registro de último cast del jugador para respetar cooldown
        self.last_cast_times = {}

    @benchmark(lambda self: self.perf_log, "4.2.2.SpellCastingSystem.update")
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

        #print(f"[SpellCastingSystem] Inicio update: {len(wants)} intenciones detectadas.")

        # Iterar sobre una copia de las llaves, porque vamos a eliminar intenciones mientras iteramos
        for eid in list(wants.keys()):
            # Solo filtrar NPCs ocupados, permitir multi-cast para el jugador
            state = npcs.get(eid)
            if eid != world.player_entity and state and not isinstance(state.fsm.current_state, AggroState):
                wants.pop(eid, None)
                continue
            intent = wants[eid]
            # Jugador: programar spawn tras prepare+channel
            if eid == world.player_entity:
                now = time.time()
                cfg = SPELLS.get(intent.spell, {})
                cd = cfg.get('cooldown_duration', 0)
                last = self.last_cast_times.get(eid, 0)
                if now < last + cd:
                    wants.pop(eid, None)
                    continue
                self.last_cast_times[eid] = now
                # calcular dirección y offset de spawn desde centro actual
                pos_cmp = world.components['Position'][eid]
                cx, cy = pos_cmp.x, pos_cmp.y
                sprite_cmp = world.components['Sprite'].get(eid)
                if sprite_cmp:
                    w, h = sprite_cmp.image.get_size()
                    cx += w/2; cy += h/2
                offset = (max(w, h)/2) if sprite_cmp else 0
                pd = cfg.get('prepare_duration', 0)
                ch = cfg.get('channel_duration', 0)
                spawn_time = now + pd + ch
                print(f"[DEBUG][SpellCastingSystem] schedule spawn_time={spawn_time:.3f}, caster={eid}, spell={intent.spell}, center=({cx:.1f},{cy:.1f}), offset={offset:.1f}")
                # Store only offset and schedule time; direction recalculated at spawn
                self.pending_spawns.append((eid, intent.spell, offset, spawn_time))
                wants.pop(eid, None)
                continue

            # 1) Si la entidad con intención está en 'NPCState' y NO es jugador, se trata de un NPC
            if eid in npcs and eid != world.player_entity:
                print(f"[SpellCastingSystem] Entidad {eid} es NPC. Iniciando sub-FSM de hechizo.")
                # Obtener el componente NPCState (que contiene la FSM)
                npc_state = npcs[eid]
                entity_proxy = _EntityProxy(world, eid)
                # Preparar nuevo estado de hechizo con contexto en sub-FSM
                new_state = CastState()
                new_state.spell_fsm.context['spell'] = intent.spell
                print(f"[SpellCastingSystem] NPC {eid} switch FSM a CastState con hechizo '{intent.spell}'.")
                npc_state.fsm.change_state(new_state, entity_proxy)

            # 3) Eliminar intención procesada
            wants.pop(eid, None)
            print(f"[SpellCastingSystem] Intención de hechizo de entidad {eid} eliminada.\n")

        # Procesar spawns diferidos del jugador
        now = time.time()
        remaining = []
        for caster, spell, offset, t in self.pending_spawns:
            if now >= t:
                cfg = SPELLS.get(spell, {})
                resolver = SPELL_RESOLVERS.get(cfg.get('type', 'projectile'))
                print(f"[DEBUG][SpellCastingSystem] resolve spell={spell} type={cfg.get('type')} caster={caster} at {now:.3f}")
                resolver.resolve(world, caster, {'offset': offset}, cfg, camera)
            else:
                remaining.append((caster, spell, offset, t))
        self.pending_spawns = remaining

    # def _spawn_fireball(self, world, caster, spell_key, direction, spawn_pos):
    #     cfg = SPELLS.get(spell_key, {})
    #     dx, dy = direction
    #     sx, sy = spawn_pos
    #     fid = world.create_entity()
    #     world.components['Position'][fid] = Position(sx, sy)
    #     spd = cfg.get('speed', 0)
    #     world.components['Velocity'][fid] = Velocity(dx*spd, dy*spd)
    #     world.components['FireballComponent'][fid] = FireballComponent(
    #         dx*spd, dy*spd,
    #         damage=cfg.get('damage', 0),
    #         lifespan=cfg.get('lifespan', 0),
    #         caster=caster
    #     )
    #     img = pygame.image.load(cfg.get('sprite')).convert_alpha()
    #     world.components['Sprite'][fid] = Sprite(img)
    #     world.components['Scale'][fid] = Scale(scale=cfg.get('scale', 1.0))
