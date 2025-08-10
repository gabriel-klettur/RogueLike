import pygame
from typing import Any, Dict
from roguelike_editors.items.items_title_panel.items_title_view import ItemsTitleView

class ItemEditorView:
    """
    Clase encargada de renderizar la interfaz de usuario para el editor de ítems,
    incluyendo el panel de detalles y la rejilla de iconos.
    """
    def __init__(self, assets: Dict[str, pygame.Surface], font: pygame.font.Font):
        # Diccionario de superficies de Pygame para cada ID de ítem
        self.assets = assets
        # Fuente tipográfica para renderizado de texto
        self.font = font
        # Propiedades de depuración (no persisten entre actualizaciones)
        self.last_debug_property = None
        self.last_debug_mode = None
        # Intervalo de parpadeo del cursor en ms
        self.blink_interval = 500
        # Professional title bar (lazy state binding)
        self.title_view: ItemsTitleView | None = None

    def _wrap_text(self, text: str, max_width: int) -> list[str]:
        """
        Ajusta texto en varias líneas para no superar el ancho máximo.
        - Divide el texto en palabras.
        - Construye líneas incrementales hasta llegar al límite.
        - Retorna una lista de líneas.
        """
        words = text.split(' ')
        lines: list[str] = []
        current = ''
        for w in words:
            # Prueba añadiendo la palabra a la línea actual
            test = current + (' ' if current else '') + w
            if self.font.size(test)[0] <= max_width:
                current = test  # Encaja dentro del ancho
            else:
                lines.append(current)  # Línea completa
                current = w         # Empieza nueva línea
        if current:
            lines.append(current)  # Añade la última línea si existe
        return lines

    def _truncate_text(self, text: str, max_width: int) -> str:
        """
        Trunca una cadena y añade '...' hasta que quepa en el ancho máximo.
        - Si ya cabe, retorna el texto original.
        - Itera recortando un carácter hasta ajustarse con '...'.
        """
        if self.font.size(text)[0] <= max_width:
            return text
        text = text.rstrip()
        # Elipsis ocupa espacio adicional
        while text and self.font.size(text + '...')[0] > max_width:
            text = text[:-1]
        return text + '...'

    # ===== Métodos de dibujo modularizados =====
    def _draw_overlay(self, screen: pygame.Surface) -> None:
        """
        Dibuja un fondo semitransparente que atenúa la escena principal.
        """
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))  # Negro con 180/255 alfa
        screen.blit(overlay, (0, 0))

    def _draw_grid(self, screen: pygame.Surface, model: Any) -> None:
        """
        Dibuja una rejilla de iconos de ítems:
        - Calcula filas y columnas según tamaño de pantalla.
        - Escala los íconos y los pinta en celdas con margen.
        - Omite el placeholder 'image_item_not_found'.
        """
        margin = 20
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        sw, sh = screen.get_size()
        # Layout top offset to avoid overlapping the title bar
        title_rect = getattr(self, 'title_rect', None)
        grid_top = max(margin, (title_rect.bottom + 10) if title_rect else margin)
        columns = 12
        # Número de filas visibles en la altura disponible
        visible_rows = max(1, (sh - grid_top - margin) // (cell_height + margin))
        # Excluir placeholder de imagen faltante
        item_ids = [i for i in model.items.keys() if i != "image_item_not_found"]
        total_rows = (len(item_ids) + columns - 1) // columns
        # Control de desplazamiento (scroll)
        scroll = max(0, min(model.scroll_index, total_rows - visible_rows))
        for idx, item_id in enumerate(item_ids):
            col = idx % columns
            row = idx // columns
            # Solo dibujar si está en la vista actual
            if row < scroll or row >= scroll + visible_rows:
                continue
            x = margin + col * (cell_size + margin)
            y = grid_top + (row - scroll) * (cell_height + margin)
            # Fondo de la celda
            cell_rect = pygame.Rect(x, y, cell_size, cell_size)
            pygame.draw.rect(screen, (50, 50, 50), cell_rect)
            # Icono escalado
            icon = self.assets.get(item_id)
            if icon:
                icon_surf = pygame.transform.smoothscale(icon, (cell_size, cell_size))
                screen.blit(icon_surf, (x, y))

    def _draw_highlight(self, screen: pygame.Surface, model: Any) -> None:
        """
        Resalta el ítem seleccionado o el que tenga el cursor encima.
        - Calcula la posición del ítem activo.
        - Dibuja un rectángulo amarillo alrededor de la celda.
        """
        # Reutilizamos márgenes y tamaños para coherencia
        margin = 20
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        sw, sh = screen.get_size()
        title_rect = getattr(self, 'title_rect', None)
        grid_top = max(margin, (title_rect.bottom + 10) if title_rect else margin)
        columns = 12
        visible_rows = max(1, (sh - grid_top - margin) // (cell_height + margin))
        item_ids = [i for i in model.items.keys() if i != "image_item_not_found"]
        total_rows = (len(item_ids) + columns - 1) // columns
        scroll = max(0, min(model.scroll_index, total_rows - visible_rows))
        # Determina el ID activo (selección o hover)
        active_id = model.selected_item_id or model.hovered_item_id
        if active_id and active_id in item_ids:
            idx_h = item_ids.index(active_id)
            col = idx_h % columns
            row = idx_h // columns
            if scroll <= row < scroll + visible_rows:
                x = margin + col * (cell_size + margin)
                y = grid_top + (row - scroll) * (cell_height + margin)
                highlight_rect = pygame.Rect(x-2, y-2, cell_size+4, cell_size+4)
                pygame.draw.rect(screen, (255, 255, 0), highlight_rect, 3)

    def _draw_info_panel(self, screen: pygame.Surface, model: Any) -> None:
        """
        Muestra un panel con los detalles del ítem activo (seleccionado o hover):
        - Nombre, descripción envuelta en líneas.
        - Otras propiedades serializadas (model_dump o dict).
        - Recorta líneas demasiado largas y detecta áreas clicables.
        """
        margin = 20
        sw, sh = screen.get_size()
        # Respect title height for top placement
        title_rect = getattr(self, 'title_rect', None)
        top_y = max(margin, (title_rect.bottom + 10) if title_rect else margin)
        active_id = model.selected_item_id or model.hovered_item_id
        if not active_id or active_id not in model.items:
            return
        item = model.items[active_id]
        raw_name = item.name
        desc = item.description
        # Split descripción en líneas
        desc_lines = self._wrap_text(desc, sw - margin*4)
        desc_count = len(desc_lines)
        # Prepara lista de líneas: nombre + descripción
        lines = [raw_name] + desc_lines
        # Obtiene propiedades restantes vía pydantic/attrs o __dict__
        if hasattr(item, 'model_dump'):
            data = item.model_dump()
        else:
            try:
                data = item.dict()
            except:
                data = vars(item)
        for key, val in data.items():
            if key in ("name", "description") or val is None:
                continue
            lines.append(f"{key}: {val}")
        # Calcula dimensiones del panel según el contenido y la pantalla
        font_h = self.font.get_height()
        panel_padding = 10
        max_text_w = max(self.font.size(line)[0] for line in lines)
        panel_w = min(max_text_w + panel_padding*2, sw - margin*2, 500)
        panel_h = min(len(lines)*(font_h + 2) + panel_padding*2, sh - margin*2)
        panel_x = sw - panel_w - margin
        panel_y = top_y
        # Crea superficie semitransparente para el panel
        info_surf = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        info_surf.fill((0, 0, 0, 200))
        screen.blit(info_surf, (panel_x, panel_y))
        # Guarda área para detección de clics
        model.panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        # Dibuja cada línea, aplica truncado y estilo (cursiva para descripción)
        tx = panel_x + panel_padding
        ty = panel_y + panel_padding
        truncated_entries = []
        model.property_entries = []
        for idx_line, line in enumerate(lines):
            color = (255, 255, 0) if idx_line == 0 else (200, 200, 200)
            max_line_width = panel_w - panel_padding*2
            if idx_line > desc_count:
                # Si estamos en propiedades extra, reemplaza con texto editable si aplica
                key, val = line.split(': ', 1)
                text_content = (
                    f"{key}: {model.editing_text}" if model.editing_property == key
                    else f"{key}: {val}"
                )
            else:
                text_content = line
            display_text = self._truncate_text(text_content, max_line_width)
            # Aplica cursiva en descripción
            if 0 < idx_line <= desc_count:
                self.font.set_italic(True)
            txt_surf = self.font.render(display_text, True, color)
            if 0 < idx_line <= desc_count:
                self.font.set_italic(False)
            screen.blit(txt_surf, (tx, ty))
            # Registra áreas de propiedades para edición
            if idx_line > desc_count:
                rect = pygame.Rect(tx, ty, txt_surf.get_width(), font_h)
                model.property_entries.append((rect, key))
            # Detecta texto truncado para tooltips
            if display_text != text_content:
                rect_tt = pygame.Rect(tx, ty, txt_surf.get_width(), font_h)
                truncated_entries.append((rect_tt, text_content))
            ty += font_h + 2
        self._truncated_entries = truncated_entries

    def _draw_inline_edit(self, screen: pygame.Surface, model: Any) -> None:
        """
        Dibuja indicadores para edición inline:
        - Bordes púrpura para propiedad en edición.
        - Cursor parpadeante según el intervalo.
        - Si solo está enfocado (sin edición), resalta en amarillo.
        """
        font_h = self.font.get_height()
        if getattr(model, 'editing_property', None):
            # Bucle para encontrar rect del prop actual
            for rect_prop, key_prop in getattr(model, 'property_entries', []):
                if key_prop == model.editing_property:
                    ed_rect = rect_prop.inflate(4, 0)
                    pygame.draw.rect(screen, (128, 0, 128), ed_rect, 2)
                    t = pygame.time.get_ticks()
                    if (t % self.blink_interval) < (self.blink_interval // 2):
                        prefix = f"{model.editing_property}: "
                        before_text = model.editing_text[:model.editing_cursor]
                        x_offset = self.font.size(prefix + before_text)[0]
                        caret_x = rect_prop.x + x_offset
                        caret_y_top = rect_prop.y
                        caret_y_bottom = rect_prop.y + font_h
                        pygame.draw.line(
                            screen,
                            (255, 255, 255),
                            (caret_x, caret_y_top),
                            (caret_x, caret_y_bottom),
                            1
                        )
                    break
        elif getattr(model, 'focused_property', None):
            for rect_prop, key_prop in getattr(model, 'property_entries', []):
                if key_prop == model.focused_property:
                    hl_rect = rect_prop.inflate(4, 0)
                    pygame.draw.rect(screen, (255, 255, 0), hl_rect, 2)
                    break

    def _draw_tooltips(self, screen: pygame.Surface) -> None:
        """
        Muestra tooltips al pasar el ratón sobre texto truncado:
        - Detecta colisiones con rects almacenados.
        - Crea superficie semitransparente y renderiza texto completo.
        """
        margin = 20
        font_h = self.font.get_height()
        mx, my = pygame.mouse.get_pos()
        sw, sh = pygame.display.get_surface().get_size()
        for rect, full_text in getattr(self, '_truncated_entries', []):
            if rect.collidepoint(mx, my):
                tt_w = self.font.size(full_text)[0] + 8
                tt_h = font_h + 4
                tt_x = min(mx + 10, sw - tt_w - margin)
                tt_y = min(my + 10, sh - tt_h - margin)
                tooltip_surf = pygame.Surface((tt_w, tt_h), pygame.SRCALPHA)
                tooltip_surf.fill((0, 0, 0, 220))
                tooltip_txt = self.font.render(full_text, True, (255, 255, 255))
                tooltip_surf.blit(tooltip_txt, (4, 2))
                screen.blit(tooltip_surf, (tt_x, tt_y))
                break

    def draw(self, screen: pygame.Surface, model: Any) -> None:
        """
        Punto de entrada para renderizar la vista completa:
        - Solo dibuja si el modelo es visible.
        - Orquesta overlay, rejilla, resaltado, panel de info, edición inline y tooltips.
        """
        if not model.visible:
            return
        # Dim background first
        self._draw_overlay(screen)
        # Ensure title view is bound to current state and render title at top-left above overlay
        if self.title_view is None:
            self.title_view = ItemsTitleView(None, model)
        else:
            self.title_view.state = model
        self.title_rect = self.title_view.render(screen)
        self._draw_grid(screen, model)
        self._draw_highlight(screen, model)
        self._draw_info_panel(screen, model)
        self._draw_inline_edit(screen, model)
        self._draw_tooltips(screen)
        return
