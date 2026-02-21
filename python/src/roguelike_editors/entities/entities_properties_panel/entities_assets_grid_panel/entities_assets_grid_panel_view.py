from typing import Any, Dict, Optional, Tuple

import pygame

from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_model import (
    AssetsGridPanelModel,
)
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.cells_renderer import (
    CellsRenderer,
)



class AssetsGridPanelView:
    """
    Vista de cuadrícula de assets para el panel de propiedades.

    Dibuja el nombre de la entidad, tint, grid de thumbnails,
    resaltado de hover y selección, y muestra la ruta activa.
    """

    # Configuración de layout y colores
    _GRID_COLS: int = 3
    _BORDER_COLOR: Tuple[int, int, int] = (150, 150, 150)
    _HOVER_COLOR: Tuple[int, int, int, int] = (255, 255, 0, 80)
    _TEXT_COLOR: Tuple[int, int, int] = (255, 255, 0)
    _KEY_COLOR: Tuple[int, int, int] = (255, 255, 255)
    _NONE_TINT_COLOR: Tuple[int, int, int] = (128, 0, 128)

    def __init__(self, font: pygame.font.Font) -> None:
        """Inicializa la vista con la fuente y la caché de thumbnails."""
        self.font = font
        self.thumbnail_cache: Dict[str, Optional[pygame.Surface]] = {}
        # Renderer perezoso: se crea cuando existen los controladores externos
        self._renderer: Optional[CellsRenderer] = None
        # Atributos externos esperados:
        # - self.parent_model: AssetsGridPanelModel de nivel superior
        # - self.state_tabs_controller
        # - self.assets_subtabs_controller

    

    def draw(
        self,
        screen: pygame.Surface,
        model: AssetsGridPanelModel,
        entity_data: Dict[str, Any],
        px: int,
        py: int,
        pad: int,
        font_h: int,
        panel_w: int,
    ) -> None:
        """Dibuja el panel completo: nombre, tint, grid de thumbnails, hover y selección."""
        model.asset_cell_entries.clear()
        # logger.debug(f" draw assets grid: tint={entity_data.get('tint')}")

        # 1. Nombre de la entidad
        name_y = self._draw_entity_name(screen, entity_data, px, py, pad)

        # 2. Tint de la entidad
        tint_y = self._draw_tint(screen, entity_data, px, name_y + font_h + 2, pad)

        # 3. Grid: posición y tamaño de celda
        grid_x, grid_y, cell_size = self._compute_grid(
            px, pad, tint_y + font_h + pad, panel_w
        )

        # 4. Dibujar celdas (delegado)
        self._ensure_renderer()
        assert self._renderer is not None
        self._renderer.render_cells(screen, model, entity_data, grid_x, grid_y, cell_size)

        # 5. Ruta y highlight activos (delegado)
        self._renderer.draw_selected_path(
            screen, model, entity_data, px, pad, grid_y, cell_size
        )
        # 6. Label y combobox "Activo"
        active_label_y = grid_y + cell_size * self._GRID_COLS + pad + font_h + pad
        # Label
        label_surf = self._render_text(screen, "Activo: ", self._KEY_COLOR, (px + pad, active_label_y))
        # Valor actual
        current = entity_data.get("active_set", "sets")
        value_surf = self.font.render(current, True, self._TEXT_COLOR)
        value_x = px + pad + label_surf.get_width()
        # Dibujar valor
        screen.blit(value_surf, (value_x + pad//2, active_label_y))
        # Combobox
        combo_rect = pygame.Rect(value_x, active_label_y, value_surf.get_width() + pad, font_h)
        pygame.draw.rect(screen, self._BORDER_COLOR, combo_rect, 1)
        # Guardar rect para eventos
        model.active_set_rect = combo_rect

    def _ensure_renderer(self) -> None:
        """Construye el renderer si aún no existe."""
        if self._renderer is not None:
            return
        # Los controladores externos deben existir en la vista
        state_tabs = getattr(self, 'state_tabs_controller', None)
        assets_subtabs = getattr(self, 'assets_subtabs_controller', None)
        parent_model = getattr(self, 'parent_model', None)
        if not (state_tabs and assets_subtabs and parent_model):
            # No se puede construir aún; el caller debe asegurarse de inyectarlos antes de draw
            return
        self._renderer = CellsRenderer(
            state_tabs_controller=state_tabs,
            assets_subtabs_controller=assets_subtabs,
            parent_model=parent_model,
            thumbnail_cache=self.thumbnail_cache,
            grid_cols=self._GRID_COLS,
            border_color=self._BORDER_COLOR,
            hover_color=self._HOVER_COLOR,
            text_color=self._TEXT_COLOR,
            font=self.font,
        )

    def _render_text(
        self,
        screen: pygame.Surface,
        text: str,
        color: Tuple[int, int, int],
        pos: Tuple[int, int],
    ) -> pygame.Surface:
        """Renderiza texto con la fuente de la vista y lo blitea. Devuelve el surface.

        Útil para unificar render+blit en llamadas cortas.
        """
        surf = self.font.render(text, True, color)
        screen.blit(surf, pos)
        return surf

    def _draw_entity_name(
        self,
        screen: pygame.Surface,
        entity_data: Dict[str, Any],
        px: int,
        py: int,
        pad: int,
    ) -> int:
        """Dibuja el ID o nombre de la entidad y devuelve la coordenada Y donde quedó."""
        label = entity_data.get('id') or entity_data.get('name') or ''
        x, y = px + pad, py + pad
        self._render_text(screen, label, self._TEXT_COLOR, (x, y))
        return y

    def _draw_tint(
        self,
        screen: pygame.Surface,
        entity_data: Dict[str, Any],
        x_base: int,
        y: int,
        pad: int,
    ) -> int:
        """Dibuja 'tint:' y su valor coloreado; devuelve la coordenada Y."""
        tint = entity_data.get('tint')
        val_str = str(tint) if tint is not None else 'None'
        color = self._NONE_TINT_COLOR if tint is None else self._TEXT_COLOR
        x = x_base + pad
        key_surf = self._render_text(screen, 'tint: ', self._KEY_COLOR, (x, y))
        self._render_text(screen, val_str, color, (x + key_surf.get_width(), y))
        return y

    def _compute_grid(
        self,
        px: int,
        pad: int,
        start_y: int,
        panel_w: int,
    ) -> Tuple[int, int, int]:
        """Calcula y devuelve (grid_x, grid_y, cell_size)."""
        grid_x = px + pad
        grid_y = start_y
        grid_w = panel_w - pad * 2
        cell_size = grid_w // self._GRID_COLS
        return grid_x, grid_y, cell_size

