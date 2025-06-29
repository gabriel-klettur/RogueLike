# Path: src/roguelike_game/ecs/components/transform/z_layer.py
import typing

class ZLayer:
    """
    Componente para asignar capa Z de renderizado y lógica.
    layer: int
    """
    def __init__(self, layer: int):
        self.layer = layer