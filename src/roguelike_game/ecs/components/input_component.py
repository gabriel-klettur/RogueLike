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
        self.skill_q: bool = False
        self.skill_e: bool = False
        self.skill_x: bool = False
        self.skill_1: bool = False
        self.skill_2: bool = False
        self.click: bool = False
