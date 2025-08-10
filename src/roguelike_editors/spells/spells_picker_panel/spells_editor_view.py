import pygame
from typing import Any
from roguelike_editors.spells.spells_picker_panel.spells_editor_model import SpellEditorModel
from roguelike_editors.spells.spells_title_panel.spells_title_view import SpellsTitleView

class SpellEditorView:
    """Render the spell editor UI."""
    def __init__(self, assets: dict[str, pygame.Surface], font: pygame.font.Font):
        self.assets = assets
        self.font = font
        self.blink_interval = 500
        self.title_view: SpellsTitleView | None = None
        self.title_rect: pygame.Rect | None = None

    def _truncate_text(self, text: str, max_width: int) -> str:
        if self.font.size(text)[0] <= max_width:
            return text
        text = text.rstrip()
        while self.font.size(text + '...')[0] > max_width and text:
            text = text[:-1]
        return text + '...'

    def draw(self, screen: pygame.Surface, model: SpellEditorModel) -> None:
        if not model.visible:
            return
        # Semi-transparent background
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))
        screen.blit(overlay, (0, 0))
        # Title above the dim background
        if self.title_view is None:
            self.title_view = SpellsTitleView(None, model)
        else:
            self.title_view.state = model
        self.title_rect = self.title_view.render(screen)

        margin = 20
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        sw, sh = screen.get_size()
        columns = 8  # fewer columns since spells less numerous
        # Layout top offset to avoid overlapping the title bar
        title_rect = getattr(self, 'title_rect', None)
        grid_top = max(margin, (title_rect.bottom + 10) if title_rect else margin)

        spell_ids = list(model.spells.keys())
        total_rows = (len(spell_ids) + columns - 1) // columns
        max_rows_visible = max(1, (sh - grid_top - margin) // (cell_height + margin))
        scroll = max(0, min(model.scroll_index, total_rows - max_rows_visible))

        # Draw grid of icons
        for idx, sid in enumerate(spell_ids):
            col = idx % columns
            row = idx // columns
            if row < scroll or row >= scroll + max_rows_visible:
                continue
            x = margin + col * (cell_size + margin)
            y = grid_top + (row - scroll) * (cell_height + margin)
            cell_rect = pygame.Rect(x, y, cell_size, cell_size)
            pygame.draw.rect(screen, (50, 50, 50), cell_rect)
            icon = self.assets.get(sid)
            if icon:
                icon_surf = pygame.transform.smoothscale(icon, (cell_size, cell_size))
                screen.blit(icon_surf, (x, y))

        # Highlight selected/hover
        active = model.selected_id or model.hovered_id
        if active in spell_ids:
            idx_h = spell_ids.index(active)
            col = idx_h % columns
            row = idx_h // columns
            if scroll <= row < scroll + max_rows_visible:
                x = margin + col * (cell_size + margin)
                y = grid_top + (row - scroll) * (cell_height + margin)
                pygame.draw.rect(screen, (255, 255, 0), (x-2, y-2, cell_size+4, cell_size+4), 3)

                # Draw properties panel
                data = model.spells.get(active, {})
                lines = [active] + [f"{k}: {v}" for k, v in data.items() if v is not None]
                max_w = max(self.font.size(line)[0] for line in lines)
                pad = 10
                panel_w = min(max_w + pad*2, sw - margin*2, 500)
                panel_h = min(len(lines) * (font_h+2) + pad*2, sh - margin*2)
                px = sw - panel_w - margin
                # Respect title height for top placement
                py = grid_top
                info_surf = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
                info_surf.fill((0, 0, 0, 200))
                screen.blit(info_surf, (px, py))
                model.panel_rect = pygame.Rect(px, py, panel_w, panel_h)
                tx, ty = px + pad, py + pad
                model.property_entries.clear()
                for i, line in enumerate(lines):
                    color = (255, 255, 0) if i == 0 else (200, 200, 200)
                    text = self._truncate_text(line, panel_w - pad*2)
                    key = line.split(': ',1)[0] if i > 0 else ''
                    txt_surf = self.font.render(text, True, color)
                    screen.blit(txt_surf, (tx, ty))
                    if i > 0:
                        rect = pygame.Rect(tx, ty, txt_surf.get_width(), font_h)
                        model.property_entries.append((rect, key))
                    ty += font_h + 2

                # Draw editing caret
                if model.editing_property:
                    for rect, key in model.property_entries:
                        if key == model.editing_property:
                            er = rect.inflate(4, 0)
                            pygame.draw.rect(screen, (128, 0, 128), er, 2)
                            t = pygame.time.get_ticks()
                            if (t % self.blink_interval) < (self.blink_interval // 2):
                                pre = f"{key}: "
                                bx = er.x
                                by = er.y
                                caret_x = bx + self.font.size(pre + model.editing_text[:model.editing_cursor])[0]
                                pygame.draw.line(screen, (255, 255, 255), (caret_x, by), (caret_x, by + font_h), 2)
                            break
                elif model.focused_property:
                    for rect, key in model.property_entries:
                        if key == model.focused_property:
                            hl_rect = rect.inflate(4, 0)
                            pygame.draw.rect(screen, (255, 255, 0), hl_rect, 2)
                            break
