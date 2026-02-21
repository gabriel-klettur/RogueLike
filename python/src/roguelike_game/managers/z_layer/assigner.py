"""
Asignador de capas Z: asigna capas a player, obstáculos y edificios.
"""
from roguelike_engine.config.config_z_layer import Z_LAYERS

class ZLayerAssigner:
    """
    Asigna capas Z a las entidades según configuración.
    """
    def assign(self, z_state, entities):
        # Jugador
        z_state.set(entities.player, Z_LAYERS["player"])

        # Obstáculos
        for obs in getattr(entities, 'obstacles', []):
            z_state.set(obs, Z_LAYERS.get("low_object", 0))

        # Edificios según su z_bottom
        for b in getattr(entities, 'buildings', []):
            z_state.set(b, b.z_bottom)
