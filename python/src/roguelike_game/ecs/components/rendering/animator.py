from dataclasses import dataclass, field
from typing import Dict, List, Any
import time

@dataclass
class Animator:
    """
    Componente de animación para un sprite.
    animations: mapeo estado (e.g. 'up','down') a lista de frames (pygame.Surface).
    current_state: estado activo.
    frame_idx: índice del frame actual.
    """
    animations: Dict[str, List[Any]]
    current_state: str
    frame_idx: int = 0
    prev_state: str = field(init=False)
    state_start_time: float = field(init=False)
    # Optional: precomputed masks parallel to animations (same keys and lengths)
    masks: Dict[str, List[Any]] = field(default_factory=dict)
    # Internal: index of the frame returned by last next_frame() call
    last_frame_idx: int = field(default=0, init=False)

    def __post_init__(self):
        # Initialize state tracking
        self.prev_state = self.current_state
        self.state_start_time = time.time()
        self.last_frame_idx = 0

    def next_frame(self):
        now = time.time()
        # Reset on state change
        if self.current_state != self.prev_state:
            self.prev_state = self.current_state
            self.frame_idx = 0
            self.state_start_time = now
        frames = self.animations.get(self.current_state, [])
        if not frames:
            return None
        # Si solo hay un frame, siempre retornarlo (sin animación)
        if len(frames) < 2:
            self.last_frame_idx = 0
            return frames[0]
        # Idle special: hold first frame for 1 segundos, then loop remaining frames
        if self.current_state.endswith('_idle'):
            if self.frame_idx == 0:
                # Hold first frame
                if now - self.state_start_time < 1.0:       #! Aqui se establece el tiempo de espera
                    self.last_frame_idx = 0
                    return frames[0]
                # Move to next frame after hold
                self.frame_idx = 1
                self.state_start_time = now
                self.last_frame_idx = 1
                return frames[1]
            # Loop frames 1..end
            img = frames[self.frame_idx]
            self.last_frame_idx = self.frame_idx
            self.frame_idx += 1
            if self.frame_idx >= len(frames):
                self.frame_idx = 1
            return img
        # Walk special: skip first frame, loop frames 1..end
        elif self.current_state.endswith('_walk'):
            if self.frame_idx < 1:
                self.frame_idx = 1
            img = frames[self.frame_idx]
            self.last_frame_idx = self.frame_idx
            self.frame_idx += 1
            if self.frame_idx >= len(frames):
                self.frame_idx = 1
            return img
        # Default animation pacing
        self.frame_idx %= len(frames)
        img = frames[self.frame_idx]
        self.last_frame_idx = self.frame_idx
        self.frame_idx = (self.frame_idx + 1) % len(frames)
        return img