from dataclasses import dataclass
from typing import Dict, List, Any

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

    def next_frame(self):
        frames = self.animations.get(self.current_state, [])
        if not frames:
            return None
        img = frames[self.frame_idx]
        self.frame_idx = (self.frame_idx + 1) % len(frames)
        return img
