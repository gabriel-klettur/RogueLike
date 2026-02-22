import pygame
import logging
import os
from typing import Any, Callable, Optional
from roguelike_editors.spells.spells_picker_panel.spells_editor_model import SpellEditorModel
from roguelike_editors.spells.spells_title_panel.spells_title_view import SpellsTitleView
from roguelike_editors.entities.services.constants import UI_MARGIN
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.picker_panel import PickerPanel, PickerPanelState
from roguelike_ui.ui_blocker import register_blocker
from roguelike_ui.widgets.hover import draw_hover

# Module logger and throttled debug controls
logger = logging.getLogger(__name__)
LOG_SPELLS_VIEW_DEBUG = (
    os.getenv("RL_SPELLS_VIEW_DEBUG") == "1"
    or os.getenv("RL_SPELLS_EDITOR_DEBUG") == "1"
)
_last_dt_log_ts = 0
_last_providers_log_ts = 0

class SpellEditorView:
    """Render the spell editor UI."""
    def __init__(self, assets: dict[str, pygame.Surface], font: pygame.font.Font):
        self.assets = assets
        self.font = font
        self.blink_interval = 500
        self.title_view: SpellsTitleView | None = None
        self.title_rect: pygame.Rect | None = None
        # Optional provider to override the left anchor (x) for the picker grid
        # Should return an int x or None to keep default margin
        self.get_picker_left_anchor_x: Optional[Callable[[], int | None]] = None
        # Optional per-spell preview providers: (size, dt_ms) -> Surface
        self.preview_providers: dict[str, Callable[[tuple[int, int], int], pygame.Surface]] = {}
        # Frame timing for previews
        self._last_ticks: int = pygame.time.get_ticks()
        self._dt_ms: int = 16
        self._max_dt_ms: int = 50

        # Panel configuration to match Entities picker
        self.margin = 20
        self.cell_size = 64
        self.text_margin = 4
        self.columns = 10

        # Panel position (left/top anchors). Kept in sync with external anchor provider.
        self.x = 0
        self.y = 0

        # Draggable panel and reusable PickerPanel (grid renderer)
        self.draggable_panel = DraggablePanel(0, 0)
        cell_h_with_label = self.cell_size + self.text_margin + self.font.get_height()
        self.picker = PickerPanel(
            cell_size=(self.cell_size, cell_h_with_label),
            margin=self.margin,
            padding=self.margin,
            draw_panel_bg=False,
            allow_dragging=False,
            draw_overlays=False,
            grid_bg_color=None,
        )
        self._current_spell_ids: list[str] = []
        self._last_model: Optional[SpellEditorModel] = None
        self.picker.set_item_count(lambda: len(self._current_spell_ids))
        self.picker.set_draw_item(lambda surf, rect, idx, sel, hov: self._draw_spell_cell(surf, rect, self._current_spell_ids[idx]))
        self.picker_state = PickerPanelState(rect=pygame.Rect(0, 0, 0, 0), visible=True)

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
        # Frame delta for previews
        now = pygame.time.get_ticks()
        self._dt_ms = max(1, now - self._last_ticks)
        # Clamp dt to avoid large spikes on window focus or selection changes
        self._dt_ms = min(self._dt_ms, getattr(self, '_max_dt_ms', 50))
        self._last_ticks = now
        # Debug summary for this view draw (throttled and gated)
        if LOG_SPELLS_VIEW_DEBUG and logger.isEnabledFor(logging.DEBUG):
            global _last_dt_log_ts
            now_ms = pygame.time.get_ticks()
            if now_ms - _last_dt_log_ts >= 1000:
                try:
                    logger.debug("[SpellsView] dt_ms=%d", self._dt_ms)
                except Exception:
                    pass
                _last_dt_log_ts = now_ms
        # Removed full-screen dim overlay to keep game visible behind the editor
        # Title above the dim background
        if self.title_view is None:
            self.title_view = SpellsTitleView(None, model)
        else:
            self.title_view.state = model
        self.title_rect = self.title_view.render(screen)

        # If picker is not visible, do not render grid/properties
        if not getattr(model, 'picker_visible', False):
            model.panel_rect = None
            return

        # Anchor relative to title and external left provider
        sw, sh = screen.get_size()
        title_rect = getattr(self, 'title_rect', None)
        default_y = max(self.margin, (title_rect.bottom + UI_MARGIN) if title_rect else self.margin)
        default_x = self.margin
        try:
            provider = getattr(self, 'get_picker_left_anchor_x', None)
            if callable(provider):
                x_override = provider()
                if isinstance(x_override, int):
                    default_x = x_override
        except Exception:
            pass

        # Compute dynamic panel size like Entities picker (grid area only; no header tabs)
        spell_ids = list(model.spells.keys())
        rows = (len(spell_ids) + self.columns - 1) // self.columns
        cell_h_with_label = self.cell_size + self.text_margin + self.font.get_height()
        used_cols = min(self.columns, len(spell_ids))
        panel_w = self.margin + used_cols * self.cell_size + (used_cols + 1) * self.margin
        grid_h = self.margin + rows * (cell_h_with_label + self.margin)
        # Footer height (for centered label)
        footer_h = self.font.get_height() + 10
        panel_h = grid_h + footer_h

        # Update panel rect and blocker
        self.draggable_panel.resize(panel_w, panel_h)
        # Use anchors only as default position; otherwise honor dragging
        if self.draggable_panel.pos is None:
            self.draggable_panel.pos = (default_x, default_y)
        self.x, self.y = self.draggable_panel.pos
        model.panel_rect = pygame.Rect(self.x, self.y, panel_w, panel_h)
        register_blocker(model.panel_rect)

        # Draw semi-transparent panel background (not the full-screen overlay)
        bg = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        bg.fill((0, 0, 0, 200))
        screen.blit(bg, (self.x, self.y))

        # Prepare PickerPanel state and render grid
        self._current_spell_ids = spell_ids
        self._last_model = model
        self.picker_state.rect = pygame.Rect(self.x, self.y, panel_w, grid_h)
        # Expose grid rect for other panels (e.g., properties)
        self.grid_rect = self.picker_state.rect.copy()
        self.picker_state.selected_index = (spell_ids.index(model.selected_id) if model.selected_id in spell_ids else None)
        self.picker_state.hovered_index = (spell_ids.index(model.hovered_id) if model.hovered_id in spell_ids else None)
        # Convert row scroll to pixel scroll to match Entities behavior
        self.picker_state.scroll_y = max(0, model.scroll_index) * (cell_h_with_label + self.margin)
        # Log how many preview providers will be asked this draw (throttled and gated)
        if LOG_SPELLS_VIEW_DEBUG and logger.isEnabledFor(logging.DEBUG):
            global _last_providers_log_ts
            now_ms = pygame.time.get_ticks()
            if now_ms - _last_providers_log_ts >= 1000:
                try:
                    if spell_ids:
                        have_prev = sum(1 for sid in spell_ids if callable(self.preview_providers.get(sid)))
                        logger.debug("[SpellsView] providers=%d/%d", have_prev, len(spell_ids))
                except Exception:
                    pass
                _last_providers_log_ts = now_ms
        self.picker.render(screen, self.picker_state)

        # Footer label (hovered or selected)
        label_text = model.hovered_id or model.selected_id or ""
        if label_text:
            pretty = label_text.replace("_", " ").title()
            text_surf = self.font.render(pretty, True, (255, 230, 0))
            tx = self.x + (panel_w - text_surf.get_width()) // 2
            ty = self.y + grid_h + (footer_h - text_surf.get_height()) // 2
            screen.blit(text_surf, (tx, ty))

        # Properties panel rendering is delegated to SpellsPropertiesPanelView via controller

    def _draw_spell_cell(self, screen: pygame.Surface, rect: pygame.Rect, spell_id: str) -> None:
        """Draw a single picker cell: background, icon (aspect-correct), and label."""
        # Cell background
        pygame.draw.rect(screen, (50, 50, 50), rect)
        pad = 6
        max_w = self.cell_size - 2 * pad
        max_h = self.cell_size - 2 * pad
        # Prefer particle preview if provided for this spell
        provider = self.preview_providers.get(spell_id)
        if callable(provider):
            try:
                frame = provider((max_w, max_h), self._dt_ms)
                fw, fh = frame.get_size()
                dest_x = rect.x + (self.cell_size - fw) // 2
                dest_y = rect.y + (self.cell_size - fh - pad)
                screen.blit(frame, (dest_x, dest_y))
            except Exception:
                pass
        else:
            icon = self.assets.get(spell_id)
            if icon:
                orig_w, orig_h = icon.get_size()
                if orig_w > 0 and orig_h > 0:
                    scale = min(max_w / orig_w, max_h / orig_h)
                    new_w = max(1, int(orig_w * scale))
                    new_h = max(1, int(orig_h * scale))
                    icon_surf = pygame.transform.smoothscale(icon, (new_w, new_h))
                    dest_x = rect.x + (self.cell_size - new_w) // 2
                    dest_y = rect.y + (self.cell_size - new_h - pad)  # bottom align inside cell
                    # subtle shadow
                    shadow = pygame.Surface((new_w, new_h), pygame.SRCALPHA)
                    shadow.fill((0, 0, 0, 80))
                    screen.blit(shadow, (dest_x + 2, dest_y + 2))
                    screen.blit(icon_surf, (dest_x, dest_y))
        # Label below icon
        label = self._truncate_text(spell_id, self.cell_size)
        text = self.font.render(label, True, (255, 255, 255))
        scale_text = 0.65
        tw, th = text.get_size()
        text = pygame.transform.smoothscale(text, (int(tw * scale_text), int(th * scale_text)))
        tx = rect.x + (self.cell_size - text.get_width()) // 2
        ty = rect.y + self.cell_size + self.text_margin
        screen.blit(text, (tx, ty))
        # Overlays: hover/selection/delete-mode like Entities picker
        if not self._last_model:
            return
        if spell_id == self._last_model.hovered_id and not getattr(self._last_model, 'delete_mode_active', False):
            draw_hover(screen, rect)
        if spell_id == self._last_model.selected_id:
            pygame.draw.rect(screen, (255, 255, 0), rect.inflate(4, 4), 3)
        if getattr(self._last_model, 'delete_mode_active', False) and spell_id == self._last_model.hovered_id:
            pygame.draw.rect(screen, (255, 0, 0), rect.inflate(4, 4), 3)
            red = pygame.Surface((rect.width, rect.height), pygame.SRCALPHA)
            red.fill((255, 0, 0, 60))
            screen.blit(red, (rect.x, rect.y))
