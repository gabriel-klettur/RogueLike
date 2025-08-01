import pygame
import logging
from typing import Dict, List, Optional, Tuple

from roguelike_engine.utils.loader import load_image
from roguelike_ui.widgets.hover import draw_hover
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_model import (
    AssetsGridPanelModel,
)


class AssetsGridPanelView:
    """Vista de cuadrícula de assets para el panel de propiedades."""

    # Configuración de layout y colores
    _ORDER: List[Optional[str]] = ['nw', 'n', 'ne', 'w', None, 'e', 'sw', 's', 'se']
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
        # Track last active state and sub_tab for one-time logging
        self._last_active_state: Optional[str] = None
        self._last_sub_tab: Optional[str] = None
        # Atributos externos esperados:
        # - state_tabs_controller
        # - set_ot_assets_tab_controller

    def draw(
        self,
        screen: pygame.Surface,
        model: AssetsGridPanelModel,
        entity_data: dict,
        px: int,
        py: int,
        pad: int,
        font_h: int,
        panel_w: int
    ) -> None:
        """Dibuja el panel completo: nombre, tint, grid de thumbnails, hover y selección."""
        model.asset_cell_entries.clear()

        # 1. Nombre de la entidad
        name_y = self._draw_entity_name(screen, entity_data, px, py, pad)

        # 2. Clave y valor de tint
        tint_y = self._draw_tint(screen, entity_data, px, name_y + font_h + 2, pad)

        # 3. Grid: posición y tamaño de celda
        grid_x, grid_y, cell_size = self._compute_grid(px, pad, tint_y + font_h + pad, panel_w)

        # 4. Dibujar celdas
        self._draw_cells(screen, model, entity_data, grid_x, grid_y, cell_size)

        # 5. Ruta y highlight activos
        self._draw_selected_path(screen, model, entity_data, px, pad, grid_y, cell_size)

    def _draw_entity_name(
        self,
        screen: pygame.Surface,
        entity_data: dict,
        px: int,
        py: int,
        pad: int
    ) -> int:
        """Dibuja el ID o nombre de la entidad y devuelve la coordenada Y donde quedó."""
        ent_id = entity_data.get('id') or entity_data.get('name') or ''
        surf = self.font.render(ent_id, True, self._TEXT_COLOR)
        x, y = px + pad, py + pad
        screen.blit(surf, (x, y))
        return y

    def _draw_tint(
        self,
        screen: pygame.Surface,
        entity_data: dict,
        x_base: int,
        y: int,
        pad: int
    ) -> int:
        """Dibuja 'tint:' y su valor coloreado; devuelve la coordenada Y."""
        tint = entity_data.get('tint')
        val_str = str(tint) if tint is not None else 'None'
        key_surf = self.font.render('tint: ', True, self._KEY_COLOR)
        color = self._NONE_TINT_COLOR if tint is None else self._TEXT_COLOR
        val_surf = self.font.render(val_str, True, color)
        x = x_base + pad
        screen.blit(key_surf, (x, y))
        screen.blit(val_surf, (x + key_surf.get_width(), y))
        return y

    def _compute_grid(
        self,
        px: int,
        pad: int,
        start_y: int,
        panel_w: int
    ) -> Tuple[int, int, int]:
        """Calcula y devuelve (grid_x, grid_y, cell_size)."""
        grid_x = px + pad
        grid_y = start_y
        grid_w = panel_w - pad * 2
        cell_size = grid_w // self._GRID_COLS
        return grid_x, grid_y, cell_size

    def _draw_cells(
        self,
        screen: pygame.Surface,
        model: AssetsGridPanelModel,
        entity_data: dict,
        grid_x: int,
        grid_y: int,
        cell_size: int
    ) -> None:
        """Dibuja cada celda, aplica hover y thumbnails según sub-tab."""
        active_state = self.state_tabs_controller.model.active_state_tab
        sub_tab = self.set_ot_assets_tab_controller.model.active_sub_tab

        # Log once when state or sub_tab changes
        if active_state != self._last_active_state or sub_tab != self._last_sub_tab:
            logging.debug(f"[DEBUG][PROPERTIES PANEL][GRID] active_state={active_state}, sub_tab={sub_tab}")
            for dir_key in self._ORDER:
                if dir_key:
                    asset_key = f"asset_{active_state}_{dir_key}"
                    value = entity_data.get(asset_key)
                    logging.debug(f"[DEBUG][PROPERTIES PANEL][GRID] asset_key={asset_key}, value={value}")
            self._last_active_state = active_state
            self._last_sub_tab = sub_tab


        for idx, dir_key in enumerate(self._ORDER):
            row, col = divmod(idx, self._GRID_COLS)
            rect = pygame.Rect(
                grid_x + col * cell_size,
                grid_y + row * cell_size,
                cell_size,
                cell_size
            )

            if dir_key:
                asset_key = f"asset_{active_state}_{dir_key}"
                model.asset_cell_entries.append((rect, asset_key))


                # Hover highlight: toda la grilla o solo hovered
                if (sub_tab == 'asset set' and model.hovered_asset_cell) or model.hovered_asset_cell == asset_key:
                    hover = pygame.Surface((cell_size, cell_size), pygame.SRCALPHA)
                    hover.fill(self._HOVER_COLOR)
                    screen.blit(hover, (rect.x, rect.y))

                # Dibujar thumbnail: animación o ruta
                if sub_tab == 'asset set':
                    self._draw_set_thumb(screen, model, entity_data, rect, asset_key, cell_size)
                else:
                    # Render no-set paths using merged entity_data
                    path = entity_data.get(asset_key)
                    if not path:
                        inner = rect.inflate(-2, -2)
                        pygame.draw.rect(screen, (0, 0, 0), inner)
                    else:
                        raw = self.thumbnail_cache.get(path)
                        if raw is None:
                            try:
                                img = load_image(path)
                                raw = pygame.transform.smoothscale(img, (cell_size - 4, cell_size - 4))
                            except Exception:
                                raw = None
                            self.thumbnail_cache[path] = raw
                        if raw:
                            self._blit_tinted(screen, rect, raw, entity_data.get('tint'))

            # Borde de celda
            pygame.draw.rect(screen, self._BORDER_COLOR, rect, 1)

    def _draw_set_thumb(
        self,
        screen: pygame.Surface,
        model: AssetsGridPanelModel,
        entity_data: dict,
        rect: pygame.Rect,
        key: str,
        cell_size: int
    ) -> None:
        """Dibuja animación si existe; si no, cae a thumbnail por ruta."""
        anim = model.animators.get(key)
        frame = model.last_frames.get(key)
        if anim and frame:
            self._blit_tinted(screen, rect, frame, entity_data.get('tint'))
        else:
            # Fallback a thumbnail de ruta
            self._draw_path_thumb(screen, entity_data, rect, key, cell_size)

    def _draw_path_thumb(
        self,
        screen: pygame.Surface,
        entity_data: dict,
        rect: pygame.Rect,
        key: str,
        cell_size: int
    ) -> None:
        """Carga/cacha imagen de ruta, aplica tint, o limpia si no hay asset."""
        path = entity_data.get(key)
        if not path:
            inner = rect.inflate(-2, -2)
            pygame.draw.rect(screen, (0, 0, 0), inner)
            return

        raw = self.thumbnail_cache.get(path)
        if raw is None:
            try:
                img = load_image(path)
                raw = pygame.transform.smoothscale(img, (cell_size - 4, cell_size - 4))
            except Exception:
                raw = None
            self.thumbnail_cache[path] = raw

        if raw:
            self._blit_tinted(screen, rect, raw, entity_data.get('tint'))

    def _blit_tinted(
        self,
        screen: pygame.Surface,
        rect: pygame.Rect,
        image: pygame.Surface,
        tint: Optional[Tuple[int, ...]]
    ) -> None:
        """Aplica tint al surface y lo centra dentro del rect."""
        thumb = image.copy()
        if tint:
            c = tuple(tint) if len(tint) in (3, 4) else (*tint, 255)
            thumb.fill(c, special_flags=pygame.BLEND_RGBA_MULT)
        tx = rect.x + (rect.width - thumb.get_width()) // 2
        ty = rect.y + (rect.height - thumb.get_height()) // 2
        screen.blit(thumb, (tx, ty))

    def _draw_selected_path(
        self,
        screen: pygame.Surface,
        model: AssetsGridPanelModel,
        entity_data: dict,
        px: int,
        pad: int,
        grid_y: int,
        cell_size: int
    ) -> None:
        """Muestra ruta del asset hovered/seleccionado y aplica highlight final."""
        sel = model.hovered_asset_cell or model.selected_asset_cell
        if not sel:
            return

        sub_tab = self.set_ot_assets_tab_controller.model.active_sub_tab
        path = entity_data.get(sel)
        if sub_tab == 'no-set' and path is None:
            return

        info = str(path) if path is not None else 'None'
        info_surf = self.font.render(info, True, self._TEXT_COLOR)
        info_x = px + pad
        info_y = grid_y + cell_size * self._GRID_COLS + pad
        screen.blit(info_surf, (info_x, info_y))

        if model.selected_asset_cell:
            if sub_tab == 'asset set':
                for rect, _ in model.asset_cell_entries:
                    pygame.draw.rect(screen, self._TEXT_COLOR, rect, 2)
            else:
                for rect, key in model.asset_cell_entries:
                    if key == model.selected_asset_cell:
                        pygame.draw.rect(screen, self._TEXT_COLOR, rect, 2)
