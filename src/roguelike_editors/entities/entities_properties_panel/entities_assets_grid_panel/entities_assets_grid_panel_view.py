import logging
from typing import Any, Dict, List, Optional, Tuple

import pygame
from roguelike_engine.utils.loader import load_image

from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_model import (
    AssetsGridPanelModel,
)
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
        self._last_active_state: Optional[str] = None
        self._last_sub_tab: Optional[str] = None
        self._tint_logged_once: bool = False
        # Atributos externos esperados:
        # - self.parent_model: AssetsGridPanelModel de nivel superior
        # - self.state_tabs_controller
        # - self.assets_subtabs_controller

    def _current_entity_id(self) -> Optional[str]:
        """Devuelve el id actual (hovered o seleccionado) de la entidad en el panel."""
        pm = getattr(self, 'parent_model', None)
        if pm is None:
            return None
        return pm.hovered_entity_id or pm.selected_id

    def _draw_inner_black(self, screen: pygame.Surface, rect: pygame.Rect) -> None:
        """Rellena de negro el interior de una celda con un margen de 1px."""
        inner = rect.inflate(-2, -2)
        pygame.draw.rect(screen, (0, 0, 0), inner)

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

        # 4. Dibujar celdas
        self._draw_cells(screen, model, entity_data, grid_x, grid_y, cell_size)

        # 5. Ruta y highlight activos
        self._draw_selected_path(
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

    def _draw_cells(
        self,
        screen: pygame.Surface,
        model: AssetsGridPanelModel,
        entity_data: Dict[str, Any],
        grid_x: int,
        grid_y: int,
        cell_size: int,
    ) -> None:
        """Dibuja cada celda, aplica hover y thumbnails según sub-tab."""
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
                ent_id = (
                    self.parent_model.hovered_entity_id
                    or self.parent_model.selected_id
                )
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

                # Dibujar thumbnail: animación o ruta
                if sub_tab == SUBTAB_SET:
                    self._draw_set_thumb(
                        screen, model, entity_data, rect, asset_key, cell_size
                    )
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
                                logger.debug(
                                    f"[ASSETS GRID][NO-SET] key={asset_key} path='{path}' abs={os.path.isabs(path)} exists={os.path.isfile(path)}"
                                )
                        except Exception:
                            pass
                        raw = self.thumbnail_cache.get(path)
                        if raw is None:
                            try:
                                img = load_image(path)
                                raw = pygame.transform.smoothscale(
                                    img, (cell_size - 4, cell_size - 4)
                                )
                            except Exception as e:
                                logger.exception(
                                    f"[ASSETS GRID] Error cargando thumbnail path='{path}': {e}"
                                )
                                raw = None
                            self.thumbnail_cache[path] = raw
                        if raw:
                            self._blit_tinted(
                                screen, rect, raw, entity_data.get('tint')
                            )
                        else:
                            self._draw_inner_black(screen, rect)

            pygame.draw.rect(screen, self._BORDER_COLOR, rect, 1)

    def _draw_set_thumb(
        self,
        screen: pygame.Surface,
        model: AssetsGridPanelModel,
        entity_data: Dict[str, Any],
        rect: pygame.Rect,
        key: str,
        size: int,
    ) -> None:
        """Dibuja animación para Asset-Set; si no hay frames, pinta la celda en negro.

        Importante: no hacer fallback a rutas aplanadas (no-sets). En la subpestaña
        'Asset-Set' sólo deben mostrarse sprites de set; si no existen, la celda
        debe quedar vacía (negra).
        """
        anim = model.animators.get(key)
        frame = model.last_frames.get(key)
        if anim and frame:
            self._blit_tinted(screen, rect, frame, entity_data.get('tint'))
        else:
            # Fallback de vista SOLO para monstruos pendientes (no confirmados):
            # mostrar el sheet asignado como vista previa si existe, ya que
            # la caché runtime aún no tiene frames construidos.
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

    def _draw_path_thumb(
        self,
        screen: pygame.Surface,
        entity_data: Dict[str, Any],
        rect: pygame.Rect,
        key: str,
        size: int,
    ) -> None:
        """Carga/cacha imagen de ruta, aplica tint, o limpia si no hay asset."""
        path = entity_data.get(key)
        if not path:
            self._draw_inner_black(screen, rect)
            return

        raw = self.thumbnail_cache.get(path)
        if raw is None:
            try:
                img = load_image(path)
                raw = pygame.transform.smoothscale(img, (size - 4, size - 4))
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
        tint: Optional[Tuple[int, ...]],
    ) -> None:
        """Aplica tint al surface y lo centra dentro del rect."""
        # logger.debug(f" rect={rect} tint={tint}")
        
        thumb = image.copy()

        if tint:
            # Ensure integer RGB tuple for tint and use RGB_MULT for proper color multiplication
            c = tuple(int(v) for v in tint[:3])
            
            thumb.fill(c, special_flags=pygame.BLEND_RGB_MULT)
        tx = rect.x + (rect.width - thumb.get_width()) // 2
        ty = rect.y + (rect.height - thumb.get_height()) // 2
        screen.blit(thumb, (tx, ty))

    def _draw_selected_path(
        self,
        screen: pygame.Surface,
        model: AssetsGridPanelModel,
        entity_data: Dict[str, Any],
        px: int,
        pad: int,
        grid_y: int,
        cell_size: int,
    ) -> None:
        """Muestra ruta del asset hovered/seleccionado y aplica highlight final."""
        sel = model.hovered_asset_cell or model.selected_asset_cell
        if not sel:
            return

        sub_tab = self.assets_subtabs_controller.model.active_sub_tab
        # Resolver la ruta de la misma forma que para las celdas
        path: Optional[str]
        if sel.startswith('asset_'):
            try:
                _, ui_state, dir_key = sel.split('_', 2)
            except ValueError:
                # Fallback si el formato no es el esperado
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
        # Mostrar placeholder específico cuando no hay asignación en no-sets
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
