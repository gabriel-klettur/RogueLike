# Configuration constants for player (moved from entities/player/config_player.py)

# ---------------------- Configuración de Sprites ----------------------
# Tamaño original de cada frame en el sprite-sheet
ORIGINAL_SPRITE_SIZE = (128, 128)
# Tamaño de renderizado deseado del jugador (se usa para dibujar y colisiones)
RENDERED_SPRITE_SIZE = (64, 64)

# ---------------------- Configuración de estadísticas ----------------------
PLAYER_STATS = {
    "first_hero": {
        "max_health": 100,
        "max_mana": 50,
        "max_energy": 100,
    },
    "valkyria": {
        "max_health": 120,
        "max_mana": 80,
        "max_energy": 60,
    }
}


# Velocidades y cooldowns
PLAYER_SPEED = 5  # velocidad de movimiento normal

