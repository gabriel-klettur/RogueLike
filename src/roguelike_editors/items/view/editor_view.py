import pygame
from typing import Any, Dict

class ItemEditorView:
    """Renderiza UI del editor de ítems: panel e ítems."""
    def __init__(self, assets: Dict[str, pygame.Surface], font: pygame.font.Font):
        self.assets = assets
        self.font = font
        self.last_debug_property = None
        self.last_debug_mode = None
        self.blink_interval = 500  # ms cursor blink interval

    def _wrap_text(self, text: str, max_width: int) -> list[str]:
        words = text.split(' ')
        lines: list[str] = []
        current = ''
        for w in words:
            test = current + (' ' if current else '') + w
            if self.font.size(test)[0] <= max_width:
                current = test
            else:
                lines.append(current)
                current = w
        if current:
            lines.append(current)
        return lines

    def _truncate_text(self, text: str, max_width: int) -> str:
        if self.font.size(text)[0] <= max_width:
            return text
        text = text.rstrip()
        while self.font.size(text + '...')[0] > max_width and text:
            text = text[:-1]
        return text + '...'

    def draw(self, screen: pygame.Surface, model: Any) -> None:

        # Fondo semi-transparente
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))
        screen.blit(overlay, (0, 0))
        # Initialize blink interval for caret if needed
        if not hasattr(self, 'blink_interval'):
            self.blink_interval = 500  # ms cursor blink interval

        # Parámetros de layout
        margin = 20
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        sw, sh = screen.get_size()
        # Columnas fijas: 12
        columns = 12
        visible_rows = max(1, (sh - 2*margin) // (cell_height + margin))
        # Exclude placeholder item from grid
        item_ids = [i for i in model.items.keys() if i != "image_item_not_found"]
        total_rows = (len(item_ids) + columns - 1) // columns
        # Clamp scroll index
        scroll = max(0, min(model.scroll_index, total_rows - visible_rows))

        # Dibujar grid de ítems (solo iconos)
        for idx, item_id in enumerate(item_ids):
            col = idx % columns
            row = idx // columns
            if row < scroll or row >= scroll + visible_rows:
                continue
            x = margin + col * (cell_size + margin)
            y = margin + (row - scroll) * (cell_height + margin)
            # Celda fondo
            cell_rect = pygame.Rect(x, y, cell_size, cell_size)
            pygame.draw.rect(screen, (50, 50, 50), cell_rect)
            # Icono escalado
            icon = self.assets.get(item_id)
            if icon:
                icon_surf = pygame.transform.smoothscale(icon, (cell_size, cell_size))
                screen.blit(icon_surf, (x, y))

        # Highlight y panel de información
        # Mostrar detalles del item seleccionado o en hover
        active_id = model.selected_item_id or model.hovered_item_id
        if active_id and active_id in item_ids:
            # Resaltar ítem
            idx_h = item_ids.index(active_id)
            col = idx_h % columns
            row = idx_h // columns
            if scroll <= row < scroll + visible_rows:
                x = margin + col * (cell_size + margin)
                y = margin + (row - scroll) * (cell_height + margin)
                highlight_rect = pygame.Rect(x-2, y-2, cell_size+4, cell_size+4)
                pygame.draw.rect(screen, (255, 255, 0), highlight_rect, 3)

                # Preparar líneas de texto
                item = model.items[active_id]
                raw_name = item.name
                desc = item.description
                # Wrap descripción con ancho máximo provisional
                desc_lines = self._wrap_text(desc, sw - margin*4)
                # Contar líneas de descripción para aplicar cursiva
                desc_count = len(desc_lines)
                lines = [raw_name] + desc_lines

                # Propiedades adicionales (cargadas desde el modelo)
                if hasattr(item, 'model_dump'):
                    data = item.model_dump()
                else:
                    try:
                        data = item.dict()
                    except:
                        data = vars(item)
                for key, val in data.items():
                    # Excluir nombre, descripción y valores null
                    if key in ("name", "description") or val is None:
                        continue
                    lines.append(f"{key}: {val}")

                # Calcular dimensiones dinámicas
                max_text_w = max(self.font.size(line)[0] for line in lines)
                panel_padding = 10
                panel_w = min(max_text_w + panel_padding*2, sw - margin*2, 500)  # ancho máximo del panel
                panel_h = min(len(lines)*(font_h + 2) + panel_padding*2, sh - margin*2)

                panel_x = sw - panel_w - margin
                panel_y = margin

                # Dibujar panel
                info_surf = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
                info_surf.fill((0, 0, 0, 200))
                screen.blit(info_surf, (panel_x, panel_y))
                # Registrar área del panel para gestión de clics
                try:
                    model.panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
                except Exception:
                    pass

                # Render texto con truncamiento, edición y tooltips
                tx = panel_x + panel_padding
                ty = panel_y + panel_padding
                truncated_entries = []
                # Area de clic de propiedades
                model.property_entries = []
                for idx_line, line in enumerate(lines):
                    color = (255, 255, 0) if idx_line == 0 else (200, 200, 200)
                    max_line_width = panel_w - panel_padding*2
                    # Lineas de propiedad
                    if idx_line > desc_count:
                        key, val = line.split(": ", 1)
                        # Si está en edición, mostrar editing_text
                        if model.editing_property == key:
                            text_content = f"{key}: {model.editing_text}"
                        else:
                            text_content = f"{key}: {val}"
                    else:
                        text_content = line
                    display_text = self._truncate_text(text_content, max_line_width)
                    # Aplicar cursiva a descripción
                    if 0 < idx_line <= desc_count:
                        self.font.set_italic(True)
                    txt_surf = self.font.render(display_text, True, color)
                    if 0 < idx_line <= desc_count:
                        self.font.set_italic(False)
                    screen.blit(txt_surf, (tx, ty))
                    # Registrar entry de propiedades para edición
                    if idx_line > desc_count:
                        rect = pygame.Rect(tx, ty, txt_surf.get_width(), font_h)
                        model.property_entries.append((rect, key))
                    # Registrar entradas truncadas para tooltips
                    if display_text != text_content:
                        rect_tt = pygame.Rect(tx, ty, txt_surf.get_width(), font_h)
                        truncated_entries.append((rect_tt, text_content))
                    ty += font_h + 2

                
                
                # Debug: print border once per change
                cur_prop = model.editing_property or model.focused_property
                cur_mode = 'editing' if model.editing_property else ('focus' if model.focused_property else None)
                if cur_prop:
                    if cur_prop != self.last_debug_property or cur_mode != self.last_debug_mode:
                        color = 'purple' if cur_mode=='editing' else 'yellow'
                        print(f"[DEBUG view] drawing {color} border for {cur_prop}")
                        self.last_debug_property = cur_prop
                        self.last_debug_mode = cur_mode
                else:
                    self.last_debug_property = None
                    self.last_debug_mode = None

                # Draw input box for editing or highlight focused property
                if model.editing_property:
                    # Debug draw editing border
                    
                    # Purple border for active editing
                    for rect_prop, key_prop in model.property_entries:
                        if key_prop == model.editing_property:
                            ed_rect = rect_prop.inflate(4, 0)
                            pygame.draw.rect(screen, (128, 0, 128), ed_rect, 2)
                            # Blinking caret
                            t = pygame.time.get_ticks()
                            if (t % self.blink_interval) < (self.blink_interval // 2):
                                prefix = f"{model.editing_property}: "
                                before_text = model.editing_text[:model.editing_cursor]
                                x_offset = self.font.size(prefix + before_text)[0]
                                caret_x = rect_prop.x + x_offset
                                caret_y_top = rect_prop.y
                                caret_y_bottom = rect_prop.y + font_h
                                pygame.draw.line(screen, (255, 255, 255), (caret_x, caret_y_top), (caret_x, caret_y_bottom), 1)
                            break
                elif model.focused_property:
                    # Debug draw focus border
                    
                    # Yellow border for focus
                    for rect_prop, key_prop in model.property_entries:
                        if key_prop == model.focused_property:
                            hl_rect = rect_prop.inflate(4, 0)
                            pygame.draw.rect(screen, (255, 255, 0), hl_rect, 2)
                            break

                # Tooltips on hover
                mx, my = pygame.mouse.get_pos()
                for rect, full_text in truncated_entries:
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
        # Parámetros de layout
        margin = 20
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        sw, sh = screen.get_size()
        columns = max(1, (sw - margin) // (cell_size + margin))

