# roguelike_project/systems/z_layer/config.py

"""
Configuración central de capas Z para renderizado y lógica.
Las capas se ordenan visualmente de menor a mayor.
"""

Z_LAYERS = {
    "background": 0,       # Por ejemplo, niebla o decoraciones del fondo
    "ground": 1,           # Piso (tiles)
    "low_object": 2,       # Obstáculos bajos, árboles cortos
    "high_object": 3,      # Edificios, estructuras grandes
    "player": 4,           # Jugador, enemigos, NPCs en piso
    "floating": 5,         # Entidades flotando, proyectiles mágicos
    "sky": 6,              # Elementos atmosféricos (rayos, fuego, lluvia, etc.)
    "ui": 10               # Cualquier render que va sobre todo (crosshair, HUD)
}

DEFAULT_Z = Z_LAYERS["ground"]