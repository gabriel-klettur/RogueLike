from ..components.patrol import Patrol
from ..components.position import Position

import pygame

class PatrolSystem:
    """
    Sistema para actualizar la lógica de patrulla de NPCs.
    """
    def __init__(self):
        """Inicializa el sistema de patrulla. No mantiene estado interno."""
        pass

    def update(self, world):
        """Actualiza la lógica de patrulla: asigna velocidad, gestiona waypoints y actualiza animación y sprite."""
        for eid in world.get_entities_with('Position', 'Patrol', 'Velocity', 'MultiCollider'):
            pos = world.components['Position'][eid]
            patrol = world.components['Patrol'][eid]
            speed = self._get_speed(world, eid, patrol)
            self._ensure_valid_index(patrol)
            target = patrol.waypoints[patrol.current_index]
            dx, dy = self._compute_delta(pos, target)
            if self._is_at_waypoint(dx, dy, speed):
                self._reach_waypoint(pos, patrol, target)
                continue
            vx, vy, direction = self._compute_velocity(world, eid, pos, dx, dy, speed)
            vel = world.components['Velocity'][eid]
            vel.vx, vel.vy = vx, vy
            if vx == 0 and vy == 0:
                patrol.current_index = (patrol.current_index + 1) % len(patrol.waypoints)
                continue
            self._update_animator(world, eid, direction)
            self._update_sprite(world, eid, direction, patrol)

    def _get_speed(self, world, eid, patrol: Patrol) -> int:
        """Devuelve la velocidad de movimiento: prioriza MovementSpeed, fallback a patrol.speed."""
        comp = world.components['MovementSpeed'].get(eid)
        return comp.speed if comp else patrol.speed

    def _ensure_valid_index(self, patrol: Patrol):
        """Corrige el índice de waypoint: reinicia current_index si excede waypoints."""
        if patrol.current_index >= len(patrol.waypoints):
            patrol.current_index = 0

    def _compute_delta(self, pos: Position, target: tuple[int, int]) -> tuple[int, int]:
        """Calcula (dx, dy) entre posición actual y destino."""
        return target[0] - pos.x, target[1] - pos.y

    def _is_at_waypoint(self, dx: int, dy: int, speed: int) -> bool:
        """Determina si el NPC ha alcanzado el waypoint (distancia ≤ speed)."""
        return abs(dx) <= speed and abs(dy) <= speed

    def _reach_waypoint(self, pos: Position, patrol: Patrol, target: tuple[int, int]):
        """Mueve al NPC al waypoint y actualiza current_index al siguiente."""
        pos.x, pos.y = target
        patrol.current_index = (patrol.current_index + 1) % len(patrol.waypoints)

    def _compute_velocity(self, world, eid, pos: Position, dx: int, dy: int, speed: int) -> tuple[int, int, str | None]:
        """Calcula velocidad (vx, vy) y dirección válida probando colisiones: eje X primero, luego Y."""
        multi = world.components['MultiCollider'][eid]
        feet = multi.colliders.get('feet')
        if not feet:
            return 0, 0, None
        rect = pygame.Rect(pos.x + feet.offset_x, pos.y + feet.offset_y, feet.width, feet.height)
        buildings = getattr(world, 'buildings', [])  # lista de Building
        # Try X then Y
        if dx != 0:
            vx = speed if dx > 0 else -speed
            new_rect_x = rect.move(vx, 0)
            # Colisión contra tiles y edificios
            blocked_map = any(new_rect_x.colliderect(t.rect) for t in world.map_manager.solid_tiles)
            blocked_build = any(
                new_rect_x.colliderect(cell)
                for b in buildings
                for cell in getattr(b, 'collision_tiles', [])
            )
            if not blocked_map and not blocked_build:
                return vx, 0, 'right' if vx > 0 else 'left'
        if dy != 0:
            vy = speed if dy > 0 else -speed
            new_rect_y = rect.move(0, vy)
            # Colisión contra tiles y edificios
            blocked_map = any(new_rect_y.colliderect(t.rect) for t in world.map_manager.solid_tiles)
            blocked_build = any(
                new_rect_y.colliderect(cell)
                for b in buildings
                for cell in getattr(b, 'collision_tiles', [])
            )
            if not blocked_map and not blocked_build:
                return 0, vy, 'down' if vy > 0 else 'up'
        # Diagonal fallback: si ambos ejes cuestan, probar movimiento diagonal
        if dx != 0 and dy != 0:
            vx = speed if dx > 0 else -speed
            vy = speed if dy > 0 else -speed
            new_rect_d = rect.move(vx, vy)
            blocked_map = any(new_rect_d.colliderect(t.rect) for t in world.map_manager.solid_tiles)
            blocked_build = any(
                new_rect_d.colliderect(cell)
                for b in buildings
                for cell in getattr(b, 'collision_tiles', [])
            )
            if not blocked_map and not blocked_build:
                return vx, vy, None
        return 0, 0, None

    def _update_animator(self, world, eid, direction):
        """Actualiza el estado del Animator según la dirección de movimiento."""
        if direction and eid in world.components['Animator']:
            world.components['Animator'][eid].current_state = direction

    def _update_sprite(self, world, eid, direction, patrol: Patrol):
        """Actualiza la imagen del sprite basado en la dirección o waypoint."""
        if not direction:
            return
        sprite = world.components['Sprite'][eid]
        frames = patrol.sprites_by_direction.get(direction)
        if frames:
            # Acepta lista de frames o Surface individual
            if isinstance(frames, (list, tuple)) and frames:
                sprite.image = frames[0]
            elif isinstance(frames, pygame.Surface):
                sprite.image = frames
            return
        # Fallback por waypoint si coincide índice
        if patrol.current_index in patrol.sprite_per_point:
            sprite.image = patrol.sprite_per_point[patrol.current_index]
        elif patrol.default_sprite:
            sprite.image = patrol.default_sprite
