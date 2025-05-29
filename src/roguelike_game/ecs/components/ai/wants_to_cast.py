"""
Evento: indicador de lanzamiento de hechizo.
"""

class WantsToCastSpell:
    def __init__(self, caster: int, spell: str, target: tuple = None, direction: tuple = None):
        self.caster = caster
        self.spell = spell
        self.target = target
        self.direction = direction
