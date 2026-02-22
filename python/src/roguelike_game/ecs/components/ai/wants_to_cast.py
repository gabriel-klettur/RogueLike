"""
Evento: indicador de lanzamiento de hechizo.
"""

class WantsToCastSpell:
    def __init__(self, caster: int, spell: str, target: tuple = None, direction: tuple = None, meta: dict | None = None):
        self.caster = caster
        self.spell = spell
        self.target = target
        self.direction = direction
        # Metadatos/overrides opcionales (p.ej., scale de sprite para proyectiles)
        self.meta = meta or {}

# Path: src/roguelike_game/ecs/components/ai/wants_to_cast.py