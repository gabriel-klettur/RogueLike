from __future__ import annotations

"""Tema (colores y textos) para los overlays del Spawner Editor."""

# Alphas
FOCUS_DIM_ALPHA = 140            # Oscurecer foco de visuales
MODAL_BACKDROP_ALPHA = 160       # Backdrop para confirmaciones y pickers

# Colores básicos
COLOR_BLACK = (0, 0, 0)
COLOR_WHITE = (255, 255, 255)
COLOR_HINT = (0, 200, 255)       # Texto de hint

# Overlays de confirmación (zona)
ZONE_PANEL_BG = (20, 20, 20)
ZONE_PANEL_BORDER = (200, 200, 200)

# Overlays de confirmación (delete instancia)
DELETE_PANEL_BG = (30, 0, 0)
DELETE_PANEL_BORDER = (220, 60, 60)
DELETE_TEXT = (255, 200, 200)

# Hover/selección
HOVER_RECT = (0, 255, 255)
SELECT_RECT = (255, 215, 0)

# Controles de edificio
HANDLE_BORDER = (0, 0, 0)
HANDLE_DELETE_BG = (220, 40, 40)
HANDLE_DELETE_HOVER = (255, 255, 0)
HANDLE_RESET_BG = (255, 255, 255)
HANDLE_RESET_HOVER = (0, 255, 255)
HANDLE_RESIZE_BG = (80, 120, 255)
HANDLE_RESIZE_HOVER = (255, 0, 255)
HANDLE_DECORATIVE = (255, 255, 0)

# Textos
HINT_TEXT = "Spawner Editor (RMB drag to move)"
ZONE_CONFIRM_LINE_1 = "Move spawner to zone '{prop_zone}'?"
ZONE_CONFIRM_LINE_2 = "Original zone: '{orig_zone}'"
ZONE_CONFIRM_LINE_3 = "Press Y/Enter to confirm, N/Esc to cancel"
DELETE_CONFIRM_LINE_1 = "Delete spawner instance?"
DELETE_CONFIRM_LINE_3 = "Press Y/Enter to confirm, N/Esc to cancel"
