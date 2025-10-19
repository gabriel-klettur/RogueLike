import logging
from typing import Any, Dict, Optional, Tuple

import pygame
from roguelike_engine.utils.loader import load_image

from roguelike_editors.entities.entities_properties_panel.services.assets_constants import (
    SUBTAB_SET,
    SUBTAB_NO_SET,
)
from roguelike_editors.entities.entities_properties_panel.services.assets_maps import (
    GRID_ORDER_3X3 as _ORDER,
)
from roguelike_editors.entities.entities_properties_panel.services.assets_helpers import (
    build_asset_key,
    resolve_asset_path,
)

logger = logging.getLogger(__name__)


class CellsRenderer:
    """Responsable de renderizar la grilla de assets y la información seleccionada.

    Separa la lógica de dibujo para mantener la vista ligera y enfocada.
    """

    def __init__(
        self,
        *,
        state_tabs_controller: Any,
        assets_subtabs_controller: Any,
        parent_model: Any,
        thumbnail_cache: Dict[str, Optional[pygame.Surface]],
        grid_cols: int,
        border_color: Tuple[int, int, int],
        hover_color: Tuple[int, int, int, int],
        text_color: Tuple[int, int, int],
        font: pygame.font.Font,
    ) -> None:
        self.state_tabs_controller = state_tabs_controller
        self.assets_subtabs_controller = assets_subtabs_controller
        self.parent_model = parent_model
        self.thumbnail_cache = thumbnail_cache
        self._GRID_COLS = grid_cols
        self._BORDER_COLOR = border_color
        self._HOVER_COLOR = hover_color
        self._TEXT_COLOR = text_color
        self.font = font

        # Internal state for debounced logs
        self._last_active_state: Optional[str] = None
        self._last_sub_tab: Optional[str] = None
        self._last_logged_asset_cell: Optional[str] = None
        self._last_logged_path: Optional[str] = None

    # ----------------------------- public API -----------------------------
    def render_cells(
        self,
        screen: pygame.Surface,
        model: Any,
        entity_data: Dict[str, Any],
        grid_x: int,
        grid_y: int,
        cell_size: int,
    ) -> None:
        """Dibuja celdas, hover/selección y thumbnails de la grilla."""
        active_state = self.state_tabs_controller.model.active_state_tab
        sub_tab = self.assets_subtabs_controller.model.active_sub_tab

        # Log once when state or sub_tab changes
        if active_state != self._last_active_state or sub_tab != self._last_sub_tab:
            logger.debug(
                f"[DEBUG][PROPERTIES PANEL][GRID] "
                f"active_state={active_state}, sub_tab={sub_tab}"
            )
            for dir_key in _ORDER:
                if not dir_key:
                    continue
                asset_key = build_asset_key(active_state, dir_key)
                ent_id = self._current_entity_id()
                value = resolve_asset_path(
                    ent_id,
                    self.parent_model,
                    entity_data,
                    active_state,
                    dir_key,
                    sub_tab,
                )
                logger.debug(f"[DEBUG][GRID] asset_key={asset_key}, value={value}")
            self._last_active_state = active_state
            self._last_sub_tab = sub_tab

        for idx, dir_key in enumerate(_ORDER):
            row, col = divmod(idx, self._GRID_COLS)
            rect = pygame.Rect(
                grid_x + col * cell_size,
                grid_y + row * cell_size,
                cell_size,
                cell_size,
            )

            if dir_key:
                asset_key = build_asset_key(active_state, dir_key)
                model.asset_cell_entries.append((rect, asset_key))

                # Hover highlight: toda la grilla o solo hovered
                if (sub_tab == SUBTAB_SET and model.hovered_asset_cell) or (
                    model.hovered_asset_cell == asset_key
                ):
                    hover = pygame.Surface((cell_size, cell_size), pygame.SRCALPHA)
                    hover.fill(self._HOVER_COLOR)
                    screen.blit(hover, (rect.x, rect.y))

                # Thumbnails: set vs no-set
                if sub_tab == SUBTAB_SET:
                    self._draw_set_thumb(screen, model, entity_data, rect, asset_key, cell_size)
                else:
                    ent_id = self._current_entity_id()
                    path = resolve_asset_path(
                        ent_id,
                        self.parent_model,
                        entity_data,
                        active_state,
                        dir_key,
                        sub_tab,
                    )

                    if not path:
                        self._draw_inner_black(screen, rect)
                    else:
                        try:
                            import os
                            if model.hovered_asset_cell == asset_key or model.selected_asset_cell == asset_key:
                                if (
                                    asset_key != self._last_logged_asset_cell
                                    or path != self._last_logged_path
                                ):
                                    logger.debug(
                                        f"[ASSETS GRID][NO-SET] key={asset_key} path='{path}' abs={os.path.isabs(path)} exists={os.path.isfile(path)}"
                                    )
                                    self._last_logged_asset_cell = asset_key
                                    self._last_logged_path = path
                        except Exception:
                            pass

                        raw = self.thumbnail_cache.get(path)
                        if raw is None:
                            try:
                                img = load_image(path)
                                raw = pygame.transform.smoothscale(img, (cell_size - 4, cell_size - 4))
                            except Exception as e:
                                logger.exception(
                                    f"[ASSETS GRID] Error cargando thumbnail path='{path}': {e}"
                                )
                                raw = None
                            self.thumbnail_cache[path] = raw
                        if raw:
                            self._blit_tinted(screen, rect, raw, entity_data.get('tint'))
                        else:
                            self._draw_inner_black(screen, rect)

            pygame.draw.rect(screen, self._BORDER_COLOR, rect, 1)

    def draw_selected_path(
        self,
        screen: pygame.Surface,
        model: Any,
        entity_data: Dict[str, Any],
        px: int,
        pad: int,
        grid_y: int,
        cell_size: int,
    ) -> None:
        """Muestra la ruta del asset hovered/seleccionado y aplica resaltado final."""
        sel = model.hovered_asset_cell or model.selected_asset_cell
        if not sel:
            return

        sub_tab = self.assets_subtabs_controller.model.active_sub_tab
        # Resolver ruta igual que en celdas
        path: Optional[str]
        if sel.startswith('asset_'):
            try:
                _, ui_state, dir_key = sel.split('_', 2)
            except ValueError:
                path = entity_data.get(sel)
            else:
                ent_id = self._current_entity_id()
                path = resolve_asset_path(
                    ent_id,
                    self.parent_model,
                    entity_data,
                    ui_state,
                    dir_key,
                    sub_tab,
                )
        else:
            path = entity_data.get(sel)

        if sub_tab == SUBTAB_NO_SET and path is None:
            info = 'No asignado en no-sets'
        else:
            info = str(path) if path is not None else 'None'
        info_x = px + pad
        info_y = grid_y + cell_size * self._GRID_COLS + pad
        self._render_text(screen, info, self._TEXT_COLOR, (info_x, info_y))

        if model.selected_asset_cell:
            if sub_tab == SUBTAB_SET:
                for rect, _ in model.asset_cell_entries:
                    pygame.draw.rect(screen, self._TEXT_COLOR, rect, 2)
            else:
                for rect, key in model.asset_cell_entries:
                    if key == model.selected_asset_cell:
                        pygame.draw.rect(screen, self._TEXT_COLOR, rect, 2)

    # ----------------------------- helpers -----------------------------
    def _current_entity_id(self) -> Optional[str]:
        pm = self.parent_model
        return pm.hovered_entity_id or pm.selected_id if pm else None

    def _render_text(
        self,
        screen: pygame.Surface,
        text: str,
        color: Tuple[int, int, int],
        pos: Tuple[int, int],
    ) -> pygame.Surface:
        surf = self.font.render(text, True, color)
        screen.blit(surf, pos)
        return surf

    def _draw_inner_black(self, screen: pygame.Surface, rect: pygame.Rect) -> None:
        inner = rect.inflate(-2, -2)
        pygame.draw.rect(screen, (0, 0, 0), inner)

    def _draw_set_thumb(
        self,
        screen: pygame.Surface,
        model: Any,
        entity_data: Dict[str, Any],
        rect: pygame.Rect,
        key: str,
        size: int,
    ) -> None:
        anim = model.animators.get(key)
        frame = model.last_frames.get(key)
        if anim and frame:
            self._blit_tinted(screen, rect, frame, entity_data.get('tint'))
            return

        is_pending = False
        try:
            ent_id = self._current_entity_id()
            m_entry = (
                self.parent_model.monsters.get(ent_id)
                if isinstance(self.parent_model.monsters, dict)
                else None
            )
            is_pending = bool(isinstance(m_entry, dict) and m_entry.get('__pending__'))
        except Exception:
            is_pending = False

        if is_pending:
            path = entity_data.get(key)
            if path:
                raw = self.thumbnail_cache.get(path)
                if raw is None:
                    try:
                        img = load_image(path)
                        raw = pygame.transform.smoothscale(img, (size - 4, size - 4))
                    except Exception as e:
                        logger.exception(
                            f"[ASSETS GRID] Error cargando thumbnail (pending SET) path='{path}': {e}"
                        )
                        raw = None
                    self.thumbnail_cache[path] = raw
                if raw:
                    self._blit_tinted(screen, rect, raw, entity_data.get('tint'))
                else:
                    self._draw_inner_black(screen, rect)
            else:
                self._draw_inner_black(screen, rect)
        else:
            self._draw_inner_black(screen, rect)

    def _blit_tinted(
        self,
        screen: pygame.Surface,
        rect: pygame.Rect,
        image: pygame.Surface,
        tint: Optional[Tuple[int, ...]],
    ) -> None:
        thumb = image.copy()
        if tint:
            c = tuple(int(v) for v in tint[:3])
            thumb.fill(c, special_flags=pygame.BLEND_RGB_MULT)
        tx = rect.x + (rect.width - thumb.get_width()) // 2
        ty = rect.y + (rect.height - thumb.get_height()) // 2
        screen.blit(thumb, (tx, ty))
