"""
Module: player_tag.py
Componente para marcar la entidad como jugador.
"""
class PlayerTagComponent:
    """
    Componente vacío que etiqueta la entidad como jugador.
    """
    def __init__(self, class_name=None):
        # Almacena la clase de jugador para configuraciones específicas
        self.class_name = class_name
# Path: src/roguelike_game/ecs/components/core/player_tag.py