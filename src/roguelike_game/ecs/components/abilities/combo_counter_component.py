import time
from dataclasses import dataclass, field
from typing import Dict


@dataclass
class ComboCounterComponent:
    """
    Estado de combo por entidad atacante (normalmente el jugador).

    - current: contador de hits actuales dentro de la ventana.
    - window_s: duración de la ventana que se refresca en cada golpe válido.
    - window_end_time: instante (epoch) en el que expira el combo si no hay nuevo golpe.
    - last_hit_time_by_target: anti-spam por objetivo para evitar múltiples conteos instantáneos.
    - same_target_cooldown_s: tiempo mínimo entre conteos al MISMO objetivo.
    - best: récord de combo (opcional para UI/logros).
    - min_window_s: ventana mínima al aplicar dificultad progresiva.
    - difficulty_increase_per_hit: incremento de dificultad por hit (reduce ventana).
    - break_flash_end_time: timestamp hasta el cual se debe mostrar flash/fade por ruptura.
    - break_flash_duration_s: duración del flash en segundos para cálculo de alpha.
    """
    current: int = 0
    window_s: float = 2.0
    window_end_time: float = 0.0
    last_hit_time_by_target: Dict[int, float] = field(default_factory=dict)
    same_target_cooldown_s: float = 0.5
    best: int = 0
    last_target_id: int | None = None
    min_window_s: float = 0.3
    difficulty_increase_per_hit: float = 0.05
    break_flash_end_time: float = 0.0
    break_flash_duration_s: float = 0.3
    total_completed: int = 0
    last_completed_count: int = 0
    last_window_start_time: float = 0.0
    last_window_duration: float = 0.0
    # Kills logrados manteniendo el combo activo
    kill_combo_current: int = 0
    kill_combo_best: int = 0

    def is_active(self, now: float | None = None) -> bool:
        now = time.time() if now is None else now
        return self.current > 0 and now < self.window_end_time

    def reset(self):
        self.current = 0
        self.window_end_time = 0.0
        self.last_hit_time_by_target.clear()
        self.last_window_start_time = 0.0
        self.last_window_duration = 0.0
        self.kill_combo_current = 0

    def _effective_window_for_count(self, n: int) -> float:
        """Devuelve la ventana efectiva para un combo de longitud n (n>=1).
        Aplica reducción multiplicativa por dificultad y límite mínimo.
        """
        if n <= 1:
            return float(self.window_s)
        # clamp dificultad a [0, 0.95] para evitar colapso instantáneo
        diff = max(0.0, min(float(self.difficulty_increase_per_hit), 0.95))
        base = float(self.window_s)
        # ventana = base * (1 - diff)^(n-1)
        effective = base * ((1.0 - diff) ** (max(0, n - 1)))
        return max(float(self.min_window_s), effective)

    def on_valid_hit(self, target_eid: int, at_time: float | None = None):
        t = time.time() if at_time is None else at_time
        self.current += 1
        if self.current > self.best:
            self.best = self.current
        # Usar ventana efectiva según dificultad progresiva
        eff = self._effective_window_for_count(self.current)
        self.window_end_time = t + eff
        self.last_window_start_time = t
        self.last_window_duration = eff
        self.last_hit_time_by_target[target_eid] = t
        self.last_target_id = target_eid
