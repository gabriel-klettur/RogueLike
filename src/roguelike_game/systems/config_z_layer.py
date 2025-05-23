# Path: src/roguelike_game/systems/z_layer/config_z_layer.py

"""
Configuración central de capas Z para renderizado y lógica.
Las capas se ordenan visualmente de menor a mayor.
"""

Z_LAYERS = {
    "background": 0,       # Por ejemplo, niebla o decoraciones del fondo
    "ground": 1,           # Piso (tiles)
    "low_object": 2,       # Obstáculos bajos, árboles cortos
    "building_low": 3,      # Edificios, estructuras grandes
    "player": 4,           # Jugador, enemigos, NPCs en piso
    "building_high": 5,         # Entidades flotando, proyectiles mágicos
    "sky": 6,              # Elementos atmosféricos (rayos, fuego, lluvia, etc.)
    "ui": 10               # Cualquier render que va sobre todo (crosshair, HUD)
}

DEFAULT_Z = Z_LAYERS["ground"]
