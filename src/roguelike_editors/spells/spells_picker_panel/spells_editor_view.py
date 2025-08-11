import pygame
from typing import Any
from roguelike_editors.spells.spells_picker_panel.spells_editor_model import SpellEditorModel
from roguelike_editors.spells.spells_title_panel.spells_title_view import SpellsTitleView
from roguelike_editors.entities.services.constants import UI_MARGIN

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

        # If picker is not visible, do not render grid/properties
        if not getattr(model, 'picker_visible', False):
            return

        margin = 20
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        sw, sh = screen.get_size()
        columns = 8  # fewer columns since spells less numerous
        # Layout top offset to avoid overlapping the title bar
        title_rect = getattr(self, 'title_rect', None)
        grid_top = max(margin, (title_rect.bottom + UI_MARGIN) if title_rect else margin)
        # Expose picker grid rect for external anchoring (e.g., properties panel)
        grid_width = columns * (cell_size + margin) - margin
        try:
            self.grid_rect = pygame.Rect(margin, grid_top, grid_width, sh - grid_top - margin)
        except Exception:
            self.grid_rect = None

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
            icon = model.assets.get(sid)
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

        # Delete-mode hover highlight in red (independent of properties rendering)
        if getattr(model, 'delete_mode_active', False) and model.hovered_id in spell_ids:
            idx_h = spell_ids.index(model.hovered_id)
            col = idx_h % columns
            row = idx_h // columns
            if scroll <= row < scroll + max_rows_visible:
                x = margin + col * (cell_size + margin)
                y = grid_top + (row - scroll) * (cell_height + margin)
                pygame.draw.rect(screen, (255, 0, 0), (x-2, y-2, cell_size+4, cell_size+4), 3)
                overlay = pygame.Surface((cell_size, cell_size), pygame.SRCALPHA)
                overlay.fill((255, 0, 0, 60))
                screen.blit(overlay, (x, y))

        # Properties panel rendering is delegated to SpellsPropertiesPanelView via controller
