import time
from typing import Optional, Tuple


class PuddleComponent:
    """
    Componente ECS para un "puddle" (área/charco) con efecto periódico.
    """
    def __init__(
        self,
        *,
        radius: float,
        duration: float,
        tick_period: float,
        damage: float = 0.0,
        heal: float = 0.0,
        status: Optional[str] = None,
        move_speed_mult: float = 1.0,
        element: Optional[str] = None,
        color: Optional[Tuple[int, int, int]] = None,
        alpha: int = 80,
        owner: Optional[int] = None,
        spell_key: str = "",
        # Opcionales para animación por secuencia de sprites
        sequence_frames: Optional[list] = None,  # list[pygame.Surface]
        sequence_times: Optional[list] = None,   # cumulative seconds [0, t1, t2, ...]
        hold_last_frame: bool = True,
        # Expiración por colisión específica (e.g., jugador)
        expire_on_player_collision: bool = False,
    ) -> None:
        now = time.time()
        self.radius = float(radius)
        self.duration = float(duration)
        self.tick_period = max(0.05, float(tick_period))
        self.damage = float(damage)
        self.heal = float(heal)
        self.status = status
        self.move_speed_mult = float(move_speed_mult)
        self.element = element
        self.color = tuple(color) if isinstance(color, (list, tuple)) else None
        self.alpha = int(alpha)
        self.owner = owner
        self.spell_key = spell_key
        self.start_time = now
        self.last_tick_time = now
        # Animación
        self.sequence_frames = list(sequence_frames) if isinstance(sequence_frames, list) else []
        self.sequence_times = list(sequence_times) if isinstance(sequence_times, list) else []
        self.sequence_idx = 0
        self.hold_last_frame = bool(hold_last_frame)
        # Reglas de expiración
        self.expire_on_player_collision = bool(expire_on_player_collision)
