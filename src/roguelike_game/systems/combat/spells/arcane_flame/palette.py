# Path: src/roguelike_game/systems/combat/spells/arcane_flame/palette.py
"""
Paleta y constantes para el efecto ArcaneFlame (pixel fire).
"""
CELL_SIZE = 6
FLAME_COLOR_DEPTH = 24

# Gradient inspirado en Doom PSX fire:
FLAME_COLOR_PALETTE = [
    (230, 230, 250),  # lavender
    (255, 255, 0),    # yellow
    (255, 215, 0),    # gold
    (255, 105, 180),  # hotpink
    (255, 99, 71),    # tomato
    (72, 61, 139),    # darkslateblue
    (34, 34, 34),     # dark gray (as base)
]

# Direcciones de propagación, con mayor peso hacia abajo
SPREAD_FROM = ['bottom'] * 10 + ['left'] * 2 + ['right'] * 2 + ['top']
