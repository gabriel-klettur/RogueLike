from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.components.combat.death_timer import DeathTimer
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.rendering.grayscale_component import GrayscaleComponent
import time

class DeathState(State):
    """
    Estado Death: gestiona temporizador y eliminación de entidad muerta.
    """
    def enter(self, entity):
        """Registra temporizador."""
        world = entity.world
        eid = entity.id
        print(f"[DeathState.enter] eid={eid}, is_player={eid in world.components.get('PlayerTagComponent', {})}")
        # Iniciar temporizador
        world.components['DeathTimer'][eid] = DeathTimer(time.time())
        # Cambiar el sprite al de muerte para ocultar el sprite anterior
        sprite = world.components['Sprite'].get(eid)
        if sprite and hasattr(sprite, 'death_image'):
            sprite.image = sprite.death_image
            # Deshabilitar animación para no sobreescribir el sprite de muerte
            world.components.get('Animator', {}).pop(eid, None)
            world.components.get('AnimationTimer', {}).pop(eid, None)


    def execute(self, entity, dt):
        """Espera a que expire el temporizador antes de eliminar la entidad."""
        world = entity.world
        nid = entity.id
        dt_cmp = world.components['DeathTimer'][nid]
        now = time.time()
        elapsed = now - dt_cmp.start_time
        duration = dt_cmp.duration
        comps = world.components
        # Ejecutar acciones tras expiración del temporizador
        if elapsed >= duration:
            if nid in comps.get('PlayerTagComponent', {}):
                if nid not in comps.get('GrayscaleComponent', {}):
                    comps['GrayscaleComponent'][nid] = GrayscaleComponent()
            else:
                world.remove_entity(nid)
        # Debug logs: solo una vez cada segundo
        if now - dt_cmp.last_log_time >= 1.0:
            if elapsed >= duration:
                print(f"[DeathState.execute] Timer expired for eid={nid}")
                if nid in comps.get('PlayerTagComponent', {}):
                    print(f"[DeathState.execute] eid={nid} is Player -> grayscaling once")
                else:
                    print(f"[DeathState.execute] eid={nid} removed from world")
            else:
                print(f"[DeathState.execute] eid={nid}, elapsed={elapsed:.2f}/{duration} - waiting")
            dt_cmp.last_log_time = now


    def exit(self, entity):
        """Limpia si fuera necesario."""
        print(f"[DeathState.exit] eid={entity.id}")
        pass