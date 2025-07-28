import pygame
from roguelike_ui.ui_blocker import register_blocker
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.hover import draw_hover


class EntityPropertiesPanelView:
    """
    Vista encargada de renderizar el panel de propiedades de la entidad seleccionada.
    
    Características:
    - Fondo semitransparente.
    - Soporte para arrastre (DraggablePanel).
    - Dibujo dinámico de propiedades con hover, foco y edición activa.
    """

    def __init__(self, font: pygame.font.Font, blink_interval: int = 500):
        self.font = font
        self.blink_interval = blink_interval
        self.draggable_panel = DraggablePanel(0, 0)

    # ----------------------------
    # RENDER PRINCIPAL
    # ----------------------------
    def draw(self, screen: pygame.Surface, model: EntityPropertiesPanelModel) -> None:
        """Renderiza el panel y las propiedades si hay una entidad seleccionada."""
        if not model.selected_id:
            return

        # Datos y dimensiones básicas
        sw, sh = screen.get_size()
        entity_data = self._get_entity_data(model)
        lines = [model.selected_id] + [f"{k}: {v}" for k, v in entity_data.items() if v is not None]
        font_h = self.font.get_height()

        # Calcular tamaño del panel
        pad, margin = 10, 20
        max_w = max(self.font.size(line)[0] for line in lines)
        panel_w = min(max_w + pad * 2, sw - margin * 2, 500)
        panel_h = min(len(lines) * (font_h + 2) + pad * 2, sh - margin * 2)

        # Posición inicial (esquina superior derecha)
        px, py = sw - panel_w - margin, margin

        # Ajustar panel draggable
        self.draggable_panel.resize(panel_w, panel_h)
        if self.draggable_panel.pos is None:
            self.draggable_panel.pos = (px, py)
        else:
            px, py = self.draggable_panel.pos

        # Actualizar rect para detección de eventos
        model.panel_rect = pygame.Rect(px, py, panel_w, panel_h)
        register_blocker(model.panel_rect)

        # 1. Dibujar fondo
        self._draw_background(screen, px, py, panel_w, panel_h)

        # 2. Dibujar propiedades (texto + hover)
        self._draw_properties(screen, model, lines, px, py, pad, font_h, panel_w)

        # 3. Indicadores de edición o foco
        self._draw_editing_indicator(screen, model, font_h)

    # ----------------------------
    # MÉTODOS PRIVADOS
    # ----------------------------
    def _get_entity_data(self, model: EntityPropertiesPanelModel) -> dict:
        """Obtiene los datos de la entidad seleccionada."""
        if model.selected_id in model.player_stats:
            return model.player_stats.get(model.selected_id, {})
        return model.monsters.get(model.selected_id, {})

    def _draw_background(self, screen: pygame.Surface, x: int, y: int, w: int, h: int) -> None:
        """Dibuja el fondo semitransparente del panel."""
        info_surf = pygame.Surface((w, h), pygame.SRCALPHA)
        info_surf.fill((0, 0, 0, 200))
        screen.blit(info_surf, (x, y))

    def _draw_properties(self, screen: pygame.Surface, model: EntityPropertiesPanelModel,
                         lines: list[str], px: int, py: int, pad: int, font_h: int, panel_w: int) -> None:
        """Renderiza las propiedades, maneja hover y actualiza las áreas clicables."""
        tx, ty = px + pad, py + pad
        model.property_entries.clear()

        for i, line in enumerate(lines):
            # Primer línea (ID) en amarillo, resto en gris
            color = (255, 255, 0) if i == 0 else (200, 200, 200)
            text = self._truncate_text(line, panel_w - pad * 2)
            txt_surf = self.font.render(text, True, color)

            # Registrar área clicable (excepto ID)
            if i > 0:
                key = line.split(': ', 1)[0]
                rect = pygame.Rect(tx, ty, txt_surf.get_width(), font_h)
                model.property_entries.append((rect, key))

                # Hover visual
                if key == model.hovered_property:
                    draw_hover(screen, rect)

            screen.blit(txt_surf, (tx, ty))
            ty += font_h + 2

    def _draw_editing_indicator(self, screen: pygame.Surface, model: EntityPropertiesPanelModel, font_h: int) -> None:
        """Dibuja los indicadores de edición activa o foco."""
        # Si hay edición activa
        if model.editing_property:
            for rect, key in model.property_entries:
                if key == model.editing_property:
                    # Marco púrpura
                    er = rect.inflate(4, 0)
                    pygame.draw.rect(screen, (128, 0, 128), er, 2)

                    # Cursor parpadeante
                    t = pygame.time.get_ticks()
                    if (t % self.blink_interval) < (self.blink_interval // 2):
                        prefix = f"{key}: "
                        caret_x = er.x + self.font.size(prefix + model.editing_text[:model.editing_cursor])[0]
                        caret_y = er.y
                        pygame.draw.line(screen, (255, 255, 255), (caret_x, caret_y),
                                         (caret_x, caret_y + font_h), 2)
                    break

        # Si solo hay foco
        elif model.focused_property:
            for rect, key in model.property_entries:
                if key == model.focused_property:
                    hl_rect = rect.inflate(4, 0)
                    pygame.draw.rect(screen, (255, 255, 0), hl_rect, 2)
                    break

    def _truncate_text(self, text: str, max_width: int) -> str:
        """Trunca el texto y añade '...' si excede el ancho disponible."""
        if self.font.size(text)[0] <= max_width:
            return text
        text = text.rstrip()
        while self.font.size(text + '...')[0] > max_width and text:
            text = text[:-1]
        return text + '...'
