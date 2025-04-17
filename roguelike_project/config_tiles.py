# roguelike_project/config_tiles.py

"""
Mapeo de códigos de overlay de 3 caracteres a nombres de archivos de tiles.
"""

# Código de overlay → nombre base de asset (sin extensión)
OVERLAY_CODE_MAP: dict[str, str] = {
    "93l": "lava_2",
    "01g": "grass_1",
    # Añade aquí tantos códigos como necesites
}

# Mapeo inverso: nombre base → lista de códigos (útil en el editor)
INVERSE_OVERLAY_MAP: dict[str, list[str]] = {}
for code, name in OVERLAY_CODE_MAP.items():
    INVERSE_OVERLAY_MAP.setdefault(name, []).append(code)

# Mapeo por defecto para tiles base (solo caracteres simples)
DEFAULT_TILE_MAP: dict[str, str] = {
    "#": "wall",
    ".": "floor",
    "=": "tunnel",
    "O": "room",
    # etc.
}

