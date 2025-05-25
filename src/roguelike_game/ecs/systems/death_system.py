import time
from roguelike_engine.utils.loader import load_image
from roguelike_game.ecs.components.death_timer import DeathTimer

class DeathSystem:
    """
    Gestión de muerte de NPCs:
      1) Cambia el sprite a la imagen de cadáver.
      2) Deshabilita movimiento, colisión y animación.
      3) Elimina la entidad cuando expire el temporizador.
    """
    def __init__(self, default_duration: float = 60.0):
        """
        Inicializa el sistema de muerte.

        Args:
            default_duration (float): Segundos antes de eliminar el cadáver.
        """
        self.default_duration = default_duration

    def update(self, world):
        """
        Actualiza las entidades en estado de muerte.

        - Primera detección: aplica efectos de muerte.
        - Detecciones subsecuentes: elimina al expirar el temporizador.

        Args:
            world (NPCWorld): Instancia del mundo ECS.
        """
        now = time.time()
        dt_store = world.components['DeathTimer']
        for eid, hp in list(world.components['Health'].items()):
            if hp.current_hp > 0:
                continue
            self._process_entity(eid, world, now, dt_store)

    def _process_entity(self, eid, world, now, dt_store):
        """
        Procesa la muerte de una entidad individual.
        """
        sprite = world.components['Sprite'].get(eid)
        if not sprite:
            return

        if eid not in dt_store:
            self._handle_initial_death(eid, sprite, world, now, dt_store)
        else:
            self._handle_expiration(eid, dt_store[eid], now, world)

    def _handle_initial_death(self, eid, sprite, world, now, dt_store):
        """
        Aplica efectos de la muerte inicial:
        - Cambia al sprite de cadáver.
        - Deshabilita movimiento, colisión y animación.
        - Registra el temporizador de muerte.
        """
        death_img = self._get_death_image(sprite)
        sprite.image = death_img
        self._disable_entity_systems(eid, world)
        dt_store[eid] = DeathTimer(start_time=now, duration=self.default_duration)

    def _get_death_image(self, sprite):
        """
        Carga o reutiliza la imagen de muerte con escalado si se define.
        """
        img = getattr(sprite, 'death_image', None)
        if img:
            return img

        death_path = getattr(sprite, 'death_image_path', None)
        if death_path:
            raw = load_image(death_path)
            scale_v = getattr(sprite, 'death_scale', None)
            if scale_v:
                w, h = raw.get_size()
                raw = pygame.transform.scale(raw, (int(w * scale_v), int(h * scale_v)))
        else:
            raw = sprite.image

        sprite.death_image = raw
        return raw

    def _disable_entity_systems(self, eid, world):
        """
        Elimina componentes de movimiento, colisión y animación para la entidad.
        """
        for comp in ('Patrol', 'Velocity', 'MultiCollider', 'Animator'):
            world.components[comp].pop(eid, None)

    def _handle_expiration(self, eid, dt, now, world):
        """
        Elimina la entidad si el temporizador ha expirado.
        """
        if now - dt.start_time >= dt.duration:
            world.remove_entity(eid)
