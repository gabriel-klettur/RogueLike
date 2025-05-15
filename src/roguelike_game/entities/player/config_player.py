
#!---------------------- HUD Paths ----------------------#!
HUD_RESTORE =       "assets/ui/restore_icon.png"
HUD_DASH =          "assets/ui/dash_icon.png"
HUD_SLASH =         "assets/ui/slash_icon.png"
HUD_SHIELD =        "assets/ui/shield_icon.png"
HUD_FIREWORK =      "assets/ui/firework_icon.png"
HUD_SMOKE =         "assets/ui/smoke_icon.png"
HUD_LIGHTNING =     "assets/ui/lightning_icon.png"
HUD_ARCANE_FIRE =   "assets/ui/pixel_fire_icon.png"
HUD_TELEPORT =      "assets/ui/teleport_icon.png"


#!---------------------- Configuración de Sprites ----------------------#!
# Tamaño original de cada frame en el sprite-sheet
ORIGINAL_SPRITE_SIZE = (128, 128)

# Tamaño de renderizado deseado del jugador (se usa para dibujar y colisiones)
RENDERED_SPRITE_SIZE = (64, 64)

#!---------------------- Configuración de estadísticas ----------------------#!
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

#!---------------------- Configuración de habilidades ----------------------#!
PLAYER_SKILLS = {
    "first_hero": {
        "restore": {
            "cooldown": 5.0,
            "duration": 10.0,
        },
        "shield": {
            "cooldown": 20.0,
            "duration": 10.0,
            "points": 50,
        },
        "firework": {
            "cooldown": 5.0,
            "duration": 2.0,
        },
        "smoke": {
            "cooldown": 6.0,
            "duration": 3.0,
        },
        "lightning": {
            "cooldown": 4.0,
            "duration": 1.0,
        },
        "pixel_fire": {
            "cooldown": 3.0,
            "duration": 2.0,
        }
    }
}

PLAYER_SPEED = 10  # velocidad de movimiento normal
PLAYER_DASH_SPEED = 2000  # velocidad de dash
PLAYER_DASH_COOLDOWN = 2.0  # cooldown de dash
PLAYER_DASH_DURATION = 0.2  # duración del dash
PLAYER_TELEPORT_COOLDOWN = 0.5  # cooldown de teleport
PLAYER_TELEPORT_DISTANCE = 100  # distancia de teleport
# Path: src/roguelike_game/entities/player/config_player.py