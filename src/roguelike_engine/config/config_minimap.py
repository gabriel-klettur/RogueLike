MINIMAP_WIDTH = 150
MINIMAP_HEIGHT = 150
MINIMAP_ZOOM = 1
MINIMAP_PADDING = (20, 20)
 
# Transparencia del fondo del minimapa (aplicada a la surface principal)
MINIMAP_BG_ALPHA = 180

# Intervalos de actualización en milisegundos por capa
# Fondo de tiles (más pesado): 1s por defecto
MINIMAP_TILE_UPDATE_MS = 1000
# Edificios (semi-estático): 1.5s por defecto
MINIMAP_BUILDINGS_UPDATE_MS = 1500
# Entidades (dinámico): 150ms por defecto
MINIMAP_ENTITIES_UPDATE_MS = 150

# Límite de entidades a dibujar para evitar saturación
MINIMAP_MAX_ENTITIES = 400

# Paleta de colores del minimapa
MINIMAP_COLORS = {
    "bg": (10, 10, 10),
    "player": (0, 255, 0),
    "building": (120, 120, 120),
    "ally": (0, 200, 255),
    "enemy": (255, 80, 80),
    "neutral": (255, 255, 0),
}

# Zonas del mapa (para bordes en el minimapa)
MINIMAP_ZONE_COLORS = {
    "lobby": (255, 255, 0),      # amarillo
    "dungeon": (0, 255, 0),     # verde
    "default": (200, 200, 200), # gris
}
MINIMAP_ZONE_BORDER_WIDTH = 1

# UI de botones para capas
MINIMAP_BTN_SIZE = (18, 18)
MINIMAP_BTN_MARGIN = 4  # separación entre botones y desde el borde
MINIMAP_BTN_BG = (30, 30, 30)
MINIMAP_BTN_BG_ACTIVE = (60, 120, 60)
MINIMAP_BTN_BG_INACTIVE = (60, 60, 60)
MINIMAP_BTN_BORDER = (200, 200, 200)
MINIMAP_BTN_BORDER_HOVER = (255, 215, 0)
MINIMAP_BTN_TEXT = (230, 230, 230)
