"""Constantes de UI del inventario.

Separadas para mejorar mantenibilidad y reutilización.
"""

BGCOLOR = (50, 50, 50)
BORDER_COLOR = (200, 200, 200)
CLOSE_BUTTON_COLOR = (200, 50, 50)
SLOT_BG_COLOR = (80, 80, 80)
SLOT_BORDER_COLOR = (150, 150, 150)
TEXT_COLOR = (255, 255, 255)
GRID_COLS = 5
GRID_ROWS = 5
PADDING = 10
SLOT_SIZE = 64
CLOSE_BUTTON_SIZE = 20
GRAB_PROGRESS_COLOR = (255, 255, 0)
GRAB_PROGRESS_ALPHA = 220
PULSE_BORDER_COLOR = (255, 215, 0)
PULSE_BASE_ALPHA = 90
PULSE_MAX_ALPHA = 200
PULSE_BASE_THICKNESS = 2
PULSE_MAX_THICKNESS = 5
PULSE_FREQ = 2.0
GRAB_SUCCESS_COLOR = (80, 220, 120)
PULSE_SUCCESS_COLOR = (80, 220, 120)
DRAG_READY_RATIO = 1.0
INCREASE_COLOR = (80, 220, 120)
DECREASE_COLOR = (230, 90, 90)
QUANTITY_FLASH_DURATION_MS = 900

# Etiquetas de pestañas para el panel de inventario (categorías)
# Orden: 0 = Equipo, 1 = Materiales, 2 = Consumibles
TABS_LABELS = [
    "Equipo",
    "Materiales",
    "Consumibles",
]

# Identificadores de ítems que se consideran moneda/oro para el footer del inventario.
# Se pueden adaptar a los IDs reales de tu catálogo.
CURRENCY_ITEM_IDS = [
    "gold",
    "coins",
    "coin",
    "gold_coin",
]
