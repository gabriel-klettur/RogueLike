"""
Module: input_component.py
Componente que almacena el estado de entrada de la entidad.
"""
class InputComponent:
    """
    Componente que guarda las acciones de entrada (movimiento, ataque, habilidades).
    """
    def __init__(self):
        # Movimiento: valores en [-1,0,1]
        self.move_x: int = 0
        self.move_y: int = 0
        # Acciones de combate
        self.attack: bool = False
        # Habilidades secundarias
        self.spell_lightball: bool = False
        self.spell_slash: bool = False
        self.spell_healing_aura: bool = False
        self.spell_darkball: bool = False
        self.spell_iceball: bool = False
        self.spell_lightning: bool = False
        self.spell_arcane_flame: bool = False
        self.spell_firework_launch: bool = False
        self.spell_smoke: bool = False
        self.spell_smoke_emitter: bool = False
        self.click: bool = False
# Path: src/roguelike_game/ecs/components/input_component.py