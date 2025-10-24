"""
Componente ECS para auto-casteo periódico de hechizos por NPCs.
"""
from __future__ import annotations

import time


class AutoCastComponent:
    def __init__(self, spell: str, period_s: float = 2.0, meta: dict | None = None):
        self.spell = str(spell)
        self.period_s = float(period_s)
        self.last_cast_ts: float = 0.0
        # Opcional: habilitar/deshabilitar en runtime
        self.enabled: bool = True
        # Metadatos/overrides opcionales (p.ej., scale o scale_multiplier)
        self.meta: dict = meta or {}
