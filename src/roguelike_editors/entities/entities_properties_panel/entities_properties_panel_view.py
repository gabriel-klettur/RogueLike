import pygame
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel

class EntityPropertiesPanelView:
    """Renderiza el panel de propiedades de la entidad seleccionada."""
    def __init__(self, font: pygame.font.Font, blink_interval: int = 500):
        self.font = font
        self.blink_interval = blink_interval

    def _truncate_text(self, text: str, max_width: int) -> str:
        if self.font.size(text)[0] <= max_width:
            return text
        text = text.rstrip()
        while self.font.size(text + '...')[0] > max_width and text:
            text = text[:-1]
        return text + '...'

    def draw(self, screen: pygame.Surface, model: EntityPropertiesPanelModel) -> None:
        if not model.selected_id:
            return
        sw, sh = screen.get_size()
        # Obtener datos de la entidad
        if model.selected_id in model.player_stats:
            data = model.player_stats.get(model.selected_id, {})
        else:
            data = model.monsters.get(model.selected_id, {})
        # Preparar líneas de texto
        lines = [model.selected_id] + [f"{k}: {v}" for k, v in data.items() if v is not None]
        font_h = self.font.get_height()
        # Calcular dimensiones del panel
        max_w = max(self.font.size(line)[0] for line in lines)
        pad = 10
        margin = 20
        panel_w = min(max_w + pad*2, sw - margin*2, 500)
        panel_h = min(len(lines)*(font_h+2) + pad*2, sh - margin*2)
        px = sw - panel_w - margin
        py = margin
        # Dibujar fondo semitransparente
        info_surf = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        info_surf.fill((0, 0, 0, 200))
        screen.blit(info_surf, (px, py))
        # Actualizar rectángulo de panel
        model.panel_rect = pygame.Rect(px, py, panel_w, panel_h)
        # Dibujar líneas de texto y áreas clicables
        tx = px + pad
        ty = py + pad
        model.property_entries.clear()
        for i, line in enumerate(lines):
            color = (255,255,0) if i == 0 else (200,200,200)
            text = self._truncate_text(line, panel_w - pad*2)
            txt_surf = self.font.render(text, True, color)
            screen.blit(txt_surf, (tx, ty))
            if i > 0:
                key = line.split(': ', 1)[0]
                rect = pygame.Rect(tx, ty, txt_surf.get_width(), font_h)
                model.property_entries.append((rect, key))
            ty += font_h + 2
        # Indicadores de enfoque o edición
        if model.editing_property:
            for rect, key in model.property_entries:
                if key == model.editing_property:
                    er = rect.inflate(4, 0)
                    pygame.draw.rect(screen, (128,0,128), er, 2)
                    t = pygame.time.get_ticks()
                    if (t % self.blink_interval) < (self.blink_interval // 2):
                        prefix = f"{key}: "
                        caret_x = er.x + self.font.size(prefix + model.editing_text[:model.editing_cursor])[0]
                        caret_y = er.y
                        pygame.draw.line(screen, (255,255,255), (caret_x, caret_y), (caret_x, caret_y + font_h), 2)
                    break
        elif model.focused_property:
            for rect, key in model.property_entries:
                if key == model.focused_property:
                    hl_rect = rect.inflate(4,0)
                    pygame.draw.rect(screen, (255,255,0), hl_rect, 2)
                    break
