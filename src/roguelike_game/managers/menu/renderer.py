

# Usar implementación reusable de roguelike_ui
from roguelike_ui.widgets.menu_renderer import MenuRenderer as _UI_MenuRenderer
# Sobrescribir clase MenuRenderer local con la clase reusable
MenuRenderer = _UI_MenuRenderer
