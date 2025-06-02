"""
Sistema ECS que detecta componentes 'WantsToCastSpell' y, según corresponda,
arranca la máquina de estados de hechizos para NPCs o genera instantáneamente
una fireball para el jugador.
"""
from roguelike_game.ecs.fsm.states.cast_state import CastState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy
import pygame

from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite
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
            intent = wants[eid]
            print(f"\n[SpellCastingSystem] Procesando entidad {eid} con intención de hechizo '{intent.spell}'.")

            # 1) Si la entidad con intención está en 'npcs', se trata de un NPC
            if eid in npcs:
                print(f"[SpellCastingSystem] Entidad {eid} es NPC. Iniciando sub-FSM de hechizo.")
                # Obtener el componente NPCState (que contiene la FSM)
                npc_state = npcs[eid]
                # Crear un proxy para la entidad: lo usa la FSM para acceder a world e id
                entity_proxy = _EntityProxy(world, eid)
                print(f"[SpellCastingSystem] Cambiando estado FSM del NPC {eid} a CastState.")
                # Cambiar estado de la FSM del NPC a CastState, que iniciará la sub-FSM de hechizo
                npc_state.fsm.change_state(CastState(), entity_proxy)

            else:
                # 2) Si la entidad NO está en NPCState, asumimos que es el jugador
                print(f"[SpellCastingSystem] Entidad {eid} NO es NPC. Se asume jugador. Generando fireball.")

                # 2.1) Obtener componente Position del lanzador
                pos_cmp = world.components['Position'][eid]
                print(f"[SpellCastingSystem] Posición del lanzador (jugador) = ({pos_cmp.x}, {pos_cmp.y})")

                # 2.2) Obtener posición del mouse en pantalla
                mx, my = pygame.mouse.get_pos()
                print(f"[SpellCastingSystem] Posición del mouse en pantalla = ({mx}, {my})")
                # Convertir coordenadas de pantalla (mx,my) a coordenadas de mundo:
                wx = mx / camera.zoom + camera.offset_x
                wy = my / camera.zoom + camera.offset_y
                print(f"[SpellCastingSystem] Posición objetivo en mundo = ({wx:.2f}, {wy:.2f})")

                # 2.3) Calcular vector (dx, dy) desde el lanzador hasta el click del mouse
                dx = wx - pos_cmp.x
                dy = wy - pos_cmp.y
                # Calcular magnitud para normalizar; usar 'or 1' para evitar división por cero
                length = (dx * dx + dy * dy) ** 0.5 or 1
                dir_x, dir_y = dx / length, dy / length
                print(f"[SpellCastingSystem] Vector dirección crudo = ({dx:.2f}, {dy:.2f}), longitud = {length:.2f}")
                print(f"[SpellCastingSystem] Vector dirección normalizado = ({dir_x:.2f}, {dir_y:.2f})")

                # 2.4) Calcular posición de spawn de la fireball:
                spawn_x, spawn_y = pos_cmp.x, pos_cmp.y
                sprite_cmp = world.components['Sprite'].get(eid)
                if sprite_cmp:
                    w, h = sprite_cmp.image.get_size()
                    spawn_x += w / 2
                    spawn_y += h / 2
                    print(f"[SpellCastingSystem] Ajuste spawn por sprite: nuevo punto = ({spawn_x}, {spawn_y})")
                else:
                    print(f"[SpellCastingSystem] No se encontró Sprite en entidad {eid}. Spawn en ({spawn_x}, {spawn_y}).")

                # 2.5) Obtener la configuración del hechizo desde SPELLS
                cfg = SPELLS.get(intent.spell, {})

                # 2.6) Crear una nueva entidad para la fireball
                fid = world.create_entity()
                print(f"[SpellCastingSystem] Creada entidad fireball con id {fid}.")

                # 2.7) Agregar componente Position para la fireball
                world.components['Position'][fid] = Position(spawn_x, spawn_y)
                print(f"[SpellCastingSystem] Fireball {fid} Position = ({spawn_x}, {spawn_y})")

                # 2.8) Agregar componente Velocity: velocidad = dirección × magnitud 'speed'
                speed = cfg.get('speed', 0)
                world.components['Velocity'][fid] = Velocity(dir_x * speed, dir_y * speed)
                print(f"[SpellCastingSystem] Fireball {fid} Velocity = ({dir_x * speed:.2f}, {dir_y * speed:.2f})")

                # 2.9) Agregar componente FireballComponent con parámetros de daño, duración, caster
                damage = cfg.get('damage', 0)
                lifespan = cfg.get('lifespan', 0)
                world.components['FireballComponent'][fid] = FireballComponent(
                    dir_x * speed,
                    dir_y * speed,
                    damage=damage,
                    lifespan=lifespan,
                    caster=eid
                )
                print(f"[SpellCastingSystem] Fireball {fid} FireballComponent = "
                      f"(damage={damage}, lifespan={lifespan}, caster={eid})")

                # 2.10) Agregar componente Sprite según configuración del hechizo
                sprite_path = cfg.get('sprite')
                if sprite_path:
                    world.components['Sprite'][fid] = Sprite(sprite_path)
                    print(f"[SpellCastingSystem] Spell entity {fid} Sprite cargado desde '{sprite_path}'")
                else:
                    print(f"[SpellCastingSystem] No se encontró ruta de sprite para '{intent.spell}' en la configuración")

            # 3) Una vez procesada la intención (NPC o jugador), la removemos del diccionario
            wants.pop(eid, None)
            print(f"[SpellCastingSystem] Intención de hechizo de entidad {eid} eliminada.\n")
