
# Path: src/roguelike_game/game/z_layer_manager.py
from roguelike_engine.config_z_layer import Z_LAYERS

class ZLayerManager:
    """
    Se encarga de asignar capas Z a todas las entidades,
    de acuerdo a la configuración central.
    """
    def __init__(self, z_state):
        self.z_state = z_state

    def initialize(self, state, entities):
        """
        state: GameState
        entities: objeto con atributos
          - player          
          - obstacles (iterable)
          - buildings (iterable, cada uno con .z_bottom)
        """
        zs = self.z_state
        state.z_state = zs

        # Jugador a capa 'player'
        zs.set(entities.player, Z_LAYERS["player"])

        # Edificios por su propiedad z_bottom
        for b in entities.buildings:
            zs.set(b, b.z_bottom)