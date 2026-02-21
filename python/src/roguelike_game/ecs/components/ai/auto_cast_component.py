"""
Componente ECS para auto-casteo periódico de hechizos por NPCs.
Admite uno o varios autocasts independientes.
"""
from __future__ import annotations

import time


class AutoCastComponent:
    def __init__(self, spell: str | None = None, period_s: float | None = None, meta: dict | None = None, entries: list | None = None):
        # Modo legado (un solo autocast)
        self.spell = str(spell) if spell else None
        self.period_s = float(period_s) if period_s is not None else None
        self.last_cast_ts: float = 0.0
        # Opcional: habilitar/deshabilitar en runtime
        self.enabled: bool = True
        # Metadatos/overrides opcionales (p.ej., scale o scale_multiplier)
        self.meta: dict = meta or {}
        # Modo nuevo: lista de entradas
        # Cada entrada: {
        #   'spell': str,
        #   'period_s': float (opcional), 'min_period_s': float (opcional), 'max_period_s': float (opcional),
        #   'channel_s': float (opcional), 'wire_from': [r,g,b], 'wire_to': [r,g,b], 'target': 'player'|'self', ...,
        #   'meta': dict, 'last_cast_ts': float
        # }
        self.entries: list = []
        if isinstance(entries, list):
            # Deep copy defensivo simplificado
            self.entries = [dict(e) for e in entries]
        elif self.spell is not None:
            self.entries = [{
                'spell': self.spell,
                'period_s': float(self.period_s if self.period_s is not None else 2.0),
                'meta': dict(self.meta),
                'last_cast_ts': 0.0,
            }]
        # Estado de canalizado activo (si lo hay)
        # {'spell': str, 'start_ts': float, 'duration': float, 'wire_from': (r,g,b), 'wire_to': (r,g,b), 'target': 'player'|'self'}
        self.active_channel: dict | None = None
